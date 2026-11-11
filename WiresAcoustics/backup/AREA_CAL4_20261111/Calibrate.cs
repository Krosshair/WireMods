using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // One-off measurement of Unity's reverb (Debug > Measure the reverb): how loud its output is
    // against what's sent into it, which no documentation gives and the room's reverb level needs.
    // A test noise plays 1 m in front of you with everything else muted, first dry (reverb zones
    // bypassed), then into the reverb zone set to known values: late reverb only at three decay
    // times, then early reflections only. Each step settles, then the game's actual output is
    // measured (AudioListener.GetOutputData) for a second. The reverb's own energy is the total
    // minus the dry. Results go to the log as energy ratios to the dry sound.
    internal sealed class Noise : MonoBehaviour
    {
        private uint m_state = 22222u;
        private void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                m_state ^= m_state << 13; m_state ^= m_state >> 17; m_state ^= m_state << 5;
                float v = ((m_state & 0xFFFFFF) / 16777216f * 2f - 1f) * .25f;
                for (int c = 0; c < channels; c++) data[i + c] = v;
            }
        }
    }

    internal static class Calibrate
    {
        private static readonly float[] Buf = new float[1024];
        private static readonly List<AudioSource> Muted = new List<AudioSource>();
        private static AudioSource s_src;
        private static int s_step = -1;
        private static float s_until, s_settleEnd;
        private static double s_energy;
        private static int s_samples;
        private static double s_dry;
        // 0: the zone on but silent (Room -10000), the baseline: the same path as the others, no reverb
        // 6-8: the source's reverb mix at 0.5, 0.25 and 1.1, to see how it scales the send
        // 9-11: late +1000 and -1000 mB, and Room -1000 mB, to see the zone's levels are millibels
        private static readonly string[] Names = { "zone silent", "late, decay 0.5 s", "late, decay 1 s", "late, decay 2 s", "early only", "reverb zones bypassed",
            "late, mix 0.5", "late, mix 0.25", "late, mix 1.1", "late +1000", "late -1000", "late, room -1000" };
        private static readonly int[] Late = { -10000, 0, 0, 0, -10000, -10000, 0, 0, 0, 1000, -1000, 0 };
        private static readonly float[] Mix = { 1f, 1f, 1f, 1f, 1f, 1f, .5f, .25f, 1.1f, 1f, 1f, 1f };
        private static readonly double[] Results = new double[12];
        private static readonly string[] ReadBack = new string[12];
        internal static bool Running => s_step >= 0;

        internal static void Step(AudioReverbZone zone, Transform listener)
        {
            if (s_step < 0)
            {
                if (!Plugin.MeasureReverb.Value || zone == null || listener == null) return;
                Start(listener);
                Begin(0, zone);
                return;
            }
            if (s_src == null || zone == null) { Finish("stopped: the test sound or the reverb zone went away"); return; }
            s_src.transform.position = listener.position + listener.forward;
            s_src.reverbZoneMix = Mix[s_step]; // nothing else may move it mid-step
            float now = Time.unscaledTime;
            if (now >= s_settleEnd)
            {
                for (int ch = 0; ch < 2; ch++)
                {
                    AudioListener.GetOutputData(Buf, ch);
                    for (int i = 0; i < Buf.Length; i++) s_energy += Buf[i] * Buf[i];
                    s_samples += Buf.Length;
                }
            }
            if (now < s_until) return;
            Results[s_step] = s_samples > 0 ? s_energy / s_samples : 0.0;
            if (s_step == 0) s_dry = Results[0];
            if (s_step + 1 < Names.Length) Begin(s_step + 1, zone);
            else Finish(null);
        }

        private static void Start(Transform listener)
        {
            Muted.Clear();
            foreach (AudioSource a in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (a != null && !a.mute) { a.mute = true; Muted.Add(a); }
            var go = new GameObject("WiresAcousticsCalibration");
            s_src = go.AddComponent<AudioSource>();
            go.AddComponent<Noise>(); // no clip: the filter is the sound (Unity: OnAudioFilterRead)
            s_src.loop = true;
            s_src.spatialBlend = 1f;
            s_src.rolloffMode = AudioRolloffMode.Linear;
            s_src.minDistance = 5f; s_src.maxDistance = 10f; // full level at 1 m
            s_src.dopplerLevel = 0f;
            s_src.reverbZoneMix = 1f;
            s_src.transform.position = listener.position + listener.forward;
            s_src.Play();
            Plugin.Log.LogInfo("[calibrate] measuring Unity's reverb: everything else muted for about 45 seconds");
        }

        private static void Begin(int step, AudioReverbZone z)
        {
            z.reverbPreset = AudioReverbPreset.User;
            s_step = step;
            s_energy = 0.0; s_samples = 0;
            z.room = step == 0 ? -10000 : step == 11 ? -1000 : 0; z.roomHF = 0; z.roomLF = 0;
            z.decayHFRatio = 1f;
            z.reflectionsDelay = .02f; z.reverbDelay = .04f;
            z.diffusion = 100f; z.density = 100f;
            float decay = step == 1 ? .5f : step == 3 ? 2f : 1f;
            z.decayTime = decay;
            z.reflections = step == 4 ? 0 : -10000;
            z.reverb = Late[step];
            s_src.bypassReverbZones = step == 5;
            s_src.reverbZoneMix = Mix[step];
            // what the zone says it has now, to see the values took
            ReadBack[step] = "room " + z.room + " refl " + z.reflections + " late " + z.reverb + " decay " + z.decayTime.ToString("F2") + " mix " + s_src.reverbZoneMix.ToString("F2") + " enabled " + z.enabled + " preset " + z.reverbPreset;
            float now = Time.unscaledTime;
            s_settleEnd = now + decay * 2f + .5f;
            s_until = s_settleEnd + 1.5f;
        }

        private static void Finish(string error)
        {
            foreach (AudioSource a in Muted) if (a != null) a.mute = false;
            Muted.Clear();
            if (s_src != null) Object.Destroy(s_src.gameObject);
            s_src = null;
            if (error != null) Plugin.Log.LogWarning("[calibrate] " + error);
            else
            {
                var sb = new System.Text.StringBuilder("[calibrate] Unity reverb, energy added against the silent-zone baseline (1 m, mix 1):");
                for (int i = 1; i < Names.Length; i++)
                    sb.Append(" " + Names[i] + " " + (s_dry > 0 ? (Results[i] - s_dry) / s_dry : 0).ToString("F4") + " (" +
                        (s_dry > 0 && Results[i] > s_dry ? (10.0 * System.Math.Log10((Results[i] - s_dry) / s_dry)).ToString("F1") : "-inf") + " dB);");
                sb.Append(" baseline level " + (s_dry > 0 ? (10.0 * System.Math.Log10(s_dry)).ToString("F1") : "-inf") + " dBFS");
                Plugin.Log.LogInfo(sb.ToString());
                for (int i = 0; i < Names.Length; i++) Plugin.Log.LogInfo("[calibrate] " + Names[i] + ": " + ReadBack[i] + ", level " + (Results[i] > 0 ? (10.0 * System.Math.Log10(Results[i])).ToString("F2") : "-inf") + " dBFS");
            }
            s_step = -1;
            Plugin.MeasureReverb.Value = false;
            Room.ReapplyZone(); // the room's own values back on the zone
        }
    }
}
