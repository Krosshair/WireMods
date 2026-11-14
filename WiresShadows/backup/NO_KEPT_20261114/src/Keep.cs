using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Shadow maps outlive their lights. Leaving a base (a portal, walking off) unloads it and
    // destroys its lights; coming back, every light needed its map rendered again, a couple of
    // seconds of lag. Now a light's map is kept when it loses its slot, under where the light stands,
    // with a fingerprint of everything in its reach that can cast into it (the pieces and items by
    // their saved world id, where they stand, and each door's open or shut). A light that loads in
    // at the same spot with the same fingerprint gets its old map back without a render; anything
    // built, broken, moved or opened there since means a new one, as before.
    // Kept maps are made small (the smallest size, nearest depth kept, KeptTier): one comes back at
    // once, a little coarse, and re-renders at its own size from there. At most as many as lights can
    // have maps at once; the oldest go first, and when a new map needs the room. Ground dug or raised
    // there while away isn't in the fingerprint.
    internal sealed partial class ShadowCache
    {
        private struct KeptMap { internal int Tier, Cube, Hash; internal float Range; internal Vector3 Center; internal float At; }
        private readonly Dictionary<int, KeptMap> _kept = new Dictionary<int, KeptMap>();
        // Goes up whenever something that can cast into cached maps appears, goes, moves or opens
        // (pieces, dropped and placed items, doors): a light's fingerprint taken at the same number
        // is still right.
        internal static int WorldVersion;

        private static int KeepKey(Vector3 center, float range) => Quantize(center, 100f) * 31 + Mathf.RoundToInt(range * 100f);

        // Per collider, what the fingerprint needs from its parents, looked up once: a base has
        // thousands in a light's reach, and every render after any change took them all again.
        // Instance ids aren't reused in a session; emptied if it ever gets very large.
        private sealed class HashPart { internal bool Skip, Door; internal ZNetView Nv; }
        private static readonly Dictionary<int, HashPart> HashParts = new Dictionary<int, HashPart>(4096);

        private static HashPart PartOf(Collider c)
        {
            int id = c.GetInstanceID();
            if (HashParts.TryGetValue(id, out HashPart p)) return p;
            if (HashParts.Count >= 1 << 16) HashParts.Clear();
            p = new HashPart();
            // moving maps, not this one: characters; and what the static camera never draws
            p.Skip = c.isTrigger || ((1 << c.gameObject.layer) & StaticLayerMask) == 0 || c.GetComponentInParent<Character>() != null;
            if (!p.Skip)
            {
                p.Nv = c.GetComponentInParent<ZNetView>();
                p.Door = p.Nv != null && p.Nv.GetComponent<Door>() != null;
            }
            HashParts[id] = p;
            return p;
        }

        // What can cast into a light's map, as a number that's the same when nothing there changed.
        private static int CasterHash(Vector3 center, float range)
        {
            int n = CollectStaticHits(center, range);
            int sum = 0;
            unchecked
            {
                for (int i = 0; i < n; i++)
                {
                    Collider c = Hits[i];
                    if (c == null) continue;
                    HashPart part = PartOf(c);
                    if (part.Skip) continue;
                    ZNetView nv = part.Nv;
                    ZDO zdo = nv != null ? nv.GetZDO() : null;
                    int h = zdo != null ? zdo.m_uid.GetHashCode() : 0;
                    Transform tr = c.transform;
                    h = h * 31 + Quantize(tr.position, 100f);
                    h = h * 31 + Quantize(tr.rotation * Vector3.forward, 100f);
                    if (zdo != null && part.Door) h = h * 31 + zdo.GetInt(ZDOVars.s_state);
                    sum += h * 16777619 ^ (h >> 13); // order doesn't matter: summed
                }
            }
            return sum == 0 ? 1 : sum;
        }

        // A slotted light letting go of its map: kept if it was finished, freed otherwise.
        private void KeepOrFree(Tracked t, int sl)
        {
            if (!_tiered || sl < 0 || sl >= MaxSlots || _mapTier[sl] < 0f || !t.Ready || t.CasterHash == 0)
            {
                FreeSlotMap(sl);
                return;
            }
            int key = KeepKey(t.Center, t.NativeCacheRange);
            if (_kept.TryGetValue(key, out KeptMap old)) { FreeCube(old.Tier, old.Cube); _kept.Remove(key); }
            // no more kept than lights can have maps at once: the oldest go first
            while (_kept.Count >= MaxKept && DropOldestKept(-1)) { }
            int tier = (int)_mapTier[sl], cube = (int)_mapCube[sl];
            _mapTier[sl] = -1f;
            _mapCube[sl] = -1f;
            // kept small: the smallest size, each texel the nearest of the ones it covers (it comes
            // back like this, on screen at once, and re-renders at its own size from there)
            if (tier < KeptTier && CanShrink)
            {
                int small = AllocCube(KeptTier);
                if (small >= 0)
                {
                    ShrinkInto(tier, cube, small);
                    FreeCube(tier, cube);
                    tier = KeptTier;
                    cube = small;
                }
            }
            _kept[key] = new KeptMap
            {
                Tier = tier, Cube = cube, Hash = t.CasterHash,
                Range = t.NativeCacheRange, Center = t.Center, At = Time.time
            };
        }

        // Kept maps are stored at the smallest size (a 1024 px map is 12 MB, a 128 px one 0.19 MB),
        // at most as many as lights can hold maps at once.
        private const int KeptTier = TierCount - 1, MaxKept = MaxSlots;
        private bool CanShrink => CombineShader != null && CombineShader.passCount >= 6;
        private static readonly int KeepSrcId = Shader.PropertyToID("_WireKeepSrc"), KeepSliceId = Shader.PropertyToID("_WireKeepSlice"),
            KeepFaceId = Shader.PropertyToID("_WireKeepFace"), KeepStepId = Shader.PropertyToID("_WireKeepStep"),
            KeepSizeId = Shader.PropertyToID("_WireKeepSize");
        private CommandBuffer _keepCb;

        // A map's six faces into a small cube (combine pass 5: each texel the nearest of its block).
        private void ShrinkInto(int tier, int cube, int small)
        {
            if (_combine == null)
                _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
            if (_keepCb == null) _keepCb = new CommandBuffer { name = "Wire's Shadows kept map" };
            _keepCb.Clear();
            _keepCb.SetGlobalTexture(KeepSrcId, _pool[tier]);
            _keepCb.SetGlobalFloat(KeepSliceId, cube);
            _keepCb.SetGlobalFloat(KeepStepId, TierRes(tier) / TierRes(KeptTier));
            _keepCb.SetGlobalFloat(KeepSizeId, TierRes(KeptTier));
            for (int f = 0; f < 6; f++)
            {
                _keepCb.SetRenderTarget(new RenderTargetIdentifier(_pool[KeptTier], 0, CubemapFace.Unknown, small * 6 + f));
                _keepCb.SetGlobalFloat(KeepFaceId, f);
                _keepCb.DrawProcedural(Matrix4x4.identity, _combine, 5, MeshTopology.Triangles, 3);
            }
            Graphics.ExecuteCommandBuffer(_keepCb);
        }

        // A render about to start: its kept map instead, if nothing in its reach changed.
        private bool TryReuseKept(Tracked t, Vector3 center, float range, int hash)
        {
            int key = KeepKey(center, range);
            if (!_kept.TryGetValue(key, out KeptMap k)) return false;
            _kept.Remove(key);
            if (k.Hash != hash || t.Slot < 0) { FreeCube(k.Tier, k.Cube); return false; }
            if (_mapTier[t.Slot] >= 0f) FreeCube((int)_mapTier[t.Slot], (int)_mapCube[t.Slot]);
            _mapTier[t.Slot] = k.Tier;
            _mapCube[t.Slot] = k.Cube;
            t.Center = k.Center;
            t.NativeCacheRange = k.Range;
            t.CasterHash = hash;
            return true;
        }

        // A new map needs room, or there are as many kept as allowed: the oldest kept map goes (in
        // that pool, or any for tier -1).
        private bool DropOldestKept(int tier)
        {
            if (!OldestKept(tier, out int best)) return false;
            KeptMap k = _kept[best];
            _kept.Remove(best);
            FreeCube(k.Tier, k.Cube);
            return true;
        }

        // tier -1: any pool
        private bool OldestKept(int tier, out int key)
        {
            key = 0;
            bool found = false;
            float at = float.MaxValue;
            foreach (var kv in _kept)
                if ((tier < 0 || kv.Value.Tier == tier) && kv.Value.At < at)
                {
                    at = kv.Value.At; key = kv.Key; found = true;
                }
            return found;
        }

    }
}
