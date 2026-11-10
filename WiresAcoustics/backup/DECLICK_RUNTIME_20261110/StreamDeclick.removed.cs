using System;

namespace WiresAcoustics
{
    // Live version of the click removal, run on every sound effect as it plays (the
    // per-sound filter calls it first). Each channel is split into a low band (a moving
    // average of a moving average, 9 samples each) and the high band that's left over.
    // When the high band jumps 10 dB over its own recent level while the low band
    // doesn't move (a click is only high band; a real hit is both), the high band is
    // dropped for 2 ms and brought back over about 2 ms. The signal runs 20 samples
    // (about 0.4 ms) late so the drop is already in place when the click arrives.
    // A few dozen multiply-adds per sample, no allocation after the first call.
    internal sealed class StreamDeclick
    {
        private const int Len = 13, Win = 12, Ahead = 12, Delay = Len - 1 + Ahead, Ring = 64, Mask = Ring - 1;
        private const float Rise = 3.16f, Floor = 3e-4f, LowGuard = 2.5f;
        private const int Hold = 96, Settle = 240; // 5 ms for the references to find the level after a start

        private sealed class Lane
        {
            internal readonly float[] X = new float[Ring], B = new float[Ring], Hq = new float[Ring],
                Lq = new float[Ring], Lo = new float[Ring], Hi = new float[Ring];
            internal double S1, S2, Sh, Sl;
            internal float RefH, RefL, Gain = 1f;
            internal int Held, N, Warm = Settle;
        }

        private Lane[] m_lanes = new Lane[0];
        private float m_up, m_down;
        internal bool Stale = true;
        internal int Triggers;
        internal long Closed, Seen;

        internal void Process(float[] data, int channels, int rate)
        {
            if (channels <= 0 || channels > 8) return;
            if (m_lanes.Length != channels)
            {
                m_lanes = new Lane[channels];
                for (int c = 0; c < channels; c++) m_lanes[c] = new Lane();
                rate = Math.Max(8000, rate);
                m_up = 1f / (.03f * rate);    // the reference climbs slowly, so a click hardly lifts it
                m_down = 1f / (.005f * rate); // and falls back fast in quiet stretches
                Stale = false;
            }
            else if (Stale)
            {
                foreach (Lane l in m_lanes) Reset(l);
                Stale = false;
            }
            int frames = data.Length / channels;
            for (int c = 0; c < channels; c++)
            {
                Lane l = m_lanes[c];
                for (int f = 0; f < frames; f++)
                {
                    int i = f * channels + c;
                    int n = l.N;
                    float x = data[i];
                    l.X[n & Mask] = x;
                    l.S1 += x - l.X[(n - Len) & Mask];
                    float b = (float)(l.S1 / Len);
                    l.B[n & Mask] = b;
                    l.S2 += b - l.B[(n - Len) & Mask];
                    float low = (float)(l.S2 / Len);
                    float high = l.X[(n - (Len - 1)) & Mask] - low;
                    l.Lo[n & Mask] = low;
                    l.Hi[n & Mask] = high;
                    float hq = high * high, lq = low * low;
                    l.Sh += hq - l.Hq[(n - Win) & Mask]; l.Hq[n & Mask] = hq;
                    l.Sl += lq - l.Lq[(n - Win) & Mask]; l.Lq[n & Mask] = lq;
                    float eh = (float)Math.Sqrt(Math.Max(0.0, l.Sh) / Win);
                    float el = (float)Math.Sqrt(Math.Max(0.0, l.Sl) / Win);
                    bool click = l.Warm == 0 && eh > Rise * l.RefH && eh > Floor && el < LowGuard * Math.Max(l.RefL, 1e-6f);
                    l.RefH += (eh - l.RefH) * (eh > l.RefH ? m_up : m_down);
                    l.RefL += (el - l.RefL) * (el > l.RefL ? m_up : m_down);
                    if (l.Warm > 0) l.Warm--;
                    if (click) { l.Held = Hold; Triggers++; }
                    float target = l.Held > 0 ? 0f : 1f;
                    if (l.Held > 0) l.Held--;
                    l.Gain += (target - l.Gain) * (target < l.Gain ? .25f : .01f);
                    data[i] = l.Lo[(n - Ahead) & Mask] + l.Hi[(n - Ahead) & Mask] * l.Gain;
                    Seen++; if (l.Gain < .5f) Closed++;
                    l.N = n + 1;
                }
            }
        }

        private static void Reset(Lane l)
        {
            Array.Clear(l.X, 0, Ring); Array.Clear(l.B, 0, Ring); Array.Clear(l.Hq, 0, Ring);
            Array.Clear(l.Lq, 0, Ring); Array.Clear(l.Lo, 0, Ring); Array.Clear(l.Hi, 0, Ring);
            l.S1 = l.S2 = l.Sh = l.Sl = 0; l.RefH = l.RefL = 0f; l.Gain = 1f; l.Held = 0; l.Warm = Settle;
        }
    }
}
