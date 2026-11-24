using UnityEngine;

namespace WiresAcoustics
{
    // Reusable environmental geometry plus the previously accepted
    // original-loop roof coloration. No material sound assets, synthesis,
    // additional emitters or separate rain propagation are introduced.
    internal static class WeatherAcoustics
    {
        // How much a roof above counts, by how high it is over you (Room's survey).
        internal static float RoofImpactWeight(float verticalDistance)
        {
            float h = Mathf.Max(0f, verticalDistance);
            return 1f / (1f + h * h / 20.25f);
        }

        // The roof's gain and low-pass come from the survey (Room.RoofGain, RoofCutoff): the materials
        // the rain is heard through. They shape the EXISTING Valheim rain recording only.

        // Room.BuiltRoof: how much of the sky above is roofed (the outermost built surface,
        // distance-weighted). Keep the old partial-cover/enclosure response.
        internal static float RoofRainPresence(float builtRoof,
            float enclosure)
        {
            float roof = Mathf.Clamp01(builtRoof * 2f);
            float inside = Mathf.Clamp01(Mathf.Max(enclosure,
                roof * .45f));
            return roof * inside;
        }

        // General exterior sound already supplies the base gain/spectrum.
        // The roof only contributes the *extra* nearby impact presence;
        // it cannot attenuate that exterior field or turn it into a second
        // separate recording. No old global indirect-rain volume floor.
        internal static void BlendRoof(float exteriorGain,
            float exteriorCutoff, float roofPresence,
            out float gain, out float cutoff)
        {
            float mix = Mathf.Clamp01(roofPresence);
            gain = Mathf.Lerp(exteriorGain,
                Mathf.Max(exteriorGain, Room.RoofGain), mix);
            cutoff = AcousticMath.LogLerp(exteriorCutoff,
                Mathf.Max(exteriorCutoff, Room.RoofCutoff), mix);
        }

        // Ordinary non-rain ambient/wind copies retain their existing
        // relative opening loudness and spatial routing.
        internal static float OpeningGain(float share, float radius,
            float distance)
        {
            float diffuse = Mathf.Sqrt(Mathf.Clamp01(share));
            float direct = radius > 0f ? Mathf.Clamp01(
                radius / Mathf.Max(.25f, Mathf.Max(0f, distance) +
                    .35f * radius)) : 0f;
            return Mathf.Max(diffuse, direct);
        }
    }
}
