using UnityEngine;

namespace WiresAcoustics
{
    // Small shared propagation result model: all geometry queries are
    // performed by the batched ray solver, never on the audio thread.
    internal static class AcousticModel
    {
        // Shared propagation math for the direct sound and nearby wall edges.
        // Geometry queries remain batched and specialized,
        // but gains, low-pass mixing and source localization use one model.
        internal static bool Consider(float occ, in Occlusion.Block block,
            float lengthGain, float bendGain, float bendCut, Vector3 at,
            ref float bestGain, ref float bestCutoff, ref float bestOcc,
            ref Vector3 bestAt)
        {
            Occlusion.Wall(block, out float g, out float cutoff);
            g *= lengthGain * bendGain;
            cutoff = Mathf.Min(cutoff, bendCut);
            if (g <= bestGain) return false;
            bestGain = g;
            bestCutoff = cutoff;
            bestOcc = occ;
            bestAt = at;
            return true;
        }

        internal static void Combine(ref float directGain,
            ref float directCutoff, ref float directOcc,
            float pathGain, float pathCutoff, float pathOcc,
            Vector3 listener, Vector3 source, Vector3 pathAt,
            out Vector3 heard, out bool routed)
        {
            heard = source;
            routed = false;
            if (pathGain <= 0f) return;
            float ed = directGain * directGain;
            float ep = pathGain * pathGain;
            float sum = ed + ep;
            if (sum <= 1e-9f) return;
            float share = ep / sum;
            directCutoff = Mathf.Exp((ed * Mathf.Log(Mathf.Max(20f,
                directCutoff)) + ep * Mathf.Log(Mathf.Max(20f,
                pathCutoff))) / sum);
            directGain = Mathf.Min(1f, Mathf.Sqrt(sum));
            directOcc = Mathf.Lerp(directOcc, pathOcc, share);
            float w = HeardFrom(listener, source, pathAt, ed, ep);
            heard = Toward(listener, source, pathAt, w);
            routed = w > .05f;
        }

        // Localization uses the arrival-time precedence effect. Even when a
        // path transmits more energy, it should not instantly replace a
        // slightly earlier direct arrival as the perceived direction.
        private static float HeardFrom(Vector3 listener, Vector3 source,
            Vector3 via, float eDirect, float eRound)
        {
            float delay = ((via - listener).magnitude +
                (source - via).magnitude -
                (source - listener).magnitude) / 343f;
            if (eRound <= 0f) return 0f;
            if (eDirect <= 0f) return 1f;
            if (delay < .001f) return eRound / (eDirect + eRound);
            float db = 10f * Mathf.Log10(eRound / eDirect);
            if (delay > .05f) return Mathf.Clamp01((db + 2f) / 4f);
            return Mathf.Clamp01((db - 8f) / 4f);
        }

        private static Vector3 Toward(Vector3 listener, Vector3 source,
            Vector3 via, float w)
        {
            Vector3 a = source - listener, b = via - listener;
            float dist = a.magnitude;
            if (dist < 1e-3f || b.sqrMagnitude < 1e-6f)
                return source;
            Vector3 dir = Vector3.Slerp(a / dist, b.normalized, w);
            return listener + dir * dist;
        }

        // Shared distance/reverb-send policy for ordinary world sounds.
        // Called from existing Occlusion paths, not from the audio thread.
        internal static float RoomSend(float distance, float criticalDistance,
            float transmittedGain)
        {
            float directVsDiffuse = distance /
                Mathf.Max(.01f, distance + Mathf.Max(.01f, criticalDistance));
            return Mathf.Clamp01(directVsDiffuse * transmittedGain);
        }
    }
}
