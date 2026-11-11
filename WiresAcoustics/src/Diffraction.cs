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

        internal static float Loss(float delta, float f)
        {
            float n = 2f * delta * f / Sound;
            if (n <= 1e-6f) return 5f;
            float x = Mathf.Sqrt(2f * Mathf.PI * n);
            float t = (float)System.Math.Tanh(x);
            return Mathf.Min(Cap, 5f + 20f * Mathf.Log10(x / t));
        }

        // Something in the way that sound can go over and round both sides of (a rock, furniture, a
        // trunk): the three ways added by energy, as ISO 9613-2 does for a barrier of limited width
        // (over the top and round each vertical edge). Each way by the formula above, from the thing's
        // box. A thin trunk barely matters (all three ways are short), a boulder does.
        internal static bool RoundObstacle(Bounds b, Vector3 listener, Vector3 sound, out float gain, out float cutoff)
        {
            gain = 1f; cutoff = 22000f;
            if (b.Contains(listener) || b.Contains(sound)) return false;
            Vector3 d = sound - listener;
            float len2 = d.sqrMagnitude;
            if (len2 < 1e-4f) return false;
            Vector3 p = listener + d * Mathf.Clamp01(Vector3.Dot(b.center - listener, d) / len2);
            Vector3 perp = Vector3.Cross(Vector3.up, d);
            perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector3.right;
            float w = Mathf.Abs(b.extents.x * perp.x) + Mathf.Abs(b.extents.z * perp.z);
            float off = Vector3.Dot(b.center - p, perp);
            float direct = Mathf.Sqrt(len2);
            float low = 0f, mid = 0f;
            for (int k = 0; k < 3; k++)
            {
                Vector3 e = k == 0 ? new Vector3(p.x, Mathf.Max(p.y, b.max.y), p.z) : p + perp * (k == 1 ? off + w : off - w);
                float delta = Mathf.Max(0f, (e - listener).magnitude + (sound - e).magnitude - direct);
                low += Mathf.Pow(10f, -Loss(delta, 125f) / 10f);
                mid += Mathf.Pow(10f, -Loss(delta, 2000f) / 10f);
            }
            low = Mathf.Min(1f, low); mid = Mathf.Min(1f, mid);
            gain = Mathf.Sqrt(low);
            float r = low / Mathf.Max(1e-9f, mid) - 1f; // the low-pass that takes the rest at 2 kHz
            cutoff = r <= 1e-4f ? 22000f : Mathf.Clamp(2000f / Mathf.Sqrt(r), 200f, 22000f);
            return true;
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
