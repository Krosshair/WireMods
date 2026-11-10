using System;
using System.Collections.Generic;

namespace WiresAcoustics
{
    // TEST: the earlier "declicked3" processing (the version the user heard as fixed).
    // Many of the game's sound effects (first noticed on wood and stone footsteps) have short bursts of high-frequency crackle baked
    // into them (about 10 samples ringing near 10 kHz, often on the quiet tail, where
    // they come out as a sharp click). This cleans a mono clip's samples:
    //  - the tail (once the step is 12 dB below its loudest) loses everything above
    //    roughly 5 kHz, fading out over 10 ms;
    //  - anywhere else, a spot 10 dB rougher than the 10 ms around it is smoothed,
    //    re-checked, and smoothed harder until nothing sticks out (6 passes at most);
    //  - the 3 ms around the loudest sample, the impact itself, is never touched.
    // One channel at a time. Pure math, no Unity calls, so it can run off the main thread.
    internal static class DeclickMathC
    {
        private const int Window = 12, Reach = 480, Guard = 144, Edge = 32, Span = 96;
        private static readonly int[] Passes = { 9, 13, 19, 27, 39, 55 };

        // One smoothing step of the cleaning: where, and in which pass. With the point the
        // tail cut starts from, enough to redo the same cleaning without searching.
        internal struct Step
        {
            internal int Pos;
            internal byte Pass;
        }

        internal static float[] Clean(float[] x, bool tail, out int found) => Clean(x, tail, out found, null, out _);

        internal static float[] Clean(float[] x, bool tail, out int found, List<Step> steps, out int tailFrom)
        {
            int n = x.Length;
            found = 0;
            tailFrom = n;
            if (n < 2 * Reach + 2) return x;
            float[] s = Smooth(x, 9);
            float[] y = new float[n];

            float[] sq = new float[n];
            for (int i = 0; i < n; i++) sq[i] = x[i] * x[i];
            float[] env = Box(sq, 480);
            int p = 0;
            for (int i = 1; i < n; i++) if (env[i] > env[p]) p = i;
            // a looping clip has no tail: cutting the top there would come back at every loop point
            int from = n;
            if (tail) for (int i = p; i < n; i++) if (env[i] < env[p] * .0625f) { from = i; break; } // 0.25 in level
            tailFrom = from;
            CutTail(x, s, y, from);

            int loudest = 0;
            for (int i = 1; i < n; i++) if (Math.Abs(x[i]) > Math.Abs(x[loudest])) loudest = i;

            for (int pass = 0; pass < Passes.Length; pass++)
            {
                List<int> hits = Find(y, 3.16f, loudest);
                if (hits.Count == 0) break;
                found += hits.Count;
                if (steps != null) foreach (int j in hits) steps.Add(new Step { Pos = j, Pass = (byte)pass });
                y = Patch(y, hits, Passes[pass]);
            }
            return y;
        }

        // Past `from` the part above the smoothed signal fades out over 480 samples and stays out.
        private static void CutTail(float[] x, float[] s, float[] y, int from)
        {
            int n = x.Length, ramp = Math.Min(480, n - from);
            for (int i = 0; i < n; i++)
            {
                float g = 1f;
                if (i >= from + 480) g = 0f;
                else if (i >= from) g = ramp > 1 ? 1f - (float)(i - from) / (ramp - 1) : 0f;
                y[i] = i < from ? x[i] : s[i] + (x[i] - s[i]) * g;
            }
        }

        // Redo a recorded cleaning on a whole channel, in place. Positions past the end
        // (the game's copy can be shorter than the one the record was made from) are skipped.
        internal static void Replay(float[] x, int tailFrom, List<Step> steps)
        {
            int n = x.Length;
            float[] y = x;
            if (tailFrom < n)
            {
                y = new float[n];
                CutTail(x, Smooth(x, 9), y, tailFrom);
            }
            var group = new List<int>();
            for (int pass = 0; pass < Passes.Length; pass++)
            {
                group.Clear();
                if (steps != null) foreach (Step st in steps) if (st.Pass == pass && st.Pos < n) group.Add(st.Pos);
                if (group.Count == 0) continue;
                y = Patch(y, group, Passes[pass]);
            }
            if (!ReferenceEquals(y, x)) Array.Copy(y, x, n);
        }

