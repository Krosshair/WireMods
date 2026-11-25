using System;
using System.Collections.Generic;

namespace EnemyHudRework
{
    // What only the machine running a creature knows (its bubble, its
    // status effects) is kept in the creature's network data, which is also
    // what shares it with other players. With Sync info off it goes into a
    // copy on this machine instead, read back the same way, so your own
    // view keeps working and nothing is shared.
    public sealed partial class EnemyHudReworkPlugin
    {
        private struct LocalKey : IEquatable<LocalKey>
        {
            public ZDOID Id; public int Key;
            public bool Equals(LocalKey o) { return Id == o.Id && Key == o.Key; }
            public override bool Equals(object o) { return o is LocalKey k && Equals(k); }
            public override int GetHashCode() { return Id.GetHashCode() * 397 ^ Key; }
        }

        private static readonly Dictionary<LocalKey, float> LocalFloats = new Dictionary<LocalKey, float>();
        private static readonly Dictionary<LocalKey, long> LocalLongs = new Dictionary<LocalKey, long>();
        private static readonly Dictionary<LocalKey, int> LocalInts = new Dictionary<LocalKey, int>();
        private static uint _localRevision;

        private static bool SyncOn => Instance == null || Instance._syncStaggerForMultiplayer.Value;
        private static bool LocalOnly(ZDO zdo) { return !SyncOn && zdo.IsOwner(); }
        private static LocalKey KeyOf(ZDO zdo, int key) { return new LocalKey { Id = zdo.m_uid, Key = key }; }

        // Entries for creatures that no longer exist (killed, unloaded) are
        // dropped once a copy grows this big, so it can't keep growing.
        private const int PruneAt = 256;
        private static readonly List<LocalKey> Gone = new List<LocalKey>();

        private static void Prune<T>(Dictionary<LocalKey, T> map)
        {
            if (ZDOMan.instance == null) return;
            Gone.Clear();
            foreach (LocalKey k in map.Keys)
                if (ZDOMan.instance.GetZDO(k.Id) == null)
                    Gone.Add(k);
            foreach (LocalKey k in Gone)
                map.Remove(k);
        }

        // Change counter for readers: the network data's own, plus ours.
        private static uint SharedRevision(ZDO zdo) { return zdo.DataRevision + _localRevision * 2654435761u; }

        private static float GetShared(ZDO zdo, int key, float fallback = 0f)
        {
            float v;
            if (LocalOnly(zdo)) return LocalFloats.TryGetValue(KeyOf(zdo, key), out v) ? v : fallback;
            return zdo.GetFloat(key, fallback);
        }

        private static long GetShared(ZDO zdo, int key, long fallback)
        {
            long v;
            if (LocalOnly(zdo)) return LocalLongs.TryGetValue(KeyOf(zdo, key), out v) ? v : fallback;
            return zdo.GetLong(key, fallback);
        }

        private static int GetSharedInt(ZDO zdo, int key)
        {
            int v;
            if (LocalOnly(zdo)) return LocalInts.TryGetValue(KeyOf(zdo, key), out v) ? v : 0;
            return zdo.GetInt(key);
        }

        private static void SetShared(ZDO zdo, int key, float value)
        {
            if (!LocalOnly(zdo)) { if (zdo.GetFloat(key) != value) zdo.Set(key, value); return; }
            LocalKey k = KeyOf(zdo, key);
            float old;
            if (LocalFloats.TryGetValue(k, out old) && old == value) return;
            if (value == 0f) LocalFloats.Remove(k);
            else
            {
                if (!LocalFloats.ContainsKey(k) && LocalFloats.Count >= PruneAt) Prune(LocalFloats);
                LocalFloats[k] = value;
            }
            _localRevision++;
        }

        private static void SetShared(ZDO zdo, int key, long value)
        {
            if (!LocalOnly(zdo)) { if (zdo.GetLong(key) != value) zdo.Set(key, value); return; }
            LocalKey k = KeyOf(zdo, key);
            long old;
            if (LocalLongs.TryGetValue(k, out old) && old == value) return;
            if (value == 0L) LocalLongs.Remove(k);
            else
            {
                if (!LocalLongs.ContainsKey(k) && LocalLongs.Count >= PruneAt) Prune(LocalLongs);
                LocalLongs[k] = value;
            }
            _localRevision++;
        }

        private static void SetSharedInt(ZDO zdo, int key, int value)
        {
            if (!LocalOnly(zdo)) { if (zdo.GetInt(key) != value) zdo.Set(key, value); return; }
            LocalKey k = KeyOf(zdo, key);
            int old;
            if (LocalInts.TryGetValue(k, out old) && old == value) return;
            if (value == 0) LocalInts.Remove(k);
            else
            {
                if (!LocalInts.ContainsKey(k) && LocalInts.Count >= PruneAt) Prune(LocalInts);
                LocalInts[k] = value;
            }
            _localRevision++;
        }
    }
}
