using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Wall checks for sounds already playing (looping sounds once a second, longer one-off sounds and
    // other placed sources twice a second, voice chat 5 times a second), and the paths round walls
    // for a sound that just started behind them, batched onto worker threads like the room rays:
    // queued during the frame, cast together, read the next frame. All hits along each
    // ray, up to 8. Each sound also gets one ray from each of the biggest openings (Portals) to it,
    // and, if it had walls in the way last time, two per point around the blocker (Corners) and one
    // from each of its three shortest bounce paths (Bounces).
    internal static class Batch
    {
        private const int MaxHits = 8;
        private struct Req
        {
            internal AcousticFilter F;
            internal Vector3 To, Listener;
            internal bool Under, Reverb, Corners;
            internal int An;                  // an area source: how many of its points are checked (Area)
            internal Vector3 A0, A1, A2, A3;
            internal int First, Routes;
            internal Portals.Portal P0, P1;
            internal Vector3 C0, C1, C2, C3, C4;
            internal int Bn;
            internal Bounces.Try B0, B1, B2;
        }
        private static readonly Bounces.Try[] Tried = new Bounces.Try[Bounces.Tries];
        private static readonly Vector3[] APts = new Vector3[4];
        private static readonly List<Req> Queue = new List<Req>(64);
        private static readonly Vector3[] Pts = new Vector3[Corners.Count];
        private static Req[] s_flight = new Req[64];
        private static int s_flightCount;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        // Main-thread diagnostic counters, emitted in the existing room log.
        private static int s_direct, s_openings, s_cornerRays, s_reflectionRays;
        private static int s_clear, s_blocked, s_portalWins, s_cornerWins, s_bounceWins, s_secondBounceWins, s_noPath, s_silentRaysSkipped;
        internal static string Summary()
        {
            string s = "paths rays direct " + s_direct + ", openings " + s_openings +
                ", corners " + s_cornerRays + ", bounces " + s_reflectionRays +
                "; checks clear " + s_clear + ", blocked " + s_blocked +
                ", chosen opening " + s_portalWins + ", corner " + s_cornerWins +
                ", reflection " + s_bounceWins + " (second " + s_secondBounceWins + ")" +
                ", through walls " + s_noPath + ", inaudible sound checks skipped " + s_silentRaysSkipped;
            s_direct = s_openings = s_cornerRays = s_reflectionRays = 0;
            s_clear = s_blocked = s_portalWins = s_cornerWins = s_bounceWins = s_secondBounceWins = s_noPath = s_silentRaysSkipped = 0;
            return s;
        }

        internal static void Enqueue(AcousticFilter f, Vector3 to, bool under, bool reverb)
        {
            if (f == null) return;
            // A completely faded placed SFX/other source cannot be heard,
            // independent of how blocked it is. Continue updating the
            // direction/fade; raycast when it approaches audibility again.
            // Preserve last blockage and effect state to avoid a transition.
            if ((f.Kind == AcousticFilter.Kinds.Sfx ||
                 f.Kind == AcousticFilter.Kinds.Other) &&
                f.Placed && f.DistanceGain < 1e-4f &&
                !AcousticFilter.GlobalActive)
            {
                s_silentRaysSkipped++;
                return;
            }
            // No wall processing requested: don't submit ANY geometry rays.
            // Underwater's attenuation still applies when necessary.
            if (!Plugin.Occlusion.Value)
            {
                f.Set(under ? Underwater.Gain : 1f,
                      under ? Underwater.Cutoff : 22000f, false);
                f.LastOcc = 0f;
                f.LastBlocker = null;
                f.Routed = false;
                return;
            }
            Queue.Add(new Req { F = f, To = to, Under = under, Reverb = reverb,
                                Corners = f.LastOcc >= .3f });
        }

        // Every frame, from the game's audio update.
        internal static void Step(Vector3 listener)
        {
            if (s_pending) { s_job.Complete(); s_pending = false; Apply(); }
            int n = Queue.Count;
            if (n == 0) return;
            int routes = Mathf.Min(Portals.Routes, Portals.List.Count);
            int cmds = 0;
            foreach (Req r in Queue) cmds += 1 + (r.Corners ? routes + 2 * Corners.Count + Bounces.Tries : 0) + (r.F != null && r.F.AreaRadius > 0f ? 4 : 0);
            if (!s_cmds.IsCreated || s_cmds.Length < cmds || s_flight.Length < n)
            {
                if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(64, cmds));
                s_cmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(cap * MaxHits, Allocator.Persistent);
                s_flight = new Req[Mathf.NextPowerOfTwo(Mathf.Max(64, n))];
            }
            var q = new QueryParameters(Room.Mask, false, QueryTriggerInteraction.Ignore, false);
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                Req r = Queue[i];
                r.Listener = listener;
                r.First = c;
                r.Routes = r.Corners ? routes : 0;
                if (r.Routes > 0) r.P0 = Portals.List[0];
                if (r.Routes > 1) r.P1 = Portals.List[1];
                s_cmds[c++] = Cast(listener, r.To, q);
                // a sound with a shape: its middle, sides and top too; the least hidden part is heard
                r.An = r.F != null ? Area.Points(r.F, listener, APts) : 0;
                if (r.An > 0) { r.A0 = APts[0]; r.A1 = APts[1]; r.A2 = APts[2]; r.A3 = APts[3]; }
                for (int k = 0; k < r.An; k++) s_cmds[c++] = Cast(listener, APts[k], q);
                if (r.Routes > 0) s_cmds[c++] = Cast(r.P0.Point, r.To, q);
                if (r.Routes > 1) s_cmds[c++] = Cast(r.P1.Point, r.To, q);
                s_direct += 1 + r.An;
                s_openings += r.Routes;
                if (r.Corners)
                {
                    Corners.Around(listener, r.To, r.F != null ? r.F.LastBlockDist : (r.To - listener).magnitude * .5f,
                                   r.F != null ? r.F.LastBlocker : null, Pts);
                    r.C0 = Pts[0]; r.C1 = Pts[1]; r.C2 = Pts[2]; r.C3 = Pts[3]; r.C4 = Pts[4];
                    foreach (Vector3 p in Pts) { s_cmds[c++] = Cast(listener, p, q); s_cmds[c++] = Cast(p, r.To, q); }
                    r.Bn = Bounces.Pick(listener, r.To, Tried);
                    if (r.Bn > 0) r.B0 = Tried[0];
                    if (r.Bn > 1) r.B1 = Tried[1];
                    if (r.Bn > 2) r.B2 = Tried[2];
                    for (int k = 0; k < r.Bn; k++) s_cmds[c++] = Cast(Tried[k].End, r.To, q);
                    s_cornerRays += 2 * Corners.Count;
                    s_reflectionRays += r.Bn;
                }
                s_flight[i] = r;
            }
            s_flightCount = n;
            Queue.Clear();
            s_job = RaycastCommand.ScheduleBatch(s_cmds.GetSubArray(0, c), s_hits.GetSubArray(0, c * MaxHits), 8, MaxHits);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static RaycastCommand Cast(Vector3 from, Vector3 to, QueryParameters q)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            return dist > Occlusion.SourceClearance + .1f
                ? new RaycastCommand(from, d / dist, q, dist - Occlusion.SourceClearance)
                : new RaycastCommand(from, Vector3.up, q, 0f);
        }

        // The sea (Ocean): the beach and the seabed within 15 m of where the waves break don't hide them.
        private static bool s_shore;

        private static float Walls(int cmd, Vector3 to, out float first) =>
            Walls(cmd, to, out first, out _);

        private static float Walls(int cmd, Vector3 to, out float first,
                                   out Collider blocker)
        {
            float occ = 0f;
            first = float.MaxValue;
            blocker = null;
            for (int h = 0; h < MaxHits; h++)
            {
                RaycastHit hit = s_hits[cmd * MaxHits + h];
                if (hit.collider == null) continue;
                if (s_shore && (hit.point - to).sqrMagnitude < 15f * 15f && hit.collider.GetComponentInParent<Heightmap>() != null) continue;
                float w = Occlusion.Counts(hit.collider, to);
                if (w <= 0f) continue;
                occ += w;
                if (hit.distance < first)
                {
                    first = hit.distance;
                    blocker = hit.collider;
                }
            }
            return occ;
        }

        // A way round: kept if it arrives louder than the best so far.
        private static bool Consider(float occ, float lengthGain, Vector3 at, ref float bestGain, ref float bestCutoff, ref float bestOcc, ref Vector3 bestAt)
        {
            Occlusion.Muffle(occ, out float g, out float cut);
            g *= lengthGain;
            if (g <= bestGain) return false;
            bestGain = g;
            bestCutoff = cut;
            bestOcc = occ;
            bestAt = at;
            return true;
        }

        private static void Apply()
        {
            bool on = Plugin.Occlusion.Value;
            for (int i = 0; i < s_flightCount; i++)
            {
                Req r = s_flight[i];
                s_flight[i].F = null;
                AcousticFilter f = r.F;
                if (f == null) continue;
                float occ = 0f, first = 0f;
                Occlusion.Own = f.ShapeRoot != null ? f.ShapeRoot.root : null;
                s_shore = f.Kind == AcousticFilter.Kinds.Ocean;
                Collider firstBlocker = null;
                if (on) occ = Walls(r.First, r.To, out first, out firstBlocker);
                f.LastBlockDist = first < float.MaxValue ? first : (r.To - r.Listener).magnitude * .5f;
                // The direct sound (through the walls) and the best way round (an opening, a corner, a
                // bounce) are both heard: neither stops the other. The way round is picked by how loud
                // it arrives, and the two add up by energy.
                float pathGain = 0f, pathCutoff = 22000f, pathOcc = 0f;
                Vector3 pathAt = r.To;
                int bestKind = 0; // 1 opening, 2 corner, 3 reflection
                bool bestIsSecondBounce = false;
                int c = r.First + 1;
                int open = -1; // which point on its shape is least hidden (none better than its own spot: -1)
                float o0 = 9f, o1 = 9f, o2 = 9f, o3 = 9f;
                for (int k = 0; k < r.An; k++, c++)
                    if (on && occ > .05f)
                    {
                        float o = Walls(c, k == 0 ? r.A0 : k == 1 ? r.A1 : k == 2 ? r.A2 : r.A3, out _);
                        if (k == 0) o0 = o; else if (k == 1) o1 = o; else if (k == 2) o2 = o; else o3 = o;
                    }
                // Ties go to the nearest point, then the top, and both sides alike to the middle between
                // them: the right side was checked first and won every tie, so most muffled shapes
                // were heard from their right.
                float best = Mathf.Min(Mathf.Min(o0, o1), Mathf.Min(o2, o3));
                Vector3 openAt = Vector3.zero;
                if (best < occ)
                {
                    const float tie = .02f;
                    if (o0 <= best + tie) { open = 0; openAt = r.A0; }
                    else if (o3 <= best + tie) { open = 3; openAt = r.A3; }
                    else if (o1 <= best + tie && o2 <= best + tie) { open = 1; openAt = (r.A1 + r.A2) * .5f; }
                    else if (o1 <= best + tie) { open = 1; openAt = r.A1; }
                    else { open = 2; openAt = r.A2; }
                    occ = best;
                }
                // a sound with a shape is heard from its least hidden point (Directions)
                if (r.An > 0 && f.ShapeRoot != null)
                {
                    if (open >= 0)
                    {
                        f.HeardLocal = f.ShapeRoot.InverseTransformPoint(openAt);
                        f.HeardKnown = true;
                    }
                    else f.HeardKnown = false;
                }
                // The LEAST blocked spot on an extended source controls
                // audibility. Use that result for next-step routing decisions
                // and whether second-bounce jobs are worth scheduling.
                f.LastOcc = occ;
                f.LastBlocker = occ >= .3f ? firstBlocker : null;
                if (occ >= .3f) { s_blocked++; Bounces.Wake(); }
                else s_clear++;
                if (on && occ >= .3f)
                    for (int k = 0; k < r.Routes; k++, c++)
                    {
                        Portals.Portal p = k == 0 ? r.P0 : r.P1;
                        float total = Walls(c, r.To, out _) + Portals.Bend(p, r.To);
                        if (Consider(total, Portals.LengthGain(r.Listener, p, r.To), p.Point, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                            bestKind = 1;
                    }
                else c += r.Routes;
                if (r.Corners)
                    for (int k = 0; k < Corners.Count; k++, c += 2)
                    {
                        if (!on || f.LastOcc < .3f) continue;
                        Vector3 p = k == 0 ? r.C0 : k == 1 ? r.C1 : k == 2 ? r.C2 : k == 3 ? r.C3 : r.C4;
                        float total = Walls(c, p, out _) + Walls(c + 1, r.To, out _) + Corners.Bend(r.Listener, p, r.To);
                        if (Consider(total, Corners.LengthGain(r.Listener, p, r.To), p, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                            bestKind = 2;
                    }
                if (r.Corners)
                    for (int k = 0; k < r.Bn; k++, c++)
                    {
                        if (!on || f.LastOcc < .3f) continue;
                        Bounces.Try t = k == 0 ? r.B0 : k == 1 ? r.B1 : r.B2;
                        float total = Walls(c, r.To, out _) + Bounces.Cost(t, r.To);
                        if (Consider(total, Bounces.LengthGain(r.Listener, t, r.To), t.Heard, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                        {
                            bestKind = 3;
                            bestIsSecondBounce = (t.End - t.Heard).sqrMagnitude > .04f;
                        }
                    }
                Occlusion.Muffle(occ, out float gain, out float cutoff);
                bool routed = false;
                Vector3 heard = r.To;
                if (pathGain > 0f)
                {
                    float ed = gain * gain, ep = pathGain * pathGain, share = ep / (ed + ep);
                    cutoff = Mathf.Exp((ed * Mathf.Log(cutoff) + ep * Mathf.Log(pathCutoff)) / (ed + ep));
                    gain = Mathf.Min(1f, Mathf.Sqrt(ed + ep));
                    occ = Mathf.Lerp(occ, pathOcc, share);
                    // heard from between the two, leaning to the louder (the HRTF places it there)
                    heard = Vector3.Lerp(r.To, pathAt, share);
                    routed = share > .05f;
                }
                if (routed)
                {
                    if (bestKind == 1) s_portalWins++;
                    else if (bestKind == 2) s_cornerWins++;
                    else if (bestKind == 3)
                    {
                        s_bounceWins++;
                        if (bestIsSecondBounce) s_secondBounceWins++;
                    }
                }
                else if (occ >= .3f) s_noPath++;
                Occlusion.Own = null;
                s_shore = false;
                if (r.Under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); }
                f.Set(gain, cutoff, false);
                f.Routed = routed;
                f.RoutePoint = heard;
                if (occ > .05f && r.Reverb && f.Source != null)
                    f.Source.reverbZoneMix = Mathf.Max(f.Source.reverbZoneMix, Mathf.Min(1f, .3f + .25f * occ));
            }
            s_flightCount = 0;
        }

        // Pending checks dropped (switched off).
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < s_flightCount; i++) s_flight[i].F = null;
            s_flightCount = 0;
            Queue.Clear();
        }

        internal static void Shutdown()
        {
            Flush();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }
    }
}
