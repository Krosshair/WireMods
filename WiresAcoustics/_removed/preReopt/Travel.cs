using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sound takes time to arrive: 343 m/s, so a hit 60 m away is heard about 0.17 s after it
    // happens, your own footsteps a few milliseconds after your foot lands (the camera is the
    // listener). Every one-off placed sound is held back by that long (Unity's PlayDelayed: the clip
    // starts late, nothing is buffered, no cost while waiting). Loops and game cues start at once.
    // Capped at 0.5 s.
    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
    internal static class Travel
    {
        internal const float Speed = 343f, Most = .5f;
        private static int s_delayed;

        private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
        {
            AudioSource src = ___m_audioSource;
            if (!Plugin.Enabled.Value || src == null || !src.isPlaying || src.loop || src.spatialBlend < .5f) return;
            AcousticFilter f = __instance.GetComponent<AcousticFilter>();
            if (f != null && f.Cue) return;
            AudioListener l = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (l == null) return;
            float dist = (__instance.transform.position - l.transform.position).magnitude;
            if (dist < .35f || dist > src.maxDistance) return; // under 1 ms: nothing to hold back
            src.Stop();
            src.PlayDelayed(Mathf.Min(dist / Speed, Most));
            s_delayed++;
        }

        internal static string Summary()
        {
            string s = "delayed by distance " + s_delayed;
            s_delayed = 0;
            return s;
        }
    }
}
