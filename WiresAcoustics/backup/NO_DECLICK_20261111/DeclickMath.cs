using System;
using System.Collections.Generic;

namespace WiresAcoustics
{
    // Many of the game's sound effects have the same defect baked in: a short ring
    // somewhere around 2.5 to 9.5 kHz (lower on stone, higher on wood) that dies away within about a third of a
    // millisecond. On a quiet tail it comes out as a sharp click.
    // Every rough spot is checked against that shape first (a ring rising and
    // decaying at one pitch, fitted over pitch, decay and start). Only spots where
    // the ring accounts for at least 60% of what sticks out are treated as the
    // click; natural grain and creature clicks don't fit and are left as they are.
    // A matched spot is smoothed over 4 ms (blended in and out), harder each round,
    // until it sits no more than 3 dB over the sound around it. Nothing else in the
    // clip is touched.
    // One channel at a time. Pure math, no Unity calls, so it can run off the main thread.
    internal static class DeclickMath
    {
        private const int Window = 12, Reach = 480, Edge = 32, Span = 96, Ring = 64, Pad = 16;
        private const float Rough = 3.16f, Match = .6f, Settled = 1.4f;
        private static readonly int[] Passes = { 9, 13, 19, 27, 39 };
        private static readonly int[] Decays = { 2, 3, 5, 8, 12 };
        private static readonly Dictionary<int, Shape[]> Shapes = new Dictionary<int, Shape[]>();

        private sealed class Shape
        {
            internal float[] C, S;
            internal double CC, CS, SS, Det;
        }

        // One removed click: where, in which detection round, and how many smoothing
        // passes it took. Enough to redo exactly the same fix without searching.
        internal struct Fix
        {
            internal int Pos;
            internal byte Round, Passes;
        }

        internal static float[] Clean(float[] x, int rate, out int found)
        {
            float[] y = Clean(x, rate, null);
            found = s_lastCount;
            return y;
        }

        [System.ThreadStatic] private static int s_lastCount;

        internal static float[] Clean(float[] x, int rate, List<Fix> fixes)
        {
            int n = x.Length;
            s_lastCount = 0;
            if (n < 2 * Reach + 2) return x;
            Shape[] shapes = ShapesFor(rate);
            float[] y = (float[])x.Clone();
            var marked = new List<int>();
            for (int round = 0; round < 3; round++)
            {
                float[] h = High(y);
                var fresh = new List<int>();
                foreach (int j in Find(y, Rough, -1000000))
                {
                    bool near = false;
                    foreach (int m in marked) if (Math.Abs(j - m) < 48) { near = true; break; }
                    if (near) continue;
                    if (Fit(h, j, shapes) >= Match) fresh.Add(j);
                }
                if (fresh.Count == 0) break;
                marked.AddRange(fresh);
                var passes = new Dictionary<int, int>();
                var todo = fresh;
                for (int p = 0; p < Passes.Length; p++)
                {
                    y = Patch(y, todo, Passes[p]);
                    foreach (int j in todo) passes[j] = p + 1;
                    h = High(y);
                    var left = new List<int>();
                    foreach (int j in todo) if (StandsOut(h, j)) left.Add(j);
                    todo = left;
                    if (todo.Count == 0) break;
                }
                if (fixes != null)
                    foreach (int j in fresh) fixes.Add(new Fix { Pos = j, Round = (byte)round, Passes = (byte)passes[j] });
            }
            s_lastCount = marked.Count;
            return y;
        }

        // Redo known fixes on part of one channel. buf holds samples [start, start + buf.Length)
        // of a clip `length` samples long; it must reach at least Margin past every fix, or
        // to the clip's edge.
        internal const int Margin = Span + 40;

        internal static void Replay(float[] buf, int start, int length, List<Fix> fixes)
        {
            int rounds = 0;
            foreach (Fix f in fixes) rounds = Math.Max(rounds, f.Round + 1);
            var group = new List<int>();
            for (int r = 0; r < rounds; r++)
                for (int p = 0; p < Passes.Length; p++)
                {
                    group.Clear();
                    foreach (Fix f in fixes) if (f.Round == r && f.Passes > p) group.Add(f.Pos);
                    if (group.Count == 0) break;
                    PatchLocal(buf, start, length, group, Passes[p]);
                }
        }

        private static void PatchLocal(float[] buf, int start, int length, List<int> spots, int len)
        {
            int n = buf.Length;
            var m = new float[n];
            var s = new float[n];
            var have = new bool[n];
            float[] src = (float[])buf.Clone();
            int h = (len - 1) / 2;
            foreach (int j in spots)
            {
                int a = Math.Max(0, j - Span), b = Math.Min(length, j + Span), w = b - a;
                int f = Math.Min(Edge, w / 2);
                for (int k = 0; k < w; k++)
                {
                    float v = 1f;
                    if (f > 0 && k < f) v = Hann(k, 2 * f);
                    else if (f > 0 && k >= w - f) v = Hann(k - (w - f) + f, 2 * f);
                    int i = a + k - start;
                    if (v > m[i]) m[i] = v;
                    if (!have[i]) { s[i] = SmoothAt(src, start, length, a + k, len, h); have[i] = true; }
                }
            }
            for (int i = 0; i < n; i++) if (m[i] > 0f) buf[i] = src[i] * (1f - m[i]) + s[i] * m[i];
        }

