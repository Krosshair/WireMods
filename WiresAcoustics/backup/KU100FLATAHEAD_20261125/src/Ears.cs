using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading;

namespace WiresAcoustics
{
    // The ears of a Neumann KU100 dummy head, measured (SADIE II subject D1, University of York,
    // Apache 2.0, see SADIE_LICENSE.txt). ku100.bin is made from the recordings by tools/ku100/make.js: for
    // each of the 24 recorded heights (the poles included) and every 5 degrees around, each ear's
    // response as a 64-sample minimum-phase filter (the tone and level) and its delay. The way OpenAL Soft does it. Of what both
    // ears hear together (the colour) the tool keeps only the broad shape, measured from straight ahead.
    internal sealed unsafe class Ears
    {
        internal readonly int Rows, Cols, N, Rate;
        private readonly float[] m_heights;  // degrees up, lowest first
        internal readonly int Past;          // how many samples back the far ear can reach
        private readonly float[] m_taps;     // reversed: the last one is the first to arrive
        private readonly float[] m_delays;   // samples

        private static Ears s_measured;      // as recorded, 48 kHz
        private static volatile Ears s_current;
        private static int s_making;
        internal static Ears Current => s_current;

        private Ears(int rate, int cols, int n, float[] heights, float[] taps, float[] delays)
        {
            Rate = rate; Rows = heights.Length; Cols = cols; N = n; m_heights = heights; m_taps = taps; m_delays = delays;
            float lo = float.MaxValue, hi = 0f;
            foreach (float d in delays) { lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
            Past = (int)(hi - lo) + EarState.Lead + 12;
        }

        internal static void Load(Stream s)
        {
            // packed without loss by tools/ku100/make.js
            if (s.ReadByte() != 'K' || s.ReadByte() != 'U' || s.ReadByte() != '4' || s.ReadByte() != 'F') throw new InvalidDataException("ku100.bin");
            using (BinaryReader r = new BinaryReader(new DeflateStream(s, CompressionMode.Decompress)))
            {
                int rate = r.ReadInt32(), rows = r.ReadInt32(), cols = r.ReadInt32(), n = r.ReadInt32();
                float scale = r.ReadSingle();
                float[] heights = new float[rows], delays = new float[2 * rows * cols], taps = new float[2 * rows * cols * n]; // left, then right
                for (int i = 0; i < rows; i++) heights[i] = r.ReadSingle();
                // the delays' bytes in four runs
                int count = delays.Length;
                byte[] d = r.ReadBytes(4 * count), one = new byte[4];
                for (int i = 0; i < count; i++)
                {
                    for (int k = 0; k < 4; k++) one[k] = d[k * count + i];
                    delays[i] = BitConverter.ToSingle(one, 0);
                }
                // the filters sample by sample, each as the step from the direction before
                byte[] hi = r.ReadBytes(count * n), lo = r.ReadBytes(count * n);
                for (int i = n - 1, q = 0; i >= 0; i--)
                {
                    short v = 0;
                    for (int g = 0; g < count; g++, q++)
                    {
                        int u = hi[q] << 8 | lo[q];
                        v = (short)(v + (u >> 1 ^ -(u & 1)));
                        taps[g * n + i] = v * scale;
                    }
                }
                s_measured = new Ears(rate, cols, n, heights, taps, delays);
                s_current = s_measured;
            }
        }

        // The output isn't at 48 kHz: the filters are worked out again for that rate, on a worker.
        internal static void Use(int rate)
        {
            Ears measured = s_measured, now = s_current;
            if (measured == null || rate <= 0 || now.Rate == rate) return;
            if (rate == measured.Rate) { s_current = measured; return; }
            if (Interlocked.Exchange(ref s_making, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { s_current = measured.At(rate); }
                finally { Interlocked.Exchange(ref s_making, 0); }
            });
        }

        internal Ears At(int rate)
        {
            // The filters are cut where they've died away (64 at 48 kHz). At double rates and up that costs
            // twice as much per sample on twice the samples, so there it's half as long.
            int n = rate <= 48000 ? N : Math.Max(N, (int)Math.Ceiling(32.0 * rate / 48000 / 8) * 8);
            const int size = 1024, half = size / 2;
            float[] delays = new float[m_delays.Length], taps = new float[m_delays.Length * n];
            double[] re = new double[size], im = new double[size], log = new double[half + 1];
            for (int g = 0; g < delays.Length; g++)
            {
                delays[g] = m_delays[g] * rate / Rate;
                Array.Clear(re, 0, size); Array.Clear(im, 0, size);
                for (int i = 0; i < N; i++) re[i] = m_taps[g * N + N - 1 - i];
                Fft(re, im, false);
                for (int k = 0; k <= half; k++) log[k] = .5 * Math.Log(Math.Max(1e-12, re[k] * re[k] + im[k] * im[k]));
                // magnitude at the new rate's frequencies, then back to minimum phase through the cepstrum
                for (int k = 0; k <= half; k++)
                {
                    double at = Math.Min(half, k * (double)rate / Rate);
                    int a = Math.Min(half - 1, (int)at);
                    double v = log[a] + (log[a + 1] - log[a]) * (at - a);
                    re[k] = v; im[k] = 0;
                    if (k > 0 && k < half) { re[size - k] = v; im[size - k] = 0; }
                }
                Fft(re, im, true);
                for (int i = 1; i < half; i++) { re[i] *= 2; im[i] = 0; }
                for (int i = half + 1; i < size; i++) { re[i] = 0; im[i] = 0; }
                im[0] = im[half] = 0;
                Fft(re, im, false);
                for (int i = 0; i < size; i++) { double e = Math.Exp(re[i]), p = im[i]; re[i] = e * Math.Cos(p); im[i] = e * Math.Sin(p); }
                Fft(re, im, true);
                const int fade = 16;
                for (int i = 0; i < n; i++)
                    taps[g * n + n - 1 - i] = (float)(re[i] * (i < n - fade ? 1 : .5 + .5 * Math.Cos(Math.PI * (i - (n - fade) + 1) / (fade + 1))));
            }
            return new Ears(rate, Cols, n, m_heights, taps, delays);
        }

        private static void Fft(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int b = n >> 1;
                for (; (j & b) != 0; b >>= 1) j ^= b;
                j ^= b;
                if (i >= j) continue;
                double t = re[i]; re[i] = re[j]; re[j] = t;
                t = im[i]; im[i] = im[j]; im[j] = t;
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double a = (inverse ? 2 : -2) * Math.PI / len;
                for (int k = 0; k < len / 2; k++)
                {
                    double c = Math.Cos(a * k), s = Math.Sin(a * k);
                    for (int x = k; x < n; x += len)
                    {
                        int y = x + len / 2;
                        double r = re[y] * c - im[y] * s, m = re[y] * s + im[y] * c;
                        re[y] = re[x] - r; im[y] = im[x] - m; re[x] += r; im[x] += m;
                    }
                }
            }
            if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }

