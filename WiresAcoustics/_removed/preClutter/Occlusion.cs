using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sounds behind walls, buildings or hills. Checked when a sound starts and once a second for
    // looping ones (the game's own reverb update, ZSFX.UpdateReverb): one ray from the listener to the
    // sound, every solid thing in between is a layer. Each layer muffles (low-pass) and quietens it,
    // and it gets more of the room's reverb (what you hear of it comes round the walls). Also sets
    // each sound's reverb send from the measured space instead of distance alone.
    [HarmonyPatch(typeof(ZSFX), "UpdateReverb")]
    internal static class Occlusion
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private static readonly float[] Gain = { 1f, .6f, .4f, .28f };
        private static readonly float[] Cutoff = { 22000f, 1500f, 800f, 500f };
        private const float SourceClearance = .5f; // the thing making the sound (a hearth, a door) doesn't hide it
        private static int s_checked, s_muffled;

        private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
        {
            long t0 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            Run(__instance, ___m_audioSource);
            if (t0 != 0L) Cost.Add(ref Cost.Sound, t0);
        }

        private static void Run(ZSFX __instance, AudioSource ___m_audioSource)
        {
            AudioSource src = ___m_audioSource;
            if (!Plugin.Enabled.Value || src == null) return;
            if (src.spatialBlend < .5f)
            {
                // Positionless ambience (birds, biome loops): muffled by how closed in you are.
                if (Ambience.IsAmbient(src)) Ambience.Apply(__instance.gameObject, !src.isPlaying);
                return;
            }
            AudioListener listener = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (listener == null) return;

            // Reverb send: vanilla raises it with distance only; in an enclosed space every sound
            // reaches the walls.
            bool reverb = Plugin.RoomReverb.Value && !src.bypassReverbZones;
            if (reverb)
                src.reverbZoneMix = Mathf.Max(src.reverbZoneMix, Mathf.Lerp(src.reverbZoneMix, 1f, Room.Enclosure));

            bool hrtf = Plugin.Hrtf.Value;
            AcousticFilter f = __instance.GetComponent<AcousticFilter>();
            Vector3 from = listener.transform.position, to = __instance.transform.position;
            Vector3 d = to - from;
            float dist = d.magnitude;
            int layers = 0;
            if (Plugin.Occlusion.Value && dist > SourceClearance + .1f)
            {
                int n = Physics.RaycastNonAlloc(from, d / dist, Hits, dist - SourceClearance, Room.Mask, QueryTriggerInteraction.Ignore);
                layers = Mathf.Min(n, Gain.Length - 1);
            }
            s_checked++;
            if (layers > 0) s_muffled++;
            bool under = Underwater.Muffles(to);
            if (layers == 0 && !under && f == null && !hrtf && !AcousticFilter.GlobalActive) return;
            if (f == null) f = __instance.gameObject.AddComponent<AcousticFilter>();
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            float gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, Gain[layers], s), .05f, 1f);
            float cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), Mathf.Log(Cutoff[layers]), s)), 200f, 22000f);
            if (under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); } // the sound is under water
            // A sound starting now jumps straight to its value; a playing loop glides to it.
            f.Set(gain, cutoff, !src.isPlaying);
            // Direction is ours (AcousticFilter): Unity's panning is flattened to "both ears equally".
            if (hrtf)
            {
                if (f.BaseSpread < 0f) f.BaseSpread = src.spread;
                src.spread = 180f;
                f.SetDirection(listener.transform.InverseTransformDirection(dist > .01f ? d / dist : listener.transform.forward));
            }
            else if (f.BaseSpread >= 0f) { src.spread = f.BaseSpread; f.BaseSpread = -1f; }
            f.Hrtf = hrtf;
            if (layers > 0 && reverb)
                src.reverbZoneMix = Mathf.Max(src.reverbZoneMix, .4f + .2f * layers);
            if (Plugin.DebugLog.Value && layers > 0)
                Plugin.Log.LogInfo("[acoustics] " + __instance.name + " " + dist.ToString("F1") + " m away behind " + layers + " layer(s): gain " +
                    gain.ToString("F2") + ", low-pass " + cutoff.ToString("F0") + " Hz" + (src.loop ? " (loop)" : ""));
        }

        // Layers between the listener and a point, as gain and low-pass cutoff (strength applied).
        internal static int Layers(Vector3 from, Vector3 to, out float gain, out float cutoff)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            int layers = 0;
            if (Plugin.Occlusion.Value && dist > SourceClearance + .1f)
                layers = Mathf.Min(Physics.RaycastNonAlloc(from, d / dist, Hits, dist - SourceClearance, Room.Mask, QueryTriggerInteraction.Ignore), Gain.Length - 1);
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, Gain[layers], s), .05f, 1f);
            cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), Mathf.Log(Cutoff[layers]), s)), 200f, 22000f);
            return layers;
        }

        // Voice chat streams, from Room's tick (5 times a second).
        internal static void Voices(Vector3 listener)
        {
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || !f.Voice || f.Source == null) continue;
                if (f.Source.spatialBlend < .5f) { f.Set(1f, 22000f, false); continue; }
                Vector3 at = f.transform.position;
                Layers(listener, at, out float gain, out float cutoff);
                if (Underwater.Muffles(at)) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); } // the speaker is under water
                f.Set(gain, cutoff, false);
            }
        }

        internal static string Summary()
        {
            string s = "sounds checked " + s_checked + ", muffled " + s_muffled + "; " + AcousticFilter.Format();
            s_checked = s_muffled = 0;
            return s;
        }
    }

    // Per sound, on its own audio (Unity's filter callback, audio thread): occlusion (gain and a
    // one-pole low-pass) and our HRTF. Parameters glide per buffer so nothing clicks.
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
        internal volatile bool Hrtf;
        private volatile float m_dx, m_dy, m_dz = 1f;
        internal float BaseSpread = -1f;
        internal AudioSource Source;
        internal bool FollowBlend; // a sound that can switch between placed and positionless (voice chat)
        internal bool Voice;       // a voice chat stream: muffled from Room's tick
        private float m_gain = 1f, m_cutoff = 22000f;
        private readonly float[] m_state = new float[8];

        private const int Ring = 256;
        private const float HeadRadius = .0875f, Sound = 343f;
        private readonly float[] m_ring = new float[Ring];
        private int m_pos;
        private bool m_primed;
        private float m_mono, m_delayL, m_delayR, m_headL, m_headR, m_backL, m_backR, m_gainL = 1f, m_gainR = 1f;
        private float m_x1L, m_x2L, m_y1L, m_y2L, m_x1R, m_x2R, m_y1R, m_y2R;

        private void Awake()
        {
            s_rate = AudioSettings.outputSampleRate;
            Source = GetComponent<AudioSource>();
        }
        private void OnEnable() => Live.Add(this);
        private void OnDisable() => Live.Remove(this);

        internal void Set(float gain, float cutoff, bool snap)
        {
            m_targetGain = gain;
            m_targetCutoff = cutoff;
            if (snap) m_snap = true;
        }

        internal void SetDirection(Vector3 d) { m_dx = d.x; m_dy = d.y; m_dz = d.z; }

        // Every frame, from the game's audio update: where each sound is relative to your head.
        internal static void UpdateDirections(Transform listener)
        {
            Vector3 lp = listener.position;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                AcousticFilter f = Live[i];
                if (f == null) { Live.RemoveAt(i); continue; }
                if (f.FollowBlend && f.Source != null)
                {
                    bool on = Plugin.Hrtf.Value && f.Source.spatialBlend >= .5f;
                    if (on != f.Hrtf)
                    {
                        f.Hrtf = on;
                        if (on) { if (f.BaseSpread < 0f) f.BaseSpread = f.Source.spread; f.Source.spread = 180f; }
                        else if (f.BaseSpread >= 0f) f.Source.spread = f.BaseSpread;
                    }
                }
                if (!f.Hrtf) continue;
                Vector3 d = f.transform.position - lp;
                float m = d.magnitude;
                f.SetDirection(m > .01f ? listener.InverseTransformDirection(d / m) : Vector3.forward);
            }
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
            float tg = m_targetGain * s_globalGain, tc = Mathf.Min(m_targetCutoff, s_globalCutoff);
            if (m_snap)
            {
                m_snap = false;
                m_gain = tg; m_cutoff = tc;
                for (int c = 0; c < m_state.Length; c++) m_state[c] = 0f;
                m_primed = false;
            }
            if (channels <= 0 || channels > m_state.Length) return;
            if (Hrtf && channels >= 2) { Spatialize(data, channels, tg, tc); return; }
            m_primed = false;
            if (m_gain >= .999f && tg >= .999f && m_cutoff >= 20000f && tc >= 20000f)
            {
                // Untouched; the filter state follows the signal so closing it later doesn't click.
                for (int c = 0; c < channels && data.Length >= channels; c++) m_state[c] = data[data.Length - channels + c];
                return;
            }
            int frames = data.Length / channels;
            float g0 = m_gain, g1 = Mathf.Lerp(m_gain, tg, .5f);
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), .5f));
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
            float g0 = m_gain, g1 = Mathf.Lerp(m_gain, tg, .5f);
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), .5f));
            m_gain = g1;
            float aOcc = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);

            // direction
            float x = Mathf.Clamp(m_dx, -1f, 1f), y = Mathf.Clamp(m_dy, -1f, 1f), z = Mathf.Clamp(m_dz, -1f, 1f);
            float lat = Mathf.Abs(x), th = Mathf.Asin(lat);
            float itd = HeadRadius / Sound * (th + Mathf.Sin(th)) * s_rate; // samples
            float tL = x > 0f ? itd : 0f, tR = x < 0f ? itd : 0f;          // the far ear hears it later
            float farA = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Exp(Mathf.Lerp(Mathf.Log(20000f), Mathf.Log(1800f), lat)) / s_rate);
            float aL = x > 0f ? farA : 1f, aR = x < 0f ? farA : 1f;
            float near = 1f + .12f * lat, far = 1f - .3f * lat;
            float tgL = x > 0f ? far : near, tgR = x < 0f ? far : near;
            float shade = .45f * Mathf.Clamp01(-z);
            float aBack = 1f - Mathf.Exp(-2f * Mathf.PI * 3500f / s_rate);
            // RBJ peaking EQ for the outer-ear notch
            float f0 = Mathf.Clamp(7500f + 2500f * y, 3000f, s_rate * .45f);
            float A = Mathf.Pow(10f, -9f / 40f), w = 2f * Mathf.PI * f0 / s_rate, cw = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * 1.5f);
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
                m_mono += aOcc * (m - m_mono);
                m_ring[m_pos] = m_mono * (g0 + (g1 - g0) * t);
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
                data[k] = oL * (gL0 + (tgL - gL0) * t);
                data[k + 1] = oR * (gR0 + (tgR - gR0) * t);
                for (int c = 2; c < ch; c++) data[k + c] = 0f;
            }
        }

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
