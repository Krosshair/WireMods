using UnityEngine;

namespace WiresAcoustics
{
    // Analytic knife-edge transfer for ONE sampled point of a 3D source
    // volume. Evaluated only when a finished geometry job has validated
    // exactly one edge. No physics raycasts and no DSP callback work.
    internal static class VolumeFresnel
    {
        internal static bool Transfer(Vector3 listener, Vector3 source,
            Vector3 wallAt, Vector3 wallNormal,
            Vector3 edge, Vector3 openDirection, float maxSpan,
            float wallGain, float wallCutoff,
            out float gain, out float cutoff, out float bent)
        {
            gain = 1f; cutoff = 22000f; bent = 0f;
            Vector3 direct = source - listener;
            float along = Vector3.Dot(direct, wallNormal);
            float before = Vector3.Dot(wallAt - listener, wallNormal);
            // A 3D emitter can straddle the wall: points on the listener's
            // side have a genuinely clear direct path, not a failed
            // Fresnel evaluation that switches the entire emitter model.
            if (Mathf.Abs(along) < .0001f) return true;
            float t = before / along;
            if (t <= 0f || t >= 1f) return true;
            Vector3 crossing = listener + direct * t;
            float signed = Vector3.Dot(crossing - edge, openDirection);
            if (Mathf.Abs(signed) > maxSpan) return false;
            FresnelEdge.PathDistances(direct.magnitude, t,
                out float d1, out float d2);
            if (d1 <= .001f || d2 <= .001f) return false;
            FresnelEdge.Transfer(signed, d1, d2,
                wallGain, wallCutoff, out gain, out cutoff, out bent);
            return true;
        }
    }
}
