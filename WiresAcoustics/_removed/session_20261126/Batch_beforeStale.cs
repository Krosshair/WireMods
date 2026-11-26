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
            internal bool Under, Reverb, Corners, Probe, Started, Waiting; // Started: this check sent probes for its walls; Waiting: some still on their way
            internal int Slot, Dir, K, Version, Gen; // a probe piece: which record, which way, how many, which pass; geometry revision
            internal Vector3 At, N;             // and where on which wall
            internal int First, Routes, EdgeN, VolumeN;
            internal Portals.Portal P0, P1, P2, P3;
        }
        private static readonly List<Req> Queue = new List<Req>(64);
        private static Req[] s_flight = new Req[64];
        private static Vector3[] s_edges = new Vector3[64 * Corners.MaxEdges];
        private static Vector3[] s_volumePoints = new Vector3[64 * SourceVolume.MaxSamples]; // the edges each check tried
        private static int s_flightCount, s_applied;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        // Validate listener -> opening once per batch, not once per source.
        // The opening -> source legs remain individual batched raycasts.
        private static int s_entranceCount;
        private static readonly Vector3[] s_entrancePoints = new Vector3[Portals.Routes];
        private static readonly float[] s_entranceGain = new float[Portals.Routes];
        private static readonly float[] s_entranceCutoff = new float[Portals.Routes];
        private static int s_entranceRays, s_entranceObstructed;
        // Main-thread diagnostic counters, emitted in the existing room log.
        private static int s_direct, s_openings, s_cornerRays, s_volumeRays;
        private static int s_clear, s_blocked, s_portalWins, s_cornerWins, s_noPath, s_silentRaysSkipped;
        internal static string Summary()
        {
            string s = "paths rays direct " + s_direct + ", source-volume " + s_volumeRays + ", openings " + s_openings +
                ", corners " + s_cornerRays + ", listener->opening " + s_entranceRays +
                "; checks clear " + s_clear + ", blocked " + s_blocked +
                ", obstructed opening legs " + s_entranceObstructed +
                ", stale geometry results retried " + s_discardedStaleJobs +
                ", chosen opening " + s_portalWins + ", corner " + s_cornerWins +
                ", through walls " + s_noPath + ", inaudible sound checks skipped " + s_silentRaysSkipped;
            s_direct = s_openings = s_cornerRays = s_volumeRays = s_entranceRays = s_entranceObstructed = s_discardedStaleJobs = 0;
            s_clear = s_blocked = s_portalWins = s_cornerWins = s_noPath = s_silentRaysSkipped = 0;
            return s;
        }

        // A world event requests fresh propagation for nearby live sounds.
        // Work is still batched on the existing audio-update job; no
        // synchronous scene raycasts or extra per-frame hook.
        internal static void GeometryChanged(Vector3 changed, bool everywhere)
        {
            if (everywhere) SharedEdges.Clear();
            if (!Plugin.Enabled.Value || !Plugin.Occlusion.Value) return;
            Vector3 listener = Hearing.At;
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || f.Source == null || !f.Source.isPlaying ||
                    !f.Placed || !f.Active || f.Cue || f.Voice &&
                    !Plugin.VoiceChatOn.Value ||
                    f.Kind == AcousticFilter.Kinds.Ocean ||
                    f.Kind == AcousticFilter.Kinds.Ambient ||
                    f.Kind == AcousticFilter.Kinds.Outside) continue;
                Vector3 at = f.transform.position;
                if (!everywhere)
                {
                    float reach = Corners.Reach + Occlusion.SameWall;
                    bool affected = Occlusion.NearLine(changed, listener, at, reach);
                    // Geometry can change on an indirect path without being
                    // near the source's direct ray. A cached route must also
                    // be invalidated when its detour legs change.
                    if (!affected && f.Routed)
                        affected = Occlusion.NearLine(changed, listener, f.RoutePoint, reach) ||
                                   Occlusion.NearLine(changed, f.RoutePoint, at, reach);
                    // Sampled portal candidates can become valid or blocked
                    // after placement/door changes even when not the winner
                    // of the last update. Avoid preserving a stale winner.
                    if (!affected && f.Walls.Near)
                        foreach (Portals.Portal portal in Portals.List)
                            if (Occlusion.NearLine(changed, listener, portal.Point, reach) ||
                                Occlusion.NearLine(changed, portal.Point, at, reach))
                            {
                                affected = true;
                                break;
                            }
                    if (!affected) continue;
                }
                f.FastGeometryResponse = true;
                Enqueue(f, at, Underwater.Muffles(at, f.transform),
                    Plugin.RoomReverb.Value && !f.Source.bypassReverbZones);
            }
        }

        internal static void Enqueue(AcousticFilter f, Vector3 to, bool under, bool reverb)
        {
            if (f == null) return;
            f.CheckedFrom = Hearing.At;
            f.CheckedAt = to;
            f.CheckedTime = Time.time;
            // from what you acted on: its walls traced to the point you touched, while it's in reach (Interaction)
            if (f.Interacted && f.Touch.Now(out Vector3 touched)) to = touched;
            f.CheckedGen = Occlusion.WorldGen;
            // Skip expensive obstacle rays for a source with estimated open
            // gain below -60 dB. This is ONLY a geometry-work skip: the source
            // itself is not muted, and is rechecked when it becomes audible.
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
                f.EdgeModelValid = false;
                f.UnderAmount = Underwater.SourceAmount(to, f.transform);
                f.Set(Mathf.Lerp(1f, Underwater.Gain, f.UnderAmount), 22000f, false);
                f.LastOcc = 0f;
                f.Routed = false;
                f.RouteSpread = 0f;
                return;
            }
            // No wall processing requested: don't submit ANY geometry rays.
            // Underwater's attenuation still applies when necessary.
            if (!Plugin.Occlusion.Value)
            {
                f.EdgeModelValid = false;
                f.UnderAmount = Underwater.SourceAmount(to, f.transform);
                f.Set(Mathf.Lerp(1f, Underwater.Gain, f.UnderAmount), 22000f, false);
                f.LastOcc = 0f;
                if (f.Records != null) foreach (Corners.Record rec in f.Records) rec.Valid = false;
                f.Routed = false;
                f.RouteSpread = 0f;
                return;
            }
            // A single cached box wall already has continuous Fresnel
            // diffraction; don't schedule duplicate corner-probe jobs for
            // the SAME geometry every time the listener moves.
            Queue.Add(new Req { F = f, To = to, Under = under, Reverb = reverb,
                // Independent window/door routes remain valid candidates
                // even when this source uses analytical direct-wall sound.
                // The cached wall's own corner probes stay suppressed.
                Corners = f.AnalyticHasPortal ||
                    (!f.AnalyticActive && (f.Walls.Near || f.Walls.Far)) });
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
        // The order as one number each, and only the requests' places sorted: a request is a big
        // struct, and sorting the list itself copied two of them per comparison, thousands of times a
        // frame at a base (most of the mod's main-thread time). A wall added right now first, then
        // probe pieces, then the loudest, then the nearest. Returns how many are in s_order.
        private static double[] s_keys = new double[64];
        private static int[] s_order = new int[64];

        private static int Tidy()
        {
            Seen.Clear();
            int n = 0, frame = Time.frameCount;
            if (s_keys.Length < Queue.Count) { s_keys = new double[Queue.Count * 2]; s_order = new int[Queue.Count * 2]; }
            // from the back: a sound queued again keeps its latest check
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                Req r = Queue[i];
                AcousticFilter f = r.F;
                if (ReferenceEquals(f, null) || !f.Alive || (!r.Probe && !Seen.Add(f))) continue;
                // A probe piece from a pass that has since started over (the wall moved as you or the
                // sound did): its answer would be thrown away (Probed), so it isn't cast at all. These
                // piled up into thousands at a base, all sorted again every frame.
                if (r.Probe && (f.Records == null || r.Slot < 0 || r.Slot >= f.Records.Length ||
                    f.Records[r.Slot].Version != r.Version || !f.Records[r.Slot].Valid)) continue;
                s_keys[n] = (f.FastGeometryResponse ? 0.0 : r.Probe ? 1e15 : 2e15) - Budget.Open(f, frame) * 1e6 + f.LastDist;
                s_order[n] = i;
                n++;
            }
            System.Array.Sort(s_keys, s_order, 0, n);
            return n;
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
            if (Queue.Count == 0) return;
            // Schedule at least one request even when the earlier audio tick
            // exceeded its 0.5 ms budget; the loop below defers the rest.
            // Otherwise dense scenes can permanently starve propagation.
            int n = Tidy();
            int routes = Mathf.Min(Portals.Routes, Portals.List.Count);
            int cmds = 0, m = 0, budget = ProbeBudget;
            for (int i = 0; i < n; i++)
            {
                // out of this frame's time: the rest of the line waits for the next batch
                if (m > 0 && Frame.Over) { for (; i < n; i++) Later.Add(Queue[s_order[i]]); break; }
                Req r = Queue[s_order[i]];
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
                // Once an unchanged single box wall has been established,
                // its source-coverage area is solved analytically by the
                // existing listener update. Recasting 4–32 source-volume
                // rays on every movement check adds no information for
                // that SAME wall. Keep the three direct discovery rays to
                // detect new intervening objects and revert to volumetric
                // sampling if geometry changes or the model stops applying.
                bool cachedAnalytic = r.F != null && r.F.AnalyticActive &&
                    r.F.AnalyticWall != null &&
                    r.F.AnalyticWorldGen == Occlusion.WorldGen;
                r.VolumeN = !cachedAnalytic && r.F != null &&
                    Plugin.Enabled.Value && Plugin.Occlusion.Value
                    ? SourceVolume.Count(r.F, listener, r.To,
                        Plugin.VolumetricRays.Value) : 0;
                int edgesAt = m;
                if (r.Corners && r.F != null && !r.F.AnalyticActive)
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
                        int count = Corners.Probes(listener, w.NearAt, r.To, w.NearGain);
                        if (!SharedEdges.TryCopy(f.Records[mN], w.NearAt, w.NearN,
                                                 count * Corners.Spacing))
                        {
                            StartProbes(f, r, mN, w.NearAt, w.NearN, count);
                            r.Started = true;
                        }
                    }
                    if (w.Far && mF < 0)
                    {
                        mF = mN == 0 ? 1 : 0;
                        int count = Corners.Probes(listener, w.FarAt, r.To, w.FarGain);
                        if (!SharedEdges.TryCopy(f.Records[mF], w.FarAt, w.FarN,
                                                 count * Corners.Spacing))
                        {
                            StartProbes(f, r, mF, w.FarAt, w.FarN, count);
                            r.Started = true;
                        }
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
                        if (rec.Pending > 0 && Time.time - rec.StartedAt < 1f) r.Waiting = true;
                    }
                }
                Fly(ref m, r);
                cmds += 3 * (1 + r.Routes) + 6 * r.EdgeN + r.VolumeN;
            }
            Queue.Clear();
            Queue.AddRange(Later); // over a budget: next batch
            Later.Clear();
            if (m == 0) return;
            // The room identifies candidate openings, but that alone does
            // NOT establish an unobstructed listener-to-opening path now.
            // The same listener and opening are shared by all sources in
            // this job, so cast the entrance leg just once per opening.
            int entranceCount = 0;
            for (int i = 0; i < m; i++)
                if (!s_flight[i].Probe)
                    entranceCount = Mathf.Max(entranceCount, s_flight[i].Routes);
            s_entranceCount = entranceCount;
            cmds += 3 * entranceCount;
            RayJobs.Ensure(ref s_cmds, ref s_hits, cmds, MaxHits);
            var q = RayJobs.Query(Room.Mask);
            int c = 0;
            for (int k = 0; k < entranceCount; k++)
            {
                Vector3 point = Portals.List[k].Point;
                s_entrancePoints[k] = point;
                Cast3(listener, point, q, ref c);
            }
            s_entranceRays += entranceCount * 3;
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
                if (r.Routes > 2) r.P2 = Portals.List[2];
                if (r.Routes > 3) r.P3 = Portals.List[3];
                Cast3(listener, r.To, q, ref c);
                SourceVolume.Projection projection = r.VolumeN > 0 ?
                    SourceVolume.Project(r.F, listener, r.To) : default;
                for (int v = 0; v < r.VolumeN; v++)
                {
                    Vector3 p = SourceVolume.Point(in projection, r.To, v, r.VolumeN);
                    s_volumePoints[i * SourceVolume.MaxSamples + v] = p;
                    s_cmds[c++] = Cast(listener, p, q);
                }
                if (r.Routes > 0) Cast3(r.P0.Point, r.To, q, ref c);
                if (r.Routes > 1) Cast3(r.P1.Point, r.To, q, ref c);
                if (r.Routes > 2) Cast3(r.P2.Point, r.To, q, ref c);
                if (r.Routes > 3) Cast3(r.P3.Point, r.To, q, ref c);
                s_direct += 1;
                s_volumeRays += r.VolumeN;
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
            s_job = RayJobs.Submit(s_cmds.GetSubArray(0, c),
                s_hits.GetSubArray(0, c * MaxHits), 8, MaxHits);
            s_pending = true;
        }

        private static void EnsureFlight(int n)
        {
            if (s_flight.Length >= n) return;
            int fl = Mathf.NextPowerOfTwo(Mathf.Max(64, n));
            System.Array.Resize(ref s_flight, fl);
            System.Array.Resize(ref s_edges, fl * Corners.MaxEdges);
            System.Array.Resize(ref s_volumePoints, fl * SourceVolume.MaxSamples);
        }

        private static void Fly(ref int m, Req r)
        {
            EnsureFlight(m + 1);
            r.Gen = Occlusion.WorldGen;
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
            rec.StartedAt = Time.time;
            for (int d = 0; d < Corners.Dirs; d++)
            {
                rec.Has[d] = false;
                Later.Add(new Req { F = f, To = check.To, Under = check.Under, Reverb = check.Reverb, Probe = true,
                                    Slot = slot, Dir = d, K = k, At = at, N = n, Version = rec.Version });
            }
        }

        // One line as three rays: itself and the two beside it (Occlusion.Lines).
        private static void Cast3(Vector3 from, Vector3 to, QueryParameters q, ref int c) =>
            RayJobs.Cast3(s_cmds, from, to, q, ref c);

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
            if (rec.Pending == 0) SharedEdges.Publish(rec);
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

        internal static RaycastCommand Cast(Vector3 from, Vector3 to, QueryParameters q)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            return dist > Occlusion.SourceClearance + .1f
                ? new RaycastCommand(from, d / dist, q, dist - Occlusion.SourceClearance)
                : new RaycastCommand(from, Vector3.up, q, 0f);
        }

        private static readonly RaycastHit[] Line = new RaycastHit[MaxHits];

        // Direct sound is NOT a median-hit switch. The existing rays
        // represent portions of the listener/source aperture: average
        // their frequency-dependent energies, retaining wall geometry
        // separately for edge searches. This result is also used before
        // one-shot playback starts, so initial sound is not incorrectly
        // assigned a binary state.
        internal static float DirectWalls(NativeArray<RaycastCommand> cmds,
            NativeArray<RaycastHit> hits, int cmd, Vector3 to,
            ref Occlusion.Block b, out float gain, out float cutoff,
            out float coverage)
        {
            Occlusion.Block b0 = b, b1 = b, b2 = b;
            float o0 = Ray(cmds, hits, cmd, to, ref b0);
            float o1 = Ray(cmds, hits, cmd + 1, to, ref b1);
            float o2 = Ray(cmds, hits, cmd + 2, to, ref b2);
            Occlusion.Wall(b0, out float g0, out float c0);
            Occlusion.Wall(b1, out float g1, out float c1);
            Occlusion.Wall(b2, out float g2, out float c2);
            bool h0 = b0.Walls > 0 || b0.Terrain || b0.Soft > 0f ||
                      b0.Through < 1f || b0.BendGain < 1f;
            bool h1 = b1.Walls > 0 || b1.Terrain || b1.Soft > 0f ||
                      b1.Through < 1f || b1.BendGain < 1f;
            bool h2 = b2.Walls > 0 || b2.Terrain || b2.Soft > 0f ||
                      b2.Through < 1f || b2.BendGain < 1f;
            ThreeRayTransmission.Combine(g0, c0, g1, c1, g2, c2,
                h0, h1, h2, out gain, out cutoff, out coverage);
            // Geometry for wall-edge probing must come from an actual
            // blocked ray, even when its two neighbors are clear.
            b = h0 ? b0 : h1 ? b1 : h2 ? b2 : b0;
            return .5f * o0 + .25f * (o1 + o2);
        }

        // Shared physically measured opening transfer, used both by
        // ordinary SFX and world-space environmental emitter patches.
        // A window is a PROPAGATION ROUTE, never an emitting source.
        internal static float PortalTransfer(Vector3 ear, Vector3 source,
            Portals.Portal portal, float directCoverage,
            float entranceGain, float entranceCut,
            float exitGain, float exitCut, out float cutoff)
        {
            Diffraction.Opening(ear, portal.Point, portal.Dir, portal.Radius,
                source, out float bendGain, out float bendCut);
            SpectralPaths.Cascade(exitGain, exitCut, entranceGain, entranceCut,
                out float gain, out cutoff);
            SpectralPaths.Cascade(gain, cutoff, bendGain, bendCut,
                out gain, out cutoff);
            return gain * Portals.LengthGain(ear, portal, source) *
                ThreeRayTransmission.IndirectAmplitude(directCoverage);
        }

        private static float DirectWalls(int cmd, Vector3 to,
            ref Occlusion.Block b, out float gain, out float cutoff,
            out float coverage) =>
            DirectWalls(s_cmds, s_hits, cmd, to, ref b,
                        out gain, out cutoff, out coverage);

        // Also used by the held first-sound pass: one independently
        // measured source-volume point using its original raycast buffer.
        internal static float VolumeRay(NativeArray<RaycastCommand> cmds,
            NativeArray<RaycastHit> hits, int cmd, Vector3 to,
            ref Occlusion.Block b, out float gain, out float cutoff)
        {
            float occ = Ray(cmds, hits, cmd, to, ref b);
            Occlusion.Wall(b, out gain, out cutoff);
            return occ;
        }

        // Hits of one ray, and the ridge if the ground was among them (from the ray's own start).
        private static float Ray(NativeArray<RaycastCommand> cmds, NativeArray<RaycastHit> hits, int cmd, Vector3 to, ref Occlusion.Block b)
        {
            bool hadGround = b.Terrain;
            b.Terrain = false;
            int n = RayJobs.ReadHits(hits, cmd, MaxHits, Line);
            Vector3 from = cmds[cmd].from;
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
        // Relative-delay transitions are continuous through both the onset
        // (roughly 1 ms) and the loss of precedence (roughly 50 ms), rather
        // than abrupt switches at those values.
        private static float HeardFrom(Vector3 listener, Vector3 source, Vector3 via, float eDirect, float eRound)
        {
            float delay = ((via - listener).magnitude + (source - via).magnitude - (source - listener).magnitude) / 343f;
            if (eRound <= 0f) return 0f;
            if (eDirect <= 0f) return 1f;
            float db = 10f * Mathf.Log10(eRound / eDirect);
            float simultaneous = eRound / (eDirect + eRound);
            float precedence = Mathf.Clamp01((db - 8f) / 4f);
            float separated = Mathf.Clamp01((db + 2f) / 4f);
            // The psychoacoustic precedence effect changes gradually
            // with relative delay. The earlier 1 ms / 50 ms hard switches
            // caused visible (and potentially audible) direction steps
            // even as a path length changed continuously.
            float onset = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(.0005f, .002f, delay));
            float late = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(.04f, .06f, delay));
            return Mathf.Lerp(Mathf.Lerp(simultaneous, precedence, onset),
                separated, late);
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

        // Completed ray jobs can become stale after a door, mining or building
        // change during their flight. A direct-line-only generation check
        // misses changes on indirect legs; validate the entire sampled route.
        private static bool ChangedDuringFlight(in Req r, int flightIndex)
        {
            if (r.Gen == Occlusion.WorldGen) return false;
            float margin = Corners.Reach + Occlusion.SameWall;
            if (r.Probe)
                return Occlusion.ChangedNear(r.Gen, r.At, r.At,
                    r.K * Corners.Spacing + Occlusion.SameWall);
            if (Occlusion.ChangedNear(r.Gen, r.Listener, r.To, margin))
                return true;
            for (int k = 0; k < r.Routes; k++)
            {
                Portals.Portal p = k == 0 ? r.P0 : k == 1 ? r.P1 : k == 2 ? r.P2 : r.P3;
                if (Occlusion.ChangedNear(r.Gen, r.Listener, p.Point, margin) ||
                    Occlusion.ChangedNear(r.Gen, p.Point, r.To, margin))
                    return true;
            }
            for (int k = 0; k < r.EdgeN; k++)
            {
                Vector3 edge = s_edges[flightIndex * Corners.MaxEdges + k];
                if (Occlusion.ChangedNear(r.Gen, r.Listener, edge, margin) ||
                    Occlusion.ChangedNear(r.Gen, edge, r.To, margin))
                    return true;
            }
            return false;
        }

        private static int s_discardedStaleJobs;

        private static bool Apply()
        {
            bool on = Plugin.Occlusion.Value;
            long until = Frame.Until;
            int start = s_applied;
            if (start == 0)
            {
                // Candidate openings come from sampled room rays. Their
                // centers are estimates, not guaranteed exact portal meshes.
                // Evaluate both legs and attenuate obstacles in the first
                // leg rather than silently dropping a legitimate route.
                Occlusion.Own = null;
                for (int k = 0; k < s_entranceCount; k++)
                {
                    Occlusion.Block entrance = Occlusion.Block.Clear;
                    DirectWalls(3 * k, s_entrancePoints[k], ref entrance,
                        out s_entranceGain[k], out s_entranceCutoff[k],
                        out float entranceCoverage);
                    if (entranceCoverage > 0f)
                        s_entranceObstructed++;
                }
            }
            for (; s_applied < s_flightCount; s_applied++)
            {
                int i = s_applied;
                // this frame's slice used up: the rest next frame (at least one each frame)
                if (i > start && System.Diagnostics.Stopwatch.GetTimestamp() > until) { Occlusion.Own = null; return false; }
                Req r = s_flight[i];
                s_flight[i].F = null;
                AcousticFilter f = r.F;
                if (f == null) continue;
                // Never apply a ray result after a geometry event on ANY
                // of its sampled legs. Requeue rather than leave old state.
                if (ChangedDuringFlight(r, i))
                {
                    s_discardedStaleJobs++;
                    f.EdgeModelValid = false;
                    if (r.Probe && f.Records != null)
                    {
                        Corners.Record rec = f.Records[r.Slot];
                        if (rec.Version == r.Version)
                        {
                            rec.Valid = false;
                            rec.Pending = 0;
                            rec.Version++;
                        }
                    }
                    if (f.Source != null && f.Source.isPlaying && f.Active)
                        Enqueue(f, f.transform.position,
                            Underwater.Muffles(f.transform.position, f.transform),
                            Plugin.RoomReverb.Value && !f.Source.bypassReverbZones);
                    continue;
                }
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
                float directGain = 1f, directCut = 22000f, directCoverage = 0f;
                if (on) occ = DirectWalls(r.First, r.To, ref direct,
                    out directGain, out directCut, out directCoverage);
                bool volumeSingleWall = direct.Walls <= 1 &&
                    !direct.Terrain && direct.Soft <= 0f;
                if (on && r.VolumeN > 0)
                {
                    // Treat every sampled point on the source's actual
                    // projected volume as an emitting region. Average
                    // intensity, NOT binary majority or amplitude.
                    float lowEnergy = directGain * directGain;
                    float highEnergy = SpectralPaths.HighEnergy(directGain, directCut);
                    float covered = directCoverage, obstruction = occ;
                    for (int v = 0; v < r.VolumeN; v++)
                    {
                        Occlusion.Block piece = Occlusion.Block.Clear;
                        float localOcc = Ray(s_cmds, s_hits, r.First + 3 + v,
                            s_volumePoints[i * SourceVolume.MaxSamples + v], ref piece);
                        Occlusion.Wall(piece, out float g, out float cut);
                        if (piece.Walls > 1 || piece.Terrain ||
                            piece.Soft > 0f) volumeSingleWall = false;
                        lowEnergy += g * g;
                        highEnergy += SpectralPaths.HighEnergy(g, cut);
                        covered += piece.Walls > 0 || piece.Terrain ||
                                   piece.Soft > 0f || piece.Through < 1f ||
                                   piece.BendGain < 1f ? 1f : 0f;
                        obstruction += localOcc;
                        if (!direct.HasWall && piece.HasWall) direct = piece;
                        // A perforated Valheim panel has no solid edge record;
                        // still keep its actual coarse BoxCollider for the
                        // continuous projected-coverage calculation.
                        else if (direct.FirstBox == null && piece.FirstBox != null)
                            direct.FirstBox = piece.FirstBox;
                    }
                    float normalization = 1f / (r.VolumeN + 1f);
                    SpectralPaths.Decode(lowEnergy * normalization,
                        highEnergy * normalization, out directGain, out directCut);
                    directCoverage = covered * normalization;
                    occ = obstruction * normalization;
                }
                // For a known planar box surface use continuous projected
                // area, not the fraction of discrete ray hits. The raycast
                // results still discover the actual surface and other paths.
                bool analyticDirect = false;
                if (on && volumeSingleWall && !direct.Terrain && direct.Soft <= 0f)
                {
                    if (direct.FirstBox != null)
                        AnalyticOcclusion.Bind(f, direct.FirstBox.C as BoxCollider,
                                               direct.FirstBox, r.To);
                    if (AnalyticOcclusion.Evaluate(f, r.Listener, r.To,
                        out float areaGain, out float areaCut, out float areaCovered))
                    {
                        directGain = areaGain;
                        directCut = areaCut;
                        directCoverage = areaCovered;
                        occ = areaCovered;
                        analyticDirect = true;
                    }
                }
                if (!analyticDirect) f.AnalyticWall = null;
                if (on)
                {
                    f.Walls = Corners.From(direct);
                    ContinuousEdges.Measured(f, direct, r.Listener, r.To);
                }
                // The direct sound (through the walls) and the best way round (an opening, a corner)
                // are both heard: neither stops the other. The way round is picked by how loud
                // it arrives, and the two add up by energy.
                float pathGain = 0f, pathCutoff = 22000f, pathOcc = 0f;
                Vector3 pathAt = r.To;
                int bestKind = 0; // 1 opening, 2 corner
                bool clearEdgePath = false; // only extrapolate validated unobstructed detours
                // Multiple real opening paths contribute to a single
                // perceived direction. Weight directions by received energy,
                // not the ranking of the portals in the room-ray scan.
                // Combine distinct openings by energy, not sample amplitude.
                float openingEnergy = 0f, openingPrecedence = 0f;
                float cornerEnergy = 0f, cornerHigh = 0f, cornerOccSum = 0f;
                Vector3 cornerPosition = Vector3.zero;
                int cornerCount = 0;
                bool allCornerLegsClear = true;
                float pathDispersion = 0f;
                float openingHigh = 0f, openingOccSum = 0f;
                Vector3 openingDirection = Vector3.zero;
                int openingCount = 0;
                int c = r.First + 3 + r.VolumeN;
                // The result also decides the routing next time.
                f.LastOcc = occ;
                f.LastCoverage = directCoverage;
                f.LastVolumeSamples = r.VolumeN;
                // Any genuine wall or terrain intersection changes direct
                // transmission NOW. The path search follows in the next
                // batch if its edge candidates were not yet available.
                // Never hold the previous clear value while probes run.
                bool blocked = directCoverage > 0f;
                if (on && direct.Walls > 0 && !r.Corners && !analyticDirect)
                    Queue.Add(new Req { F = f, To = r.To, Under = r.Under,
                        Reverb = r.Reverb, Corners = true });
                if (blocked) s_blocked++;
                else s_clear++;
                if (on && blocked)
                {
                    float directEnergy = directGain * directGain;
                    for (int k = 0; k < r.Routes; k++, c += 3)
                    {
                        Portals.Portal p = k == 0 ? r.P0 : k == 1 ? r.P1 : k == 2 ? r.P2 : r.P3;
                        Occlusion.Block leg = Occlusion.Block.Clear;
                        float total = DirectWalls(c, r.To, ref leg,
                            out float openingGain, out float openingCut,
                            out _);
                        // Shared with the distributed environmental
                        // field: exactly the same serial wall/diffraction
                        // model as a physical sound at this world point.
                        openingGain = PortalTransfer(r.Listener, r.To, p,
                            directCoverage, s_entranceGain[k],
                            s_entranceCutoff[k], openingGain, openingCut,
                            out openingCut);
                        if (openingGain <= 0f) continue;

                        // Each physically checked opening contributes its own
                        // arrival direction and precedence. Averaging energies
                        // avoids sudden left/right switches when rankings change.
                        float energy = openingGain * openingGain;
                        Vector3 towardOpening = p.Point - r.Listener;
                        if (towardOpening.sqrMagnitude > 1e-6f)
                        {
                            openingDirection += towardOpening.normalized * energy;
                            openingPrecedence += energy * HeardFrom(r.Listener, r.To,
                                p.Point, directEnergy, energy);
                            openingEnergy += energy;
                            openingHigh += SpectralPaths.HighEnergy(openingGain, openingCut);
                            openingOccSum += energy * total;
                            openingCount++;
                        }
                        // One opening retains the original exact propagation
                        // calculation; multiple openings blend subsequently.
                        if (openingGain > pathGain)
                        {
                            pathGain = openingGain;
                            pathCutoff = openingCut;
                            pathOcc = total;
                            pathAt = p.Point;
                            bestKind = 1;
                        }
                    }
                    if (openingCount > 1 && openingEnergy > 1e-8f)
                    {
                        // Physically distinct routes combine their energies;
                        // spectral loss and remaining obstruction are
                        // energy-weighted. Both remain bounded downstream.
                        SpectralPaths.Decode(openingEnergy, openingHigh,
                                             out pathGain, out pathCutoff);
                        pathOcc = openingOccSum / openingEnergy;
                    }
                }
                else c += 3 * r.Routes;
                for (int k = 0; k < r.EdgeN; k++, c += 6)
                {
                    // Analytic single-wall Fresnel already includes that
                    // same box's edge diffraction. Do not add a second
                    // independently sampled corner copy of the same sound.
                    if (!on || !blocked || analyticDirect) continue;
                    Vector3 p = s_edges[i * Corners.MaxEdges + k];
                    Occlusion.Block legA = Occlusion.Block.Clear;
                    Occlusion.Block legB = Occlusion.Block.Clear;
                    Occlusion.Round = true; Occlusion.RoundAt = p;
                    float total = DirectWalls(c, p, ref legA,
                        out float cg, out float cc, out float coverA);
                    total += DirectWalls(c + 3, r.To, ref legB,
                        out float exitGain, out float exitCut, out float coverB);
                    Occlusion.Round = false;
                    Diffraction.Round(r.Listener, p, r.To, out float bg, out float bc);
                    // Physically consecutive legs and diffraction are
                    // serial, even when only part of a leg is obstructed.
                    SpectralPaths.Cascade(cg, cc, exitGain, exitCut,
                        out cg, out cc);
                    SpectralPaths.Cascade(cg, cc, bg, bc, out cg, out cc);
                    cg *= Corners.LengthGain(r.Listener, p, r.To) *
                        ThreeRayTransmission.IndirectAmplitude(directCoverage);
                    if (cg > 1e-6f)
                    {
                        float ce = cg * cg;
                        cornerEnergy += ce;
                        cornerHigh += SpectralPaths.HighEnergy(cg, cc);
                        cornerOccSum += ce * total;
                        cornerPosition += ce * p;
                        cornerCount++;
                        if (coverA > 0f || coverB > 0f) allCornerLegsClear = false;
                    }
                    if (cg > pathGain)
                    {
                        pathGain = cg;
                        pathCutoff = cc;
                        pathOcc = total;
                        pathAt = p;
                        bestKind = 2;
                        clearEdgePath = coverA <= 0f && coverB <= 0f;
                    }
                }
                // Multiple corners are a spatial distribution, not a winner
                // that jumps between left/right edges. Combine their energy,
                // spectrum and arrival position when no verified portal routes
                // compete with them.
                if (openingCount == 0 && cornerCount > 0 && cornerEnergy > 1e-8f)
                {
                    SpectralPaths.Decode(cornerEnergy, cornerHigh,
                                         out pathGain, out pathCutoff);
                    pathOcc = cornerOccSum / cornerEnergy;
                    pathAt = cornerPosition / cornerEnergy;
                    bestKind = 2;
                    clearEdgePath = allCornerLegsClear;
                }
                // Missing or still-running edge probes must never preserve
                // an obsolete unobstructed value. Apply the direct path
                // now; newly found diffraction paths update it afterward.
                float gain = directGain, cutoff = directCut;
                if (on && !blocked && Corners.Illuminated(f.Records,
                    r.Listener, r.To, out float litGain, out float litCut))
                {
                    // Clear-side diffraction fades continuously to unity
                    // as the ray clears a known physical wall edge.
                    // Near a verified wall edge the clear-side
                    // diffraction transfer acts in series with any direct
                    // transmission already measured on this segment.
                    SpectralPaths.Cascade(gain, cutoff, litGain, litCut,
                                          out gain, out cutoff);
                }
                bool routed = false;
                Vector3 heard = r.To;
                if (pathGain > 0f)
                {
                    float ed = gain * gain, ep = pathGain * pathGain, share = ep / (ed + ep);
                    SpectralPaths.Combine(gain, cutoff, pathGain, pathCutoff,
                                          out gain, out cutoff);
                    occ = Mathf.Lerp(occ, pathOcc, share);
                    float w = HeardFrom(r.Listener, r.To, pathAt, ed, ep);
                    if (bestKind == 1 && openingCount > 1 && openingEnergy > 1e-8f)
                    {
                        float combined = openingDirection.magnitude;
                        if (combined > 1e-6f)
                        {
                            // Averaging opposed paths gives a diffuse result
                            // instead of arbitrarily snapping to either side.
                            float coherence = Mathf.Clamp01(combined / openingEnergy);
                            pathDispersion = 1f - coherence;
                            w = (openingPrecedence / openingEnergy) * coherence;
                            Vector3 averageAt = r.Listener +
                                openingDirection / combined *
                                (pathAt - r.Listener).magnitude;
                            heard = Toward(r.Listener, r.To, averageAt, w);
                        }
                        else { w = 0f; heard = r.To; pathDispersion = 1f; }
                    }
                    else heard = Toward(r.Listener, r.To, pathAt, w);
                    // Arrival dispersion remains audible when opposite
                    // openings cancel their direction vectors. A diffuse
                    // field is not a point at the original source.
                    f.RouteSpread = pathDispersion * share;
                    routed = w > 0f;
                }
                // Continuous 1-edge wave field integrated across the
                // measured volume, only when the geometry really is a
                // single verified half-plane. Never overwrite competing
                // portal/corner propagation or multi-wall sound.
                bool volumeFresnel = false;
                if (on && !analyticDirect && r.VolumeN > 0 && volumeSingleWall &&
                    direct.Walls == 1 && openingCount == 0 &&
                    cornerCount == 0 &&
                    ContinuousEdges.EvaluateVolume(f, r.Listener, r.To,
                        s_volumePoints, i * SourceVolume.MaxSamples,
                        r.VolumeN, out float edgeGain,
                        out float edgeCut, out Vector3 edgeHeard))
                {
                    gain = edgeGain;
                    cutoff = edgeCut;
                    heard = edgeHeard;
                    routed = (heard - r.To).sqrMagnitude > 1e-4f;
                    volumeFresnel = true;
                }
                // The direct energy uses the analytical area above,
                // regardless of whether a separate measured diffraction or
                // opening route is also present. Preserve its measured
                // frequency response but let its energy scale continuously
                // with the obstructed area as the listener moves.
                f.AnalyticActive = analyticDirect;
                f.AnalyticHasPortal = analyticDirect && openingCount > 0;
                if (analyticDirect)
                {
                    f.AnalyticIndirectGain = pathGain > 0f &&
                        directCoverage > 0.0001f
                        ? pathGain / Mathf.Sqrt(directCoverage) : 0f;
                    f.AnalyticIndirectCutoff = pathCutoff;
                    f.LastCoverage = directCoverage;
                    volumeFresnel = false;
                }
                else f.AnalyticIndirectGain = 0f;
                if (routed)
                {
                    if (bestKind == 1) s_portalWins++;
                    else if (bestKind == 2) s_cornerWins++;
                }
                else if (blocked) s_noPath++;
                Occlusion.Own = null;
                f.UnderAmount = Underwater.SourceAmount(r.To, f.transform);
                Underwater.Apply(ref gain, ref cutoff, f.UnderAmount);
                f.AcousticPath = f.AnalyticActive ?
                    (pathGain > 0f ? "analytic-box+indirect" : "analytic-box-coverage") :
                                 volumeFresnel ? "volume-fresnel" :
                                 bestKind == 1 ? "portal" : bestKind == 2 ? "corner" :
                                 blocked ? (directCoverage < 1f ? "direct-partial" : "direct-through") :
                                 "direct-clear";
                f.AcousticRouteCount = openingCount + cornerCount;
                f.AcousticResolvedAt = Time.time;
                f.AcousticGeneration = Occlusion.WorldGen;
                f.Set(gain, cutoff, false);
                // Refine the measured result against the listener's CURRENT
                // position using a validated cached wall edge where possible.
                if (pathGain <= 0f) f.RouteSpread = 0f;
                f.Routed = routed;
                f.RoutePoint = heard;
                // The analytical Fresnel field also determines the
                // apparent arrival point. Do not leave a first-frame
                // directional switch until the next tracked-source tick.
                if (analyticDirect && !f.AnalyticHasPortal)
                {
                    float bend = Mathf.Clamp01(f.AnalyticBendShare);
                    f.Routed = bend > .001f;
                    f.RoutePoint = Vector3.Lerp(r.To,f.AnalyticEdgePoint,bend);
                }
                // A SINGLE validated wall edge must be allowed to resolve
                // its smooth Fresnel transition even while all direct rays
                // remain blocked; otherwise it can only start AFTER a ray
                // clears and reintroduces exactly the A/B discontinuity.
                // Don't replace measured independent portal/multipath
                // contributions with an unrelated half-plane field.
                // A point-source Fresnel field must NOT replace the
                // independently sampled transfer of a volume emitter.
                f.EdgeCanOverride = !f.AnalyticActive && r.VolumeN == 0 && openingCount == 0 &&
                    (cornerCount == 0 ||
                     (bestKind == 2 && clearEdgePath));
                f.EdgePositionKnown = false;
                // Initial sampled reverb send; a validated continuous edge
                // immediately refines this with the listener's current pose.
                if (r.Reverb && f.Source != null)
                    Occlusion.Send(f, Occlusion.ReverbSend((r.To - r.Listener).magnitude, f.TargetGain));
                if (f.WatchLoop) ContinuousEdges.Evaluate(f, Hearing.At, f.transform.position);
            }
            s_flightCount = 0;
            s_applied = 0;
            s_entranceCount = 0;
            return true;
        }

        // Pending checks dropped (switched off).
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < s_flightCount; i++) s_flight[i].F = null;
            s_flightCount = 0;
            s_applied = 0;
            s_entranceCount = 0;
            Queue.Clear();
        }

        internal static void Shutdown()
        {
            Flush();
            SharedEdges.Clear();
            RayJobs.Dispose(ref s_cmds, ref s_hits);
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
