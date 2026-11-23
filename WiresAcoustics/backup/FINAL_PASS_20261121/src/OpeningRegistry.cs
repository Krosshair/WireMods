using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // A stable source registry shared by the environmental propagation paths.
    // Observation is not source creation/destruction: a room ray can miss a
    // perfectly real opening without affecting its current audio.
    internal static class OpeningRegistry
    {
        internal const int Capacity = 8;
        private const float NearbySameOpening = 2.25f;
        private const float SupportMatchRange = 2.75f;
        private const float LeaveRange = 65f;
        private const float ChangeGrace = 1.5f;

        private sealed class Source
        {
            internal int Id, SupportId, Generation;
            internal Collider Support;
            internal Vector3 Point, Normal;
            internal float Radius, LastSeen;
            internal bool Valid, ChangePending;
            internal float ChangeTime;
        }

        private static readonly Source[] Sources = new Source[Capacity];
        private static readonly bool[] Matched = new bool[Capacity];
        private static int s_nextId, s_births, s_deaths, s_reused;

        // Called only when the main room raycast batch has completed.
        internal static void Observe(List<Portals.Portal> observations, Vector3 listener)
        {
            float now = Time.time;
            System.Array.Clear(Matched, 0, Matched.Length);
            // First pass: match all existing world sources before admitting
            // new candidates. This prevents current ray ranking from swapping
            // two occupied audio slots.
            for (int i = 0; i < observations.Count; i++)
            {
                Portals.Portal p = observations[i];
                int best = Match(p);
                if (best < 0) continue;
                Matched[best] = true;
                Source s = Sources[best];
                s.LastSeen = now;
                s.ChangePending = false;
                // Point, normal and aperture area are not changed by
                // listener-relative sampling. Only real scene events can
                // trigger their reconstruction.
            }

            // Second pass: create independent, world-anchored sources only
            // for genuinely new geometry. Sources cannot steal a fading
            // audio slot, including when a larger opening changes rank.
            for (int i = 0; i < observations.Count; i++)
            {
                Portals.Portal p = observations[i];
                if (FindExisting(p) >= 0) continue;
                if (p.Support == null || p.Radius <= .05f) continue;
                int free = -1;
                for (int j = 0; j < Capacity; j++)
                {
                    if (Sources[j] != null && Sources[j].Valid) continue;
                    if (Ambience.SlotSounding(j)) continue;
                    free = j;
                    break;
                }
                if (free < 0)
                {
                    // On very large builds retain nearer windows rather than
                    // letting eight old, distant sources monopolize the slots.
                    // Retire a distant one; do not reuse its audio slot until
                    // its existing copy has faded completely.
                    int farthest = -1;
                    float maxDistance2 = 28f * 28f;
                    for (int j = 0; j < Capacity; j++)
                    {
                        Source old = Sources[j];
                        if (old == null || !old.Valid || Matched[j]) continue;
                        float distance2 = (old.Point - listener).sqrMagnitude;
                        if (distance2 <= maxDistance2) continue;
                        maxDistance2 = distance2;
                        farthest = j;
                    }
                    if (farthest >= 0) Retire(Sources[farthest]);
                    break;
                }
                Sources[free] = new Source {
                    Id = ++s_nextId,
                    Support = p.Support,
                    SupportId = p.SupportId,
                    Point = p.Point + p.Dir,
                    Normal = p.Dir,
                    Radius = Mathf.Clamp(p.Radius, .5f, 5f),
                    LastSeen = now,
                    Generation = Occlusion.WorldGen,
                    Valid = true
                };
                Matched[free] = true;
                s_births++;
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("[acoustics] aperture born id=" +
                        Sources[free].Id + " support=" +
                        Sources[free].SupportId + " point=" +
                        Sources[free].Point.ToString("F2") + " radius=" +
                        Sources[free].Radius.ToString("F2"));
            }

            for (int j = 0; j < Capacity; j++)
            {
                Source s = Sources[j];
                if (s == null || !s.Valid) continue;
                if (s.Support == null)
                {
                    Retire(s);
                    continue;
                }
                if (Matched[j]) continue;
                // Structural identity persists across missing listener rays.
                // If its actual supporting collider disappeared, retire.
                if (s.Support == null)
                {
                    Retire(s);
                    continue;
                }
                // A local build/door event is allowed to invalidate geometry,
                // but gives asynchronous room observations time to settle.
                // Simply moving or crossing the ray grid never does.
                if (s.ChangePending && now - s.ChangeTime >= ChangeGrace)
                {
                    Retire(s);
                    continue;
                }
                if ((s.Point - listener).sqrMagnitude > LeaveRange * LeaveRange
                    && now - s.LastSeen > 3f)
                    Retire(s);
            }
        }

        private static int FindExisting(Portals.Portal p)
        {
            Vector3 candidate = p.Point + p.Dir;
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid) continue;
                if (Vector3.Dot(s.Normal, p.Dir) < .50f) continue;
                float distance2 = (s.Point - candidate).sqrMagnitude;
                float threshold = s.SupportId != 0 &&
                    p.SupportId == s.SupportId
                    ? SupportMatchRange : NearbySameOpening;
                if (distance2 <= threshold * threshold) return i;
            }
            return -1;
        }

        private static int Match(Portals.Portal p)
        {
            Vector3 candidate = p.Point + p.Dir;
            int best = -1;
            float distance = float.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid || Matched[i]) continue;
                if (Vector3.Dot(s.Normal, p.Dir) < .50f) continue;
                float d2 = (s.Point - candidate).sqrMagnitude;
                float range = s.SupportId != 0 && p.SupportId == s.SupportId
                    ? SupportMatchRange : NearbySameOpening;
                if (d2 > range * range || d2 >= distance) continue;
                best = i;
                distance = d2;
            }
            if (best >= 0) s_reused++;
            return best;
        }

        private static void Retire(Source s)
        {
            if (!s.Valid) return;
            s.Valid = false;
            s_deaths++;
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("[acoustics] aperture retired id=" +
                    s.Id + " point=" + s.Point.ToString("F2") +
                    " age=" + (Time.time - s.LastSeen).ToString("F2"));
        }

        // Called from the already-existing build/door event path. This is
        // location-scoped, not a global reset when ANY door changes.
        internal static void GeometryChanged(Vector3 at, bool everywhere)
        {
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid) continue;
                if (!everywhere &&
                    (s.Point - at).sqrMagnitude > 4f * 4f) continue;
                s.ChangePending = true;
                s.ChangeTime = Time.time;
            }
        }

        internal static bool TryGet(int index, out int id, out Vector3 point,
            out Vector3 normal, out float radius)
        {
            Source s = Sources[index];
            if (s != null && s.Valid)
            {
                id = s.Id;
                point = s.Point;
                normal = s.Normal;
                radius = s.Radius;
                return true;
            }
            id = 0;
            point = normal = Vector3.zero;
            radius = 0f;
            return false;
        }

        internal static string Summary()
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++)
                if (Sources[i] != null && Sources[i].Valid) count++;
            if (!Plugin.DebugLog.Value)
                return "opening sources " + count;
            var sb = new System.Text.StringBuilder("opening sources ");
            sb.Append(count).Append(", born ").Append(s_births)
              .Append(", retired ").Append(s_deaths)
              .Append(", matched ").Append(s_reused);
            for (int i = 0; i < Capacity; i++)
            {
                Source s = Sources[i];
                if (s == null || !s.Valid) continue;
                sb.Append(" [").Append(s.Id).Append(" support ")
                  .Append(s.SupportId).Append(" ")
                  .Append(s.Point.ToString("F1")).Append(" radius ")
                  .Append(s.Radius.ToString("F1")).Append(" age ")
                  .Append((Time.time - s.LastSeen).ToString("F1"))
                  .Append("s]");
            }
            s_births = s_deaths = s_reused = 0;
            return sb.ToString();
        }

        internal static void Reset()
        {
            for (int i = 0; i < Capacity; i++) Sources[i] = null;
            s_births = s_deaths = s_reused = 0;
        }
    }
}
