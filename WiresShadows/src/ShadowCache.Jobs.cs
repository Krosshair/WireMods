using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresShadows
{
    // Physics queries on Unity's worker threads instead of the main thread.
    //
    // Can a light see a moving thing (SeenByLight's five lines from the light to it, against the
    // pieces, rocks and terrain the cached maps already hold): asked each frame for every moving
    // thing in every light, now cast together as one batch and read the next frame. A thing that
    // steps out from behind a wall is drawn into that light's moving map a frame later; one not
    // asked before counts as seen.
    //
    // A light's surroundings for a full map (the colliders in its reach, FindCasters): searched a
    // frame ahead on the workers when the map is due, and the render starts the frame after with
    // them. A redraw for something you did (the faces it changed) still searches at once: it's
    // shown that frame.
    internal sealed partial class ShadowCache
    {
        // ---- Line of sight for moving shadows ----
        private struct SeenAsk { internal long Key; }
        private static readonly List<SeenAsk> s_seenAsks = new List<SeenAsk>(128);
        private static readonly List<RaycastCommand> s_seenQueue = new List<RaycastCommand>(640);
        private static Dictionary<long, bool> s_seenLast = new Dictionary<long, bool>(256), s_seenNext = new Dictionary<long, bool>(256);
        private static NativeArray<RaycastCommand> s_seenCmds;
        private static NativeArray<RaycastHit> s_seenHits;
        private static SeenAsk[] s_seenFlight = new SeenAsk[128];
        private static int s_seenFlightCount;
        private static JobHandle s_seenJob;
        private static bool s_seenPending;

        // What the light saw of it last frame (seen if not asked before); asks again for this frame.
        private static bool SeenCached(int slot, Group g, Vector3 lp)
        {
            Vector3 d = g.Center - lp;
            float dist = d.magnitude;
            if (dist < .6f) return true;
            Renderer first = g.Count > 0 ? Casters[g.Start].R : null;
            long key = ((long)slot << 32) | (uint)(first != null ? first.GetInstanceID() : g.Hash);
            Vector3 dir = d / dist;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(side, dir);
            float r = g.Radius * .8f;
            Vector3 from = lp + dir * .35f;
            var q = new QueryParameters(Occluders, false, QueryTriggerInteraction.Ignore, false);
            Line(from, g.Center, q);
            Line(from, g.Center + up * r, q);
            Line(from, g.Center - up * r, q);
            Line(from, g.Center + side * r, q);
            Line(from, g.Center - side * r, q);
            s_seenAsks.Add(new SeenAsk { Key = key });
            return !s_seenLast.TryGetValue(key, out bool seen) || seen;
        }

        private static void Line(Vector3 from, Vector3 to, QueryParameters q)
        {
            Vector3 d = to - from;
            float m = d.magnitude;
            s_seenQueue.Add(m > 1e-4f ? new RaycastCommand(from, d / m, q, m) : new RaycastCommand(from, Vector3.up, q, 0f));
        }

        // Start of the frame's work: last frame's answers in.
        private static void SeenRead()
        {
            if (!s_seenPending) return;
            s_seenJob.Complete();
            s_seenPending = false;
            s_seenNext.Clear();
            for (int i = 0; i < s_seenFlightCount; i++)
            {
                bool seen = false;
                for (int k = 0; k < 5 && !seen; k++)
                    seen = s_seenHits[i * 5 + k].colliderInstanceID == 0; // a line got through
                s_seenNext[s_seenFlight[i].Key] = seen;
            }
            var swap = s_seenLast; s_seenLast = s_seenNext; s_seenNext = swap;
        }

        // End of the frame's work: this frame's lines to the workers.
        private static void SeenSchedule()
        {
            int n = s_seenAsks.Count;
            if (n == 0 || s_seenPending) { s_seenAsks.Clear(); s_seenQueue.Clear(); return; }
            int c = s_seenQueue.Count;
            if (!s_seenCmds.IsCreated || s_seenCmds.Length < c)
            {
                if (s_seenCmds.IsCreated) { s_seenCmds.Dispose(); s_seenHits.Dispose(); }
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, c));
                s_seenCmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                s_seenHits = new NativeArray<RaycastHit>(cap, Allocator.Persistent);
            }
            for (int i = 0; i < c; i++) s_seenCmds[i] = s_seenQueue[i];
            if (s_seenFlight.Length < n) System.Array.Resize(ref s_seenFlight, Mathf.NextPowerOfTwo(n));
            for (int i = 0; i < n; i++) s_seenFlight[i] = s_seenAsks[i];
            s_seenFlightCount = n;
            s_seenAsks.Clear(); s_seenQueue.Clear();
            s_seenJob = RaycastCommand.ScheduleBatch(s_seenCmds.GetSubArray(0, c), s_seenHits.GetSubArray(0, c), 16, 1);
            JobHandle.ScheduleBatchedJobs();
            s_seenPending = true;
        }

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
            if (s_seenPending) { s_seenJob.Complete(); s_seenPending = false; }
            if (s_searchPending) { s_searchJob.Complete(); s_searchPending = false; }
            if (s_seenCmds.IsCreated) { s_seenCmds.Dispose(); s_seenHits.Dispose(); }
            if (s_searchCmds.IsCreated) { s_searchCmds.Dispose(); s_searchHits.Dispose(); }
            s_seenLast.Clear(); s_seenNext.Clear(); s_seenAsks.Clear(); s_seenQueue.Clear();
            s_searches.Clear(); s_searchAsks.Clear(); s_preps.Clear();
        }
    }
}
