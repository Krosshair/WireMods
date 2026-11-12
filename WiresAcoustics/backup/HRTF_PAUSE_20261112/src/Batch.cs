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
    // ray, up to 8. A sound that had walls in the way last time also gets one ray from each of the
    // biggest openings (Portals) to it, and two per point around the blocker (Corners).
    internal static class Batch
    {
        private const int MaxHits = 8;
        private struct Req
        {
            internal AcousticFilter F;
            internal Vector3 To, Listener;
            internal bool Under, Reverb, Corners;
            internal int First, Routes;
            internal Portals.Portal P0, P1;
            internal Vector3 C0, C1, C2, C3, C4;
        }
        private static readonly List<Req> Queue = new List<Req>(64);
        private static readonly Vector3[] Pts = new Vector3[Corners.Count];
        private static Req[] s_flight = new Req[64];
        private static int s_flightCount;
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
            foreach (Req r in Queue) cmds += 1 + (r.Corners ? routes + 2 * Corners.Count : 0);
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
                if (r.Routes > 0) s_cmds[c++] = Cast(r.P0.Point, r.To, q);
                if (r.Routes > 1) s_cmds[c++] = Cast(r.P1.Point, r.To, q);
                s_direct += 1;
                s_openings += r.Routes;
                if (r.Corners)
                {
                    Corners.Around(listener, r.To, r.F != null ? r.F.LastBlockDist : (r.To - listener).magnitude * .5f,
                                   r.F != null ? r.F.LastBlocker : null, Pts);
                    r.C0 = Pts[0]; r.C1 = Pts[1]; r.C2 = Pts[2]; r.C3 = Pts[3]; r.C4 = Pts[4];
                    foreach (Vector3 p in Pts) { s_cmds[c++] = Cast(listener, p, q); s_cmds[c++] = Cast(p, r.To, q); }
                    s_cornerRays += 2 * Corners.Count;
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
        private static readonly RaycastHit[] Line = new RaycastHit[MaxHits];

        private static float Walls(int cmd, Vector3 to, ref Occlusion.Block b) =>
            Walls(cmd, to, out _, out _, ref b);

        // Hits of one ray, and the ridge if the ground was among them (from the ray's own start).
        private static float Walls(int cmd, Vector3 to, out float first,
                                   out Collider blocker, ref Occlusion.Block b)
        {
            bool hadGround = b.Terrain;
            b.Terrain = false;
            float occ = WallHits(cmd, to, out first, out blocker, ref b);
            if (b.Terrain) Occlusion.Ridge(s_cmds[cmd].from, to, ref b);
            b.Terrain |= hadGround;
            return occ;
        }

        private static float WallHits(int cmd, Vector3 to, out float first,
                                   out Collider blocker, ref Occlusion.Block b)
        {
            int n = 0;
            for (int h = 0; h < MaxHits; h++)
            {
                RaycastHit hit = s_hits[cmd * MaxHits + h];
                // the first empty slot ends the ray's hits: Unity doesn't write the ones after it, so
                // they can still hold hits from an earlier batch (RaycastCommand, Unity docs)
                if (hit.collider == null) break;
                if (s_shore && (hit.point - to).sqrMagnitude < 15f * 15f && hit.collider.GetComponentInParent<Heightmap>() != null) continue;
                Line[n++] = hit;
            }
            return Occlusion.Along(Line, n, s_cmds[cmd].from, to, ref b, out first, out blocker);
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
                Occlusion.Block direct = Occlusion.Block.Clear;
                if (on) occ = Walls(r.First, r.To, out first, out firstBlocker, ref direct);
                f.LastBlockDist = first < float.MaxValue ? first : (r.To - r.Listener).magnitude * .5f;
                // The direct sound (through the walls) and the best way round (an opening, a corner)
                // are both heard: neither stops the other. The way round is picked by how loud
                // it arrives, and the two add up by energy.
                float pathGain = 0f, pathCutoff = 22000f, pathOcc = 0f;
                Vector3 pathAt = r.To;
                int bestKind = 0; // 1 opening, 2 corner
                int c = r.First + 1;
                // The result also decides the routing next time.
                f.LastOcc = occ;
                f.LastBlocker = occ >= .3f ? firstBlocker : null;
                // Just gone behind something (stepping down off a wall, the sea behind it): its ways
                // round weren't tried this time, and the wall alone would shut it off for a moment
                // and then open it again. Kept as it was for one more frame, tried with them.
                if (on && occ >= .3f && !r.Corners)
                {
                    Occlusion.Own = null;
                    s_shore = false;
                    Queue.Add(new Req { F = f, To = r.To, Under = r.Under, Reverb = r.Reverb, Corners = true });
                    continue;
                }
                if (occ >= .3f) s_blocked++;
                else s_clear++;
                if (on && occ >= .3f)
                    for (int k = 0; k < r.Routes; k++, c++)
                    {
                        Portals.Portal p = k == 0 ? r.P0 : r.P1;
                        Occlusion.Block leg = Occlusion.Block.Clear;
                        float total = Walls(c, r.To, ref leg);
                        Diffraction.Opening(r.Listener, p.Point, p.Dir, p.Radius, r.To, out float bg, out float bc);
                        if (Consider(total, leg, Portals.LengthGain(r.Listener, p, r.To), bg, bc, p.Point, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                            bestKind = 1;
                    }
                else c += r.Routes;
                if (r.Corners)
                    for (int k = 0; k < Corners.Count; k++, c += 2)
                    {
                        if (!on || f.LastOcc < .3f) continue;
                        Vector3 p = k == 0 ? r.C0 : k == 1 ? r.C1 : k == 2 ? r.C2 : k == 3 ? r.C3 : r.C4;
                        Occlusion.Block leg = Occlusion.Block.Clear;
                        float total = Walls(c, p, ref leg) + Walls(c + 1, r.To, ref leg);
                        Diffraction.Round(r.Listener, p, r.To, out float bg, out float bc);
                        if (Consider(total, leg, Corners.LengthGain(r.Listener, p, r.To), bg, bc, p, ref pathGain, ref pathCutoff, ref pathOcc, ref pathAt))
                            bestKind = 2;
                    }
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
                s_shore = false;
                if (r.Under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); }
                f.Set(gain, cutoff, false);
                f.Routed = routed;
                f.RoutePoint = heard;
                // the reverb send from scratch each check: what gets through, in the room too
                if (r.Reverb && f.Source != null) f.Source.reverbZoneMix = Occlusion.ReverbSend((r.To - r.Listener).magnitude, gain);
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
