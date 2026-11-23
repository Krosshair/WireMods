using UnityEngine;

namespace WiresAcoustics
{
    // Deterministic disk sampling of the projected COLLIDER volume of a source.
    // Uses the cached, rotated/scaled box dimensions already measured by Area.
    // Samples live in the plane perpendicular to the listener->emitter ray.
    // Center is handled separately by the usual direct three-ray probe.
    // No allocations, no per-frame polling, no changes to the audio callback.
    internal static class SourceVolume
    {
        internal const int MaxSamples = 16;
        private const float Reach = .88f;

        internal static int Count(AcousticFilter f, Vector3 listener, Vector3 source)
        {
            if (f == null || !f.AreaKnown || f.ShapeRoot == null ||
                f.AreaRadius <= .05f) return 0;
            float dist = (source - listener).magnitude;
            if (dist < .15f) return 0;
            Extents(f, listener, source, out float x, out float y,
                    out _, out _);
            float angle = Mathf.Max(x, y) / dist;
            // Coarse distant field; progressively denser near big emitters.
            return angle > .16f ? 16 : angle > .045f ? 8 : angle > .018f ? 4 : 0;
        }

        internal static void Extents(AcousticFilter f, Vector3 listener,
            Vector3 source, out float x, out float y,
            out Vector3 right, out Vector3 up)
        {
            Vector3 forward = source - listener;
            right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.Cross(Vector3.right, forward);
            right.Normalize();
            up = Vector3.Cross(forward, right).normalized;
            Vector3 half = f.ShapeHalf;
            Transform root = f.ShapeRoot;
            Vector3 vx = root.TransformVector(new Vector3(half.x, 0f, 0f));
            Vector3 vy = root.TransformVector(new Vector3(0f, half.y, 0f));
            Vector3 vz = root.TransformVector(new Vector3(0f, 0f, half.z));
            x = Mathf.Min(4f, Mathf.Abs(Vector3.Dot(vx, right)) +
                Mathf.Abs(Vector3.Dot(vy, right)) + Mathf.Abs(Vector3.Dot(vz, right)));
            y = Mathf.Min(4f, Mathf.Abs(Vector3.Dot(vx, up)) +
                Mathf.Abs(Vector3.Dot(vy, up)) + Mathf.Abs(Vector3.Dot(vz, up)));
        }

        internal static Vector3 Point(AcousticFilter f, Vector3 listener,
            Vector3 source, int index, int count)
        {
            Extents(f, listener, source, out float x, out float y,
                out Vector3 right, out Vector3 up);
            // Equally-spaced disk annulus with staggered inner/outer rings.
            // Unlike fixed 8cm offsets this follows actual sound-emitter size.
            float angle = (index + .5f) * (2f * Mathf.PI / count);
            float radial = count >= 8 && (index & 1) != 0 ? .45f : Reach;
            return source + right * (x * radial * Mathf.Cos(angle)) +
                up * (y * radial * Mathf.Sin(angle));
        }
    }
}
