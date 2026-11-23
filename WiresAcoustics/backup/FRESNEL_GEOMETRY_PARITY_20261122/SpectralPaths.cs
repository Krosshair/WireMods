using System;
using UnityEngine;

namespace WiresAcoustics
{
    // Propagation-energy fit to AcousticFilter's ACTUAL two-pole digital DSP.
    //
    // At digital angular frequency w, each first-order stage has
    // a = 1-exp(-2*pi*FilterStage*cutoff/sampleRate),
    // |H1(w)|^2 = a^2 / (a^2 + 2*(1-a)*(1-cos(w))).
    // TWO cascaded identical stages have power |H2|^2=|H1|^4.
    // The direct DSP deliberately bypasses at cutoff >=20000 Hz; this
    // discontinuity is respected and accounted for in Decode.
    //
    // Incoherent alternative paths add spectral energy; consecutive
    // transmissions multiply spectral energy. This is a two-point
    // approximation (DC and 2 kHz), NOT full-band acoustic propagation.
    internal static class SpectralPaths
    {
        private const float Frequency = 2000f;
        private const float Bypass = 20000f;
        private static int s_sampleRate = 48000;

        internal static void SetSampleRate(int rate)
        {
            if (rate >= 8000 && rate <= 384000) s_sampleRate = rate;
        }

        internal static float Response(float cutoff)
        {
            if (cutoff >= Bypass) return 1f;
            double rate = s_sampleRate;
            double w = 2.0 * Math.PI * Math.Min(Frequency, rate * .49) / rate;
            double a = 1.0 - Math.Exp(-2.0 * Math.PI *
                AcousticMath.FilterStage * Math.Max(1.0, cutoff) / rate);
            double a2 = a * a;
            double denom = a2 + 2.0 * (1.0 - a) * (1.0 - Math.Cos(w));
            if (denom <= 0.0) return 0f;
            double onePoleEnergy = a2 / denom;
            return (float)(onePoleEnergy * onePoleEnergy);
        }

        internal static float HighEnergy(float gain, float cutoff)
        {
            return gain * gain * Response(cutoff);
        }

        internal static void Add(float gain, float cutoff,
                                 ref float dc, ref float high)
        {
            dc += gain * gain;
            high += HighEnergy(gain, cutoff);
        }

        // Analytic inverse of the cascaded digital one-pole response.
        // q=sqrt(E_high/E_DC) is the one-pole power response. Solving
        // q=a^2/(a^2+K*(1-a)) for the positive a yields
        // a=2*q*K/(sqrt(q*q*K*K+4*q*K*(1-q))+q*K).
        private static float CutoffFor(float fraction)
        {
            if (fraction >= 1f) return 22000f;
            if (fraction <= 0f) return 1f;

            double rate = s_sampleRate;
            double w = 2.0 * Math.PI * Math.Min(Frequency, rate * .49) / rate;
            double k = 2.0 * (1.0 - Math.Cos(w));
            double q = Math.Sqrt(fraction);
            double b = q * k;
            double disc = Math.Sqrt(b * b + 4.0 * q * k * (1.0 - q));
            double a = (2.0 * q * k) / (disc + b);
            a = Math.Min(1.0 - 1e-14, Math.Max(1e-14, a));
            double cutoff = -rate * Math.Log(1.0 - a) /
                (2.0 * Math.PI * AcousticMath.FilterStage);

            // The actual DSP bypasses at 20k rather than approaching it
            // continuously. Choose whichever implementable response is closer.
            if (cutoff >= Bypass)
            {
                float filtered = Response(19999f);
                return Math.Abs(fraction - 1f) < Math.Abs(fraction - filtered)
                    ? 22000f : 19999f;
            }
            return (float)Math.Max(1.0, cutoff);
        }

        internal static void Decode(float low, float high,
                                    out float gain, out float cutoff)
        {
            // When separate paths sum above unity, attenuate the whole
            // energy spectrum by the same factor before fitting the DSP.
            // Clamping DC alone would spuriously change the spectral shape.
            float total = Mathf.Max(0f, low);
            float ratio = total > 1f ? 1f / total : 1f;
            low = Mathf.Min(1f, total);
            high = Mathf.Clamp(high * ratio, 0f, low);
            gain = Mathf.Sqrt(low);
            if (low <= 1e-12f)
            {
                cutoff = 22000f;
                return;
            }
            cutoff = CutoffFor(high / low);
        }

        internal static void Cascade(float aGain, float aCut,
                                     float bGain, float bCut,
                                     out float gain, out float cutoff)
        {
            float low = aGain * aGain * bGain * bGain;
            float high = HighEnergy(aGain, aCut) * HighEnergy(bGain, bCut);
            Decode(low, high, out gain, out cutoff);
        }

        internal static void Combine(float aGain, float aCut,
                                     float bGain, float bCut,
                                     out float gain, out float cutoff)
        {
            float low = 0f, high = 0f;
            Add(aGain, aCut, ref low, ref high);
            Add(bGain, bCut, ref low, ref high);
            Decode(low, high, out gain, out cutoff);
        }
    }
}
