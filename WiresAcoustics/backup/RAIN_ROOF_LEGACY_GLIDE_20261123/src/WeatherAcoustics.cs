using UnityEngine;

namespace WiresAcoustics
{
    // Reusable environment geometry properties. Kept under its original
    // type name for source compatibility with the wind/biome opening logic.
    // Rain does not use a special roof-volume model or bespoke audio fade.
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

        // The standard room geometry uses this same upward distance
        // weighting in its acoustic/reverberation measurements. Emission
        // itself is decided by surface exposure in EnvironmentalField.
        internal static float RoofImpactWeight(float verticalDistance)
        {
            float h = Mathf.Max(0f, verticalDistance);
            return 1f / (1f + h * h / 20.25f);
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
