using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sound takes time to travel, 343 m/s, both ways: a hit 60 m away is heard about 0.17 s after it
    // happens, and a loop stopping out there is heard stopping that much later too.
    //  - starting: every placed sound is held back by its distance (Unity's PlayDelayed: the clip
    //    starts late, nothing is buffered, no cost while waiting); under 1 ms isn't;
    //  - stopping: the game's stop and fade-out of a placed sound are held back the same way (run
    //    from Room's tick when due). A sound whose object is destroyed outright still stops at once.
    // Capped at 0.5 s (170 m). Game cues start and stop at once.
    internal static class Travel
    {
        internal const float Speed = 343f, Most = .5f;
        private struct Pending { internal ZSFX Sfx; internal float At; internal bool Fade; }
        private static readonly List<Pending> Stops = new List<Pending>(16);
        private static bool s_running; // running a held-back stop: let it through
        private static int s_delayed;

        private static float DelayOf(ZSFX z, AudioSource src)
        {
            if (!Plugin.Enabled.Value || !Plugin.RealDistance.Value || src == null || src.spatialBlend < .5f) return 0f;
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            if (f != null && f.Cue) return 0f;
            AudioListener l = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (l == null) return 0f;
            float dist = (z.transform.position - l.transform.position).magnitude;
            return dist < .35f ? 0f : Mathf.Min(dist / Speed, Most);
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class Start
        {
            private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
            {
                AudioSource src = ___m_audioSource;
                if (src == null || !src.isPlaying) return;
                float d = DelayOf(__instance, src);
                if (d <= 0f) return;
                src.Stop();
                src.PlayDelayed(d);
                s_delayed++;
            }
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Stop))]
        private static class Stop
        {
            private static bool Prefix(ZSFX __instance, AudioSource ___m_audioSource) => Hold(__instance, ___m_audioSource, false);
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.FadeOut))]
        private static class Fade
        {
            private static bool Prefix(ZSFX __instance, AudioSource ___m_audioSource) => Hold(__instance, ___m_audioSource, true);
        }

        // false: held back (runs later)
        private static bool Hold(ZSFX z, AudioSource src, bool fade)
        {
            if (s_running || src == null || !src.isPlaying) return true;
            float d = DelayOf(z, src);
            if (d < .01f) return true;
            Stops.Add(new Pending { Sfx = z, At = Time.time + d, Fade = fade });
            return false;
        }

        // From Room's tick: held-back stops that are due.
        internal static void Step(float now)
        {
            for (int i = Stops.Count - 1; i >= 0; i--)
            {
                Pending p = Stops[i];
                if (p.Sfx != null && now < p.At) continue;
                Stops.RemoveAt(i);
                if (p.Sfx == null) continue;
                s_running = true;
                try { if (p.Fade) p.Sfx.FadeOut(); else p.Sfx.Stop(); }
                finally { s_running = false; }
            }
        }

        internal static void Clear()
        {
            s_running = true;
            try { foreach (Pending p in Stops) if (p.Sfx != null) { if (p.Fade) p.Sfx.FadeOut(); else p.Sfx.Stop(); } }
            finally { s_running = false; }
            Stops.Clear();
        }

        internal static string Summary()
        {
            string s = "delayed by distance " + s_delayed + " (stops waiting " + Stops.Count + ")";
            s_delayed = 0;
            return s;
        }
    }
}
