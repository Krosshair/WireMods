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
            AudioSource src = ___m_audioSource;
            if (!Plugin.Enabled.Value || src == null || src.bypassReverbZones || src.spatialBlend < .5f) return;
            AudioListener listener = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (listener == null) return;

            // Reverb send: vanilla raises it with distance only; in an enclosed space every sound
            // reaches the walls.
            if (Plugin.RoomReverb.Value)
                src.reverbZoneMix = Mathf.Max(src.reverbZoneMix, Mathf.Lerp(src.reverbZoneMix, 1f, Room.Enclosure));

            AcousticFilter f = __instance.GetComponent<AcousticFilter>();
            if (!Plugin.Occlusion.Value)
            {
                if (f != null) f.Set(1f, 22000f, false);
                return;
            }
            Vector3 from = listener.transform.position, to = __instance.transform.position;
            Vector3 d = to - from;
            float dist = d.magnitude;
            int layers = 0;
            if (dist > SourceClearance + .1f)
            {
                int n = Physics.RaycastNonAlloc(from, d / dist, Hits, dist - SourceClearance, Room.Mask, QueryTriggerInteraction.Ignore);
                layers = Mathf.Min(n, Gain.Length - 1);
            }
            s_checked++;
            if (layers > 0) s_muffled++;
            if (layers == 0 && f == null) return;
            if (f == null) f = __instance.gameObject.AddComponent<AcousticFilter>();
            float s = Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f);
            float gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, Gain[layers], s), .05f, 1f);
            float cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), Mathf.Log(Cutoff[layers]), s)), 200f, 22000f);
            // A sound starting now jumps straight to its value; a playing loop glides to it.
            f.Set(gain, cutoff, !src.isPlaying);
            if (layers > 0 && Plugin.RoomReverb.Value)
                src.reverbZoneMix = Mathf.Max(src.reverbZoneMix, .4f + .2f * layers);
            if (Plugin.DebugLog.Value && layers > 0)
                Plugin.Log.LogInfo("[acoustics] " + __instance.name + " " + dist.ToString("F1") + " m away behind " + layers + " layer(s): gain " +
                    gain.ToString("F2") + ", low-pass " + cutoff.ToString("F0") + " Hz" + (src.loop ? " (loop)" : ""));
        }

        internal static string Summary()
        {
            string s = "sounds checked " + s_checked + ", muffled " + s_muffled + "; " + AcousticFilter.Format();
            s_checked = s_muffled = 0;
            return s;
        }
    }

    // Gain and a one-pole low-pass on the sound's own audio (Unity's filter callback, audio thread).
    // Parameters glide per buffer so a loop never clicks. Fully open: passes the audio untouched.
    public sealed class AcousticFilter : MonoBehaviour
    {
        private static int s_rate = 48000;
        private static volatile int s_channels, s_length;
        private volatile float m_targetGain = 1f, m_targetCutoff = 22000f;
        private volatile bool m_snap = true;
        private float m_gain = 1f, m_cutoff = 22000f;
        private readonly float[] m_state = new float[8];

        private void Awake() => s_rate = AudioSettings.outputSampleRate;

        internal void Set(float gain, float cutoff, bool snap)
        {
            m_targetGain = gain;
            m_targetCutoff = cutoff;
            if (snap) m_snap = true;
        }

        internal static string Format() => s_channels > 0 ? "filter sees " + s_channels + " channel(s), " + s_length + " samples per buffer at " + s_rate + " Hz" : "filter not run yet";

        private void OnAudioFilterRead(float[] data, int channels)
        {
            s_channels = channels;
            s_length = data.Length;
            float tg = m_targetGain, tc = m_targetCutoff;
            if (m_snap)
            {
                m_snap = false;
                m_gain = tg; m_cutoff = tc;
                for (int c = 0; c < m_state.Length; c++) m_state[c] = 0f;
            }
            if (channels <= 0 || channels > m_state.Length) return;
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
    }
}
