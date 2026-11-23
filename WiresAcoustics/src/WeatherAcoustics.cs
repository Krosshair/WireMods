using UnityEngine;

namespace WiresAcoustics
{
    // Reusable environmental geometry plus the previously accepted
    // original-loop roof coloration. No material sound assets, synthesis,
    // additional emitters or separate rain propagation are introduced.
    internal static class WeatherAcoustics
    {
        // An authored, exposed physical roof surface, not an intermediate
        // floor or the gable wall that carries the name "wall_roof".
        internal static bool IsRoofPieceName(string name) =>
            !string.IsNullOrEmpty(name) &&
            name.IndexOf("roof",
                System.StringComparison.OrdinalIgnoreCase) >= 0 &&
            name.IndexOf("wall_roof",
                System.StringComparison.OrdinalIgnoreCase) < 0;

        // An isolated room ray is NOT an aperture merely because one
        // 8 cm offset ray misses a roof/floor seam. Both orthogonal
        // offsets must pass the enclosing wall plane. The 10 cm depth
        // tolerance avoids mistaking a slightly slanted solid face for
        // a clean exit. This reuses the existing three rays.
        internal static bool VerifiedNarrowAperture(float sideDistance,
            float upDistance, float wallDepth) =>
            sideDistance > wallDepth + .10f &&
            upDistance > wallDepth + .10f;

        // The existing room survey weights an actual overhead roofing
        // collider by distance; intermediate floors cannot count as roof.
        internal static float RoofImpactWeight(float verticalDistance)
        {
            float h = Mathf.Max(0f, verticalDistance);
            return 1f / (1f + h * h / 20.25f);
        }

        // Reinstated from the previous single-source roof treatment.
        // These values shape the EXISTING Valheim rain recording; they
        // do not play, synthesize, or copy any additional rain audio.
        internal const float RainRoofGain = .72f;
        internal const float RainRoofCutoff = 6500f;

        // Only an actual nearby first-hit exterior roof contributes.
        // Room.BuiltRoof is already distance-weighted and rejects floors
        // between storeys. Keep the old partial-cover/enclosure response.
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
                Mathf.Max(exteriorGain, RainRoofGain), mix);
            cutoff = AcousticMath.LogLerp(exteriorCutoff,
                Mathf.Max(exteriorCutoff, RainRoofCutoff), mix);
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
