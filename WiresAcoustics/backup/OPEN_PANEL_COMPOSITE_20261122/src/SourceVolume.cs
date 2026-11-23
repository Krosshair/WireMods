using UnityEngine;

namespace WiresAcoustics
{
    // Steam Audio volumetric occlusion reference: sample points inside
    // a sphere around the emitter and trace them to the acoustic listener.
    // See https://valvesoftware.github.io/steam-audio/doc/unity/source.html
    // Radii are authored/geometry-derived; finite sample count is a
    // configurable simulation-quality choice, not an acoustic constant.
    internal static class SourceVolume
    {
        internal const int MaxSamples = 32;

        internal struct Projection
        {
            internal Vector3 Center, Right, Up, Forward;
            internal float X, Y, Depth;
            internal float Radius;
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

            // For looping flames/machines a measured owner's local bounds
            // provide the 3D source extent. Represent them by the smallest
            // centered enclosing sphere (a conservative approximation).
            if (f != null && f.AreaKnown && f.ShapeRoot != null &&
                f.Source != null && f.Source.loop &&
                f.ShapeHalf.sqrMagnitude > 1e-4f)
            {
                Vector3 h = f.ShapeHalf;
                Transform root = f.ShapeRoot;
                Vector3 ax = root.TransformVector(new Vector3(h.x, 0f, 0f));
                Vector3 ay = root.TransformVector(new Vector3(0f, h.y, 0f));
                Vector3 az = root.TransformVector(new Vector3(0f, 0f, h.z));
                radius = Mathf.Sqrt(ax.sqrMagnitude +
                                    ay.sqrMagnitude + az.sqrMagnitude);
                center = root.TransformPoint(f.ShapeCenter);
            }

            Projection p;
            p.Center = center;
            p.Right = right; p.Up = up; p.Forward = forward;
            p.Radius = Mathf.Max(.01f, radius);
            p.X = p.Y = p.Depth = p.Radius;
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
            // Simulation quality heuristic only; the acoustic model is
            // still Steam Audio's sampled spherical source visibility.
            int n = angularRadius > .35f ? 32 :
                    angularRadius > .16f ? 16 :
                    angularRadius > .045f ? 8 : 4;
            return Mathf.Min(n, Mathf.Min(MaxSamples, maxSamples));
        }

        internal static Vector3 Point(in Projection p, Vector3 source,
            int index, int count)
        {
            int i = index + 1;
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
