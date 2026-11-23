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
        internal const float IndirectRainFloor = .24f;

        // Valheim crossfades a newly selected rain clip via a two-second
        // ambient-loop transition. Keep continuous rain audible from the
        // chosen weather from the first frame; the temporary rain bed fades
        // out as Valheim's own clip reaches its target level.
        internal static float BridgeNeeded(bool rainSelected, bool playingRain,
            float playingVolume, float wantedVolume)
        {
            if (!rainSelected) return 0f;
            if (!playingRain) return 1f;
            return Mathf.Clamp01(1f - Mathf.Max(0f, playingVolume) /
                Mathf.Max(.001f, wantedVolume));
        }

        // A spatial opening is not equivalent to the average enclosure of
        // the WHOLE room. Its measured aperture solid angle gives a diffuse
        // outside contribution; nearby large openings transmit a stronger
        // direct exterior field. Uses Portals' already measured radius,
        // share and distance, without new physics or scene traversal.
        // Never exceed the game's original outdoor loop level per opening.
        internal static float OpeningGain(float share, float radius, float distance)
        {
            float diffuse = Mathf.Sqrt(Mathf.Clamp01(share));
            float direct = radius > 0f ? Mathf.Clamp01(
                radius / Mathf.Max(.25f, Mathf.Max(0f, distance) +
                                           .35f * radius)) : 0f;
            return Mathf.Max(diffuse, direct);
        }
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
            // A room without a line of sight to the outdoors still
            // receives diffuse exterior rainfall through its structure.
            // Never let a visibility change erase the entire weather bed.
            float indirect = Mathf.Lerp(ambientGain,
                Mathf.Max(ambientGain, IndirectRainFloor),
                Mathf.Clamp01(rainBlend));
            gain = Mathf.Lerp(indirect,
                Mathf.Max(indirect,RainRoofGain),roofRain);
            cutoff = AcousticMath.LogLerp(ambientCutoff,
                Mathf.Max(ambientCutoff,RainRoofCutoff),roofRain);
        }
    }
}
