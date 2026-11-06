using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Wall checks for sounds already playing (looping sounds once a second, voice chat 5 times a
    // second), batched onto worker threads like the room rays: queued during the frame, cast together,
    // read the next frame. A new sound still checks at once (a frame late would let its start through
    // the wall). All hits along each ray, up to 8. Each sound also gets one ray from each of the
    // biggest openings (Portals) to it, used if the direct line has walls in it.
    internal static class Batch
    {
        private const int MaxHits = 8;
        private struct Req
        {
            internal AcousticFilter F;
            internal Vector3 To, Listener;
            internal bool Under, Reverb;
            internal int First, Routes;
            internal Portals.Portal P0, P1;
        }
        private static readonly List<Req> Queue = new List<Req>(64);
        private static Req[] s_flight = new Req[64];
        private static int s_flightCount;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;

        internal static void Enqueue(AcousticFilter f, Vector3 to, bool under, bool reverb) =>
            Queue.Add(new Req { F = f, To = to, Under = under, Reverb = reverb });

        // Every frame, from the game's audio update.
        internal static void Step(Vector3 listener)
        {
            if (s_pending) { s_job.Complete(); s_pending = false; Apply(); }
            int n = Queue.Count;
            if (n == 0) return;
            int routes = Mathf.Min(Portals.Routes, Portals.List.Count);
            int cmds = n * (1 + routes);
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
                r.Routes = routes;
                if (routes > 0) r.P0 = Portals.List[0];
                if (routes > 1) r.P1 = Portals.List[1];
                s_cmds[c++] = Cast(listener, r.To, q);
                if (routes > 0) s_cmds[c++] = Cast(r.P0.Point, r.To, q);
                if (routes > 1) s_cmds[c++] = Cast(r.P1.Point, r.To, q);
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

        private static float Walls(int cmd, Vector3 to)
        {
            float occ = 0f;
            for (int h = 0; h < MaxHits; h++)
            {
                Collider c = s_hits[cmd * MaxHits + h].collider;
                if (c != null) occ += Occlusion.Counts(c, to);
            }
            return occ;
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
                float occ = on ? Walls(r.First, r.To) : 0f, lengthGain = 1f;
                bool routed = false;
                Vector3 heard = r.To;
                if (on && occ >= .3f)
                    for (int k = 0; k < r.Routes; k++)
                    {
                        Portals.Portal p = k == 0 ? r.P0 : r.P1;
                        float total = Walls(r.First + 1 + k, r.To) + Portals.Bend(p, r.To);
                        if (total < occ - .1f)
                        {
                            occ = total;
                            lengthGain = Portals.LengthGain(r.Listener, p, r.To);
                            heard = p.Point;
                            routed = true;
                        }
                    }
                Occlusion.Muffle(occ, out float gain, out float cutoff);
                gain *= lengthGain;
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
