using UnityEngine;

namespace WiresAcoustics
{
    // The outdoor ambience is Valheim's ORIGINAL environmental recording.
    // Weather identity is taken from the active EnvSetup's actual loop
    // clip, not just IsWet(), which also describes surfaces / effects.
    //
    // A built roof changes what reaches the listener: rain striking its
    // exterior remains audible as a diffuse component even when the
    // normal outdoor ambient path is substantially screened. The roof
    // presence and enclosure are already measured by Room. The rain
    // impact level/spectrum are authored Valheim perceptual properties,
    // not fabricated extra weather emitters or new audio recordings.
    internal static class WeatherAcoustics
    {
        internal const float RainRoofGain = .72f;
        internal const float RainRoofCutoff = 6500f;
        internal const float RainResponseSeconds = .25f;
        // Geometry and weather recognition use the same short continuous
        // transition; no extra frame hook or source-specific coroutine.
        internal static float Approach(float current,float target,float deltaTime) =>
            Mathf.MoveTowards(current,target,Mathf.Max(0f,deltaTime) / RainResponseSeconds);

        internal static bool IsActiveRainLoop(bool wet,
            AudioClip environmentLoop, AudioClip playingLoop)
        {
            return wet && environmentLoop != null &&
                   playingLoop != null &&
                   environmentLoop == playingLoop;
        }

        // All inputs/outputs are normalized continuous quantities.
        internal static float RoofRainPresence(float rainBlend,
            float builtRoof, float enclosure)
        {
            // 64 existing room rays tell us how much real build geometry
            // is above the listener. A partially roofed structure should
            // not sound identical to open air or a sealed cave.
            float roof = Mathf.Clamp01(builtRoof * 2f);
            float inside = Mathf.Clamp01(Mathf.Max(enclosure,roof * .45f));
            return Mathf.Clamp01(rainBlend) * roof * inside;
        }

        internal static void Blend(float ambientGain,float ambientCutoff,
            float rainBlend,float builtRoof,float enclosure,
            out float gain,out float cutoff)
        {
            float roofRain = RoofRainPresence(rainBlend,builtRoof,enclosure);
            gain = Mathf.Lerp(ambientGain,
                Mathf.Max(ambientGain,RainRoofGain),roofRain);
            cutoff = AcousticMath.LogLerp(ambientCutoff,
                Mathf.Max(ambientCutoff,RainRoofCutoff),roofRain);
        }
    }
}
