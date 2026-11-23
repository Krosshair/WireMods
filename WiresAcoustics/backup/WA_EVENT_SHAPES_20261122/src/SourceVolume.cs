using UnityEngine;

namespace WiresAcoustics
{
    // Volumetric sampled occlusion using actual cached owner geometry.
    // Known machine/flame sound owners use their rotated/scaled local box,
    // while events without an owner still use a compact fallback sphere.
    // This reuses Area's cached collider bounds and the existing ray count;
    // no new game-frame work, scans, or physics casts.
    internal static class SourceVolume
    {
        internal const int MaxSamples = 32;

        internal struct Projection
        {
            internal Vector3 Center, Right, Up, Forward;
            internal float X, Y, Depth;
            internal float Radius;
            internal bool Box;
            internal Vector3 AxisX, AxisY, AxisZ; // world-space box half-axes
        }

        private static float Halton(int index, int basis)
        {
            float result = 0f, fraction = 1f;
            while (index > 0)
            {
                fraction /= basis;
                result += fraction * (index % basis);
                index /= basis;
            }
            return result;
        }

        internal static Projection Project(AcousticFilter f,
            Vector3 listener, Vector3 source)
        {
            Vector3 forward = source - listener;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.Cross(Vector3.right, forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;
            forward.Normalize();

            // No valid collider: author-configurable sphere centered at the
            // actual sound event, not the entire owning GameObject.
            float radius = Plugin.SourceSphereRadius != null
                ? Plugin.SourceSphereRadius.Value : .30f;
            Vector3 center = source;
            Vector3 boxX = Vector3.zero, boxY = Vector3.zero, boxZ = Vector3.zero;
            bool knownBox = false;

            // A known looping source is sampled inside its actual rotated
            // bounding box, not the enclosing sphere (which can be far too
            // large for a door, hearth, or long thin workstation).
            if (f != null && f.AreaKnown && f.ShapeRoot != null &&
                f.Source != null && f.Source.loop &&
                f.ShapeHalf.sqrMagnitude > 1e-4f)
            {
                Vector3 h = f.ShapeHalf;
                Transform root = f.ShapeRoot;
                boxX = root.TransformVector(new Vector3(h.x, 0f, 0f));
                boxY = root.TransformVector(new Vector3(0f, h.y, 0f));
                boxZ = root.TransformVector(new Vector3(0f, 0f, h.z));
                radius = Mathf.Sqrt(boxX.sqrMagnitude +
                                    boxY.sqrMagnitude + boxZ.sqrMagnitude);
                center = root.TransformPoint(f.ShapeCenter);
                knownBox = true;
            }

            Projection p;
            p.Center = center;
            p.Right = right; p.Up = up; p.Forward = forward;
            p.Radius = Mathf.Max(.01f, radius);
            p.X = p.Y = p.Depth = p.Radius;
            p.Box = knownBox;
            p.AxisX = boxX; p.AxisY = boxY; p.AxisZ = boxZ;
            return p;
        }

        internal static int Count(AcousticFilter f, Vector3 listener,
            Vector3 source, int maxSamples = 16)
        {
            if (maxSamples <= 0) return 0;
            float dist = (source - listener).magnitude;
            if (dist < .15f) return 0;
            Projection p = Project(f, listener, source);
            float angularRadius = p.Radius / dist;
            // Preserve the same angular-size ray-budget heuristic as the
            // previous version; shape-aware samples do not cost more rays.
            int n = angularRadius > .35f ? 32 :
                    angularRadius > .16f ? 16 :
                    angularRadius > .045f ? 8 : 4;
            return Mathf.Min(n, Mathf.Min(MaxSamples, maxSamples));
        }

        internal static Vector3 Point(in Projection p, Vector3 source,
            int index, int count)
        {
            int i = index + 1;
            if (p.Box)
            {
                // Halton lattice inside the cached oriented volume.
                // The three transformed half-axes already include scale.
                return p.Center + p.AxisX * (2f * Halton(i, 2) - 1f) +
                       p.AxisY * (2f * Halton(i, 3) - 1f) +
                       p.AxisZ * (2f * Halton(i, 5) - 1f);
            }
            float phi = 2f * Mathf.PI * Halton(i, 2);
            float z = 2f * Halton(i, 3) - 1f;
            float r = p.Radius * Mathf.Pow(Halton(i, 5), 1f / 3f);
            float radial = r * Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return p.Center + p.Right * (radial * Mathf.Cos(phi)) +
                   p.Up * (radial * Mathf.Sin(phi)) +
                   p.Forward * (r * z);
        }
    }
}
