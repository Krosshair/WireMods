using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // TEMPORARY (dev): records 30 s of the game's final output, 15 s after a world loads,
    // to BepInEx/WiresAcoustics_capture.wav, with every footstep played in that time
    // (clip, pitch, whether the cleaned copy was used, when) in WiresAcoustics_capture.txt.
    internal sealed class CaptureTest : MonoBehaviour
    {
        private const float Wait = 15f, Length = 60f;
        private static CaptureTest s_on;
        private static bool s_done;
        private float[] m_buf;
        private int m_pos, m_ch;
        private volatile bool m_rec, m_full;
        private double m_start;
        // when each mix callback arrived (a late one means the sound device ran dry: a gap you hear but the recording doesn't show)
        private readonly float[] m_gaps = new float[8192];
        private readonly int[] m_gapAt = new int[8192];
        private int m_calls;
        private long m_last;
        private static readonly List<string> Steps = new List<string>();

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class WorldLoaded
        {
            private static void Postfix()
            {
                if (s_done || s_on != null) return;
                AudioListener l = Object.FindObjectOfType<AudioListener>();
                if (l == null) return;
                s_on = l.gameObject.AddComponent<CaptureTest>();
                s_on.m_buf = new float[(int)(AudioSettings.outputSampleRate * Length) * 2];
                s_on.Invoke(nameof(Begin), Wait);
                s_on.Invoke(nameof(Finish), Wait + Length + 1f);
                Plugin.Log.LogInfo("[capture] recording starts in " + Wait + " s, for " + Length + " s");
            }
        }

        private void Begin() { m_start = AudioSettings.dspTime; m_rec = true; Plugin.Log.LogInfo("[capture] recording"); }

        // Called by Declick.Prepare for every footstep start.
        internal static void Step(AudioSource src, AudioClip original, bool copy)
        {
            if (s_on == null || !s_on.m_rec || s_on.m_full || original == null || !original.name.StartsWith("Player_Footstep_")) return;
            Steps.Add((AudioSettings.dspTime - s_on.m_start).ToString("F4") + "\t" + original.name + "\t" + (copy ? "repaired" : "NOT REPAIRED") +
                "\tpitch " + src.pitch.ToString("F3") + "\tvolume " + src.volume.ToString("F2") + "\tdist " +
                (Camera.main != null ? Vector3.Distance(Camera.main.transform.position, src.transform.position).ToString("F1") : "?"));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!m_rec || m_full) return;
            long tick = System.Diagnostics.Stopwatch.GetTimestamp();
            if (m_last != 0L && m_calls < m_gaps.Length)
            {
                m_gaps[m_calls] = (float)((tick - m_last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                m_gapAt[m_calls++] = m_pos;
            }
            m_last = tick;
            m_ch = channels;
            int n = Mathf.Min(data.Length / channels, m_buf.Length / 2 - m_pos);
            for (int i = 0; i < n; i++)
            {
                m_buf[(m_pos + i) * 2] = data[i * channels];
                m_buf[(m_pos + i) * 2 + 1] = channels > 1 ? data[i * channels + 1] : data[i * channels];
            }
            m_pos += n;
            if (m_pos >= m_buf.Length / 2) m_full = true;
        }

        private void Finish()
        {
            m_rec = false;
            s_done = true;
            string dir = BepInEx.Paths.BepInExRootPath;
            int rate = AudioSettings.outputSampleRate;
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, "WiresAcoustics_capture.wav"))))
            {
                int bytes = m_pos * 2 * 4;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)3); w.Write((short)2); w.Write(rate); w.Write(rate * 8); w.Write((short)8); w.Write((short)32);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                for (int i = 0; i < m_pos * 2; i++) w.Write(m_buf[i]);
            }
            File.WriteAllLines(Path.Combine(dir, "WiresAcoustics_capture.txt"), Steps);
            var lines = new List<string>();
            for (int i = 0; i < m_calls; i++) lines.Add((m_gapAt[i] / (float)rate).ToString("F4") + "\t" + m_gaps[i].ToString("F2"));
            File.WriteAllLines(Path.Combine(dir, "WiresAcoustics_capture_timing.txt"), lines);
            Plugin.Log.LogInfo("[capture] wrote " + (m_pos / (float)rate).ToString("F1") + " s (" + m_ch + " channels in) and " + Steps.Count + " footsteps");
            m_buf = null;
            Destroy(this);
        }
    }
}
