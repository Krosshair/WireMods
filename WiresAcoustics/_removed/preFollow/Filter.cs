using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Per sound, on its own audio (Unity's filter callback, audio thread): occlusion (gain and a
    // one-pole low-pass) and our HRTF. Gain and low-pass glide (about 0.15 s) so nothing jumps or
    // clicks; only a sound that's just starting takes its values at once.
    //
    // Distance fade: Unity's logarithmic rolloff doesn't reach zero at the source's max distance, it
    // stops there, so a sound walking out of range drops from quiet to nothing. Those sources fade
    // out over the last 40% of their range instead (distance from Directions, every frame).
    //
    // HRTF, modelled (no measured data): Unity's own panning is flattened (spread 180: both ears the
    // same), then per ear:
    //  - interaural time difference, Woodworth's formula: (a / c)(theta + sin theta), head radius
    //    8.75 cm, up to ~0.66 ms; a fractional delay on the far ear;
    //  - head shadow: the far ear loses its highs (low-pass 20 kHz down to 1.8 kHz fully to the side)
    //    and some level, the near ear gains a little;
    //  - behind you the outer ear shades the highs (above ~3.5 kHz) on both ears;
    //  - elevation: the outer ear's notch, -9 dB, moving from ~5 kHz (below) to ~10 kHz (above).
    // The direction comes from the main thread every frame (Room's AudioMan.Update hook).
    public sealed class AcousticFilter : MonoBehaviour
    {
        internal static readonly List<AcousticFilter> Live = new List<AcousticFilter>(64);
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
        private static volatile int s_channels, s_length;
        private static float s_imbalance;
        private volatile float m_targetGain = 1f, m_targetCutoff = 22000f;
        private volatile bool m_snap = true;
        private volatile bool m_hrtf;
        internal bool Hrtf
        {
            get => m_hrtf;
            set { if (m_hrtf != value) { m_hrtf = value; Directions.Dirty = true; } }
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
        // How wide the game made the source (its own spread, before ours): 0 a point, 1 all around.
        // Wide sources (a waterfall, a shield dome) get less of the HRTF's direction.
        internal volatile float Width;
        internal bool Fades;                       // logarithmic rolloff: faded before its cut-off
        internal float MaxDistance;
        internal volatile float DistanceGain = 1f;

        internal float FadeAt(float dist)
        {
            if (!Fades || MaxDistance <= 0f) return 1f;
            float t = Mathf.Clamp01((dist - .6f * MaxDistance) / (.37f * MaxDistance)); // silent just before the cut
            return 1f - t * t * (3f - 2f * t);
        }

        // Takes over the source's panning for the HRTF (once), remembering how wide it was.
        internal void TakePanning(float ownSpread)
        {
            if (Source == null) return;
            if (BaseSpread < 0f)
            {
                BaseSpread = ownSpread;
                Width = Mathf.Clamp01((ownSpread - 30f) / 150f);
            }
            Source.spread = 180f;
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
            }
        }
        private void OnEnable() { Live.Add(this); Directions.Dirty = true; }
        private void OnDisable() { Live.Remove(this); Directions.Dirty = true; }

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

        // The mod switched off: every live sound back to untouched (filters stay, passing audio through).
        internal static void ReleaseAll()
        {
            ReleaseHrtf();
            foreach (AcousticFilter f in Live) if (f != null) { f.Set(1f, 22000f, false); f.DistanceGain = 1f; }
        }

        internal static string Format()
        {
            string r = s_channels > 0 ? "filter sees " + s_channels + " channel(s), " + s_length + " samples per buffer at " + s_rate + " Hz, Unity's own left/right imbalance " +
                s_imbalance.ToString("P0") + " (should be ~0 with HRTF on)" : "filter not run yet";
            s_imbalance = 0f;
            return r + ", " + Live.Count + " sounds filtered";
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            s_channels = channels;
            s_length = data.Length;
            float tg = m_targetGain * s_globalGain * DistanceGain, tc = Mathf.Min(m_targetCutoff, s_globalCutoff);
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
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : Mathf.Lerp(m_gain, tg, glide);
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), glide));
            m_gain = g1;
            float a = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);
            for (int i = 0, k = 0; i < frames; i++)
            {
                float g = g0 + (g1 - g0) * i / frames;
                for (int c = 0; c < channels; c++, k++)
                {
                    float y = m_state[c] += a * (data[k] - m_state[c]);
                    data[k] = y * g;
                }
            }
        }

        private void Spatialize(float[] data, int ch, float tg, float tc)
        {
            int frames = data.Length / ch;
            if (frames == 0) return;
            float eL = 0f, eR = 0f;
            for (int i = 0; i < frames; i++) { eL += Mathf.Abs(data[i * ch]); eR += Mathf.Abs(data[i * ch + 1]); }
            if (eL + eR > 1e-3f) { float im = Mathf.Abs(eL - eR) / (eL + eR); if (im > s_imbalance) s_imbalance = im; }

            // occlusion
            float glide = Glide(frames);
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : Mathf.Lerp(m_gain, tg, glide);
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
            // RBJ peaking EQ for the outer-ear notch
            float f0 = Mathf.Clamp(7500f + 2500f * y, 3000f, s_rate * .45f);
            float A = Mathf.Pow(10f, -9f * kw / 40f), w = 2f * Mathf.PI * f0 / s_rate, cw = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * 1.5f);
            float a0 = 1f + al / A;
            float b0 = (1f + al * A) / a0, b1 = -2f * cw / a0, b2 = (1f - al * A) / a0, a1 = -2f * cw / a0, a2 = (1f - al / A) / a0;

            if (!m_primed)
            {
                m_primed = true;
                m_delayL = tL; m_delayR = tR; m_gainL = tgL; m_gainR = tgR;
            }
            float dL0 = m_delayL, dR0 = m_delayR, gL0 = m_gainL, gR0 = m_gainR;
            m_delayL = tL; m_delayR = tR; m_gainL = tgL; m_gainR = tgR;
            float inv = 1f / ch;
            for (int i = 0; i < frames; i++)
            {
                float t = (float)i / frames;
                int k = i * ch;
                float m = 0f;
                for (int c = 0; c < ch; c++) m += data[k + c];
                m *= inv;
                float g = g0 + (g1 - g0) * t;
                // Stereo clips keep their width: the difference between their channels rides on top,
                // muffled like the rest (mono clips have none).
                float side = (data[k] - data[k + 1]) * .5f;
                m_side += aOcc * (side - m_side);
                float sw = m_side * g;
                m_mono += aOcc * (m - m_mono);
                m_ring[m_pos] = m_mono * g;
                float vL = Tap(dL0 + (tL - dL0) * t), vR = Tap(dR0 + (tR - dR0) * t);
                m_pos = (m_pos + 1) & (Ring - 1);
                m_headL += aL * (vL - m_headL); vL = m_headL;
                m_headR += aR * (vR - m_headR); vR = m_headR;
                m_backL += aBack * (vL - m_backL); vL -= shade * (vL - m_backL);
                m_backR += aBack * (vR - m_backR); vR -= shade * (vR - m_backR);
                float oL = b0 * vL + b1 * m_x1L + b2 * m_x2L - a1 * m_y1L - a2 * m_y2L;
                m_x2L = m_x1L; m_x1L = vL; m_y2L = m_y1L; m_y1L = oL;
                float oR = b0 * vR + b1 * m_x1R + b2 * m_x2R - a1 * m_y1R - a2 * m_y2R;
                m_x2R = m_x1R; m_x1R = vR; m_y2R = m_y1R; m_y1R = oR;
                data[k] = oL * (gL0 + (tgL - gL0) * t) + sw;
                data[k + 1] = oR * (gR0 + (tgR - gR0) * t) - sw;
                for (int c = 2; c < ch; c++) data[k + c] = 0f;
            }
        }

        // How far toward the target one buffer moves: a 0.15 s time constant whatever the buffer size.
        private static float Glide(int frames) => 1f - Mathf.Exp(-frames / (.15f * s_rate));

        // The ring at "now minus d samples", linearly interpolated.
        private float Tap(float d)
        {
            float p = m_pos - d;
            int i0 = Mathf.FloorToInt(p);
            float f = p - i0;
            return m_ring[i0 & (Ring - 1)] * (1f - f) + m_ring[(i0 + 1) & (Ring - 1)] * f;
        }
    }
}
