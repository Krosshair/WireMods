using System.Collections.Generic;
using UnityEngine;

namespace WiresShadows
{
    // Shadow maps outlive their lights. Leaving a base (a portal, walking off) unloads it and
    // destroys its lights; coming back, every light needed its map rendered again, a couple of
    // seconds of lag. Now a light's map is kept when it loses its slot, under where the light stands,
    // with a fingerprint of everything in its reach that can cast into it (the pieces and items by
    // their saved world id, where they stand, and each door's open or shut). A light that loads in
    // at the same spot with the same fingerprint gets its old map back without a render; anything
    // built, broken, moved or opened there since means a new one, as before.
    // Kept maps stay in the pools and count against the same memory share; when a new map needs the
    // room, the oldest kept ones go first. Ground dug or raised there while away isn't in the
    // fingerprint.
    internal sealed partial class ShadowCache
    {
        private struct KeptMap { internal int Tier, Cube, Hash; internal float Range; internal Vector3 Center; internal float At; }
        private readonly Dictionary<int, KeptMap> _kept = new Dictionary<int, KeptMap>();
        // Goes up whenever something that can cast into cached maps appears, goes, moves or opens
        // (pieces, dropped and placed items, doors): a light's fingerprint taken at the same number
        // is still right.
        internal static int WorldVersion;

        private static int KeepKey(Vector3 center, float range) => Quantize(center, 100f) * 31 + Mathf.RoundToInt(range * 100f);

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
                    if (c == null || c.isTrigger || ((1 << c.gameObject.layer) & StaticLayerMask) == 0) continue;
                    if (c.GetComponentInParent<Character>() != null) continue; // moving maps, not this one
                    ZNetView nv = c.GetComponentInParent<ZNetView>();
                    ZDO zdo = nv != null ? nv.GetZDO() : null;
                    int h = zdo != null ? zdo.m_uid.GetHashCode() : 0;
                    Transform tr = c.transform;
                    h = h * 31 + Quantize(tr.position, 100f);
                    h = h * 31 + Quantize(tr.rotation * Vector3.forward, 100f);
                    if (zdo != null && nv.GetComponent<Door>() != null) h = h * 31 + zdo.GetInt(ZDOVars.s_state);
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
            if (_kept.TryGetValue(key, out KeptMap old)) FreeCube(old.Tier, old.Cube);
            _kept[key] = new KeptMap
            {
                Tier = (int)_mapTier[sl], Cube = (int)_mapCube[sl], Hash = t.CasterHash,
                Range = t.NativeCacheRange, Center = t.Center, At = Time.time
            };
            _mapTier[sl] = -1f;
            _mapCube[sl] = -1f;
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

        // A new map needs room: the oldest kept map in its pool goes (a pool never gets smaller, so
        // only a cube freed in the same pool makes room).
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

        private void ClearKept()
        {
            foreach (var kv in _kept) FreeCube(kv.Value.Tier, kv.Value.Cube);
            _kept.Clear();
        }
    }
}
