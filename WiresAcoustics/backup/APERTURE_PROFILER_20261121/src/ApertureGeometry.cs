using UnityEngine;

namespace WiresAcoustics
{
    // An expensive operation only at first discovery or on a local build event.
    // Approximate the PHYSICAL gap in a wall plane by checking short rays
    // through the plane at fixed world positions. Unlike listener-based room
    // visibility, this grid is independent of where the listener is standing.
    internal static class ApertureGeometry
    {
        private const int Half = 4;
        private const float Step = .55f;
        private const float Inset = .9f, Span = 1.8f;
        private static readonly RaycastHit[] Hits = new RaycastHit[12];
        private struct Negative
        {
            internal int Support, X, Y, Z, Gen;
            internal float At;
        }
        private static readonly Negative[] Rejected = new Negative[128];
        private static int s_rejectNext;
        private const float RejectSeconds = 2f, CellSize = 1.2f;

        // A candidate that just proved to be a hairline seam or solid wall
        // should not be re-probed with 81 synchronous physics rays every time
        // the 64-direction listener sensor sees the same gap.
        internal static bool RecentlyRejected(Portals.Portal p)
        {
            int x = Mathf.FloorToInt(p.Point.x / CellSize);
            int y = Mathf.FloorToInt(p.Point.y / CellSize);
            int z = Mathf.FloorToInt(p.Point.z / CellSize);
            int gen = Occlusion.WorldGen;
            for (int i = 0; i < Rejected.Length; i++)
            {
                Negative r = Rejected[i];
                if (r.Support != p.SupportId || r.X != x ||
                    r.Y != y || r.Z != z || r.Gen != gen) continue;
                if (Time.time - r.At < RejectSeconds) return true;
            }
            return false;
        }

        private static void Reject(Portals.Portal p)
        {
            Rejected[s_rejectNext++ % Rejected.Length] = new Negative {
                Support = p.SupportId,
                X = Mathf.FloorToInt(p.Point.x / CellSize),
                Y = Mathf.FloorToInt(p.Point.y / CellSize),
                Z = Mathf.FloorToInt(p.Point.z / CellSize),
                Gen = Occlusion.WorldGen,
                At = Time.time
            };
        }

        internal static void Reset()
        {
            System.Array.Clear(Rejected, 0, Rejected.Length);
            s_rejectNext = 0;
        }

        internal static bool Resolve(Portals.Portal candidate, out Vector3 center,
            out Vector3 normal, out float radius)
        {
            center = candidate.Point + candidate.Dir;
            normal = candidate.Dir.normalized;
            radius = Mathf.Clamp(candidate.Radius, .5f, 5f);
            if (candidate.Support == null || normal.sqrMagnitude < .5f)
                return false;

            // A world-fixed tangent basis: the up axis never depends on the
            // listener and therefore cannot rotate an existing window.
            Vector3 right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < .01f)
                right = Vector3.Cross(Vector3.forward, normal);
            right.Normalize();
            Vector3 up = Vector3.Cross(normal, right).normalized;

            // Use the nearby structural collider to anchor the sampling
            // plane. The room-ray estimate is only the INITIAL discovery hint.
            // Moving the listener does not affect these samples.
            Vector3 face = candidate.Support.ClosestPoint(candidate.Point);
            if ((face - candidate.Point).sqrMagnitude < 4f)
            {
                // Align only along the wall normal. Moving laterally to the
                // nearest solid point would turn an opening into a wall sound.
                float planeOffset = Vector3.Dot(face - candidate.Point, normal);
                center = candidate.Point + normal * (planeOffset + .10f);
            }

            // If the middle of the hypothesized aperture is actually
            // structural wall, reject this candidate rather than invent a
            // false opening around the nearby edge.
            if (Blocked(center, normal))
            {
                Reject(candidate);
                return false;
            }
            int clear = 0;
            float dx = 0f, dy = 0f;
             for (int x = -Half; x <= Half; x++)
            for (int y = -Half; y <= Half; y++)
            {
                Vector3 sample = center + right * (x * Step) + up * (y * Step);
                if (Blocked(sample, normal)) continue;
                clear++;
                dx += x * Step;
                dy += y * Step;
             }
            // Sparse gaps between building pieces should not become full
            // acoustic windows just because a single room ray escaped.
            if (clear < 3)
            {
                Reject(candidate);
                return false;
            }
            center += right * (dx / clear) + up * (dy / clear);
            // Effective area is based on verified clear sampling cells,
            // rather than angular ray counts that change with the listener.
            float area = clear * Step * Step;
            radius = Mathf.Clamp(Mathf.Sqrt(area / Mathf.PI), .35f, 3.2f);
            return true;
        }

        private static bool Blocked(Vector3 center, Vector3 direction)
        {
            int count = Physics.RaycastNonAlloc(center - direction * Inset,
                direction, Hits, Span, Room.Mask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null) continue;
                Solids.Info s = Solids.Of(c);
                if (s != null && s.Built && s.Structural)
                    return true;
            }
            return false;
        }
    }
}