        // Both ears' filters and delays for a direction (x right, y up, z ahead): the four measured ones
        // around it, blended. A wide source (amount under 1) has that much less of them. Only the delay
        // between the ears matters, so the nearer one hears it at once.
        internal void Blend(float x, float y, float z, float amount, float* hL, float* hR, out float dL, out float dR)
        {
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (!(len > 1e-6)) { x = y = 0f; z = 1f; len = 1; }
            double az = Math.Atan2(-x, z) * (Cols / (2 * Math.PI)); // counterclockwise: 90 degrees is left
            double el = Math.Asin(Math.Max(-1.0, Math.Min(1.0, y / len))) * (180.0 / Math.PI);
            if (az < 0) az += Cols;
            int r = 0, c = (int)az;
            while (r < Rows - 2 && el >= m_heights[r + 1]) r++;
            float tr = (float)((el - m_heights[r]) / (m_heights[r + 1] - m_heights[r])), tc = (float)(az - c);
            c %= Cols;
            int c1 = (c + 1) % Cols;
            dL = dR = 0f;
            fixed (float* taps = m_taps, delays = m_delays)
            {
                for (int i = 0; i < N; i++) hL[i] = hR[i] = 0f;
                for (int k = 0; k < 4; k++)
                {
                    int col = (k & 1) == 0 ? c : c1, row = r + (k >> 1);
                    float w = ((k & 1) == 0 ? 1f - tc : tc) * (k < 2 ? 1f - tr : tr) * amount;
                    int gl = row * Cols + col, gr = Rows * Cols + gl;
                    float* sl = taps + gl * N, sr = taps + gr * N;
                    Vector4 v = new Vector4(w);
                    for (int i = 0; i < N; i += 4)
                    {
                        *(Vector4*)(hL + i) += *(Vector4*)(sl + i) * v;
                        *(Vector4*)(hR + i) += *(Vector4*)(sr + i) * v;
                    }
                    dL += delays[gl] * w; dR += delays[gr] * w;
                }
            }
            hL[N - 1] += 1f - amount; hR[N - 1] += 1f - amount;
            float near = Math.Min(dL, dR);
            dL -= near; dR -= near;
        }
    }

