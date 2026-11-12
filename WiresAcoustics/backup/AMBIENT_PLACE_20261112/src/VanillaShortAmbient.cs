using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Valheim gives EVERY unforced ambient one-shot a full one-second fade and
    // hard Stop, based on nominal clip.length rather than playback time/pitch.
    // For very short animal/water one-shots this fades almost the entire sound
    // and can stop a pitched-down clip before its last sample. Let the source
    // finish naturally instead; this is NOT an attack/declic​k DSP effect.
    // The hook runs only when vanilla creates an ambient instance, never per frame.
    [HarmonyPatch(typeof(TimedDestruction), nameof(TimedDestruction.Trigger), new System.Type[0])]
    internal static class VanillaShortAmbient
    {
        private static int s_reports;

        private static void Prefix(TimedDestruction __instance)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || __instance == null) return;
            AudioMan man = AudioMan.instance;
            if (man == null || __instance.transform.parent != man.transform) return;

            ZSFX sfx = __instance.GetComponent<ZSFX>();
            AudioSource src = __instance.GetComponent<AudioSource>();
            if (sfx == null || src == null || src.loop || src.clip == null ||
                !sfx.m_fadeOutOnAwake || src.clip.length > 2f) return;

            // Explicit biome fades and other timed effects keep their authored
            // lifetime. Match only AudioMan.UpdateRandomAmbient's default path.
            if (Mathf.Abs(sfx.m_fadeOutDuration - 1f) > 0.001f ||
                Mathf.Abs(sfx.m_fadeOutDelay - (src.clip.length - 1f)) > 0.002f ||
                Mathf.Abs(__instance.m_timeout - src.clip.length * 1.5f) > 0.01f)
                return;

            // A natural non-looping AudioSource stops at its final decoded
            // sample. No envelope or forced AudioSource.Stop is necessary.
            sfx.m_fadeOutOnAwake = false;
            float pitch = Mathf.Max(.1f, Mathf.Abs(src.pitch));
            float expectedTime = src.clip.length / pitch;
            // Retain the object long enough even for pitch variation, mixer
            // scheduling, and frame timing. A dead source costs no DSP work.
            __instance.m_timeout = Mathf.Max(__instance.m_timeout, expectedTime + 2f);

            if (Plugin.DebugLog.Value && s_reports++ < 24)
                Plugin.Log.LogInfo("[acoustics] vanilla short ambient natural completion: " +
                    src.clip.name + " clip=" + src.clip.length.ToString("F2") +
                    "s pitch=" + pitch.ToString("F2") +
                    " expected=" + expectedTime.ToString("F2") + "s");
        }
    }
}
