using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Cached shadow maps sized by what they cover on screen, as game engines size theirs: a map
    // needs about as many texels across a cube face as the light's reach is wide on screen in
    // pixels (a face sees the reach over 90 degrees, its width at the edge of the reach). So each
    // light gets the smallest of four sizes (the game's point-light size, then half, quarter,
    // eighth) that covers that, the full size whenever the camera is inside its reach. Measured
    // from its distance alone, as if in view, so turning round never shows a map being swapped.
    //
    // Maps of one size share a cube array (a pool) that grows as needed, within the memory left for
    // them. A light that needs a different size renders into a new cube while its old map stays on
    // screen, then switches over, the old cube freed. A light only gets smaller when there isn't
    // memory for another light's bigger map (smaller doesn't save anything otherwise).
    //
    // Needs the lighting shader that reads these pools (it declares _WireTieredShader) and native depth;
    // otherwise the near/far arrays stay as before.
    internal sealed partial class ShadowCache
    {
        internal static Shader LightingShader;
        internal const int TierCount = 4;
        private bool _tiered;
        private readonly RenderTexture[] _pool = new RenderTexture[TierCount];
        private readonly List<bool>[] _poolUsed = { new List<bool>(), new List<bool>(), new List<bool>(), new List<bool>() };
        private readonly float[] _mapTier = new float[MaxSlots];
        private readonly float[] _mapCube = new float[MaxSlots];
        private long _poolBudget;
        private static readonly int[] TierIds =
        {
            Shader.PropertyToID("_WireTier0"), Shader.PropertyToID("_WireTier1"),
            Shader.PropertyToID("_WireTier2"), Shader.PropertyToID("_WireTier3")
        };
        private static readonly int MapTierId = Shader.PropertyToID("_WireMapTier");
        private static readonly int MapCubeId = Shader.PropertyToID("_WireMapCube");
        private static readonly int TierResId = Shader.PropertyToID("_WireTierRes");
        private static readonly int TieredId = Shader.PropertyToID("_WireTiered");

        private int TierRes(int tier) => Mathf.Max(1, _resolution >> tier);
        private long CubeBytes(int tier) { long r = TierRes(tier); return 6L * r * r * 2; } // 16-bit depth

        private bool _tiersFailed; // couldn't make the pools once: the game's own shadows from then on
        private bool WantTiered(bool nativeOnly) =>
            nativeOnly && !_tiersFailed && LightingShader != null && LightingShader.FindPropertyIndex("_WireTieredShader") >= 0;

        // From EnsureResources.
        private bool SetUpTiers(long budget)
        {
            ReleaseTiers();
            _poolBudget = budget;
            for (int k = 0; k < TierCount; k++)
            {
                // one cube to start with, so every pool is bound
                _poolUsed[k].Clear();
                if (!GrowPool(k, 1, true)) return false;
            }
            for (int s = 0; s < MaxSlots; s++) { _mapTier[s] = -1f; _mapCube[s] = -1f; }
            Shader.SetGlobalVector(TierResId, new Vector4(TierRes(0), TierRes(1), TierRes(2), TierRes(3)));
            Shader.SetGlobalFloat(TieredId, 1f);
            MakeOnePassFaces();
            Plugin.Log.LogInfo("Shadow maps sized by screen: " + TierRes(0) + "/" + TierRes(1) + "/" + TierRes(2) + "/" + TierRes(3) +
                " px, up to " + (_poolBudget >> 20) + " MB for them.");
            return true;
        }

        private void ReleaseTiers()
        {
            _kept.Clear(); // their pools go below
            ReleaseOnePassFaces();
            for (int k = 0; k < TierCount; k++)
            {
                if (_pool[k] != null) { _pool[k].Release(); Destroy(_pool[k]); _pool[k] = null; }
                _poolUsed[k].Clear();
            }
            for (int s = 0; s < MaxSlots; s++) { _mapTier[s] = -1f; _mapCube[s] = -1f; }
            Shader.SetGlobalFloat(TieredId, 0f);
        }

        private long PoolBytes()
        {
            long sum = 0;
            for (int k = 0; k < TierCount; k++) sum += _poolUsed[k].Count * CubeBytes(k);
            return sum;
        }

        // Grows a pool to hold `cubes`, keeping the cubes in use. False if over the memory left.
        private bool GrowPool(int tier, int cubes, bool force = false)
        {
            int old = _poolUsed[tier].Count;
            if (cubes <= old) return true;
            if (!force && PoolBytes() + (cubes - old) * CubeBytes(tier) > _poolBudget) return false;
            int r = TierRes(tier);
            var next = new RenderTexture(r, r, NativeDepthBits, RenderTextureFormat.Shadowmap)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * cubes,
                useMipMap = false, filterMode = FilterMode.Bilinear, name = "WireShadowPool" + r
            };
            if (!next.Create()) { next.Release(); Destroy(next); return false; }
            RenderTexture prev = _pool[tier];
            if (prev != null)
            {
                for (int c = 0; c < old; c++)
                    if (_poolUsed[tier][c])
                        for (int f = 0; f < 6; f++)
                            Graphics.CopyTexture(prev, c * 6 + f, 0, next, c * 6 + f, 0);
                prev.Release(); Destroy(prev);
            }
            _pool[tier] = next;
            while (_poolUsed[tier].Count < cubes) _poolUsed[tier].Add(false);
            Shader.SetGlobalTexture(TierIds[tier], next);
            Plugin.Note("[tiers] " + r + " px pool: " + cubes + " cubes, all pools " + (PoolBytes() >> 20) + " MB");
            return true;
        }

        // After each reassignment: each pool grown once to hold every map waiting to be rendered
        // into it, not doubled again and again as they come (each growth is a new texture and a
        // copy of every map in it; arriving at a base that was several in a few seconds).
        private readonly int[] _waiting = new int[TierCount];
        private void SizePoolsForWaiting()
        {
            System.Array.Clear(_waiting, 0, TierCount);
            foreach (Tracked t in _slots)
                if (t != null && t.Light != null && t.Dirty && t.RenderCube < 0)
                    _waiting[Mathf.Clamp(t.WantTier, 0, TierCount - 1)]++;
            for (int k = 0; k < TierCount; k++)
            {
                if (_waiting[k] == 0) continue;
                int used = 0;
                foreach (bool u in _poolUsed[k]) if (u) used++;
                int need = used + _waiting[k];
                if (need > _poolUsed[k].Count) GrowPool(k, need); // over the memory share: AllocCube falls back as before
            }
        }

        // From the 1 s reassignment, with nothing rendering: a pool at least a quarter empty is made
        // again at the size it uses, its maps packed together (copied on the GPU) and everything
        // pointing at them (lights on screen, kept maps) moved along. Re-renders go into new cubes
        // while the old ones stay on screen, so a burst of them (arriving at a base, a setting
        // changed) grows the pools far past what they hold once it's over.
        private void CompactPools()
        {
            foreach (Tracked t in _slots)
                if (t != null && (t.Face >= 0 || t.RenderCube >= 0 || t.Dirty || t.PromotionFrom != null || t.PromotionTo != null))
                    return;
            bool packed = false;
            for (int k = 0; k < TierCount; k++)
            {
                List<bool> used = _poolUsed[k];
                int n = used.Count, u = 0;
                foreach (bool b in used) if (b) u++;
                int size = Mathf.Max(1, u);
                if (n - size < Mathf.Max(1, n / 4)) continue;
                int r = TierRes(k);
                var next = new RenderTexture(r, r, NativeDepthBits, RenderTextureFormat.Shadowmap)
                {
                    dimension = TextureDimension.CubeArray, volumeDepth = 6 * size,
                    useMipMap = false, filterMode = FilterMode.Bilinear, name = "WireShadowPool" + r
                };
                if (!next.Create()) { next.Release(); Destroy(next); continue; }
                // old cube -> new cube
                int[] moved = new int[n];
                int at = 0;
                for (int c = 0; c < n; c++)
                {
                    moved[c] = -1;
                    if (!used[c]) continue;
                    for (int f = 0; f < 6; f++)
                        Graphics.CopyTexture(_pool[k], c * 6 + f, 0, next, at * 6 + f, 0);
                    moved[c] = at++;
                }
                _pool[k].Release(); Destroy(_pool[k]);
                _pool[k] = next;
                used.Clear();
                for (int c = 0; c < size; c++) used.Add(c < u);
                Shader.SetGlobalTexture(TierIds[k], next);
                for (int sl = 0; sl < MaxSlots; sl++)
                    if ((int)_mapTier[sl] == k && _mapCube[sl] >= 0f) _mapCube[sl] = moved[(int)_mapCube[sl]];
                KeptMoved.Clear();
                foreach (var kv in _kept) if (kv.Value.Tier == k) KeptMoved.Add(kv.Key);
                foreach (int key in KeptMoved) { KeptMap m = _kept[key]; m.Cube = moved[m.Cube]; _kept[key] = m; }
                packed = true;
                Plugin.Note("[tiers] " + r + " px pool: packed to " + size + " cubes, all pools " + (PoolBytes() >> 20) + " MB");
            }
            if (packed) UploadTierMaps();
        }
        private static readonly List<int> KeptMoved = new List<int>();

        private int AllocCube(int tier)
        {
            List<bool> used = _poolUsed[tier];
            for (int c = 0; c < used.Count; c++)
                if (!used[c]) { used[c] = true; return c; }
            // one more, not double: a pool grown ahead of a burst (SizePoolsForWaiting) rarely
            // needs this, and doubling left it twice the size it needed (1.8 GB) for the session
            int n = used.Count;
            if (GrowPool(tier, n + 1))
            {
                used[n] = true;
                return n;
            }
            // no memory left: kept maps of unloaded lights make room, oldest first (Keep)
            if (DropOldestKept(tier))
                for (int c = 0; c < used.Count; c++)
                    if (!used[c]) { used[c] = true; return c; }
            return -1;
        }

        private void FreeCube(int tier, int cube)
        {
            if (tier < 0 || tier >= TierCount || cube < 0 || cube >= _poolUsed[tier].Count) return;
            _poolUsed[tier][cube] = false;
        }

        // The size a light's map needs: its reach's width on screen in pixels.
        private int WantTier(Tracked t, Camera cam)
        {
            if (cam == null || t.Light == null) return 0;
            float range = Range(t);
            Vector3 lp = t.Light.transform.position;
            float dist = Vector3.Distance(lp, cam.transform.position);
            if (dist <= range) return 0; // inside its reach: its shadows can fill the screen
            // One map texel no bigger than one screen pixel where its shadows come nearest the
            // camera: at the edge of its reach (a texel there is range x 2 / size wide, a pixel
            // at that distance (dist - range) x 2 tan(fov/2) / screen height). Measured from the
            // light's centre it came out a size too small for shadows between you and the light.
            // Out of view it's sized as if you looked at it, so turning round doesn't show a
            // small map being swapped for a bigger one.
            float px = range * cam.pixelHeight / ((dist - range) * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad));
            int tier = 0;
            while (tier < TierCount - 1 && TierRes(tier + 1) >= px) tier++;
            return tier;
        }

        // A render starting: a new cube of the size it wants (or the next smaller one with room).
        // -1: none free and no memory to grow.
        private bool StartTierRender(Tracked t)
        {
            for (int k = Mathf.Clamp(t.WantTier, 0, TierCount - 1); k < TierCount; k++)
            {
                int c = AllocCube(k);
                if (c < 0)
                {
                    if (k == t.WantTier) ShrinkOversized(); // make room over the next renders
                    continue;
                }
                t.RenderTier = k;
                t.RenderCube = c;
                t.RenderRange = Mathf.Max(1f, Range(t));
                return true;
            }
            return false;
        }

        // Out of memory for a map: lights holding bigger maps than they need re-render smaller.
        private void ShrinkOversized()
        {
            foreach (Tracked o in _slots)
                if (o != null && o.Ready && !o.Dirty && o.Face < 0 && o.Slot >= 0 &&
                    _mapTier[o.Slot] >= 0f && (int)_mapTier[o.Slot] < o.WantTier)
                {
                    o.Dirty = true;
                    o.DirtyAt = Time.time;
                }
        }

        // Each finished render: the new cube on screen, the old one freed.
        private void PublishTierMap(Tracked t)
        {
            int sl = t.Slot;
            if (sl < 0 || t.RenderCube < 0) return;
            if (_mapTier[sl] >= 0f) FreeCube((int)_mapTier[sl], (int)_mapCube[sl]);
            _mapTier[sl] = t.RenderTier;
            _mapCube[sl] = t.RenderCube;
            t.NativeCacheRange = t.RenderRange;
            t.CasterHash = t.PendingHash;
            t.RenderCube = -1;
        }

        private void DropTierRender(Tracked t)
        {
            if (t.RenderCube >= 0) FreeCube(t.RenderTier, t.RenderCube);
            t.RenderCube = -1;
        }

        private void FreeSlotMap(int sl)
        {
            if (sl < 0 || sl >= MaxSlots) return;
            if (_mapTier[sl] >= 0f) FreeCube((int)_mapTier[sl], (int)_mapCube[sl]);
            _mapTier[sl] = -1f;
            _mapCube[sl] = -1f;
        }


        private void UploadTierMaps()
        {
            for (int sl = 0; sl < MaxSlots; sl++)
            {
                Tracked t = sl < _slots.Length ? _slots[sl] : null;
                bool ready = t != null && t.Ready && t.Light != null && _mapTier[sl] >= 0f;
                _nativeActive[sl] = 0f; // the near/far arrays aren't used
                _nativeInvRanges[sl] = ready ? 1f / t.NativeCacheRange : 0f;
            }
            Shader.SetGlobalFloatArray(MapTierId, _mapTier);
            Shader.SetGlobalFloatArray(MapCubeId, _mapCube);
            Shader.SetGlobalFloatArray(NativeActiveId, _nativeActive);
            Shader.SetGlobalFloatArray(NativeRangesId, _nativeInvRanges);
        }
    }
}