    // One sound's way to both ears: what it has heard so far, and the filters it's at.
    internal sealed unsafe class EarState
    {
        internal const int Lead = 3;         // the delay's interpolation looks this far ahead
        private const int Step = 8;          // the filters move this often (samples)
        private const int Phases = 128, Width = 8;
        private static readonly float[] s_sinc = Sinc();

        private Ears m_ears;
        private float[] m_hL, m_hR, m_tL, m_tR, m_past, m_pastL, m_pastR;
        private readonly float[] m_pastSide = new float[Lead];
        private float m_dL, m_dR, m_dtL, m_dtR, m_x, m_y, m_z, m_amount = -1f;

        [ThreadStatic] private static float[] t_scratch;

        // Fractional delay: an 8-point windowed sinc at 128 steps between two samples, and a straight blend
        // in between those.
        private static float[] Sinc()
        {
            float[] t = new float[(Phases + 1) * Width];
            for (int p = 0; p <= Phases; p++)
            {
                double f = (double)p / Phases, sum = 0;
                double[] w = new double[Width];
                for (int k = 0; k < Width; k++)
                {
                    double d = k - 3 - f, u = d / 4.0;
                    double s = Math.Abs(d) < 1e-9 ? 1 : Math.Sin(Math.PI * d) / (Math.PI * d);
                    sum += w[k] = Math.Abs(u) >= 1 ? 0 : s * Bessel(3.0 * Math.Sqrt(1 - u * u)) / Bessel(3.0);
                }
                for (int k = 0; k < Width; k++) t[p * Width + k] = (float)(w[k] / sum);
            }
            return t;
        }

        private static double Bessel(double x)
        {
            double s = 1, t = 1;
            for (int k = 1; k < 25; k++) { t *= x / (2 * k) * (x / (2 * k)); s += t; }
            return s;
        }