        // Spots whose roughness (second difference, RMS over 12 samples) is th times the
        // median of the 20 ms around them, and above -54 dB of the clip's peak.
        private static List<int> Find(float[] x, float th, int loudest)
        {
            int n = x.Length;
            float[] d2 = new float[n];
            for (int i = 1; i < n - 1; i++) { float d = x[i + 1] - 2f * x[i] + x[i - 1]; d2[i] = d * d; }
            float[] e = Box(d2, Window);
            for (int i = 0; i < n; i++) e[i] = (float)Math.Sqrt(Math.Max(0f, e[i]));
            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(x[i]));
            float floor = peak * 2e-3f;

            var hits = new List<int>();
            var sorted = new List<float>(2 * Reach + 1);
            for (int k = 0; k < 2 * Reach; k++) Insert(sorted, e[k]);
            int at = Reach;
            int i0 = Reach;
            while (i0 < n - Reach)
            {
                while (at < i0)
                {
                    Remove(sorted, e[at - Reach]);
                    Insert(sorted, e[at + Reach]);
                    at++;
                }
                float local = .5f * (sorted[Reach - 1] + sorted[Reach]);
                if (e[i0] > th * local && e[i0] > floor)
                {
                    int j = i0;
                    for (int k = i0 + 1; k < Math.Min(n, i0 + Window); k++) if (e[k] > e[j]) j = k;
                    if (Math.Abs(j - loudest) > Guard) hits.Add(j);
                    i0 = j + 2 * Window;
                }
                else i0++;
            }
            return hits;
        }

        private static float[] Patch(float[] x, List<int> hits, int len)
        {
            int n = x.Length;
            float[] s = Smooth(x, len);
            float[] m = new float[n];
            foreach (int j in hits)
            {
                int a = Math.Max(0, j - Span), b = Math.Min(n, j + Span), w = b - a;
                int f = Math.Min(Edge, w / 2);
                for (int k = 0; k < w; k++)
                {
                    float v = 1f;
                    if (f > 0 && k < f) v = Hann(k, 2 * f);
                    else if (f > 0 && k >= w - f) v = Hann(k - (w - f) + f, 2 * f);
                    if (v > m[a + k]) m[a + k] = v;
                }
            }
            float[] y = new float[n];
            for (int i = 0; i < n; i++) y[i] = x[i] * (1f - m[i]) + s[i] * m[i];
            return y;
        }

        private static float Hann(int k, int len) => .5f - .5f * (float)Math.Cos(2 * Math.PI * k / (len - 1));

        private static float[] Smooth(float[] x, int len) => Box(Box(x, len), len);

        // moving average, same alignment as numpy's convolve(..., 'same'); zeros outside the clip
        private static float[] Box(float[] x, int len)
        {
            int n = x.Length;
            var sum = new double[n + 1];
            for (int i = 0; i < n; i++) sum[i + 1] = sum[i] + x[i];
            int hi = (len - 1) / 2, lo = hi - (len - 1);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i + lo), b = Math.Min(n - 1, i + hi);
                y[i] = (float)((sum[b + 1] - sum[a]) / len);
            }
            return y;
        }

        private static void Insert(List<float> list, float v)
        {
            int i = list.BinarySearch(v);
            list.Insert(i < 0 ? ~i : i, v);
        }

        private static void Remove(List<float> list, float v)
        {
            int i = list.BinarySearch(v);
            if (i >= 0) list.RemoveAt(i);
        }
    }
}
