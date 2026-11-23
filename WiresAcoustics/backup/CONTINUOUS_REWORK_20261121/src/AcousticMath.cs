using UnityEngine;

namespace WiresAcoustics
{
    // Frequency interpolation is logarithmic in every subsystem: ambience,
    // barrier transmission, diffraction, ocean shelter and underwater sound.
    // Keep Unity's exact float operators/order from published 0.6.0.
    internal static class AcousticMath
    {
        // One shared configuration policy: a disabled occlusion switch
        // always means zero strength across ambience, ocean and SFX.
        internal static float OcclusionStrength =>
            Plugin.Occlusion.Value
                ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f)
                : 0f;

        internal static float LogLerp(float from, float to, float amount) =>
            Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), amount));
    }
}