        // Smooth(x, len) at one sample: a moving average of a moving average, zeros outside the clip.
        private static float SmoothAt(float[] src, int start, int length, int at, int len, int h)
        {
            double outer = 0;
            for (int i = at - h; i <= at + h; i++)
            {
                if (i < 0 || i >= length) continue;
                double inner = 0;
                for (int k = i - h; k <= i + h; k++)
                    if (k >= 0 && k < length) inner += src[k - start];
                outer += (float)(inner / len);
            }
            return (float)(outer / len);
        }

        private static Shape[] ShapesFor(int rate)
        {
            lock (Shapes)
            {
                if (Shapes.TryGetValue(rate, out Shape[] made)) return made;
                var list = new List<Shape>();
                for (int f = 2500; f <= 9500; f += 250)
                    foreach (int tau in Decays)
                    {
                        var c = new float[Ring + 2 * Pad];
                        var s = new float[Ring + 2 * Pad];
                        for (int t = 0; t < Ring; t++)
                        {
                            double env = (double)t / tau * Math.Exp(1.0 - (double)t / tau);
                            double w = 2 * Math.PI * f / rate * t;
                            c[Pad + t] = (float)(env * Math.Cos(w));
                            s[Pad + t] = (float)(env * Math.Sin(w));
                        }
                        var sh = new Shape { C = High(c), S = High(s) };
                        for (int i = 0; i < sh.C.Length; i++)
                        {
                            sh.CC += sh.C[i] * sh.C[i];
                            sh.CS += sh.C[i] * sh.S[i];
                            sh.SS += sh.S[i] * sh.S[i];
                        }
                        sh.Det = sh.CC * sh.SS - sh.CS * sh.CS;
                        list.Add(sh);
                    }
                made = list.ToArray();
                Shapes[rate] = made;
                return made;
            }
        }

        // Best share (0..1) of the high band near j that a single ring explains.
        private static double Fit(float[] h, int j, Shape[] shapes)
        {
            double best = double.NegativeInfinity;
            int len = Ring + 2 * Pad;
            for (int t0 = j - 16; t0 < j + 6; t0++)
            {
                int a = t0 - Pad;
                if (a < 0 || a + len > h.Length) continue;
                double yy = 0;
                for (int i = 0; i < len; i++) yy += h[a + i] * h[a + i];
                if (yy <= 0) continue;
                foreach (Shape sh in shapes)
                {
                    if (Math.Abs(sh.Det) < 1e-20) continue;
                    double rc = 0, rs = 0;
                    for (int i = 0; i < len; i++) { rc += sh.C[i] * h[a + i]; rs += sh.S[i] * h[a + i]; }
                    double kc = (sh.SS * rc - sh.CS * rs) / sh.Det;
                    double ks = (sh.CC * rs - sh.CS * rc) / sh.Det;
                    double share = (kc * rc + ks * rs) / yy;
                    if (share > best) best = share;
                }
            }
            return best;
        }

        private static bool StandsOut(float[] h, int j)
        {
            double w = 0;
            for (int i = j - 24; i < j + 24; i++) w += h[i] * h[i];
            w = Math.Sqrt(w / 48);
            var around = new List<float>();
            for (int i = Math.Max(0, j - 480); i < j - 48; i++) around.Add(Math.Abs(h[i]));
            for (int i = j + 48; i < Math.Min(h.Length, j + 480); i++) around.Add(Math.Abs(h[i]));
            around.Sort();
            int m = around.Count;
            double med = m == 0 ? 0 : (m % 2 == 1 ? around[m / 2] : .5 * (around[m / 2 - 1] + around[m / 2]));
            return w > (med * 1.25 + 1e-12) * Settled;
        }

        private static float[] High(float[] x)
        {
            float[] s = Smooth(x, 9);
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = x[i] - s[i];
            return y;
        }

        // Rough spots: the high band (RMS over 12 samples) th times the median of the
        // 20 ms around it, and above -54 dB of the clip's peak.
        private static List<int> Find(float[] x, float th, int loudest)
        {
            int n = x.Length;
            float[] high = High(x);
            float[] d2 = new float[n];
            for (int i = 0; i < n; i++) d2[i] = high[i] * high[i];
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
                    if (Math.Abs(j - loudest) > 144) hits.Add(j);
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
