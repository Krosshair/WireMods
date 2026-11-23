using UnityEngine;

namespace WiresAcoustics
{
    // A rain exposure estimate, NOT a rain sound generator.
    // Valheim's SINGLE existing ambient-loop AudioSource supplies all rain
    // audio. Our existing AcousticFilter changes that one recording's gain
    // and spectrum according to the room and its measured outdoor openings.
    //
    // Apertures are derived from the existing cached 64-direction room
    // survey; listener distance is evaluated cheaply in the already
    // invoked AudioMan.Update. There are no extra sources, duplicated clips,
    // native physics calls, new frame hooks, or rain-bridge AudioSources.
    internal static class RainField
    {
        internal const int Sectors = 8;
        private const int MaxApertures = 4;

        private struct Aperture
        {
            internal Vector3 Point;
            internal float Radius;
        }

        private static readonly Aperture[] Openings = new Aperture[MaxApertures];
        private static int s_count, s_worldGen = -1, s_doorGen = -1;
        private static bool s_ready;
        private static float s_openFraction, s_windowAccess;

        internal static int Sector(Vector3 d)
        {
            float a = Mathf.Atan2(d.z, d.x);
            if (a < 0f) a += 2f * Mathf.PI;
            return Mathf.Min(Sectors - 1, (int)(a * Sectors / (2f * Mathf.PI)));
        }

        internal static bool ImpactRay(Vector3 d) =>
            d.y >= -.24f && d.y <= .65f;

        internal static float Exposure(float open, float total) =>
            total > .001f ? Mathf.Sqrt(Mathf.Clamp01(open / total)) : 0f;

        // Geometrical approximation for nearby outside exposure. Continuous
        // as the listener walks; far-away paths return to structural filtering.
        internal static float WindowAccess(float radius, float distance)
        {
            if (radius <= 0f) return 0f;
            radius = Mathf.Clamp(radius, .15f, 2.5f);
            distance = Mathf.Max(0f, distance);
            float perspective = radius / (radius + distance);
            float fade = Mathf.Clamp01((14f - distance) / 6f);
            fade = fade * fade * (3f - 2f * fade);
            return perspective * fade;
        }

        internal static void Measure(Vector3 from, Vector3[] dirs)
        {
            float outside = 0f, total = 0f;
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 d = dirs[i];
                if (!ImpactRay(d)) continue;
                float w = 1f - .35f * Mathf.Abs(d.y);
                total += w;
                if (Room.Out(i)) outside += w;
            }
            s_openFraction = Exposure(outside, total);

            // Cached doors/windows survive an occasional missing room ray.
            // Real door/build geometry changes explicitly invalidate them.
            int world = Occlusion.WorldGen, door = Occlusion.DoorGen;
            if (s_worldGen != world || s_doorGen != door)
            {
                s_worldGen = world;
                s_doorGen = door;
                s_count = 0;
            }
            for (int i = 0; i < Portals.List.Count; i++)
            {
                Portals.Portal opening = Portals.List[i];
                if (opening.Radius <= .01f) continue;
                int match = -1;
                float dist = 9f;
                for (int j = 0; j < s_count; j++)
                {
                    float d = (Openings[j].Point - opening.Point).sqrMagnitude;
                    if (d < dist) { dist = d; match = j; }
                }
                if (match >= 0)
                {
                    Aperture prev = Openings[match];
                    prev.Point = Vector3.Lerp(prev.Point, opening.Point, .2f);
                    prev.Radius = Mathf.Lerp(prev.Radius,
                        Mathf.Clamp(opening.Radius, .15f, 2.5f), .2f);
                    Openings[match] = prev;
                }
                else
                {
                    if (s_count >= MaxApertures)
                    {
                        float farthest = -1f;
                        int farthestIndex = -1;
                        for (int j = 0; j < s_count; j++)
                        {
                            float d = (Openings[j].Point - from).sqrMagnitude;
                            if (d > farthest) { farthest = d; farthestIndex = j; }
                        }
                        float incoming = (opening.Point - from).sqrMagnitude;
                        if (incoming + 4f >= farthest) continue;
                        match = farthestIndex;
                    }
                    else match = s_count++;
                    Openings[match] = new Aperture {
                        Point = opening.Point,
                        Radius = Mathf.Clamp(opening.Radius, .15f, 2.5f)
                    };
                }
            }
            s_ready = true;
        }

        internal static float Access(Vector3 listener)
        {
            if (!s_ready) return 0f;
            float exposed = 0f;
            if (s_count == 0) exposed = s_openFraction;
            else
                for (int i = 0; i < s_count; i++)
                {
                    Aperture a = Openings[i];
                    exposed = Mathf.Max(exposed,
                        WindowAccess(a.Radius, (a.Point - listener).magnitude));
                }
            // Open-window approach should brighten a single recording
            // continuously, never start a spatial copy or erase the bed.
            s_windowAccess = Mathf.Clamp01(exposed * 2f);
            return s_windowAccess;
        }

        internal static string Summary() =>
            "rain geometry: cached windows " + s_count +
            ", single-loop opening access " + s_windowAccess.ToString("F2");

        internal static void Reset()
        {
            s_count = 0; s_worldGen = s_doorGen = -1;
            s_ready = false; s_openFraction = s_windowAccess = 0f;
        }
    }
}
