using UnityEngine;

namespace WiresAcoustics
{
    // An intentionally small main-thread index of COMPLETE wall-edge probes.
    // Raycasts remain on the existing Unity worker jobs. An entry is only
    // copied after every probe finishes, never shared while mutable.
    // Same-wall identity is deliberately conservative (near same hit point
    // and normal), avoiding the assumption that separated windows or corners
    // belong to a single infinite wall plane.
    internal static class SharedEdges
    {
        private const int Capacity = 128;
        private const float HitTolerance = .08f; // smaller than a single .2m edge-probe step
        private const float NormalTolerance = .99f;
        private sealed class Entry
        {
            internal int Generation, World;
            internal Vector3 At, Normal;
            internal float Span;
            internal readonly Vector3[] Edges = new Vector3[Corners.Dirs];
            internal readonly bool[] Has = new bool[Corners.Dirs];
        }
        private static readonly Entry[] Entries = new Entry[Capacity];
        private static int s_next, s_used, s_world;
        internal static int Hits, Misses, Published;

        internal static void Clear()
        {
            for (int i = 0; i < Entries.Length; i++) Entries[i] = null;
            s_next = s_used = 0;
            s_world++;
            Hits = Misses = Published = 0;
        }

        // Cached probes never substitute for the three rays on the current
        // direct or indirect listener-source segment; those remain validated.
        internal static bool TryCopy(Corners.Record into, Vector3 at, Vector3 n,
                                     float requiredSpan)
        {
            for (int pass = 0; pass < s_used; pass++)
            {
                int index = (s_next - 1 - pass + Capacity) % Capacity;
                Entry e = Entries[index];
                if (e == null || e.World != s_world ||
                    e.Span + .001f < requiredSpan ||
                    Vector3.Dot(e.Normal, n) < NormalTolerance ||
                    (e.At - at).sqrMagnitude > HitTolerance * HitTolerance)
                    continue;
                if (Occlusion.ChangedNear(e.Generation, e.At, e.At,
                                          e.Span + Occlusion.SameWall))
                {
                    Entries[index] = null;
                    continue;
                }

                into.Valid = true;
                into.At = e.At;
                into.N = e.Normal;
                into.Span = e.Span;
                into.Gen = e.Generation;
                into.Pending = 0;
                into.Version++; // invalidate stale per-sound in-flight probes
                for (int k = 0; k < Corners.Dirs; k++)
                {
                    into.Has[k] = e.Has[k];
                    into.Edges[k] = e.Edges[k];
                }
                Hits++;
                return true;
            }
            Misses++;
            return false;
        }

        internal static void Publish(Corners.Record rec)
        {
            if (rec == null || !rec.Valid || rec.Pending != 0 ||
                Occlusion.ChangedNear(rec.Gen, rec.At, rec.At,
                                      rec.Span + Occlusion.SameWall))
                return;
            Entry entry = Entries[s_next];
            if (entry == null) entry = Entries[s_next] = new Entry();
            entry.World = s_world;
            entry.Generation = rec.Gen;
            entry.At = rec.At;
            entry.Normal = rec.N;
            entry.Span = rec.Span;
            for (int k = 0; k < Corners.Dirs; k++)
            {
                entry.Has[k] = rec.Has[k];
                entry.Edges[k] = rec.Edges[k];
            }
            s_next = (s_next + 1) % Capacity;
            if (s_used < Capacity) s_used++;
            Published++;
        }

        internal static string Summary()
        {
            string report = "shared edges cache " + Hits + " reused, " +
                Misses + " misses, " + Published + " completed probe sets";
            Hits = Misses = Published = 0;
            return report;
        }
    }
}
