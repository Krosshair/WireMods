using UnityEngine;

namespace WiresAcoustics
{
    // Sound bending round an edge (a doorway's side, a window's frame, a wall's corner): the
    // barrier formula of Maekawa as Kurze and Anderson wrote it, D = 5 + 20 lg(x / tanh x) with
    // x = sqrt(2 pi N) and the Fresnel number N = 2 delta / wavelength, delta being how much longer
    // the way round the edge is than the straight line. Capped at 20 dB, as ISO 9613-2 does for one
    // edge. The longer the detour, the more it takes, and more from the highs than the lows: here
    // as the loss at 125 Hz (the gain) and a low-pass taking the rest of the loss at 2 kHz.
    internal static class Diffraction
    {
        private const float Step = .05f, Max = 50f, Cap = 20f, Sound = 343f;
        private static readonly float[] Gains, Cutoffs;

        static Diffraction()
        {
            int n = (int)(Max / Step) + 1;
            Gains = new float[n];
            Cutoffs = new float[n];
            for (int i = 0; i < n; i++)
            {
                float delta = i * Step;
                float low = Loss(delta, 125f);
                Gains[i] = Mathf.Pow(10f, -low / 20f);
                // the low-pass that takes as much more at 2 kHz as the edge does (a single low-pass
                // keeps falling where the formula levels off at its cap: matched in the middle)
                float extra = Loss(delta, 2000f) - low;
                float r = Mathf.Pow(10f, extra / 10f) - 1f;
                Cutoffs[i] = r <= 1e-4f ? 22000f : Mathf.Clamp(2000f / Mathf.Sqrt(r), 200f, 22000f);
            }
        }

        private static float Loss(float delta, float f)
        {
            float n = 2f * delta * f / Sound;
            if (n <= 1e-6f) return 5f;
            float x = Mathf.Sqrt(2f * Mathf.PI * n);
            float t = (float)System.Math.Tanh(x);
            return Mathf.Min(Cap, 5f + 20f * Mathf.Log10(x / t));
        }

        // Round the edge at "edge" from the listener to the sound.
        internal static void Round(Vector3 listener, Vector3 edge, Vector3 sound, out float gain, out float cutoff)
        {
            float delta = (edge - listener).magnitude + (sound - edge).magnitude - (sound - listener).magnitude;
            float x = Mathf.Clamp(delta, 0f, Max) / Step;
            int i = Mathf.Min((int)x, Gains.Length - 2);
            float t = x - i;
            gain = Mathf.Lerp(Gains[i], Gains[i + 1], t);
            cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(Cutoffs[i]), Mathf.Log(Cutoffs[i + 1]), t));
        }
    }
}
