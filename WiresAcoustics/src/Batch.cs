using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Wall checks for sounds already playing (looping sounds when you, they or the buildings change, longer one-off sounds and
    // other placed sources twice a second, voice chat 5 times a second), and the paths round walls
    // for a sound that just started behind them, batched onto worker threads like the room rays:
    // queued during the frame, cast together, read the next frame. All hits along each
    // ray, up to 16. Every line is three rays, the line and two beside it, and what two of them
    // agree on counts (Occlusion.Blocking: a seam between pieces doesn't let a sound through). A sound that had walls in the way last time also gets one ray from each of the
    // biggest openings (Portals) to it, two lines per edge found round its walls, and the probes that
    // find those edges for next time (Corners).
    internal static class Batch
    {
        private const int MaxHits = Occlusion.MaxHits;
        private struct Req
        {
            internal AcousticFilter F;
            internal Vector3 To, Listener;
            internal float Loud; // how loud with nothing in the way, for the order (Tidy)
            internal bool Under, Reverb, Corners, Probe, Started; // Started: this check sent probes for its walls
            internal int Slot, Dir, K, Version; // a probe piece: which record, which way, how many, which pass
            internal Vector3 At, N;             // and where on which wall
            internal int First, Routes, EdgeN;
            internal Portals.Portal P0, P1;
        }
        private static readonly List<Req> Queue = new List<Req>(64);
        private static Req[] s_flight = new Req[64];
        private static Vector3[] s_edges = new Vector3[64 * Corners.MaxEdges]; // the edges each check tried
        private static int s_flightCount, s_applied;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        // Main-thread diagnostic counters, emitted in the existing room log.
        private static int s_direct, s_openings, s_cornerRays;
        private static int s_clear, s_blocked, s_portalWins, s_cornerWins, s_noPath, s_silentRaysSkipped;
        internal static string Summary()
        {
            string s = "paths rays direct " + s_direct + ", openings " + s_openings +
                ", corners " + s_cornerRays +
                "; checks clear " + s_clear + ", blocked " + s_blocked +
                ", chosen opening " + s_portalWins + ", corner " + s_cornerWins +
                ", through walls " + s_noPath + ", inaudible sound checks skipped " + s_silentRaysSkipped;
            s_direct = s_openings = s_cornerRays = 0;
            s_clear = s_blocked = s_portalWins = s_cornerWins = s_noPath = s_silentRaysSkipped = 0;
            return s;
        }

        internal static void Enqueue(AcousticFilter f, Vector3 to, bool under, bool reverb)
        {
            if (f == null) return;
            f.CheckedFrom = Hearing.At;
            f.CheckedAt = to;
            f.CheckedTime = Time.time;
            f.CheckedGen = Occlusion.WorldGen;
            // A placed sound too quiet to hear even with nothing in the way (under -60 dB, where
            // Budget mutes sounds) can't be heard however blocked it is: no check. Its direction and
            // fade keep updating, and it's checked again once it could be heard; its last state is
            // kept meanwhile, so nothing jumps.
            if ((f.Kind == AcousticFilter.Kinds.Sfx ||
                 f.Kind == AcousticFilter.Kinds.Other) &&
                f.Placed && Budget.Open(f) < .001f &&
                !AcousticFilter.GlobalActive)
            {
                s_silentRaysSkipped++;
                return;
            }
            // Your own sounds: nothing in the way (Occlusion.Mine), no rays.
            if (Occlusion.Mine(f.transform, f))
            {
                f.Set(under ? Underwater.Gain : 1f, under ? Underwater.Cutoff : 22000f, false);
                f.LastOcc = 0f;
                f.Routed = false;
                return;
            }
            // No wall processing requested: don't submit ANY geometry rays.
            // Underwater's attenuation still applies when necessary.
            if (!Plugin.Occlusion.Value)
            {
                f.Set(under ? Underwater.Gain : 1f,
                      under ? Underwater.Cutoff : 22000f, false);
                f.LastOcc = 0f;
                if (f.Records != null) foreach (Corners.Record rec in f.Records) rec.Valid = false;
                f.Routed = false;
                return;
            }
            Queue.Add(new Req { F = f, To = to, Under = under, Reverb = reverb,
                                Corners = f.LastOcc >= .3f });
        }

        // Probe rays per batch, at most (each probe is two short rays). A burst of sounds going
        // behind new walls at once (a door shut, walking into a base) is spread over the next frames
        // instead of all landing in one: a budget chosen for the game, not from a source.
        private const int ProbeBudget = 1024;
        private static readonly List<Req> Later = new List<Req>(16);

        // Before a batch is cast: each sound's check once (its latest: walking, every loop is queued
        // again each half metre, and while results were still being read the same sound piled up),
        // and the loudest first (as loud as it would reach you with nothing in the way, Budget.Open:
        // the machine you're stepping out to is the one muffled right now), the nearest of equally
        // loud ones first, so the results read within a frame's budget (Apply) are the ones you hear
        // change. At a base of 60 to 70 sounds the machine beside you could wait behind every far
        // one, a second and more. Probe pieces waiting from an earlier batch go first.
        private static readonly HashSet<AcousticFilter> Seen = new HashSet<AcousticFilter>();
        private static readonly System.Comparison<Req> Loudest = (a, b) =>
        {
            if (a.Probe != b.Probe) return a.Probe ? -1 : 1;
            int c = b.Loud.CompareTo(a.Loud);
            return c != 0 ? c : a.F.LastDist.CompareTo(b.F.LastDist);
        };
        private static void Tidy()
        {
            Seen.Clear();
            int keep = Queue.Count;
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                Req r = Queue[i];
                if (r.F == null || (!r.Probe && !Seen.Add(r.F))) { Queue[i] = Queue[--keep]; Queue.RemoveAt(keep); continue; }
                r.Loud = Budget.Open(r.F);
                Queue[i] = r;
            }
            Queue.Sort(Loudest);
        }

        // Every frame, from the game's audio update.
        internal static void Step(Vector3 listener)
        {
            if (s_pending)
            {
                // its raycasts still running on the worker threads: read next frame, not waited for
                if (!s_job.IsCompleted) return;
                s_job.Complete();
                s_pending = false;
                s_applied = 0;
            }
            // the last batch's results, as far as this frame's budget goes; the next batch is only
            // cast once they're all read (its hits go in the same buffer)
            if (s_flightCount > 0 && !Apply()) return;
            if (Queue.Count == 0 || Frame.Over) return; // the next batch only with time left this frame
            Tidy();
            int n = Queue.Count;
            int routes = Mathf.Min(Portals.Routes, Portals.List.Count);
            int cmds = 0, m = 0, budget = ProbeBudget;
            for (int i = 0; i < n; i++)
            {
                // out of this frame's time: the rest of the line waits for the next batch
                if (m > 0 && Frame.Over) { for (; i < n; i++) Later.Add(Queue[i]); break; }
                Req r = Queue[i];
                if (r.Probe)
                {
                    if (2 * r.K > budget && budget < ProbeBudget) { Later.Add(r); continue; }
                    budget -= 2 * r.K;
                    Fly(ref m, r);
                    cmds += 2 * r.K;
                    continue;
                }
                r.Routes = r.Corners ? routes : 0;
                r.EdgeN = 0;
                int edgesAt = m;
                if (r.Corners && r.F != null)
                {
                    AcousticFilter f = r.F;
                    if (f.Records == null) { f.Records = new Corners.Record[Corners.Slots]; for (int s = 0; s < Corners.Slots; s++) f.Records[s] = new Corners.Record(); }
                    Corners.Walls w = f.Walls;
                    // each wall in the way: a probe pass that covers it, or a slot to probe it into
                    int mN = w.Near ? Corners.Match(f.Records, w.NearAt, w.NearN) : -1;
                    int mF = w.Far ? Corners.Match(f.Records, w.FarAt, w.FarN) : -1;
                    if (w.Near && mN < 0)
                    {
                        mN = mF == 0 ? 1 : 0;
                        StartProbes(f, r, mN, w.NearAt, w.NearN, Corners.Probes(listener, w.NearAt, r.To, w.NearGain));
                        r.Started = true;
                    }
                    if (w.Far && mF < 0)
                    {
                        mF = mN == 0 ? 1 : 0;
                        StartProbes(f, r, mF, w.FarAt, w.FarN, Corners.Probes(listener, w.FarAt, r.To, w.FarGain));
                        r.Started = true;
                    }
                    // the edges found so far: listener -> edge -> sound
                    EnsureFlight(m + 1);
                    for (int s = 0; s < 2; s++)
                    {
                        int slot = s == 0 ? mN : mF;
                        if (slot < 0) continue;
                        Corners.Record rec = f.Records[slot];
                        for (int d = 0; d < Corners.Dirs; d++)
                            if (rec.Has[d]) s_edges[edgesAt * Corners.MaxEdges + r.EdgeN++] = rec.Edges[d];
                    }
                }
                Fly(ref m, r);
                cmds += 3 * (1 + r.Routes) + 6 * r.EdgeN;
            }
            Queue.Clear();
            Queue.AddRange(Later); // over a budget: next batch
            Later.Clear();
            if (m == 0) return;
            if (!s_cmds.IsCreated || s_cmds.Length < cmds)
            {
                if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(64, cmds));
                s_cmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(cap * MaxHits, Allocator.Persistent);
            }
            var q = new QueryParameters(Room.Mask, false, QueryTriggerInteraction.Ignore, false);
            int c = 0;
            for (int i = 0; i < m; i++)
            {
                Req r = s_flight[i];
                r.Listener = listener;
                r.First = c;
                if (r.Probe)
                {
                    Probe(r.At, r.N, r.Dir, r.K, q, ref c);
                    s_cornerRays += 2 * r.K;
                    s_flight[i] = r;
                    continue;
                }
                if (r.Routes > 0) r.P0 = Portals.List[0];
                if (r.Routes > 1) r.P1 = Portals.List[1];
                Cast3(listener, r.To, q, ref c);
                if (r.Routes > 0) Cast3(r.P0.Point, r.To, q, ref c);
                if (r.Routes > 1) Cast3(r.P1.Point, r.To, q, ref c);
                s_direct += 1;
                s_openings += r.Routes;
                for (int k = 0; k < r.EdgeN; k++)
                {
                    Vector3 e = s_edges[i * Corners.MaxEdges + k];
                    Cast3(listener, e, q, ref c);
                    Cast3(e, r.To, q, ref c);
                }
                s_cornerRays += 2 * r.EdgeN;
                s_flight[i] = r;
            }
            s_flightCount = m;
            s_job = RaycastCommand.ScheduleBatch(s_cmds.GetSubArray(0, c), s_hits.GetSubArray(0, c * MaxHits), 8, MaxHits);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void EnsureFlight(int n)
        {
            if (s_flight.Length >= n) return;
            int fl = Mathf.NextPowerOfTwo(Mathf.Max(64, n));
            System.Array.Resize(ref s_flight, fl);
            System.Array.Resize(ref s_edges, fl * Corners.MaxEdges);
        }

        private static void Fly(ref int m, Req r)
        {
            EnsureFlight(m + 1);
            s_flight[m++] = r;
        }

        // A wall not probed yet: its record set up now (so the next check doesn't probe it again),
        // and one small piece per way along it, each read as its own result (a whole pass at once
        // took up to 450 rays in one go, a quarter of a millisecond and more), in the line for the
        // next batch.
        private static void StartProbes(AcousticFilter f, Req check, int slot, Vector3 at, Vector3 n, int k)
        {
            Corners.Record rec = f.Records[slot];
            rec.Valid = true;
            rec.At = at; rec.N = n;
            rec.Span = k * Corners.Spacing;
            rec.Gen = Occlusion.WorldGen;
            rec.Version++;
            rec.Pending = Corners.Dirs;
            for (int d = 0; d < Corners.Dirs; d++)
            {
                rec.Has[d] = false;
                Later.Add(new Req { F = f, To = check.To, Under = check.Under, Reverb = check.Reverb, Probe = true,
                                    Slot = slot, Dir = d, K = k, At = at, N = n, Version = rec.Version });
            }
        }

        // One line as three rays: itself and the two beside it (Occlusion.Lines).
        private static void Cast3(Vector3 from, Vector3 to, QueryParameters q, ref int c)
        {
            Occlusion.Lines(from, to, out Vector3 side, out Vector3 up);
            s_cmds[c++] = Cast(from, to, q);
            s_cmds[c++] = Cast(from + side, to + side, q);
            s_cmds[c++] = Cast(from + up, to + up, q);
        }

        // Probes across a wall, one way along it: two rays through it, Twin either side of each point.
        private static void Probe(Vector3 at, Vector3 n, int dir, int count, QueryParameters q, ref int c)
        {
            Vector3 across = n * Occlusion.SameWall;
            Vector3 d = Corners.Dir(n, dir), twin = d * Occlusion.Twin;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Corners.Probe(at, d, i);
                s_cmds[c++] = Cast(p + across + twin, p - across + twin, q);
                s_cmds[c++] = Cast(p + across - twin, p - across - twin, q);
            }
        }

        // A probe piece read: the first open place that way along the wall, into its record (if the
        // record is still that pass). An edge found, or the wall's last piece read, checks the sound
        // again, so a gap is heard straight away.
        private static void ReadProbe(Req r)
        {
            AcousticFilter f = r.F;
            if (f.Records == null) return;
            Corners.Record rec = f.Records[r.Slot];
            if (rec.Version != r.Version || !rec.Valid) return;
            Vector3 d = Corners.Dir(r.N, r.Dir);
            int c = r.First;
            bool found = false;
            for (int i = 0; i < r.K; i++, c += 2)
            {
                if (!Open(c, r.To) || !Open(c + 1, r.To)) continue;
                rec.Edges[r.Dir] = Corners.Probe(r.At, d, i);
                rec.Has[r.Dir] = true;
                found = true;
                break;
            }
            rec.Pending--;
            if (found || rec.Pending <= 0)
                Queue.Add(new Req { F = f, To = r.To, Under = r.Under, Reverb = r.Reverb, Corners = true });
        }

        // Nothing solid (a wall, the ground, rock) on a probe ray.
        private static bool Open(int cmd, Vector3 to)
        {
            for (int h = 0; h < MaxHits; h++)
            {
                Solids.Info s = Solids.Of(s_hits[cmd * MaxHits + h]);
                if (s == null) return true;
                if (Occlusion.Counts(s, to) >= 1f) return false;
            }
            return false;
        }

        private static RaycastCommand Cast(Vector3 from, Vector3 to, QueryParameters q)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            return dist > Occlusion.SourceClearance + .1f
                ? new RaycastCommand(from, d / dist, q, dist - Occlusion.SourceClearance)
                : new RaycastCommand(from, Vector3.up, q, 0f);
        }

        private static readonly RaycastHit[] Line = new RaycastHit[MaxHits];

        // One line (three rays from cmd on): what two of them agree on.
        private static float Walls(int cmd, Vector3 to, ref Occlusion.Block b)
        {
            Occlusion.Block b0 = b, b1 = b, b2 = b;
            float o0 = Ray(cmd, to, ref b0);
            float o1 = Ray(cmd + 1, to, ref b1);
            float o2 = Ray(cmd + 2, to, ref b2);
            int k = Occlusion.Median(b0, b1, b2);
            b = k == 0 ? b0 : k == 1 ? b1 : b2;
            return k == 0 ? o0 : k == 1 ? o1 : o2;
        }

        // Hits of one ray, and the ridge if the ground was among them (from the ray's own start).
        private static float Ray(int cmd, Vector3 to, ref Occlusion.Block b)
        {
            bool hadGround = b.Terrain;
            b.Terrain = false;
            int n = 0;
            for (int h = 0; h < MaxHits; h++)
            {
                RaycastHit hit = s_hits[cmd * MaxHits + h];
                // the first empty slot ends the ray's hits: Unity doesn't write the ones after it, so
                // they can still hold hits from an earlier batch (RaycastCommand, Unity docs)
                if (hit.colliderInstanceID == 0) break;
                Line[n++] = hit;
            }
            Vector3 from = s_cmds[cmd].from;
            float occ = Occlusion.Along(Line, n, from, to, ref b, n >= MaxHits);
            if (b.Terrain) Occlusion.Ridge(from, to, ref b);
            b.Terrain |= hadGround;
            return occ;
        }

        // Where a sound behind something is heard from, as hearing does it (the precedence effect;
        // Haas 1951, Blauert 1983): the sound through the wall takes the straight line, so it arrives
        // first, and the first arrival sets the direction while the later one comes 2 to 50 ms after
        // it, unless the later one is more than 10 dB louder. Within 1 ms of each other the two blend
        // by level (summing localization); more than 50 ms apart they are heard as separate sounds,
        // and the louder one is where it seems to be. Returns how far toward the way round (0 to 1).
        // The 10 dB edge is spread over 2 dB either side so a sound doesn't flick between the two.
        private static float HeardFrom(Vector3 listener, Vector3 source, Vector3 via, float eDirect, float eRound)
        {
            float delay = ((via - listener).magnitude + (source - via).magnitude - (source - listener).magnitude) / 343f;
            if (eRound <= 0f) return 0f;
            if (eDirect <= 0f) return 1f;
            if (delay < .001f) return eRound / (eDirect + eRound);
            float db = 10f * Mathf.Log10(eRound / eDirect);
            if (delay > .05f) return Mathf.Clamp01((db + 2f) / 4f);
            return Mathf.Clamp01((db - 8f) / 4f);
        }

        // A point in the direction between the sound's own and the way round's, at the sound's own
        // distance (blending the two positions instead could land inside the listener's room).
        private static Vector3 Toward(Vector3 listener, Vector3 source, Vector3 via, float w)
        {
            Vector3 a = source - listener, b = via - listener;
            float dist = a.magnitude;
            if (dist < 1e-3f || b.sqrMagnitude < 1e-6f) return source;
            Vector3 dir = Vector3.Slerp(a / dist, b.normalized, w);
            return listener + dir * dist;
        }

        // A way round: kept if it arrives louder than the best so far.
        private static bool Consider(float occ, in Occlusion.Block block, float lengthGain, float bendGain, float bendCut, Vector3 at, ref float bestGain, ref float bestCutoff, ref float bestOcc, ref Vector3 bestAt)
        {
            Occlusion.Wall(block, out float g, out float cut);
            g *= lengthGain * bendGain;
            cut = Mathf.Min(cut, bendCut);
            if (g <= bestGain) return false;
            bestGain = g;
            bestCutoff = cut;
            bestOcc = occ;
            bestAt = at;
            return true;
        }

        private static bool Apply()
        {
            bool on = Plugin.Occlusion.Value;
            long until = Frame.Until;
            int start = s_applied;
            for (; s_applied < s_flightCount; s_applied++)
            {
                int i = s_applied;
                // this frame's slice used up: the rest next frame (at least one each frame)
                if (i > start && System.Diagnostics.Stopwatch.GetTimestamp() > until) { Occlusion.Own = null; return false; }
                Req r = s_flight[i];
                s_flight[i].F = null;
                AcousticFilter f = r.F;
                if (f == null) continue;
                if (r.Probe)
                {
                    Occlusion.Own = f.ShapeRoot != null ? f.ShapeRoot.root : null;
                    if (on) ReadProbe(r);
                    Occlusion.Own = null;
                    continue;
                }
                float occ = 0f;
                Occlusion.Own = f.ShapeRoot != null ? f.ShapeRoot.root : null;
                Occlusion.Block direct = Occlusion.Block.Clear;
                if (on) occ = Walls(r.First, r.To, ref direct);
                if (on) f.Walls = Corners.From(direct);
                // The direct sound (through the walls) and the best way round (an opening, a corner)
                // are both heard: neither stops the other. The way round is picked by how loud
                // it arrives, and the two add up by energy.
                float pathGain = 0f, pathCutoff = 22000f, pathOcc = 0f;
                Vector3 pathAt = r.To;
                int bestKind = 0; // 1 opening, 2 corner
                int c = r.First + 3;
                // The result also decides the routing next time.
                f.LastOcc = occ;
                // Just gone behind something (stepping down off a wall, the sea behind it): its ways
                // round weren't tried this time, and the wall alone would shut it off for a moment
                // and then open it again. Kept as it was for one more frame, tried with them.
                if (on && occ >= .3f && !r.Corners)
                {
                    Occlusion.Own = null;
                    Queue.Add(new Req { F = f, To = r.To, Under = r.Under, Reverb = r.Reverb, Corners = true });
                    continue;
                }
                if (occ >= .3f) s_blocked++;
                else s_clear++;
                if (on && occ >= .3f)
                    for (int k = 0; k < r.Routes; k++, c += 3)
                    {
                        Portals.Portal p = k == 0 ? r.P0 : r.P1;
                        Occlusion.Block leg = Occlusion.Block.Clear;
                        float total = Walls(c, r.To, ref leg);
                        Diffraction.Opening(r.Listener, p.Point, p.Dir, p.Radius, r.To, out float bg, out float bc);
                        if (Consider(total, leg, Portals.LengthGain(r.Listener, p, r.To), bg, bc, p.Point, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                            bestKind = 1;
                    }
                else c += 3 * r.Routes;
                for (int k = 0; k < r.EdgeN; k++, c += 6)
                {
                    if (!on || occ < .3f) continue;
                    Vector3 p = s_edges[i * Corners.MaxEdges + k];
                    Occlusion.Block leg = Occlusion.Block.Clear;
                    float total = Walls(c, p, ref leg) + Walls(c + 3, r.To, ref leg);
                    Diffraction.Round(r.Listener, p, r.To, out float bg, out float bc);
                    if (Consider(total, leg, Corners.LengthGain(r.Listener, p, r.To), bg, bc, p, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                        bestKind = 2;
                }
                // Its walls only just sent their probes (Started) and no edge is known yet: the wall
                // alone would muffle it for a moment before the way round is found. Kept as it was;
                // the probes check it again as soon as they find an edge (or finish).
                if (on && r.Started && r.EdgeN == 0 && occ >= .3f) { Occlusion.Own = null; continue; }
                Occlusion.Wall(direct, out float gain, out float cutoff);
                bool routed = false;
                Vector3 heard = r.To;
                if (pathGain > 0f)
                {
                    float ed = gain * gain, ep = pathGain * pathGain, share = ep / (ed + ep);
                    cutoff = Mathf.Exp((ed * Mathf.Log(cutoff) + ep * Mathf.Log(pathCutoff)) / (ed + ep));
                    gain = Mathf.Min(1f, Mathf.Sqrt(ed + ep));
                    occ = Mathf.Lerp(occ, pathOcc, share);
                    float w = HeardFrom(r.Listener, r.To, pathAt, ed, ep);
                    heard = Toward(r.Listener, r.To, pathAt, w);
                    routed = w > .05f;
                }
                if (routed)
                {
                    if (bestKind == 1) s_portalWins++;
                    else if (bestKind == 2) s_cornerWins++;
                }
                else if (occ >= .3f) s_noPath++;
                Occlusion.Own = null;
                if (r.Under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); }
                f.Set(gain, cutoff, false);
                f.Routed = routed;
                f.RoutePoint = heard;
                // the reverb send from scratch each check: what gets through, in the room too
                if (r.Reverb && f.Source != null) Occlusion.Send(f, Occlusion.ReverbSend((r.To - r.Listener).magnitude, gain));
            }
            s_flightCount = 0;
            s_applied = 0;
            return true;
        }

        // Pending checks dropped (switched off).
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < s_flightCount; i++) s_flight[i].F = null;
            s_flightCount = 0;
            s_applied = 0;
            Queue.Clear();
        }

        internal static void Shutdown()
        {
            Flush();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }
    }
}

namespace WiresAcoustics
{
    // The mod's main-thread time each frame is kept under 0.5 ms, all of it together: what has to
    // happen each frame (where sounds are, the room when it's due) goes first, and the wall checks'
    // results (Batch) take what's left, the rest waiting for the next frame (always at least one, so
    // nothing waits for ever). A sound starting is checked at once, outside this (a frame late would
    // let its start through the wall).
    internal static class Frame
    {
        private static readonly long Budget = System.Diagnostics.Stopwatch.Frequency / 2000; // 0.5 ms
        internal static long Until;
        internal static void Begin() => Until = System.Diagnostics.Stopwatch.GetTimestamp() + Budget;
        internal static bool Over => System.Diagnostics.Stopwatch.GetTimestamp() > Until;
    }
}