        // mono: the sound, n samples. side: half the difference of its channels, or null. Written to data
        // (left, right). start: nothing of what it heard before counts.
        internal void Process(Ears ears, float* mono, float* side, float* data, int n,
            float x, float y, float z, float amount, bool start)
        {
            int N = ears.N, past = ears.Past;
            if (m_ears != ears)
            {
                m_ears = ears; start = true;
                m_hL = new float[N]; m_hR = new float[N]; m_tL = new float[N]; m_tR = new float[N];
                m_past = new float[past]; m_pastL = new float[N - 1]; m_pastR = new float[N - 1];
            }
            int need = past + n + 2 * (N - 1 + n) + 2 * N;
            float[] scratch = t_scratch;
            if (scratch == null || scratch.Length < need) t_scratch = scratch = new float[need];

            fixed (float* buf = scratch, hL = m_hL, hR = m_hR, tL = m_tL, tR = m_tR, pIn = m_past, pL = m_pastL, pR = m_pastR, pS = m_pastSide)
            {
                bool moved = start || x != m_x || y != m_y || z != m_z || amount != m_amount;
                if (moved)
                {
                    m_x = x; m_y = y; m_z = z; m_amount = amount;
                    ears.Blend(x, y, z, amount, tL, tR, out m_dtL, out m_dtR);
                }
                if (start)
                {
                    // as if its first sample had always been there (no step at the start)
                    Copy(tL, hL, N); Copy(tR, hR, N);
                    m_dL = m_dtL; m_dR = m_dtR;
                    float v = mono[0];
                    for (int i = 0; i < past; i++) pIn[i] = v;
                    for (int i = 0; i < N - 1; i++) pL[i] = pR[i] = v;
                    for (int i = 0; i < Lead; i++) pS[i] = side != null ? side[0] : 0f;
                    moved = false;
                }

                float* line = buf, lineL = line + past + n, lineR = lineL + N - 1 + n, sL = lineR + N - 1 + n, sR = sL + N;
                Copy(pIn, line, past); Copy(mono, line + past, n);
                Copy(pL, lineL, N - 1); Copy(pR, lineR, N - 1);
                Delay(line + past, lineL + N - 1, n, m_dL, m_dtL);
                Delay(line + past, lineR + N - 1, n, m_dR, m_dtR);
                m_dL = m_dtL; m_dR = m_dtR;

                if (moved)
                {
                    float steps = 1f / ((n + Step - 1) / Step);
                    for (int i = 0; i < N; i++) { sL[i] = (tL[i] - hL[i]) * steps; sR[i] = (tR[i] - hR[i]) * steps; }
                }
                for (int at = 0; at < n; at += Step)
                {
                    int end = Math.Min(n, at + Step);
                    if (moved)
                        for (int i = 0; i < N; i += 4)
                        {
                            *(Vector4*)(hL + i) += *(Vector4*)(sL + i);
                            *(Vector4*)(hR + i) += *(Vector4*)(sR + i);
                        }
                    for (int i = at; i < end; i++)
                    {
                        data[2 * i] = Fir(hL, lineL + i, N);
                        data[2 * i + 1] = Fir(hR, lineR + i, N);
                    }
                }
                if (moved) { Copy(tL, hL, N); Copy(tR, hR, N); } // exactly there, no rounding left over

                Copy(line + n, pIn, past);
                Copy(lineL + n, pL, N - 1); Copy(lineR + n, pR, N - 1);
                for (int i = 0; side != null && i < n; i++)
                {
                    float s = i < Lead ? pS[i] : side[i - Lead];
                    data[2 * i] += s; data[2 * i + 1] -= s;
                }
                for (int i = 0; i < Lead; i++) pS[i] = side != null && n >= Lead ? side[n - Lead + i] : 0f;
            }
        }

        private static void Copy(float* from, float* to, int n)
        {
            for (int i = 0; i < n; i++) to[i] = from[i];
        }

        private static float Fir(float* h, float* x, int n)
        {
            Vector4 a = Vector4.Zero, b = Vector4.Zero;
            for (int j = 0; j < n; j += 8)
            {
                a += *(Vector4*)(h + j) * *(Vector4*)(x + j);
                b += *(Vector4*)(h + j + 4) * *(Vector4*)(x + j + 4);
            }
            a += b;
            return a.X + a.Y + a.Z + a.W;
        }

        // x read that many samples (and the interpolation's lead) late, gliding from one delay to the other
        private static void Delay(float* x, float* o, int n, float from, float to)
        {
            Vector4 a, b, s;
            if (from == to)
            {
                int whole = (int)from;
                if (from == whole) { Copy(x - Lead - whole, o, n); return; }
                // late by a whole number and a bit: one sample further back, and the rest of the bit on
                float* q = x - Lead - whole - 1;
                Weights(1f - (from - whole), out a, out b);
                for (int i = 0; i < n; i++, q++)
                {
                    s = *(Vector4*)(q - 3) * a + *(Vector4*)(q + 1) * b;
                    o[i] = s.X + s.Y + s.Z + s.W;
                }
                return;
            }
            float step = (to - from) / n;
            for (int i = 0; i < n; i++)
            {
                float d = from + step * (i + 1);
                if (d < 0f) d = 0f;
                int whole = (int)d;
                float* q = x + i - Lead - whole - 1;
                Weights(1f - (d - whole), out a, out b);
                s = *(Vector4*)(q - 3) * a + *(Vector4*)(q + 1) * b;
                o[i] = s.X + s.Y + s.Z + s.W;
            }
        }

        private static void Weights(float f, out Vector4 a, out Vector4 b)
        {
            float p = f * Phases;
            int r = (int)p;
            if (r >= Phases) r = Phases - 1;
            Vector4 t = new Vector4(p - r);
            fixed (float* w = &s_sinc[r * Width])
            {
                Vector4 a0 = *(Vector4*)w, b0 = *(Vector4*)(w + 4);
                a = a0 + (*(Vector4*)(w + 8) - a0) * t;
                b = b0 + (*(Vector4*)(w + 12) - b0) * t;
            }
        }
    }
}
