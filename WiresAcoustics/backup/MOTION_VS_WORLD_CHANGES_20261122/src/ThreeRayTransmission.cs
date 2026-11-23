namespace WiresAcoustics
{
    // Small finite-aperture quadrature for the three existing, spatially
    // offset direct rays. They are alternative regions, not THREE
    // simultaneous full-strength sound paths. We average spectral ENERGY.
    // Center 1/2, lateral 1/4, upper 1/4. No extra raycasts or frame hooks.
    internal static class ThreeRayTransmission
    {
        // A spatially extended sound can expose a visible face while its
        // authored pivot sits behind geometry. Both areas contribute.
        internal static void Blend(float gainA, float cutoffA,
            float gainB, float cutoffB, float shareB,
            out float gain, out float cutoff)
        {
            float w = UnityEngine.Mathf.Clamp01(shareB);
            float dc = (1f - w) * gainA * gainA + w * gainB * gainB;
            float high = (1f - w) * SpectralPaths.HighEnergy(gainA, cutoffA) +
                         w * SpectralPaths.HighEnergy(gainB, cutoffB);
            SpectralPaths.Decode(dc, high, out gain, out cutoff);
        }

        // Indirect routes carry only the obstructed fraction of a
        // partially visible emitter. Scale AMPLITUDE by sqrt(coverage)
        // so its acoustic ENERGY is proportional to hidden area.
        internal static float IndirectAmplitude(float coverage) =>
            UnityEngine.Mathf.Sqrt(UnityEngine.Mathf.Clamp01(coverage));

        internal static void Combine(
            float g0, float c0, float g1, float c1, float g2, float c2,
            bool hit0, bool hit1, bool hit2,
            out float gain, out float cutoff, out float coverage)
        {
            const float center = .5f, other = .25f;
            float dc = center * g0 * g0 +
                       other * g1 * g1 + other * g2 * g2;
            float high = center * SpectralPaths.HighEnergy(g0, c0) +
                         other * SpectralPaths.HighEnergy(g1, c1) +
                         other * SpectralPaths.HighEnergy(g2, c2);
            SpectralPaths.Decode(dc, high, out gain, out cutoff);
            coverage = (hit0 ? center : 0f) +
                       (hit1 ? other : 0f) + (hit2 ? other : 0f);
        }
    }
}
