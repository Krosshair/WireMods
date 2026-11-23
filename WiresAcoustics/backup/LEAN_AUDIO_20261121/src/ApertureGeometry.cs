using UnityEngine;

namespace WiresAcoustics
{
    // Derive a stable aperture from the BOUNDARIES of actual structural
    // colliders. One broadphase query, no per-cell raycast raster and no
    // reconstruction when the listener moves. Non-rectangular construction
    // is represented by its local effective rectangle, not an invented mesh.
    internal static class ApertureGeometry
    {
        private const float Search = 5f;
        private const float Depth = 1.1f;
        private const float Minimum = .25f;
        private const float RejectSeconds = 3f, CellSize = 1.2f;
        private const int ColliderCapacity = 128, HitCapacity = 16;
        private static readonly Collider[] Nearby = new Collider[ColliderCapacity];
        private static readonly RaycastHit[] Hits = new RaycastHit[HitCapacity];

        private struct Negative
        {
            internal int Support, X, Y, Z, Gen;
            internal float At;
        }

        private static readonly Negative[] Rejected = new Negative[128];
        private static int s_rejectNext;
        private static int s_queries, s_rejected, s_success, s_overflow;
        private static int s_maxColliders;
        private static long s_ticks, s_worst;

        internal static bool RecentlyRejected(Portals.Portal p)
        {
            int x = Mathf.FloorToInt(p.Point.x / CellSize);
            int y = Mathf.FloorToInt(p.Point.y / CellSize);
            int z = Mathf.FloorToInt(p.Point.z / CellSize);
            int gen = Occlusion.WorldGen;
            for (int i = 0; i < Rejected.Length; i++)
            {
                Negative r = Rejected[i];
                if (r.Support == p.SupportId && r.X == x &&
                    r.Y == y && r.Z == z && r.Gen == gen &&
                    Time.time - r.At < RejectSeconds)
                    return true;
            }
            return false;
        }

        private static void Reject(Portals.Portal p)
        {
            s_rejected++;
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

        internal static bool Resolve(Portals.Portal candidate,
            out Vector3 center, out Vector3 normal, out float radius)
        {
            long t = Cost.Start();
            bool good = ResolveGeometry(candidate, out center, out normal,
                out radius);
            if (good) s_success++; else Reject(candidate);
            if (t != 0L)
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - t;
                s_ticks += elapsed;
                if (elapsed > s_worst) s_worst = elapsed;
            }
            return good;
        }

        private static bool ResolveGeometry(Portals.Portal p,
            out Vector3 center, out Vector3 normal, out float radius)
        {
            center = p.Point;
            normal = p.Dir.normalized;
            radius = .5f;
            if (p.Support == null || normal.sqrMagnitude < .5f)
                return false;
            Vector3 right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < .01f)
                right = Vector3.Cross(Vector3.forward, normal);
            right.Normalize();
            Vector3 up = Vector3.Cross(normal, right).normalized;

            Quaternion orientation = Quaternion.LookRotation(normal, up);
            int count = Physics.OverlapBoxNonAlloc(p.Point,
                new Vector3(Search, Search, Depth), Nearby,
                orientation, Room.Mask, QueryTriggerInteraction.Ignore);
            s_queries++;
            if (count > s_maxColliders) s_maxColliders = count;
            if (count >= Nearby.Length)
            {
                s_overflow++;
                // A truncated collider list could invent an opening. Reject
                // conservatively instead of gambling with its boundaries.
                return false;
            }

            float left = Search, rightEdge = Search;
            float bottom = Search, top = Search;
            bool hasLeft = false, hasRight = false;
            bool hasBottom = false, hasTop = false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Nearby[i];
                if (collider == null) continue;
                Solids.Info info = Solids.Of(collider);
                if (info == null || !info.Built || !info.Structural)
                    continue;

