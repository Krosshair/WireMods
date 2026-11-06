using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Wall checks for sounds already playing (looping sounds once a second, voice chat 5 times a
    // second), batched onto worker threads like the room rays: queued during the frame, cast together,
    // read the next frame. A new sound still checks at once (a frame late would let its start through
    // the wall). All hits along each ray, up to 8.
    internal static class Batch
    {
        private const int MaxHits = 8;
        private struct Req { internal AcousticFilter F; internal Vector3 To; internal bool Under, Reverb; }
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
            if (!s_cmds.IsCreated || s_cmds.Length < n)
            {
                if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(64, n));
                s_cmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(cap * MaxHits, Allocator.Persistent);
                s_flight = new Req[cap];
            }
            var q = new QueryParameters(Room.Mask, false, QueryTriggerInteraction.Ignore, false);
            for (int i = 0; i < n; i++)
            {
                Req r = Queue[i];
                s_flight[i] = r;
                Vector3 d = r.To - listener;
                float dist = d.magnitude;
                s_cmds[i] = dist > Occlusion.SourceClearance + .1f
                    ? new RaycastCommand(listener, d / dist, q, dist - Occlusion.SourceClearance)
                    : new RaycastCommand(listener, Vector3.up, q, 0f);
            }
            s_flightCount = n;
            Queue.Clear();
            s_job = RaycastCommand.ScheduleBatch(s_cmds.GetSubArray(0, n), s_hits.GetSubArray(0, n * MaxHits), 8, MaxHits);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void Apply()
        {
            for (int i = 0; i < s_flightCount; i++)
            {
                Req r = s_flight[i];
                s_flight[i].F = null;
                AcousticFilter f = r.F;
                if (f == null) continue;
                float occ = 0f;
                for (int h = 0; h < MaxHits; h++)
                {
                    Collider c = s_hits[i * MaxHits + h].collider;
                    if (c != null) occ += Occlusion.Counts(c, r.To);
                }
                if (!Plugin.Occlusion.Value) occ = 0f;
                Occlusion.Muffle(occ, out float gain, out float cutoff);
                if (r.Under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); }
                f.Set(gain, cutoff, false);
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
