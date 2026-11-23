using UnityEngine;
using Unity.Collections;

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
        // 24 bearings at two ground distances cover the full circle from
        // the character's ears. They do not depend on camera facing or
        // on sparse generic room rays hitting a tiny distant window.
        // Each target gets a line-of-sight cast and a vertical shelter cast,
        // batched with the existing asynchronous room physics job.
        internal const int GroundBearings = 24;
        internal const int GroundRings = 2;
        private const int PortalGroundProbes = 4;
        internal const int GroundSamples =
            GroundBearings * GroundRings + PortalGroundProbes;
        internal const int GroundCommands = GroundSamples * 2;
        private const float GroundNear = 4.5f, GroundFar = 10f;
        private static readonly Vector3[] GroundPoints = new Vector3[GroundSamples];
        private static readonly bool[] GroundValid = new bool[GroundSamples];
        private static readonly bool[] GroundOpen = new bool[GroundSamples];
        private static int s_groundCount;
        private static float s_groundNearest = -1f;
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

        // Visible outdoor directions and a previously identified opening
        // are independent evidence of the same exterior acoustic path.
        // A single cached window must NEVER suppress a much wider arch
        // measured by the room rays. No extra source or ray is required.
        internal static float CombineAccess(float openHorizon, float cachedOpening)
        {
            return Mathf.Clamp01(2f * Mathf.Max(
                Mathf.Clamp01(openHorizon),
                Mathf.Clamp01(cachedOpening)));
        }

        // A visible outdoor ground patch is an actual acoustic path from
        // where rain hits the terrain, regardless of the listener's facing.
        // This result is only used after the two independent ray tests prove
        // the path is unobstructed AND the patch isn't under built shelter.
        // At 5m the gain is near outdoor level; a distant ground patch is
        // quieter. The full room-ray calculation remains the fallback.
        internal static float GroundAccess(float distance)
        {
            return Mathf.Clamp01(1.2f /
                (1f + Mathf.Max(0f, distance - 2f) / 10f));
        }

        // The only new physics work: 104 commands in the SAME asynchronous
        // batch as the existing 192 room rays, and only during rain. Ground
        // height uses Heightmap's cached mesh data, NOT ZoneSystem's API
        // (which internally casts a synchronous physics ray per sample).
        // All commands are always initialized; disabled slots are zero
        // length and cannot leak stale results on a later survey.
        internal static void ScheduleGroundSight(Vector3 ear,
            NativeArray<RaycastCommand> cmds, int offset,
            QueryParameters query, bool raining)
        {
            bool measure = raining;
            for (int i = 0; i < GroundSamples; i++)
            {
                GroundValid[i] = false;
                GroundOpen[i] = false;
                Vector3 point = ear;
                if (measure)
                {
                    bool candidate = false;
                    if (i < GroundBearings * GroundRings)
                    {
                        // Offset the two rings by half the angular step:
                        // combined rays cover 7.5-degree increments,
                        // including oblique views through narrow openings.
                        int bearing = i % GroundBearings;
                        bool near = i < GroundBearings;
                        float a = 2f * Mathf.PI *
                            (bearing + (near ? .25f : .75f)) / GroundBearings;
                        float radius = near ? GroundNear : GroundFar;
                        point.x += Mathf.Cos(a) * radius;
                        point.z += Mathf.Sin(a) * radius;
                        candidate = true;
                    }
                    else
                    {
                        // Aim four further ground paths directly at known
                        // doorway/window openings, regardless of their
                        // angle relative to the uniform probe bearings.
                        int portal = i - GroundBearings * GroundRings;
                        if (portal < Portals.List.Count)
                        {
                            Portals.Portal aperture = Portals.List[portal];
                            Vector3 horizontal = aperture.Point - ear;
                            horizontal.y = 0f;
                            if (horizontal.sqrMagnitude > .0001f)
                            {
                                float radius = Mathf.Clamp(
                                    horizontal.magnitude + 3f,
                                    GroundNear, 15f);
                                point += horizontal.normalized * radius;
                                candidate = true;
                            }
                        }
                    }
                    if (candidate &&
                        Heightmap.GetHeight(point, out float ground))
                    {
                        // Rain IMPACTS the actual terrain below this
                        // outdoor patch, but the listener only needs an
                        // unobstructed view to its OPEN AIR. Requiring a
                        // straight ray down to the ground would incorrectly
                        // hit the sill or floor of an elevated room even
                        // when an open arch plainly shows the outdoors.
                        point.y = ground + .25f;
                        GroundPoints[i] = point;
                        Vector3 air = new Vector3(point.x,
                            Mathf.Max(ear.y, ground + 1.2f), point.z);
                        Vector3 to = air - ear;
                        float length = to.magnitude;
                        if (length > 1.2f)
                        {
                            GroundValid[i] = true;
                            cmds[offset + i * 2] = new RaycastCommand(
                                ear, to / length, query,
                                Mathf.Max(.01f, length - .25f));
                            cmds[offset + i * 2 + 1] = new RaycastCommand(
                                air + Vector3.up * .25f,
                                Vector3.up, query, 20f);
                            continue;
                        }
                    }
                }
                cmds[offset + i * 2] = new RaycastCommand(
                    point, Vector3.up, query, 0f);
                cmds[offset + i * 2 + 1] = new RaycastCommand(
                    point, Vector3.up, query, 0f);
            }
        }

        internal static void ReadGroundSight(Vector3 ear,
            NativeArray<RaycastHit> hits, int offset)
        {
            s_groundCount = 0;
            s_groundNearest = -1f;
            for (int i = 0; i < GroundSamples; i++)
            {
                if (!GroundValid[i]) { GroundOpen[i] = false; continue; }
                RaycastHit direct = hits[offset + i * 2];
                // If the line from the character to an actual outdoor
                // ground patch hits a wall, floor, railing, or rock first,
                // that patch has no direct audible path.
                if (direct.colliderInstanceID != 0)
                {
                    GroundOpen[i] = false;
                    continue;
                }
                RaycastHit overhead = hits[offset + i * 2 + 1];
                if (overhead.colliderInstanceID != 0)
                {
                    Solids.Info s = Solids.Of(overhead);
                    // Unbuilt vegetation does not make rain stop falling
                    // on the ground. A roof, floor, stone ceiling, or other
                    // constructed shelter does.
                    if (s == null || (!s.Tree && (s.Built || s.Structural)))
                    { GroundOpen[i] = false; continue; }
                }
                GroundOpen[i] = true;
                s_groundCount++;
                float d = (GroundPoints[i] - ear).magnitude;
                if (s_groundNearest < 0f || d < s_groundNearest)
                    s_groundNearest = d;
            }
        }

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
            float nearestOpening = 0f;
            for (int i = 0; i < s_count; i++)
            {
                Aperture a = Openings[i];
                nearestOpening = Mathf.Max(nearestOpening,
                    WindowAccess(a.Radius, (a.Point - listener).magnitude));
            }
            // A broad open arch can expose much more outside than the
            // four cached finite-window centres. Preserve BOTH signals.
            // Previously one cached little window discarded ALL measured
            // horizon openness and made large open halls sound sealed.
            float groundAccess = 0f;
            for (int i = 0; i < GroundSamples; i++)
                if (GroundOpen[i])
                    groundAccess = Mathf.Max(groundAccess,
                        GroundAccess((GroundPoints[i] - listener).magnitude));
            // Ground line-of-sight is a direct acoustic path in its own
            // right: never gate it on room enclosure, roof fraction,
            // camera facing, or the portal clustering heuristic.
            s_windowAccess = Mathf.Max(
                CombineAccess(s_openFraction, nearestOpening),
                groundAccess);
            return s_windowAccess;
        }

        internal static string Summary() =>
            "rain geometry: cached windows " + s_count +
            ", visible ground " + s_groundCount +
            ", nearest ground " + s_groundNearest.ToString("F1") + "m" +
            ", open-horizon " + s_openFraction.ToString("F2") +
            ", single-loop opening access " + s_windowAccess.ToString("F2");

        internal static void Reset()
        {
            s_count = 0; s_worldGen = s_doorGen = -1;
            s_ready = false; s_openFraction = s_windowAccess = 0f;
            s_groundCount = 0; s_groundNearest = -1f;
            for (int i = 0; i < GroundSamples; i++)
                GroundValid[i] = GroundOpen[i] = false;
        }
    }
}
