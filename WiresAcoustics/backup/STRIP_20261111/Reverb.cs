using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // The room's sound, made here instead of by Unity's reverb zone (one preset-style effect for every
    // sound alike). Every placed sound sends a mono copy of itself into a shared bus (its filter, at
    // the cost of one add per sample); one renderer turns the bus into:
    //  - early reflections: one per wall, floor or ceiling the room rays found (rays grouped into
    //    planes), as an image source behind it: 2 d further away, from its direction (delay between
    //    the ears and the far ear quieter, as the HRTF does), weaker by what the surface absorbs and
    //    by the longer way (1 / (2 d));
    //  - the late reverb: a feedback delay network (8 lines, Householder mixing) decaying in the
    //    reverb time Sabine's formula gives per band, RT = 0.161 V / (S a + 4 m V), from what the rays
    //    hit (absorption at 250 Hz and 4 kHz per material) and the air's own absorption (ISO 9613-1).
    //
    // Levels are physical. The bus carries each sound as its level 1 m from it (its signal times its
    // volume times its full-volume distance, times what's in the way). A reflection off a wall d away
    // is that over 2 d, and the reverberant field is that over the critical distance (Room.Critical),
    // where the direct sound and the reverb are equally loud.
    //
    // Unity runs the sounds' filters and this renderer in some order within each audio buffer. The
    // bus has two halves and the renderer always reads the one the sounds filled since its last run:
    // if the sounds run first that's this buffer, if not the one before. Which it is, is measured
    // (how long before the renderer the last send came) and taken off the reflections' delays.
    internal static class Bus
    {
        internal const int Cap = 16384;
        private static readonly float[][] Slots = { new float[Cap], new float[Cap] };
        private static readonly int[] SlotFrames = new int[2];
        private static volatile int s_tick;
        private static long s_lastSend; // Stopwatch ticks of the last send
        private static int s_writers, s_overlaps, s_sends;
        internal static volatile bool On;

        // From a sound's filter (audio thread): its raw signal times k.
        internal static void Add(float[] data, int channels, float k)
        {
            if (!On || k <= 0f || channels < 1) return;
            int frames = data.Length / channels;
            if (frames > Cap) frames = Cap;
            if (System.Threading.Interlocked.Increment(ref s_writers) > 1) System.Threading.Interlocked.Increment(ref s_overlaps);
            int t = s_tick & 1;
            float[] slot = Slots[t];
            float scale = k / channels;
            for (int i = 0, n = 0; i < frames; i++)
            {
                float m = 0f;
                for (int c = 0; c < channels; c++) m += data[n++];
                slot[i] += m * scale;
            }
            if (frames > SlotFrames[t]) SlotFrames[t] = frames;
            s_lastSend = System.Diagnostics.Stopwatch.GetTimestamp();
            s_sends++;
            System.Threading.Interlocked.Decrement(ref s_writers);
        }

        // From the renderer (audio thread): this buffer's input, then cleared for the next.
        internal static int Take(float[] into, int frames, out long sinceLastSend)
        {
            int t = s_tick & 1;
            float[] slot = Slots[t];
            int n = Mathf.Min(frames, Cap);
            System.Array.Copy(slot, into, n);
            System.Array.Clear(slot, 0, Mathf.Max(n, SlotFrames[t]));
            SlotFrames[t] = 0;
            sinceLastSend = System.Diagnostics.Stopwatch.GetTimestamp() - s_lastSend;
            s_tick++;
            return n;
        }

        internal static string Summary()
        {
            int o = s_overlaps, s = s_sends;
            s_overlaps = 0; s_sends = 0;
            return "bus " + s + " sends" + (o > 0 ? ", " + o + " overlapping (more than one audio thread)" : "") + ", " + Reverb.Summary();
        }
    }

    internal sealed class ReverbRender : MonoBehaviour
    {
        private void OnAudioFilterRead(float[] data, int channels) => Reverb.Render(data, channels);
    }

    internal static class Reverb
    {
        private const int Lines = 8, MaxTaps = 8, Ring = 16384;
        // delay line lengths in samples at 48 kHz (21 to 51 ms, mutually prime), scaled to the rate
        private static readonly int[] Base = { 1009, 1123, 1277, 1399, 1567, 1741, 2003, 2459 };
        private static float[][] s_lines;
        private static int[] s_len, s_pos;
        private static float[] s_lp;
        private static float[] s_in = new float[Bus.Cap], s_ring = new float[Ring];
        private static int s_ringPos;
        private static int s_rate;
        private static volatile int s_mainRate = 48000;

        // Room state from the main thread, swapped as a whole.
        internal sealed class State
        {
            internal int Taps;
            internal readonly float[] Delay = new float[MaxTaps], Gain = new float[MaxTaps];
            internal readonly Vector3[] World = new Vector3[MaxTaps];
            internal float RtLow = .3f, RtHigh = .3f, Critical = 5f;
        }
        private static volatile State s_state = new State();
        // per tap, the ears (main thread every frame, from the listener's turn)
        private static readonly float[] s_tL = new float[MaxTaps], s_tR = new float[MaxTaps], s_gL = new float[MaxTaps], s_gR = new float[MaxTaps];
        private static volatile float[] s_ears = new float[MaxTaps * 4];
        // late reverb coefficients (main thread when the reverb time changes)
        private static volatile float[] s_coef = new float[Lines * 2 + 1];
        private static float s_coefRtL = -1f, s_coefRtH = -1f, s_coefRate = -1f;

        private static GameObject s_go;
        private static AudioSource s_src;
        private static long s_sameBuffer, s_lastBuffer, s_renders;
        private static float s_latency; // samples the bus lags behind (0 or one buffer)

        internal static void Ensure(AudioMan man, UnityEngine.Audio.AudioMixerGroup output)
        {
            if (s_go != null || man == null) return;
            s_go = new GameObject("WiresAcousticsReverb");
            s_go.transform.SetParent(man.transform, false);
            s_src = s_go.AddComponent<AudioSource>();
            s_src.playOnAwake = false;
            s_src.loop = true;
            s_src.spatialBlend = 0f;
            s_src.bypassReverbZones = true;
            s_src.priority = 0;
            s_src.outputAudioMixerGroup = output;
            s_go.AddComponent<ReverbRender>();
            s_src.Play(); // no clip: the filter is the sound (Unity: OnAudioFilterRead)
            Plugin.Log.LogInfo("[acoustics] own reverb on" + (output != null ? " (to " + output.name + ")" : ""));
        }

        internal static void Shutdown()
        {
            Bus.On = false;
            if (s_go != null) Object.Destroy(s_go);
            s_go = null; s_src = null;
        }

        // Main thread, every room measurement.
        internal static void SetRoom(State st) => s_state = st;

        // Main thread, every frame: the taps' direction relative to your head, as delays and gains.
        internal static void Ears(Transform listener, float strength)
        {
            State st = s_state;
            int rate = s_mainRate = AudioSettings.outputSampleRate;
            var e = new float[MaxTaps * 4];
            for (int i = 0; i < st.Taps; i++)
            {
                Vector3 local = listener.InverseTransformDirection(st.World[i]);
                float x = Mathf.Clamp(local.x, -1f, 1f), lat = Mathf.Abs(x), th = Mathf.Asin(lat);
                float itd = .0875f / 343f * (th + Mathf.Sin(th)) * rate * strength; // Woodworth, as the HRTF
                float near = 1f + .2f * lat * strength, far = Mathf.Max(.1f, 1f - .5f * lat * strength);
                e[i * 4] = x > 0f ? itd : 0f;
                e[i * 4 + 1] = x < 0f ? itd : 0f;
                e[i * 4 + 2] = x > 0f ? far : near;
                e[i * 4 + 3] = x < 0f ? far : near;
            }
            s_ears = e;
            int r = rate;
            if (Mathf.Abs(st.RtLow - s_coefRtL) > .01f || Mathf.Abs(st.RtHigh - s_coefRtH) > .01f || r != s_coefRate)
            {
                s_coefRtL = st.RtLow; s_coefRtH = st.RtHigh; s_coefRate = r;
                s_coef = Coefficients(st.RtLow, st.RtHigh, r);
            }
        }

        // Per line: the loop gain at the low end (g) and the one-pole low-pass (b) that makes it the
        // high band's gain at 4 kHz (Jot's frequency-dependent decay), and the output scale that keeps
        // the reverb's steady level at its input's.
        internal static float[] Coefficients(float rtLow, float rtHigh, int rate)
        {
            var c = new float[Lines * 2 + 1];
            double w = 2.0 * System.Math.PI * 4000.0 / rate, energy = 0.0;
            for (int i = 0; i < Lines; i++)
            {
                int len = Length(i, rate);
                double g = System.Math.Pow(10.0, -3.0 * len / (rate * System.Math.Max(.05, rtLow)));
                double gh = System.Math.Pow(10.0, -3.0 * len / (rate * System.Math.Max(.05, rtHigh)));
                double r = System.Math.Min(.9999, gh / g), b = 0.0;
                if (r < .9999)
                {
                    double r2 = r * r, p = 1.0 - r2 * System.Math.Cos(w);
                    b = (p - System.Math.Sqrt(System.Math.Max(0.0, p * p - (1.0 - r2) * (1.0 - r2)))) / (1.0 - r2);
                }
                c[i * 2] = (float)g;
                c[i * 2 + 1] = (float)b;
                if ((i & 1) == 0) energy += 1.0 / (1.0 - g * g);
            }
            c[Lines * 2] = (float)(1.0 / System.Math.Sqrt(System.Math.Max(1.0, energy)));
            return c;
        }

        private static int Length(int i, int rate) => Mathf.Max(64, Base[i] * rate / 48000);

        // Audio thread.
        internal static void Render(float[] data, int channels)
        {
            System.Array.Clear(data, 0, data.Length);
            int frames = data.Length / channels;
            if (frames > Bus.Cap) frames = Bus.Cap;
            int rate = s_mainRate;
            if (s_lines == null || rate != s_rate)
            {
                s_rate = rate;
                s_lines = new float[Lines][];
                s_len = new int[Lines]; s_pos = new int[Lines]; s_lp = new float[Lines];
                for (int i = 0; i < Lines; i++) { s_len[i] = Length(i, rate); s_lines[i] = new float[s_len[i]]; }
            }
            int got = Bus.Take(s_in, frames, out long since);
            if (!Bus.On || channels < 1) return;
            // which buffer the sends came from: within half a buffer of now is this one
            double buffer = (double)frames / rate * System.Diagnostics.Stopwatch.Frequency;
            if (since < buffer * .5) s_sameBuffer++; else s_lastBuffer++;
            s_renders++;
            s_latency = s_lastBuffer > s_sameBuffer ? frames : 0f;

            State st = s_state;
            float[] ears = s_ears, coef = s_coef;
            if (coef.Length < Lines * 2 + 1) return;
            float lateIn = 1f / Mathf.Max(.25f, st.Critical), outScale = coef[Lines * 2];
            int taps = st.Taps;
            for (int n = 0; n < frames; n++)
            {
                float x = n < got ? s_in[n] : 0f;
                s_ring[s_ringPos] = x;
                float l = 0f, r = 0f;
                // early reflections
                for (int k = 0; k < taps; k++)
                {
                    float d = st.Delay[k] - s_latency;
                    if (d < 1f) d = 1f;
                    float g = st.Gain[k];
                    int iL = (int)(d + ears[k * 4]), iR = (int)(d + ears[k * 4 + 1]);
                    l += s_ring[(s_ringPos - iL) & (Ring - 1)] * g * ears[k * 4 + 2];
                    r += s_ring[(s_ringPos - iR) & (Ring - 1)] * g * ears[k * 4 + 3];
                }
                s_ringPos = (s_ringPos + 1) & (Ring - 1);
                // late reverb: read every line, mix (Householder: y = v - 2/N sum v), feed back damped
                float sum = 0f;
                for (int i = 0; i < Lines; i++) sum += s_lines[i][s_pos[i]];
                float h = 2f / Lines * sum, inj = x * lateIn;
                for (int i = 0; i < Lines; i++)
                {
                    float v = s_lines[i][s_pos[i]];
                    if ((i & 1) == 0) l += v * outScale; else r += v * outScale;
                    float y = v - h;
                    float g = coef[i * 2], b = coef[i * 2 + 1];
                    s_lp[i] = g * (1f - b) * y + b * s_lp[i];
                    s_lines[i][s_pos[i]] = s_lp[i] + ((i & 2) == 0 ? inj : -inj);
                    if (++s_pos[i] >= s_len[i]) s_pos[i] = 0;
                }
                int o = n * channels;
                if (channels >= 2) { data[o] = l; data[o + 1] = r; }
                else data[o] = (l + r) * .5f;
            }
        }

        internal static string Summary()
        {
            State st = s_state;
            long same = s_sameBuffer, last = s_lastBuffer;
            return "own reverb RT " + st.RtLow.ToString("F2") + " s low / " + st.RtHigh.ToString("F2") + " s high, " + st.Taps + " reflections, critical distance " +
                st.Critical.ToString("F1") + " m, sends arrive " + (same + last == 0 ? "?" : same >= last ? "in the same buffer" : "a buffer late") +
                " (" + same + "/" + last + ")";
        }
    }
}
