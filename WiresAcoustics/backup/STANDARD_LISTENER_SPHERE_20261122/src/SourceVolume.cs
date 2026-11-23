using UnityEngine;

namespace WiresAcoustics
{
    // Adaptive 3D source-volume sampling. Repeated machine/fire loops emit
    // throughout a measured local collider box, rotated and scaled with
    // its owner; localized one-shot sounds use a finite-radius sphere
    // around their actual authored emitter (door hinge, chest lid, etc.).
    // This avoids inventing source points outside an angled collider AABB.
    // Deterministic low-discrepancy samples; no allocations or new polling.
    internal static class SourceVolume
    {
        internal const int MaxSamples = 32;
        internal struct Projection
        {
            internal Vector3 Center, Right, Up, Forward;
            internal Vector3 AxisX, AxisY, AxisZ;
            internal float X, Y, Depth;
            internal bool Box;
        }

        private static float Halton(int index, int basis)
        {
            float value = 0f, fraction = 1f;
            while (index > 0)
            {
                fraction /= basis;
                value += fraction * (index % basis);
                index /= basis;
            }
            return value;
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

            Projection p;
            p.Center = source;
            p.Right = right; p.Up = up; p.Forward = forward;
            p.Box = f != null && f.AreaKnown && f.ShapeRoot != null &&
                    f.ShapeHalf.sqrMagnitude >= .0001f &&
                    f.Source != null && f.Source.loop;
            if (!p.Box)
            {
                // A chest lid, footstep or free-floating sound is not the
                // entire owner's body: use a finite-radius emitting sphere.
                p.X = .30f; p.Y = .30f; p.Depth = .30f;
                p.AxisX = right * .30f;
                p.AxisY = up * .30f;
                p.AxisZ = forward * .30f;
                return p;
            }
            Vector3 h = f.ShapeHalf;
            Transform root = f.ShapeRoot;
            Vector3 vx = root.TransformVector(new Vector3(h.x, 0f, 0f));
            Vector3 vy = root.TransformVector(new Vector3(0f, h.y, 0f));
            Vector3 vz = root.TransformVector(new Vector3(0f, 0f, h.z));
            p.Center = root.TransformPoint(f.ShapeCenter);
            p.AxisX = vx * .9f; p.AxisY = vy * .9f; p.AxisZ = vz * .9f;
            p.X = Mathf.Min(4f, Mathf.Abs(Vector3.Dot(vx, right)) +
                Mathf.Abs(Vector3.Dot(vy, right)) + Mathf.Abs(Vector3.Dot(vz, right)));
            p.Y = Mathf.Min(4f, Mathf.Abs(Vector3.Dot(vx, up)) +
                Mathf.Abs(Vector3.Dot(vy, up)) + Mathf.Abs(Vector3.Dot(vz, up)));
            p.Depth = Mathf.Min(4f, Mathf.Abs(Vector3.Dot(vx, forward)) +
                Mathf.Abs(Vector3.Dot(vy, forward)) + Mathf.Abs(Vector3.Dot(vz, forward)));
            return p;
        }

        internal static int Count(AcousticFilter f, Vector3 listener,
            Vector3 source, int maxSamples = 16)
        {
            if (maxSamples <= 0) return 0;
            float dist = (source - listener).magnitude;
            if (dist < .15f) return 0;
            Projection p = Project(f, listener, source);
            float angle = Mathf.Max(p.X, p.Y) / dist;
            int desired = angle > .35f ? 32 : angle > .16f ? 16 :
                          angle > .045f ? 8 : 4;
            return Mathf.Min(desired, Mathf.Min(MaxSamples, maxSamples));
        }

        internal static Vector3 Point(in Projection p, Vector3 source,
            int index, int count)
        {
            // 3-dimensional low-discrepancy sequence, not an A/B ray.
            int n = index + 1;
            float a = Halton(n, 2);
            float b = Halton(n, 3);
            float c = Halton(n, 5);
            if (p.Box)
                return p.Center +
                    p.AxisX * (2f * a - 1f) +
                    p.AxisY * (2f * b - 1f) +
                    p.AxisZ * (2f * c - 1f);
            // Uniform-volume sphere: radius cuberoot(c), direction from
            // uniform azimuth and cos(theta).
            float angle = 2f * Mathf.PI * a;
            float z = 2f * b - 1f;
            float radius = Mathf.Pow(c, 1f / 3f);
            float planar = radius * Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return source +
                p.AxisX * (planar * Mathf.Cos(angle)) +
                p.AxisY * (planar * Mathf.Sin(angle)) +
                p.AxisZ * (radius * z);
        }
    }
}
