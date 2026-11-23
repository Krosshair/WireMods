using System;
using UnityEngine;

namespace WiresAcoustics
{
    // Scalar Fresnel half-plane diffraction (Kirchhoff approximation).
    // See ITU-R P.526, single knife-edge geometry, and the normalized
    // Fresnel integrals C(v) and S(v). This is explicitly a ONE-edge model:
    // it is not a multi-wall, aperture, UTD or wave-simulation solver.
    //
    // A thin wall with uniform complex transmission T has field
    // U(v) = D(v) + T * (1 - D(v)), by linearity of the half-plane
    // Fresnel integral. Here T is real, because wall phase isn't known.
    // This is an approximation; unlike a fabricated geometric blend,
    // it is continuous and has the correct T=0/T=1 limits.
    //
    // The table is built ONCE, no trigonometry/allocations in live audio
    // geometry evaluation. Existing DSP still reproduces only two bands.
    internal static class FresnelEdge
    {
        private const float Speed = 343f;
        private const float Highest = 8f, Step = .02f;
        private const int Count = 401;
        private const float MidFrequency = 2000f;
        private static readonly float[] CosIntegral = new float[Count];
        private static readonly float[] SinIntegral = new float[Count];

        static FresnelEdge()
        {
            double c = 0.0, s = 0.0;
            for (int i = 1; i < Count; i++)
            {
                double left = (i - 1) * (double)Step;
                double step = Step / 4.0;
                double csum = 0.0, ssum = 0.0;
                for (int k = 0; k <= 4; k++)
                {
                    double x = left + k * step;
                    double phase = Math.PI * x * x * .5;
                    int weight = (k == 0 || k == 4) ? 1 : (k == 2 ? 2 : 4);
                    csum += weight * Math.Cos(phase);
                    ssum += weight * Math.Sin(phase);
                }
                c += csum * step / 3.0;
                s += ssum * step / 3.0;
                CosIntegral[i] = (float)c;
                SinIntegral[i] = (float)s;
            }
        }

        private static float Sample(float[] values, float v)
        {
            float u = Mathf.Clamp(Mathf.Abs(v), 0f, Highest) / Step;
            int i = Mathf.Min((int)u, Count - 2);
            float result = Mathf.Lerp(values[i], values[i + 1], Mathf.Clamp01(u - i));
            return v < 0f ? -result : result;
        }

        // The normalized complex field through a half-plane aperture:
        // D = (1+C+S)/2 + i(S-C)/2. v>0 is illuminated, v<0 shadow.
        internal static void Field(float v, out float real, out float imaginary)
        {
            float c = Sample(CosIntegral, v);
            float s = Sample(SinIntegral, v);
            real = .5f * (1f + c + s);
            imaginary = .5f * (s - c);
        }

        // Normalized geometric clearance (ITU-R P.526 Fresnel parameter).
        // d1/d2 are perpendicular source/listener distances to the wall
        // plane and must both be positive. Signed clearance >0 = opening.
        internal static float Parameter(float clearance, float d1, float d2, float frequency)
        {
            if (d1 <= 0f || d2 <= 0f) return 0f;
            float d = Mathf.Max(.001f, d1 * d2);
            float factor = 2f * (d1 + d2) * frequency / (Speed * d);
            return clearance * Mathf.Sqrt(factor);
        }

        // Calculate complex pressure for a partially transmitting thin
        // half-plane and return received intensity, not a made-up blend.
        internal static float Intensity(float clearance, float d1, float d2,
                                        float frequency, float transmission)
        {
            float v = Parameter(clearance, d1, d2, frequency);
            Field(v, out float re, out float im);
            float t = Mathf.Clamp01(transmission);
            float real = t + (1f - t) * re;
            float imaginary = (1f - t) * im;
            return Mathf.Clamp01(real * real + imaginary * imaginary);
        }

        internal static void Transfer(float clearance, float d1, float d2,
                                      float wallGain, float wallCutoff,
                                      out float gain, out float cutoff,
                                      out float bentFraction)
        {
            float tLow = Mathf.Clamp01(wallGain);
            float r = MidFrequency / Mathf.Max(200f, wallCutoff);
            float tHigh = tLow / Mathf.Sqrt(1f + r * r);
            float low = Intensity(clearance, d1, d2, 125f, tLow);
            float mid = Intensity(clearance, d1, d2, MidFrequency, tHigh);
            // The existing single gain + lowpass cannot represent spectral
            // boosts or interference notches. Cap safely rather than invent
            // extra filter degrees of freedom.
            // Convert measured intensity to the actual cascaded DSP
            // response instead of fitting a different one-pole filter.
            SpectralPaths.Decode(low, Mathf.Min(low, mid), out gain, out cutoff);
            // Direction is a perceptual proxy, not derived from the field's
            // acoustic intensity: only move toward the validated edge when
            // the wall has actually perturbed the direct field.
            bentFraction = Mathf.Clamp01((1f - gain) * (1f - tLow));
        }
    }
}
