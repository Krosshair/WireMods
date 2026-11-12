using UnityEngine;

namespace WiresAcoustics
{
    // Sound bending around things (diffraction) where there's no opening to go through: around the
    // corner of a house, over a wall. For a sound with walls in the way, five points around the first
    // thing in the way are tried as paths: 2.5 m and 5 m to each side, and 3 m above. A path is
    // listener -> point -> sound; its loss is the walls on both legs, the bend round the edge
    // (Diffraction.Round) and the extra distance. Used like the paths through openings: the loudest
    // way round adds to the sound through the walls, and where it's heard from follows the
    // precedence effect (Batch).
    internal static class Corners
    {
        internal const int Count = 5;

        internal static void Around(Vector3 listener, Vector3 sound, float firstHit,
                                    Collider blocker, Vector3[] into)
        {
            Vector3 d = sound - listener;
            float dist = d.magnitude;
            Vector3 dir = dist > .01f ? d / dist : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            Vector3 pivot = listener + dir * Mathf.Clamp(firstHit, .5f, Mathf.Max(.5f, dist - .5f));
            float right = 2.5f, left = 2.5f, over = 3f;
            if (blocker != null && blocker.enabled)
            {
                Bounds b = blocker.bounds;
                Vector3 e = b.extents;
                float half = Mathf.Abs(side.x) * e.x + Mathf.Abs(side.y) * e.y +
                             Mathf.Abs(side.z) * e.z;
                float offset = Vector3.Dot(b.center - pivot, side);
                // Just beyond the collider's actual projected side edges.
                // Clamp when a dungeon collider spans a huge structure.
                right = Mathf.Clamp(offset + half + .4f, 1f, 5.5f);
                left = Mathf.Clamp(half - offset + .4f, 1f, 5.5f);
                over = Mathf.Clamp(b.max.y - pivot.y + .4f, 1f, 5.5f);
            }
            into[0] = pivot + side * right;
            into[1] = pivot - side * left;
            into[2] = pivot + side * Mathf.Min(7f, right + 1.5f);
            into[3] = pivot - side * Mathf.Min(7f, left + 1.5f);
            into[4] = pivot + Vector3.up * over;
        }

        internal static float LengthGain(Vector3 listener, Vector3 p, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            float path = (p - listener).magnitude + (sound - p).magnitude;
            return Mathf.Min(1f, direct / Mathf.Max(.01f, path)); // the longer way spreads the sound further (1/r)
        }
    }
}
