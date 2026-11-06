using UnityEngine;

namespace WiresAcoustics
{
    // Sound bending around things (diffraction) where there's no opening to go through: around the
    // corner of a house, over a wall. For a sound with walls in the way, five points around the first
    // thing in the way are tried as paths: 2.5 m and 5 m to each side, and 3 m above. A path is
    // listener -> point -> sound; its cost is the walls on both legs, the bend (0.3 walls at 90
    // degrees, as for openings) and its volume drops with the extra distance. Used like the paths
    // through openings: the quietest way wins, and the sound is heard from where it comes round.
    internal static class Corners
    {
        internal const int Count = 5;
        private static readonly Vector3[] Points = new Vector3[Count];

        internal static void Around(Vector3 listener, Vector3 sound, float firstHit, Vector3[] into)
        {
            Vector3 d = sound - listener;
            float dist = d.magnitude;
            Vector3 dir = dist > .01f ? d / dist : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            Vector3 pivot = listener + dir * Mathf.Clamp(firstHit, .5f, Mathf.Max(.5f, dist - .5f));
            into[0] = pivot + side * 2.5f;
            into[1] = pivot - side * 2.5f;
            into[2] = pivot + side * 5f;
            into[3] = pivot - side * 5f;
            into[4] = pivot + Vector3.up * 3f;
        }

        internal static float Bend(Vector3 listener, Vector3 p, Vector3 sound) =>
            .3f * (1f - Vector3.Dot((p - listener).normalized, (sound - p).normalized));

        internal static float LengthGain(Vector3 listener, Vector3 p, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            float path = (p - listener).magnitude + (sound - p).magnitude;
            return Mathf.Clamp(direct / Mathf.Max(.01f, path), .35f, 1f);
        }

        // A new sound, checked at once (2 rays per point).
        internal static bool Route(Vector3 listener, Vector3 sound, float firstHit, ref float occ, out float lengthGain, out Vector3 heardFrom)
        {
            lengthGain = 1f;
            heardFrom = sound;
            if (occ < .3f) return false;
            Around(listener, sound, firstHit, Points);
            bool routed = false;
            foreach (Vector3 p in Points)
            {
                float total = Occlusion.Blocking(listener, p, out _) + Occlusion.Blocking(p, sound, out _) + Bend(listener, p, sound);
                if (total < occ - .1f)
                {
                    occ = total;
                    lengthGain = LengthGain(listener, p, sound);
                    heardFrom = p;
                    routed = true;
                }
            }
            return routed;
        }
    }
}
