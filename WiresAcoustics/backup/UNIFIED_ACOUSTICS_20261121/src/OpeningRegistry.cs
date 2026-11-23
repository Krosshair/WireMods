using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Geometry identifies a window; once identified, it behaves like a
    // static positional sound. Sparse room rays cannot turn it on/off.
    internal static class OpeningRegistry
    {
        internal const int Capacity = 8;
        private const float MatchDistance = 5f;
        private const float NearChange = 3f;
        private const float LeaveRange = 32f;
        private const float LeaveGrace = 3f;
        private const float ChangeGrace = .8f;

        private sealed class Source
        {
            internal int Id;
            internal Vector3 Point, Direction;
            internal float Radius, LastSeen;
            internal int WorldGeneration;
            internal bool Valid;
        }

        private static readonly Source[] Sources = new Source[Capacity];
        private static readonly bool[] Assigned = new bool[Capacity];
        private static int s_nextId;

        // Called on a completed room scan, not on every game frame.
        internal static void Observe(List<Portals.Portal> observations, Vector3 ear)
        {
            System.Array.Clear(Assigned, 0, Capacity);
            float now = Time.time;

            for (int i = 0; i < observations.Count; i++)
            {
                Portals.Portal p = observations[i];
                Vector3 candidate = p.Point + p.Dir;
                int bestSlot = -1;
                float best = MatchDistance * MatchDistance;
                for (int slot = 0; slot < Capacity; slot++)
                {
                    Source s = Sources[slot];
                    if (Assigned[slot] || s == null || !s.Valid) continue;
                    float d2 = (s.Point - candidate).sqrMagnitude;
                    if (d2 < best && Vector3.Dot(s.Direction, p.Dir) > .65f)
                    {
                        bestSlot = slot;
                        best = d2;
                    }
                }
                if (bestSlot < 0)
                {
                    for (int slot = 0; slot < Capacity; slot++)
                    {
                        if (Assigned[slot] || Ambience.SlotSounding(slot)) continue;
                        if (Sources[slot] == null || !Sources[slot].Valid)
                        {
                            bestSlot = slot;
                            break;
                        }
                    }
                }
                if (bestSlot < 0) continue;
                Assigned[bestSlot] = true;
                Source source = Sources[bestSlot];
                if (source == null || !source.Valid)
                {
                    // Do not derive volume from changing ray counts after this
                    // point. The source's physical opening size is fixed.
                    source = Sources[bestSlot] = new Source
                    {
                        Id = ++s_nextId,
                        Point = candidate,
                        Direction = p.Dir,
                        Radius = Mathf.Clamp(p.Radius, .75f, 5f),
                        Valid = true
                    };
                }
                source.LastSeen = now;
                source.WorldGeneration = Occlusion.WorldGen;
            }

            for (int slot = 0; slot < Capacity; slot++)
            {
                Source source = Sources[slot];
                if (source == null || !source.Valid || Assigned[slot]) continue;
                float age = now - source.LastSeen;
                bool geometryChanged = Occlusion.ChangedNear(
                    source.WorldGeneration, ear, source.Point, NearChange);
                float distance2 = (source.Point - ear).sqrMagnitude;

                // A missing room ray is NOT a removal event. Retire on an
                // actual nearby construction/door change or after the listener
                // moves well outside this opening's useful acoustic range.
                if ((geometryChanged && age > ChangeGrace) ||
                    (distance2 > LeaveRange * LeaveRange && age > LeaveGrace))
                    source.Valid = false;
            }
        }

        internal static bool TryGet(int slot, out Vector3 point,
            out Vector3 direction, out float radius)
        {
            Source s = Sources[slot];
            if (s != null && s.Valid)
            {
                point = s.Point;
                direction = s.Direction;
                radius = s.Radius;
                return true;
            }
            point = direction = Vector3.zero;
            radius = 0f;
            return false;
        }

        internal static string Summary()
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++)
                if (Sources[i] != null && Sources[i].Valid) count++;
            if (!Plugin.DebugLog.Value) return "anchored openings " + count;
            var result = new System.Text.StringBuilder("anchored openings ");
            result.Append(count);
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid) continue;
                result.Append(" [").Append(s.Id).Append(" at ")
                    .Append(s.Point.ToString("F1")).Append(", radius ")
                    .Append(s.Radius.ToString("F1")).Append(", age ")
                    .Append((Time.time - s.LastSeen).ToString("F1")).Append("s]");
            }
            return result.ToString();
        }

        internal static void Reset()
        {
            for (int i = 0; i < Capacity; i++) Sources[i] = null;
        }
    }
}
