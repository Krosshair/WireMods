using System;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Valheim times random ambient termination from AudioClip.length, but that
    // is the length at pitch 1.0. The actual source is randomized in ZSFX.Play
    // before AudioMan configures the fade and TimedDestruction.Trigger runs.
    // At pitch < 1 the game can stop an ambient sound before its last samples.
    // This hook runs only for spawned random ambience at its timed destruction
    // scheduling point, preserving the game's intended 1-second end fade.
    [HarmonyPatch(typeof(TimedDestruction), nameof(TimedDestruction.Trigger), new Type[0])]
    internal static class AmbientTiming
    {
        private static void Prefix(TimedDestruction __instance)
        {
            if (!Plugin.Enabled.Value || __instance == null) return;
            AudioMan man = AudioMan.instance;
            if (man == null || __instance.transform.parent != man.transform) return;
            ZSFX sfx = __instance.GetComponent<ZSFX>();
            AudioSource src = __instance.GetComponent<AudioSource>();
            if (sfx == null || src == null || src.clip == null || src.loop ||
                !sfx.m_fadeOutOnAwake) return;

            // Only the game's automatic "fade over the last second" path.
            // Explicit forced-duration biome sounds must retain their design.
            float originalDelay = src.clip.length - 1f;
            if (Mathf.Abs(sfx.m_fadeOutDuration - 1f) > .001f ||
                Mathf.Abs(sfx.m_fadeOutDelay - originalDelay) > .002f) return;

            float pitch = Mathf.Abs(src.pitch);
            if (pitch < .01f) return;
            float actualDuration = src.clip.length / pitch;
            if (Mathf.Abs(actualDuration - src.clip.length) < .001f) return;

            // ZSFX uses wall-clock seconds for its fade timers. Match its
            // completion to the pitch-adjusted playback duration.
            sfx.m_fadeOutDuration = Mathf.Min(1f, actualDuration);
            sfx.m_fadeOutDelay = Mathf.Max(0f, actualDuration - sfx.m_fadeOutDuration);
            __instance.m_timeout = Mathf.Max(__instance.m_timeout, actualDuration + .2f);

            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("[acoustics] ambient clip timing corrected " +
                    src.clip.name + " pitch " + pitch.ToString("F2") +
                    " length " + src.clip.length.ToString("F2") +
                    " -> " + actualDuration.ToString("F2") + "s");
        }
    }
}