                // Project each actual oriented collider's local bounds into
                // the candidate wall plane. Unlike axis-aligned world Bounds,
                // this preserves orientation of rotated building pieces.
                Bounds b;
                Transform tr = collider.transform;
                BoxCollider box = collider as BoxCollider;
                MeshCollider mesh = collider as MeshCollider;
                if (box != null) b = new Bounds(box.center, box.size);
                else if (mesh != null && mesh.sharedMesh != null)
                    b = mesh.sharedMesh.bounds;
                else
                {
                    // Rare collider types use world AABB as conservative
                    // fallback; the center-path ray below validates it.
                    b = collider.bounds;
                    tr = null;
                }

                Vector3 extent = b.extents, origin = b.center;
                float xMin = float.PositiveInfinity, xMax = float.NegativeInfinity;
                float yMin = float.PositiveInfinity, yMax = float.NegativeInfinity;
                float zMin = float.PositiveInfinity, zMax = float.NegativeInfinity;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = origin + new Vector3(
                        (corner & 1) == 0 ? -extent.x : extent.x,
                        (corner & 2) == 0 ? -extent.y : extent.y,
                        (corner & 4) == 0 ? -extent.z : extent.z);
                    Vector3 relative =
                        (tr != null ? tr.TransformPoint(local) : local) - p.Point;
                    float x = Vector3.Dot(relative, right);
                    float y = Vector3.Dot(relative, up);
                    float z = Vector3.Dot(relative, normal);
                    xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
                    yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
                    zMin = Mathf.Min(zMin, z); zMax = Mathf.Max(zMax, z);
                }
                if (zMin > Depth || zMax < -Depth) continue;
                if (yMin <= .12f && yMax >= -.12f)
                {
                    if (xMax < 0f && -xMax < left)
                    { left = -xMax; hasLeft = true; }
                    if (xMin > 0f && xMin < rightEdge)
                    { rightEdge = xMin; hasRight = true; }
                }
                if (xMin <= .12f && xMax >= -.12f)
                {
                    if (yMax < 0f && -yMax < bottom)
                    { bottom = -yMax; hasBottom = true; }
                    if (yMin > 0f && yMin < top)
                    { top = yMin; hasTop = true; }
                }
            }

            // A useful gap must have actual structural boundaries. A crack
            // between two beams is rejected by the minimum area check.
            if (!(hasLeft && hasRight) &&
                !(hasBottom && hasTop)) return false;
            if (!hasLeft) left = Mathf.Min(2.5f, rightEdge);
            if (!hasRight) rightEdge = Mathf.Min(2.5f, left);
            if (!hasBottom) bottom = Mathf.Min(2.5f, top);
            if (!hasTop) top = Mathf.Min(2.5f, bottom);

            float width = left + rightEdge;
            float height = bottom + top;
            if (width < Minimum || height < Minimum) return false;
            Vector3 proposed = p.Point +
                right * ((rightEdge - left) * .5f) +
                up * ((top - bottom) * .5f);

            // Validate the center with an actual line through the material.
            // Bounds of irregular meshes are deliberately conservative.
            if (Blocked(proposed, normal)) return false;
            center = proposed + normal * .4f;
            radius = Mathf.Clamp(Mathf.Sqrt(width * height / Mathf.PI),
                .25f, 4f);
            return true;
        }

        private static bool Blocked(Vector3 center, Vector3 normal)
        {
            int n = Physics.RaycastNonAlloc(
                center - normal * Depth, normal, Hits,
                Depth * 2f, Room.Mask, QueryTriggerInteraction.Ignore);
            if (n >= Hits.Length) return true;
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null) continue;
                Solids.Info info = Solids.Of(c);
                if (info != null && info.Built && info.Structural)
                    return true;
            }
            return false;
        }

        internal static string Report()
        {
            float scale = 1000f / System.Diagnostics.Stopwatch.Frequency;
            string result = "apertures: queries " + s_queries +
                ", accepted " + s_success + ", rejected " + s_rejected +
                ", collider overflow " + s_overflow +
                ", max colliders " + s_maxColliders +
                ", geometry total " + (s_ticks * scale).ToString("F3") +
                " ms, worst " + (s_worst * scale).ToString("F3") + " ms";
            s_queries = s_success = s_rejected = s_overflow =
                s_maxColliders = 0;
            s_ticks = s_worst = 0L;
            return result;
        }
    }
}
