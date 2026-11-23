using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Persistent window/door acoustic paths. Room's sparse rays are observations,
    // not the lifetime or volume of the sound source.
    // All jobs use reusable native buffers; no independent Update or coroutine.
    internal static class OpeningRegistry
    {
        internal const int Capacity = 8;
        private const float MatchDistance = 5f;
        private const float ObservationGrace = 1.6f;
        private const float NearChange = 3f;

        private sealed class Source
        {
            internal int Id;
            internal Vector3 Point, Direction;
            internal float Radius, Share, LastSeen;
            internal float Transmission = 1f;
            internal int WorldGeneration;
            internal bool Valid;
        }

        private static readonly Source[] Sources = new Source[Capacity];
        private static int s_nextId;
        private static readonly bool[] Assigned = new bool[Capacity];
        private static NativeArray<RaycastCommand> s_commands;
        private static NativeArray<RaycastHit> s_hits;
        private static readonly int[] FlightIds = new int[Capacity];
        private static JobHandle s_job;
        private static bool s_pending;
        private static float s_lastScheduled = -100f;

        // Called after the room geometry results have been measured.
        internal static void Observe(List<Portals.Portal> observations, Vector3 ear)
        {
            float now = Time.time;
            System.Array.Clear(Assigned, 0, Capacity);
            bool[] assigned = Assigned;
            for (int i = 0; i < observations.Count; i++)
            {
                Portals.Portal p = observations[i];
                Vector3 candidate = p.Point + p.Dir;
                int bestSlot = -1;
                float best = MatchDistance * MatchDistance;
                for (int slot = 0; slot < Capacity; slot++)
                {
                    Source s = Sources[slot];
                    if (assigned[slot] || s == null || !s.Valid) continue;
                    float d2 = (s.Point - candidate).sqrMagnitude;
                    if (d2 >= best || Vector3.Dot(s.Direction, p.Dir) < .65f) continue;
                    best = d2;
                    bestSlot = slot;
                }
                if (bestSlot < 0)
                {
                    for (int slot = 0; slot < Capacity; slot++)
                        if (!assigned[slot] && !Ambience.SlotSounding(slot) &&
                            (Sources[slot] == null || !Sources[slot].Valid))
                        { bestSlot = slot; break; }
                }
                if (bestSlot < 0) continue;
                assigned[bestSlot] = true;
                Source existing = Sources[bestSlot];
                if (existing == null || !existing.Valid)
                {
                    existing = Sources[bestSlot] = new Source
                    {
                        Id = ++s_nextId,
                        Point = candidate,
                        Direction = p.Dir,
                        Transmission = 1f,
                        Valid = true
                    };
                }
                // The world anchor does not chase the listener's ray intersections.
                // Only the measurement confidence / apparent aperture size updates.
                existing.LastSeen = now;
                existing.Share = p.Share;
                existing.Radius = p.Radius;
                existing.WorldGeneration = Occlusion.WorldGen;
            }

            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid || assigned[i]) continue;
                bool changedHere = Occlusion.ChangedNear(s.WorldGeneration,
                    ear, s.Point, NearChange);
                bool clearPath = s.Transmission > .95f &&
                    (s.Point - ear).sqrMagnitude < 24f * 24f;
                // A known, unobstructed aperture can disappear from a sparse
                // room-ray sample even while clearly in front of the listener.
                // Keep it while direct geometry confirms the path. A newly
                // blocked path expires after a short observation grace.
                if (!clearPath && (changedHere ||
                    now - s.LastSeen >= ObservationGrace))
                    s.Valid = false;
            }
            Schedule(ear);
        }

        private static void Ensure()
        {
            if (s_commands.IsCreated) return;
            s_commands = new NativeArray<RaycastCommand>(Capacity, Allocator.Persistent);
            s_hits = new NativeArray<RaycastHit>(Capacity, Allocator.Persistent);
        }

        private static void Schedule(Vector3 listener)
        {
            if (s_pending || Time.time - s_lastScheduled < .12f) return;
            Ensure();
            var query = new QueryParameters(Room.Mask, false, QueryTriggerInteraction.Ignore, false);
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                bool valid = s != null && s.Valid;
                Vector3 delta = valid ? s.Point - listener : Vector3.up;
                float distance = delta.magnitude;
                FlightIds[i] = valid ? s.Id : 0;
                s_commands[i] = new RaycastCommand(listener,
                    distance > .1f ? delta / distance : Vector3.up, query,
                    valid ? Mathf.Max(.01f, distance - .1f) : .01f);
            }
            s_job = RaycastCommand.ScheduleBatch(s_commands, s_hits, 4, 1);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
            s_lastScheduled = Time.time;
        }

        // Called from the already-existing AudioMan.Update hook.
        internal static void CompleteReady()
        {
            if (!s_pending || !s_job.IsCompleted) return;
            s_job.Complete();
            s_pending = false;
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid || s.Id != FlightIds[i]) continue;
                // An obstruction changes how sound is heard, not whether its
                // world-space source exists. The diffuse indoor bed remains.
                s.Transmission = s_hits[i].colliderInstanceID == 0 ? 1f : .18f;
            }
        }

        internal static bool TryGet(int slot, out Vector3 point, out Vector3 dir,
            out float radius, out float transmission)
        {
            Source s = Sources[slot];
            if (s != null && s.Valid)
            {
                point = s.Point;
                dir = s.Direction;
                radius = s.Radius;
                transmission = s.Transmission;
                return true;
            }
            point = dir = Vector3.zero;
            radius = transmission = 0f;
            return false;
        }

        internal static string Summary()
        {
            int n = 0, blocked = 0;
            for (int i = 0; i < Capacity; i++)
                if (Sources[i] != null && Sources[i].Valid)
                {
                    n++;
                    if (Sources[i].Transmission < .5f) blocked++;
                }
            if (!Plugin.DebugLog.Value)
                return "tracked openings " + n;
            var summary = new System.Text.StringBuilder("openings ");
            summary.Append(n).Append(" active, ").Append(blocked)
                .Append(" obstructed");
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid) continue;
                summary.Append(" [").Append(s.Id).Append(":")
                    .Append(s.Point.ToString("F1"))
                    .Append(" age ").Append((Time.time - s.LastSeen).ToString("F1"))
                    .Append("s, transmit ").Append(s.Transmission.ToString("F2"))
                    .Append("]");
            }
            return summary.ToString();
        }

        internal static void Reset()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < Capacity; i++) Sources[i] = null;
            if (s_commands.IsCreated) s_commands.Dispose();
            if (s_hits.IsCreated) s_hits.Dispose();
            s_lastScheduled = -100f;
        }
    }
}
