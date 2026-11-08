using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Experimental, SFX-specific attack smoothing. Only attached to recognizable
    // footstep sources; no per-frame polling, allocations, or changes to other sounds.
    // Does not attempt to fix clicks embedded in the middle of a recording.
    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
    internal static class FootstepDeclickPatch
    {
        private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
        {
            if (!Plugin.Enabled.Value || !Plugin.FootstepDeclick.Value ||
                ___m_audioSource == null || ___m_audioSource.clip == null) return;
            string clip = ___m_audioSource.clip.name.ToLowerInvariant();
            string obj = __instance.gameObject.name.ToLowerInvariant();
            if (!IsFootstep(clip) && !IsFootstep(obj)) return;
            FootstepDeclick f = __instance.GetComponent<FootstepDeclick>();
            if (f == null) f = __instance.gameObject.AddComponent<FootstepDeclick>();
            f.Restart();
        }

        private static bool IsFootstep(string name) =>
            name.Contains("footstep") || name.Contains("foot_step") || name.Contains("foot step");
    }

    // DSP callback runs only when Unity is already rendering this source's audio.
    // 3 ms equal-power fade of attack from zero; intentionally no fade at the end,
    // as the same object may reuse the component and clips can have natural tails.
    public sealed class FootstepDeclick : MonoBehaviour
    {
        private volatile int m_restart;
        private int m_lastRestart;
        private int m_frame;
        private int m_length;

        internal void Restart()
        {
            m_length = Mathf.Max(1, Mathf.RoundToInt(AudioSettings.outputSampleRate * .003f));
            System.Threading.Interlocked.Increment(ref m_restart);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return;
            int restart = m_restart;
            if (restart != m_lastRestart)
            {
                m_lastRestart = restart;
                m_frame = 0;
            }
            if (m_frame >= m_length || m_length <= 0) return;
            int frames = data.Length / channels;
            for (int frame = 0, i = 0; frame < frames; frame++, i += channels)
            {
                float t = Mathf.Clamp01((float)m_frame / m_length);
                float gain = t * t * (3f - 2f * t);
                for (int c = 0; c < channels; c++) data[i + c] *= gain;
                if (m_frame < m_length) m_frame++;
            }
        }
    }
}
