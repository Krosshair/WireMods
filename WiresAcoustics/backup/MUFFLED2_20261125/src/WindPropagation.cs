using UnityEngine;

namespace WiresAcoustics
{
    // Ground-level wind-refraction proxy from NCHRP Report 791 model results
    // reproduced in FHWA Noise Measurement Handbook, table 4-1/4-2.
    // https://www.fhwa.dot.gov/Environment/noise/measurement/handbook.cfm
    // For traffic-noise test sources, no barriers or temperature gradient.
    // Distances (m), winds (2.5 / 5 m/s), receiver height (1.5 / 4.6 m),
    // hard / soft ground; all values are *changes vs calm*, in dB.
    //
    // Not a universal ray solver: Valheim sources have different spectra,
    // emission heights, and pathways. This is a cheap empirical proxy using
    // the ground already measured at the listener. No new raycasts or audio
    // thread work, no per-frame component hooks; called at 4 Hz/source.
    internal static class WindPropagation
    {
        private static readonly float[] Ranges = { 15.2f, 30.5f, 61f, 122f, 244f, 488f };

        // Layout: for each ground [hard,soft], height [1.5m,4.6m],
        // direction [upwind,downwind], wind [2.5m/s,5m/s].
        private static readonly sbyte[,] Levels = {
            // Hard ground, 1.5m
            { 0,-1,-2,-7,-13,-20 }, { -1,-2,-5,-11,-19,-25 },
            { 0, 0, 0, 1,  1,  2 }, {  0, 0, 1,  1, 2,  2 },
            // Hard ground, 4.6m
            { 0, 0, 0,-1, -6,-12 }, {  0, 0,-1,-4,-11,-18 },
            { 0, 0, 0, 1,  1,  1 }, {  0, 0, 0, 1,  1,  2 },
            // Soft ground, 1.5m
            { -2,-3,-4,-7,-11,-16 }, { -3,-4,-6,-9,-14,-21 },
            {  3, 6,10,13, 14, 14 }, {  3, 8,12,14, 15, 14 },
            // Soft ground, 4.6m
            { -1,-1,-3,-5, -8,-13 }, { -1,-3,-5,-8,-12,-16 },
            {  1, 2, 4, 8, 11, 12 }, {  1, 2, 6,10, 13, 13 }
        };

        // No source allocation; one distance interval shared by all table
        // lookups. Below the study's first 15m point, glide to zero at zero
        // metres. Beyond 488m hold the endpoint (no invented extrapolation).
        private static float At(int row, int segment, float between)
        {
            if (segment < 0) return Levels[row, 0] * between;
            if (segment >= 5) return Levels[row, 5];
            return Mathf.Lerp(Levels[row, segment], Levels[row, segment + 1], between);
        }

        private static float ForGroundHeight(int row, int segment, float between,
            float lowWind, float highWind)
        {
            float moderate = At(row, segment, between);
            float strong = At(row + 1, segment, between);
            return lowWind < 1f ? moderate * lowWind :
                Mathf.Lerp(moderate, strong, highWind);
        }

        internal static float Correction(float alongWindMps, float distance,
            float windExposure, float softGround, float receiverHeight)
        {
            if (windExposure <= 0f || distance <= 0f || alongWindMps == 0f) return 0f;
            // 5m/s is the strongest published class. Do not extrapolate
            // tables into more extreme storms (or beyond their 488m range).
            float speed = Mathf.Abs(alongWindMps);
            float lowWind = Mathf.Clamp01(speed / 2.5f);
            float highWind = Mathf.Clamp01((speed - 2.5f) / 2.5f);
            int segment = -1;
            float between = Mathf.Clamp01(distance / Ranges[0]);
            if (distance > Ranges[0])
            {
                segment = 5;
                between = 1f;
                for (int i = 0; i < Ranges.Length - 1; i++)
                {
                    if (distance > Ranges[i + 1]) continue;
                    segment = i;
                    between = Mathf.Clamp01((distance - Ranges[i]) /
                        (Ranges[i + 1] - Ranges[i]));
                    break;
                }
            }
            int dir = alongWindMps > 0f ? 2 : 0;
            float height = Mathf.Clamp01((receiverHeight - 1.5f) / 3.1f);
            float ground = Mathf.Clamp01(softGround);
            float hardNear = ForGroundHeight(dir, segment, between, lowWind, highWind);
            float hardHigh = ForGroundHeight(4 + dir, segment, between, lowWind, highWind);
            float softNear = ForGroundHeight(8 + dir, segment, between, lowWind, highWind);
            float softHigh = ForGroundHeight(12 + dir, segment, between, lowWind, highWind);
            float hard = Mathf.Lerp(hardNear, hardHigh, height);
            float soft = Mathf.Lerp(softNear, softHigh, height);
            return Mathf.Lerp(hard, soft, ground) * Mathf.Clamp01(windExposure);
        }
    }
}
