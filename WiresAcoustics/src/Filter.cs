using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Per sound, on its own audio (Unity's filter callback, audio thread): occlusion (gain and a
    // one-pole low-pass) and our HRTF. Gain and low-pass glide (about 0.15 s) so nothing jumps or
    // clicks; only a sound that's just starting takes its values at once.
    //
    // The callback only runs while the component is enabled, and that's Budget's call: the sounds that
    // matter most (nearest, playing) are processed, the rest are left to Unity (its own panning, no
    // callback at all). Every callback is timed, so Budget can see how busy the audio thread is.
    //
    // Distance fade: Unity's logarithmic rolloff doesn't reach zero at the source's max distance, it
    // stops there, so a sound walking out of range drops from quiet to nothing. Those sources reach
    // twice as far instead, getting quieter with distance the same way, and fade out smoothly over
    // that extra range (distance from Directions, every frame): as loud as the game made them
    // within their own range, a gentle tail past it.
    //
    // Air: the further a sound travels, the more of its highs the air swallows (a fight 60 m away is
    // duller, not just quieter). A low-pass from 22 kHz near you, about 15 kHz at 30 m, 9 kHz at
    // 60 m, 4.5 kHz at 100 m, never below 2.5 kHz (distance from Directions, every frame).
    //
    // Echo (Bounces): a delayed, duller copy of the sound from the direction of a big surface facing
    // you, panned by constant power. Its delay line comes from a pool when an echo is around.
    //
    // HRTF, modelled (no measured data): Unity's own panning is flattened (spread 180: both ears the
    // same), then per ear:
    //  - interaural time difference, Woodworth's formula: (a / c)(theta + sin theta), head radius
    //    8.75 cm, up to ~0.66 ms; a fractional delay on the far ear;
    //  - head shadow: the far ear loses its highs (low-pass 20 kHz down to 1.8 kHz fully to the side)
    //    and some level, the near ear gains a little;
    //  - behind you the outer ear shades the highs (above ~3.5 kHz) on both ears;
    //  - elevation: the outer ear's notch moving from ~5 kHz (below) to ~10 kHz (above), deeper the
    //    further a sound is above or below ear level (none within 12 degrees, -9 dB from 44 degrees),
    //    narrow (Q 4). A real ear's front response is offset by its own boosts; modelling only the
    //    dip made everything in front sound dull.
    // The direction comes from the main thread every frame (Room's AudioMan.Update hook).
    public sealed class AcousticFilter : MonoBehaviour
    {
        internal static readonly List<AcousticFilter> Live = new List<AcousticFilter>(256);
        private static int s_rate = 48000;
        private static volatile float s_globalGain = 1f, s_globalCutoff = 22000f;
        internal static volatile bool GlobalActive;

        // Applied to every filtered sound on top of its own values (under water).
        internal static void SetGlobal(float gain, float cutoff, bool active)
        {
            s_globalGain = gain;
            s_globalCutoff = cutoff;
            GlobalActive = active;
        }

        // Audio-thread time spent in these callbacks (Stopwatch ticks), read and reset by Budget.
        private static long s_ticks;
        private static int s_calls;
        internal static long TakeTicks(out int calls)
        {
            calls = System.Threading.Interlocked.Exchange(ref s_calls, 0);
            return System.Threading.Interlocked.Exchange(ref s_ticks, 0L);
        }

        private static volatile int s_channels, s_length;
        private volatile float m_targetGain = 1f, m_targetCutoff = 22000f;
        private volatile bool m_snap = true;
        private volatile bool m_hrtf;
        internal bool Hrtf
        {
            get => m_hrtf;
            set
            {
                if (m_hrtf == value) return;
                m_hrtf = value;
                Directions.Want(this);
                Pan();
            }
        }
        private volatile float m_dx, m_dy, m_dz = 1f;
        internal float BaseSpread = -1f;
        internal AudioSource Source;
        internal bool Voice;       // a voice chat stream: muffled from Room's tick
        internal bool Routed;      // heard through an opening or round a corner: the HRTF places it there
        internal Vector3 RoutePoint;
        internal enum Kinds : byte { Sfx, Voice, Ocean, Other, Menu, Ambient }
        internal Kinds Kind;
        internal float StartedAt, LastOcc, LastBlockDist;
        internal float LastDist = 1e6f;            // from the listener (Directions, Occlusion), for Budget
        internal bool InBudget;                    // in Budget's list of playing sounds
        // How wide the game made the source (its own spread, before ours): 0 a point, 1 all around.
        // Wide sources (a waterfall, a shield dome) get less of the HRTF's direction.
        internal volatile float Width;
        internal Transform Player;                 // a player's own sound, made placed (PlayerSounds)
        internal bool Cue;                         // a game cue, not a sound in the world (a skill level-up): left unplaced
        internal bool Fades;                       // logarithmic rolloff: reaches twice as far, faded out there
        internal float MaxDistance;                // the game's own
        internal volatile float DistanceGain = 1f;
        internal volatile float AirCutoff = 22000f;
        private bool m_placed;
        internal bool Placed                       // a placed sound: gets the air (Directions)
        {
            get => m_placed;
            set { if (m_placed != value) { m_placed = value; Directions.Want(this); } }
        }

        // Always processed, whatever Budget's count: voices, the sea, the ambience loops, the menu.
        internal bool AlwaysOn => Voice || Kind == Kinds.Ocean || Kind == Kinds.Ambient || Kind == Kinds.Menu;

        internal static float AirAt(float dist) =>
            Mathf.Max(2500f, 22000f * Mathf.Exp(-Mathf.Max(0f, dist - 5f) / 60f));

        internal float FadeAt(float dist)
        {
            if (!Fades || MaxDistance <= 0f) return 1f;
            float t = Mathf.Clamp01((dist - MaxDistance) / (.94f * MaxDistance)); // silent just before the new cut
            return 1f - t * t * (3f - 2f * t);
        }

        // Takes over the source's panning for the HRTF, remembering how wide it was.
        internal void TakePanning(float ownSpread)
        {
            if (Source == null) return;
            if (BaseSpread < 0f)
            {
                BaseSpread = ownSpread;
                Width = Mathf.Clamp01((ownSpread - 30f) / 150f);
            }
            Pan();
        }

        // Who handles the source: while this filter runs, our HRTF pans it (spread 180) and the longer
        // range with its fade applies; while it doesn't, Unity's own panning and range are back.
        private void Pan()
        {
            if (Source == null) return;
            if (Fades)
            {
                float range = enabled ? MaxDistance * 2f : MaxDistance;
                if (Source.maxDistance != range) Source.maxDistance = range;
            }
            if (BaseSpread < 0f) return;
            float want = m_hrtf && enabled ? 180f : BaseSpread;
            if (Source.spread != want) Source.spread = want;
        }

        private float m_gain = 1f, m_cutoff = 22000f;
        private readonly float[] m_state = new float[8];

        private const int Ring = 256;
        private const float HeadRadius = .0875f, Sound = 343f;
        private readonly float[] m_ring = new float[Ring];
        private int m_pos;
        private bool m_primed;
        private float m_mono, m_side, m_delayL, m_delayR, m_headL, m_headR, m_backL, m_backR, m_gainL = 1f, m_gainR = 1f;
        private float m_x1L, m_x2L, m_y1L, m_y2L, m_x1R, m_x2R, m_y1R, m_y2R;

        // Directions' list: where this filter is in it, and whether it should be.
        internal int Slot = -1;
        internal bool Queued;
        private bool m_alive;
        private int m_liveIndex = -1;
        internal bool Tracked => m_alive && (m_hrtf || Fades || m_placed);

        // Registered for its whole life (Awake to OnDestroy), processing or not: enabling and
        // disabling is only about the audio callback.
        private void Awake()
        {
            s_rate = AudioSettings.outputSampleRate;
            Source = GetComponent<AudioSource>();
            // Added from a prefab (not serialized): a plain source without the game's sound-effect
            // component is one of the other placed sounds.
            if (GetComponent<ZSFX>() == null) Kind = Kinds.Other;
            if (Source != null && Source.rolloffMode == AudioRolloffMode.Logarithmic && Source.spatialBlend >= .5f)
            {
                Fades = true;
                MaxDistance = Source.maxDistance;
                Pan();
            }
            m_alive = true;
            m_liveIndex = Live.Count;
            Live.Add(this);
            Directions.Want(this);
        }

        private void OnDestroy()
        {
            m_alive = false;
            if (m_liveIndex >= 0 && m_liveIndex < Live.Count && ReferenceEquals(Live[m_liveIndex], this))
            {
                // swap-remove: sounds come and go many times a second
                int last = Live.Count - 1;
                AcousticFilter moved = Live[last];
                Live[m_liveIndex] = moved;
                moved.m_liveIndex = m_liveIndex;
                Live.RemoveAt(last);
            }
            else Live.Remove(this);
            m_liveIndex = -1;
            Directions.Want(this);
            ReleaseEcho();
        }

        private void OnEnable()
        {
            m_primed = false;
            Pan();
        }

        private void OnDisable()
        {
            Pan();
            ReleaseEcho();
        }

        // Echo, shared by every sound (it's where you are that echoes): level, delay, pan, dullness.
        // Delay lines are pooled and never freed (only the processed sounds hold one).
        private const int EchoRing = 16384;
        private static readonly Stack<float[]> EchoPool = new Stack<float[]>();
        private static volatile float s_echoGain, s_echoDelay, s_echoL = .707f, s_echoR = .707f, s_echoCut = 4000f;
        private volatile float[] m_echo;
        private int m_echoPos;
        private float m_echoLevel, m_echoLp, m_echoAt;

        // Main thread, every frame: direction relative to your head (x right, z forward).
        internal static void SetEcho(float gain, float delay, Vector3 local)
        {
            s_echoGain = gain;
            s_echoDelay = delay;
            float x = Mathf.Clamp(local.x, -1f, 1f);
            s_echoL = Mathf.Sqrt((1f - x) * .5f);
            s_echoR = Mathf.Sqrt((1f + x) * .5f);
            s_echoCut = Mathf.Lerp(4000f, 2200f, Mathf.Clamp01(-local.z)); // from behind: the outer ear shades it
        }

        // Main thread: a delay line for this sound's echo while it's processed and an echo is around.
        internal void EnsureEcho()
        {
            if (m_echo != null || Cue || !enabled) return;
            float[] e = EchoPool.Count > 0 ? EchoPool.Pop() : new float[EchoRing];
            System.Array.Clear(e, 0, e.Length); // main thread, once per sound
            m_echoPos = 0;
            m_echoLevel = 0f;
            m_echoLp = 0f;
            m_echoAt = 0f;
            m_echo = e;
        }

        private void ReleaseEcho()
        {
            float[] e = m_echo;
            if (e == null) return;
            m_echo = null;
            if (EchoPool.Count < 128) EchoPool.Push(e);
        }

        // No echo around: the delay lines go back to the pool (Budget, a few times a second).
        internal static void DropEchoes()
        {
            if (s_echoGain >= 1e-4f) return;
            foreach (AcousticFilter f in Live) if (f != null && f.m_echo != null && f.m_echoLevel < 1e-4f) f.ReleaseEcho();
        }

        internal void Set(float gain, float cutoff, bool snap)
        {
            m_targetGain = gain;
            m_targetCutoff = cutoff;
            if (snap) m_snap = true;
        }

        internal void SetDirection(Vector3 d) { m_dx = d.x; m_dy = d.y; m_dz = d.z; }

        // HRTF switched off: every live sound gets Unity's own panning back.
        internal static void ReleaseHrtf()
        {
            foreach (AcousticFilter f in Live)
            {
                if (f == null) continue;
                f.Hrtf = false;
                if (f.BaseSpread >= 0f && f.Source != null) f.Source.spread = f.BaseSpread;
                f.BaseSpread = -1f;
            }
        }

        // The mod switched off: every live sound back to untouched, and no callbacks.
        internal static void ReleaseAll()
        {
            ReleaseHrtf();
            foreach (AcousticFilter f in Live)
            {
                if (f == null) continue;
                f.Set(1f, 22000f, false);
                f.DistanceGain = 1f;
                f.AirCutoff = 22000f;
                f.Placed = false;
                if (f.Fades && f.Source != null) { f.Source.maxDistance = f.MaxDistance; f.Fades = false; Directions.Want(f); }
                if (f.enabled) f.enabled = false;
            }
            Budget.Clear();
        }

        internal static string Format()
        {
            string r = s_channels > 0 ? "filter sees " + s_channels + " channel(s), " + s_length + " samples per buffer at " + s_rate + " Hz" : "filter not run yet";
            return r + ", " + Live.Count + " sounds known";
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Process(data, channels);
            Echo(data, channels);
            System.Threading.Interlocked.Add(ref s_ticks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            System.Threading.Interlocked.Increment(ref s_calls);
        }

        private void Echo(float[] data, int ch)
        {
            float[] e = m_echo;
            float g = s_echoGain;
            if (e == null || ch < 1) return;
            float g0 = m_echoLevel;
            if (g < 1e-4f && g0 < 1e-4f) return;
            int frames = data.Length / ch;
            if (frames == 0) return;
            int mask = e.Length - 1;
            float target = Mathf.Clamp(s_echoDelay * s_rate, 1f, e.Length - 2);
            float d0 = m_echoAt > 0f ? m_echoAt : target;
            float d1 = Mathf.MoveTowards(d0, target, frames * .02f); // slow, so a moving echo doesn't warble
            m_echoAt = d1;
            m_echoLevel = g;
            float gl = s_echoL, gr = s_echoR;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * s_echoCut / s_rate);
            int pos = m_echoPos;
            float lp = m_echoLp, inv = 1f / frames, dd = d1 - d0, dg = g - g0;
            for (int i = 0, k = 0; i < frames; i++, k += ch)
            {
                float t = i * inv;
                e[pos] = ch >= 2 ? (data[k] + data[k + 1]) * .5f : data[k];
                float p = pos - (d0 + dd * t);
                int i0 = (int)p;
                if (i0 > p) i0--; // floor
                float fr = p - i0;
                float y = e[i0 & mask] * (1f - fr) + e[(i0 + 1) & mask] * fr;
                pos = (pos + 1) & mask;
                lp += a * (y - lp);
                float v = lp * (g0 + dg * t);
                if (ch >= 2) { data[k] += v * gl; data[k + 1] += v * gr; }
                else data[k] += v;
            }
            m_echoPos = pos;
            m_echoLp = lp;
        }

        private void Process(float[] data, int channels)
        {
            s_channels = channels;
            s_length = data.Length;
            float tg = m_targetGain * s_globalGain * DistanceGain;
            float tc = m_targetCutoff;
            if (s_globalCutoff < tc) tc = s_globalCutoff;
            if (AirCutoff < tc) tc = AirCutoff;
            if (m_snap)
            {
                m_snap = false;
                m_gain = tg; m_cutoff = tc;
                for (int c = 0; c < m_state.Length; c++) m_state[c] = 0f;
                m_primed = false;
            }
            if (channels <= 0 || channels > m_state.Length) return;
            if (m_hrtf && channels >= 2) { Spatialize(data, channels, tg, tc); return; }
            m_primed = false;
            if (m_gain >= .999f && tg >= .999f && m_cutoff >= 20000f && tc >= 20000f)
            {
                // Untouched; the filter state follows the signal so closing it later doesn't click.
                for (int c = 0; c < channels && data.Length >= channels; c++) m_state[c] = data[data.Length - channels + c];
                return;
            }
            int frames = data.Length / channels;
            float glide = Glide(frames);
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : m_gain + (tg - m_gain) * glide;
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), glide));
            m_gain = g1;
            float a = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);
            float[] st = m_state;
            float inv = 1f / frames, dg = g1 - g0;
            for (int i = 0, k = 0; i < frames; i++)
            {
                float g = g0 + dg * i * inv;
                for (int c = 0; c < channels; c++, k++)
                {
                    float y = st[c] += a * (data[k] - st[c]);
                    data[k] = y * g;
                }
            }
        }

        private void Spatialize(float[] data, int ch, float tg, float tc)
        {
            int frames = data.Length / ch;
            if (frames == 0) return;

            // occlusion
            float glide = Glide(frames);
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : m_gain + (tg - m_gain) * glide;
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), glide));
            m_gain = g1;
            float aOcc = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);

            // direction
            float x = Mathf.Clamp(m_dx, -1f, 1f), y = Mathf.Clamp(m_dy, -1f, 1f), z = Mathf.Clamp(m_dz, -1f, 1f);
            float kw = 1f - Width; // a wide source gets less direction
            float lat = Mathf.Abs(x), th = Mathf.Asin(lat);
            float itd = HeadRadius / Sound * (th + Mathf.Sin(th)) * s_rate * kw; // samples
            float tL = x > 0f ? itd : 0f, tR = x < 0f ? itd : 0f;          // the far ear hears it later
            float farA = Mathf.Lerp(1f, 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Exp(Mathf.Lerp(Mathf.Log(20000f), Mathf.Log(1800f), lat)) / s_rate), kw);
            float aL = x > 0f ? farA : 1f, aR = x < 0f ? farA : 1f;
            float near = 1f + .12f * lat * kw, far = 1f - .3f * lat * kw;
            float tgL = x > 0f ? far : near, tgR = x < 0f ? far : near;
            float shade = .45f * Mathf.Clamp01(-z) * kw;
            float aBack = 1f - Mathf.Exp(-2f * Mathf.PI * 3500f / s_rate);
            // RBJ peaking EQ for the outer-ear notch: narrow (Q 4, like a real pinna notch) and only
            // well above or below (a wide one dulled everything just below the camera)
            float f0 = Mathf.Clamp(7500f + 2500f * y, 3000f, s_rate * .45f);
            float notch = 9f * kw * Mathf.Clamp01((Mathf.Abs(y) - .2f) / .5f); // dB
            float A = Mathf.Pow(10f, -notch / 40f), w = 2f * Mathf.PI * f0 / s_rate, cw = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * 4f);
            float a0 = 1f + al / A;
            float b0 = (1f + al * A) / a0, b1 = -2f * cw / a0, b2 = (1f - al * A) / a0, a1 = -2f * cw / a0, a2 = (1f - al / A) / a0;

            if (!m_primed)
            {
                m_primed = true;
                m_delayL = tL; m_delayR = tR; m_gainL = tgL; m_gainR = tgR;
            }
            float dL0 = m_delayL, dR0 = m_delayR, gL0 = m_gainL, gR0 = m_gainR;
            m_delayL = tL; m_delayR = tR; m_gainL = tgL; m_gainR = tgR;

            // the loop works on locals (the audio thread's hot path: no field access, no calls)
            float[] ring = m_ring;
            int pos = m_pos;
            float mono = m_mono, sideS = m_side, headL = m_headL, headR = m_headR, backL = m_backL, backR = m_backR;
            float x1L = m_x1L, x2L = m_x2L, y1L = m_y1L, y2L = m_y2L, x1R = m_x1R, x2R = m_x2R, y1R = m_y1R, y2R = m_y2R;
            float invCh = 1f / ch, invF = 1f / frames;
            float dg = g1 - g0, ddL = tL - dL0, ddR = tR - dR0, dgL = tgL - gL0, dgR = tgR - gR0;
            const int mask = Ring - 1;
            for (int i = 0; i < frames; i++)
            {
                float t = i * invF;
                int k = i * ch;
                float m = 0f;
                for (int c = 0; c < ch; c++) m += data[k + c];
                m *= invCh;
                float g = g0 + dg * t;
                // Stereo clips keep their width: the difference between their channels rides on top,
                // muffled like the rest (mono clips have none).
                float side = (data[k] - data[k + 1]) * .5f;
                sideS += aOcc * (side - sideS);
                float sw = sideS * g;
                mono += aOcc * (m - mono);
                ring[pos] = mono * g;

                float p = pos - (dL0 + ddL * t);
                int i0 = (int)p; if (i0 > p) i0--;
                float fr = p - i0;
                float vL = ring[i0 & mask] * (1f - fr) + ring[(i0 + 1) & mask] * fr;
                p = pos - (dR0 + ddR * t);
                i0 = (int)p; if (i0 > p) i0--;
                fr = p - i0;
                float vR = ring[i0 & mask] * (1f - fr) + ring[(i0 + 1) & mask] * fr;
                pos = (pos + 1) & mask;

                headL += aL * (vL - headL); vL = headL;
                headR += aR * (vR - headR); vR = headR;
                backL += aBack * (vL - backL); vL -= shade * (vL - backL);
                backR += aBack * (vR - backR); vR -= shade * (vR - backR);
                float oL = b0 * vL + b1 * x1L + b2 * x2L - a1 * y1L - a2 * y2L;
                x2L = x1L; x1L = vL; y2L = y1L; y1L = oL;
                float oR = b0 * vR + b1 * x1R + b2 * x2R - a1 * y1R - a2 * y2R;
                x2R = x1R; x1R = vR; y2R = y1R; y1R = oR;
                data[k] = oL * (gL0 + dgL * t) + sw;
                data[k + 1] = oR * (gR0 + dgR * t) - sw;
                for (int c = 2; c < ch; c++) data[k + c] = 0f;
            }
            m_pos = pos;
            m_mono = mono; m_side = sideS; m_headL = headL; m_headR = headR; m_backL = backL; m_backR = backR;
            m_x1L = x1L; m_x2L = x2L; m_y1L = y1L; m_y2L = y2L; m_x1R = x1R; m_x2R = x2R; m_y1R = y1R; m_y2R = y2R;
        }

        // How far toward the target one buffer moves: a 0.15 s time constant whatever the buffer size.
        private static float Glide(int frames) => 1f - Mathf.Exp(-frames / (.15f * s_rate));
    }
}
