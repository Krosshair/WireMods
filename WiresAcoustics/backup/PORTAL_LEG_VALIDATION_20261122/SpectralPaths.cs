using UnityEngine;

namespace WiresAcoustics
{
    // Energy-conserving spectral composition within the existing two-parameter
    // DSP approximation. An incoherent route sum is appropriate for spatially
    // separated paths at delays exceeding the coherence of typical broadband
    // game audio. This does NOT simulate phase interference or a full IR.
    //
    // Crucially: averaging cutoff frequencies (even logarithmically) cannot
    // recover the summed spectrum. We must add each route's energy in the
    // same frequency bands, then solve the equivalent lowpass.
    internal static class SpectralPaths
    {
        private const float Frequency = 2000f;

        internal static float HighEnergy(float gain, float cutoff)
        {
            float low = gain * gain;
            if (cutoff >= 22000f) return low;
            float x = Frequency / Mathf.Max(200f, cutoff);
            return low / (1f + x * x);
        }

        internal static void Add(float gain, float cutoff,
                                 ref float low, ref float high)
        {
            low += gain * gain;
            high += HighEnergy(gain, cutoff);
        }

        internal static void Decode(float low, float high,
                                    out float gain, out float cutoff)
        {
            low = Mathf.Clamp01(Mathf.Max(0f, low));
            high = Mathf.Clamp(high, 0f, low);
            gain = Mathf.Sqrt(low);
            if (low <= 1e-8f || high >= low * .99999f)
            {
                cutoff = 22000f;
                return;
            }
            float ratio = low / Mathf.Max(1e-10f, high) - 1f;
            cutoff = Mathf.Clamp(Frequency / Mathf.Sqrt(ratio), 200f, 22000f);
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
