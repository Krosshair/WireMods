using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresShadows
{
    // Physics and sorting off the main thread's frame. A light's surroundings for a full map (the
    // colliders in its reach, FindCasters): searched a frame ahead on the workers when the map is
    // due, then sorted a little each frame; the render starts once both are done. A redraw for
    // something you did still searches at once: it's shown that frame.
    internal sealed partial class ShadowCache
    {
        // ---- A light's surroundings, a frame ahead ----
        private const int SearchHits = 4096; // colliders per light per search; more: searched the old way
        private sealed class Search
        {
            internal Vector3 Center; internal float Range;
            internal int Count = -1; // -1: on its way; -2: too many (the old way)
            internal Collider[] Found;
        }
        private static readonly Dictionary<Tracked, Search> s_searches = new Dictionary<Tracked, Search>();
        private static readonly List<Tracked> s_searchAsks = new List<Tracked>(8);
        private static Tracked[] s_searchFlight = new Tracked[8];
        private static int s_searchFlightCount;
        private static NativeArray<OverlapSphereCommand> s_searchCmds;
        private static NativeArray<ColliderHit> s_searchHits;
        private static JobHandle s_searchJob;
        private static bool s_searchPending;

        // A full map is due: its search ready (same place and reach)? If not, asked for now and the
        // render waits a frame.
        private static bool SearchReady(Tracked t)
        {
            Vector3 c = RestingPosition(t.Light);
            float r = Range(t);
            if (s_searches.TryGetValue(t, out Search s) && s.Count != -1 &&
                (s.Center - c).sqrMagnitude < 1e-6f && Mathf.Abs(s.Range - r) < 1e-3f) return true;
            if (s == null) s_searches[t] = s = new Search();
            if (s.Count == -1 && s_searchAsks.Contains(t)) return false; // already asked
            s.Center = c; s.Range = r; s.Count = -1;
            if (!s_searchAsks.Contains(t)) s_searchAsks.Add(t);
            return false;
        }

        // FindCasters: the colliders the workers found, once; -1 if there's none to use.
        private static int TakeSearch(Tracked t, Vector3 center, float range)
        {
            if (!s_searches.TryGetValue(t, out Search s) || s.Count < 0 ||
                (s.Center - center).sqrMagnitude > 1e-6f || Mathf.Abs(s.Range - range) > 1e-3f) return -1;
            int n = s.Count;
            if (Hits.Length < n) System.Array.Resize(ref Hits, Mathf.NextPowerOfTwo(n));
            System.Array.Copy(s.Found, Hits, n);
            s_searches.Remove(t);
            return n;
        }

        private static void SearchRead()
        {
            if (!s_searchPending) return;
            s_searchJob.Complete();
            s_searchPending = false;
            for (int i = 0; i < s_searchFlightCount; i++)
            {
                Tracked t = s_searchFlight[i];
                s_searchFlight[i] = null;
                if (t == null || !s_searches.TryGetValue(t, out Search s)) continue;
                int n = 0, at = i * SearchHits;
                while (n < SearchHits && s_searchHits[at + n].instanceID != 0) n++;
                if (n >= SearchHits) { s.Count = -2; continue; } // more than fits: the old way
                if (s.Found == null || s.Found.Length < n) s.Found = new Collider[Mathf.NextPowerOfTwo(Mathf.Max(64, n))];
                for (int k = 0; k < n; k++) s.Found[k] = s_searchHits[at + k].collider;
                s.Count = n;
            }
            s_searchFlightCount = 0;
        }

        private static void SearchSchedule()
        {
            int n = s_searchAsks.Count;
            if (n == 0 || s_searchPending) return;
            if (!s_searchCmds.IsCreated || s_searchCmds.Length < n)
            {
                if (s_searchCmds.IsCreated) { s_searchCmds.Dispose(); s_searchHits.Dispose(); }
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(8, n));
                s_searchCmds = new NativeArray<OverlapSphereCommand>(cap, Allocator.Persistent);
                s_searchHits = new NativeArray<ColliderHit>(cap * SearchHits, Allocator.Persistent);
            }
            if (s_searchFlight.Length < n) System.Array.Resize(ref s_searchFlight, Mathf.NextPowerOfTwo(n));
            var q = new QueryParameters(~0, false, QueryTriggerInteraction.Collide, false);
            for (int i = 0; i < n; i++)
            {
                Tracked t = s_searchAsks[i];
                Search s = s_searches[t];
                s_searchCmds[i] = new OverlapSphereCommand(s.Center, s.Range, q);
                s_searchFlight[i] = t;
                // the hits buffer is reused: the slots after a search's last hit must read empty
                for (int k = i * SearchHits, end = k + SearchHits; k < end; k++) s_searchHits[k] = default;
            }
            s_searchFlightCount = n;
            s_searchAsks.Clear();
            s_searchJob = OverlapSphereCommand.ScheduleBatch(s_searchCmds.GetSubArray(0, n), s_searchHits.GetSubArray(0, n * SearchHits), 1, SearchHits);
            JobHandle.ScheduleBatchedJobs();
            s_searchPending = true;
        }

        // ---- A full map's casters sorted over frames ----
        // After the search, each object in reach is sorted into the light's lists: a few to a few
        // hundred objects, each a look through its renderers. For a full map that's done a little
        // each frame into a copy (its old map and lists stay in use meanwhile), at most PrepBudget a
        // frame for all lights together, and the render starts once it's done.
        private static readonly long PrepBudget = System.Diagnostics.Stopwatch.Frequency / 2000; // 0.5 ms a frame, picked
        private static readonly Dictionary<Tracked, Prep> s_preps = new Dictionary<Tracked, Prep>();
        private static int s_prepFrame = -1;
        private static long s_prepUntil;

        // True once this light's casters are gathered (into its lists); false: still going, try next frame.
        private static bool Prepared(Tracked t)
        {
            Vector3 at = RestingPosition(t.Light);
            if (!s_preps.TryGetValue(t, out Prep pr) || (pr.Center - at).sqrMagnitude > 1e-6f)
            {
                // a copy of the light for its lists; the search comes from the workers (SearchReady)
                var copy = new Tracked
                {
                    Light = t.Light, Lod = t.Lod, Item = t.Item, Tiny = t.Tiny, Stand = t.Stand,
                    OriginalShadows = t.OriginalShadows, OriginalShadowLod = t.OriginalShadowLod, Center = at
                };
                pr = Begin(t, copy);
                s_preps[t] = pr;
            }
            if (s_prepFrame != Time.frameCount) { s_prepFrame = Time.frameCount; s_prepUntil = System.Diagnostics.Stopwatch.GetTimestamp() + PrepBudget; }
            if (System.Diagnostics.Stopwatch.GetTimestamp() > s_prepUntil) return false; // this frame's share used up
            if (!Step(pr, s_prepUntil)) return false;
            s_preps.Remove(t);
            End(pr, t, false);
            return true;
        }

        private static void JobsShutdown()
        {
            if (s_searchPending) { s_searchJob.Complete(); s_searchPending = false; }
            if (s_searchCmds.IsCreated) { s_searchCmds.Dispose(); s_searchHits.Dispose(); }
            s_searches.Clear(); s_searchAsks.Clear(); s_preps.Clear();
        }
    }
}
