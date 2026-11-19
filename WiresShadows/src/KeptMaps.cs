using System.Collections.Generic;
using UnityEngine;

namespace WiresShadows
{
    // A light that loses its slot keeps its map (its cube stays in the pool) for when it gets a slot
    // again: then that map is shown at once instead of drawn anew, as long as nothing round it changed
    // and the light is where it was. In a big base the slots go round: each light that came back used
    // to cost a whole map (six faces, ~5 ms of CPU each, measured). A kept map only uses room the
    // pool has anyway: it gives its cube up to the first new map that finds no free one.
    internal sealed partial class ShadowCache
    {
        // The last changes to what's round the lights (a piece placed or broken, a door, food, an item
        // on a stand): where, and how far. A kept map near one is out of date.
        private struct Change { internal Vector3 Center; internal float Radius; }
        private static readonly Change[] s_changes = new Change[256];
        private static int s_changeCount; // all ever noted; the array holds the last 256
        private readonly List<Tracked> _kept = new List<Tracked>();

        private static void NoteChange(Vector3 center, float radius)
        {
            s_changes[s_changeCount & (s_changes.Length - 1)] = new Change { Center = center, Radius = radius };
            s_changeCount++;
        }

        // Release: keep the map instead of freeing it, if it's a finished one with nothing waiting on it.
        private bool KeepMap(Tracked t)
        {
            int sl = t.Slot;
            if (sl < 0 || sl >= MaxSlots || _mapTier[sl] < 0f || t.Light == null || !t.Ready || t.Dirty ||
                t.DoorFaces != 0 || t.Tiny || Movable(t) ||
                !ByLight.TryGetValue(t.Light, out Tracked own) || own != t) // a promotion's stand-in: never taken again
                return false;
            DropKept(t);
            t.KeptTier = (int)_mapTier[sl];
            t.KeptCube = (int)_mapCube[sl];
            t.KeptChange = s_changeCount;
            _mapTier[sl] = -1f;
            _mapCube[sl] = -1f;
            _kept.Add(t);
            return true;
        }

        // Take: its kept map onto the new slot, if it still shows what's there.
        private bool ReuseKept(Tracked t, int slot)
        {
            if (t.KeptCube < 0) return false;
            float range = Range(t);
            bool good = s_changeCount - t.KeptChange <= s_changes.Length && t.Light != null &&
                (RestingPosition(t.Light) - t.Center).sqrMagnitude < 1e-4f && Mathf.Abs(Mathf.Max(1f, range) - t.NativeCacheRange) < 0.01f;
            for (int i = t.KeptChange; good && i < s_changeCount; i++)
            {
                Change c = s_changes[i & (s_changes.Length - 1)];
                float reach = range + c.Radius;
                if ((c.Center - t.Center).sqrMagnitude <= reach * reach) good = false;
            }
            if (!good) { DropKept(t); return false; }
            _mapTier[slot] = t.KeptTier;
            _mapCube[slot] = t.KeptCube;
            t.KeptTier = t.KeptCube = -1;
            _kept.Remove(t);
            return true;
        }

        private void DropKept(Tracked t)
        {
            if (t == null || t.KeptCube < 0) return;
            FreeCube(t.KeptTier, t.KeptCube);
            t.KeptTier = t.KeptCube = -1;
            _kept.Remove(t);
        }

        private void DropAllKept()
        {
            foreach (Tracked t in _kept)
            {
                FreeCube(t.KeptTier, t.KeptCube);
                t.KeptTier = t.KeptCube = -1;
            }
            _kept.Clear();
        }

        // A new map with no free cube of its size: the oldest kept map of that size gives its own.
        private int TakeKeptCube(int tier)
        {
            for (int i = 0; i < _kept.Count; i++)
            {
                Tracked o = _kept[i];
                if (o.KeptTier != tier) continue;
                int cube = o.KeptCube;
                o.KeptTier = o.KeptCube = -1;
                _kept.RemoveAt(i);
                return cube; // still marked used: it's the new map's now
            }
            return -1;
        }
    }
}
