using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Event-driven, debug-only trace of the game's exact sound choices and fades.
    // No PCM modifications, no timers, no per-frame diagnostics.
    internal static class AudioInvestigation
    {
        private static int s_footsteps, s_ambients, s_fades;
        private static readonly System.Collections.Generic.HashSet<string> s_stepNames =
            new System.Collections.Generic.HashSet<string>();
        private static AudioClip s_lastAmbientRequest;
        private static bool s_wasStopped;

        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.QueueAmbientLoop))]
        private static class Queue
        {
            private static void Prefix(AudioClip clip, float vol)
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value) return;
                if (s_lastAmbientRequest == clip && !s_wasStopped) return;
                s_lastAmbientRequest = clip;
                s_wasStopped = false;
                Plugin.Log.LogInfo("[audio investigation] AMBIENT REQUEST t=" + Time.time.ToString("F2") +
                    " target=" + (clip != null ? clip.name : "none") + " vol=" + vol.ToString("F2") +
                    " " + Snapshot(AudioMan.instance));
            }
        }

        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.StopAmbientLoop))]
        private static class StopLoop
        {
            private static void Prefix()
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value || s_wasStopped) return;
                s_wasStopped = true;
                s_lastAmbientRequest = null;
                Plugin.Log.LogInfo("[audio investigation] AMBIENT STOP REQUEST t=" + Time.time.ToString("F2") +
                    " " + Snapshot(AudioMan.instance));
            }
        }

        [HarmonyPatch(typeof(FootStep), "RPC_Step")]
        private static class Steps
        {
            private static void Postfix(FootStep __instance, int effectIndex)
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value ||
                    effectIndex < 0 || effectIndex >= __instance.m_effects.Count) return;
                FootStep.StepEffect step = __instance.m_effects[effectIndex];
                string identity = step.m_name + " [" + step.m_material + "/" + step.m_motionType + "]";
                if (s_footsteps++ > 150 || !s_stepNames.Add(identity)) return;
                foreach (GameObject prefab in step.m_effectPrefabs)
                {
                    if (prefab == null) continue;
                    ZSFX sfx = prefab.GetComponentInChildren<ZSFX>();
                    if (sfx == null) { Plugin.Log.LogInfo("[audio investigation] STEP " + identity + " prefab " + prefab.name + " no ZSFX"); continue; }
                    string clips = "";
                    foreach (AudioClip clip in sfx.m_audioClips)
                        if (clip != null) clips += clip.name + "(" + clip.length.ToString("F2") + "s) ";
                    Plugin.Log.LogInfo("[audio investigation] STEP " + identity + " prefab " + prefab.name +
                        " clips " + clips + " fadeIn " + sfx.m_fadeInDuration.ToString("F2") +
                        " loop " + (sfx.GetComponent<AudioSource>()?.loop ?? false));
                }
            }
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class RandomAmbientPlay
        {
            private static void Postfix(ZSFX __instance)
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value || s_ambients >= 100) return;
                AudioMan man = AudioMan.instance;
                if (man == null || __instance.transform.parent != man.transform) return;
                AudioSource src = __instance.GetComponent<AudioSource>();
                if (src == null || !src.isPlaying || src.clip == null) return;
                s_ambients++;
                string preset = "unknown";
                foreach (AudioMan.BiomeAmbients b in man.m_randomAmbients)
                    if (b.m_randomAmbientClips.Contains(src.clip) ||
                        b.m_randomAmbientClipsDay.Contains(src.clip) ||
                        b.m_randomAmbientClipsNight.Contains(src.clip))
                    { preset = b.m_name + "/" + b.m_biome + " forcedFade=" + b.m_forceFadeout.ToString("F2") + "s"; break; }
                Plugin.Log.LogInfo("[audio investigation] RANDOM AMBIENT START " +
                    src.clip.name + " preset=" + preset + " length " + src.clip.length.ToString("F2") +
                    "s pitch " + src.pitch.ToString("F2") + " object " + __instance.name);
            }
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Stop))]
        private static class RandomAmbientStop
        {
            private static void Prefix(ZSFX __instance)
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value) return;
                AudioMan man = AudioMan.instance;
                if (man == null || __instance.transform.parent != man.transform) return;
                AudioSource src = __instance.GetComponent<AudioSource>();
                if (src == null || src.clip == null) return;
                Plugin.Log.LogInfo("[audio investigation] RANDOM AMBIENT HARD STOP " + src.clip.name +
                    " clipLength=" + src.clip.length.ToString("F2") +
                    " playhead=" + src.time.ToString("F2") + " playing=" + src.isPlaying +
                    " caller=" + new System.Diagnostics.StackTrace(1, false));
            }
        }

        [HarmonyPatch(typeof(TimedDestruction), "DestroyNow")]
        private static class AmbientDestroyed
        {
            private static void Prefix(TimedDestruction __instance)
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value) return;
                AudioMan man = AudioMan.instance;
                if (man == null || __instance.transform.parent != man.transform) return;
                AudioSource src = __instance.GetComponent<AudioSource>();
                if (src == null || src.clip == null) return;
                Plugin.Log.LogInfo("[audio investigation] RANDOM AMBIENT DESTROY " + src.clip.name +
                    " length=" + src.clip.length.ToString("F2") + " playhead=" +
                    src.time.ToString("F2") + " playing=" + src.isPlaying +
                    " timeout=" + __instance.m_timeout.ToString("F2"));
            }
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.FadeOut))]
        private static class RandomAmbientFade
        {
            private static void Prefix(ZSFX __instance)
            {
                if (!Plugin.Enabled.Value || !Plugin.DebugLog.Value || s_fades >= 100) return;
                AudioMan man = AudioMan.instance;
                if (man == null || __instance.transform.parent != man.transform) return;
                AudioSource src = __instance.GetComponent<AudioSource>();
                if (src == null || src.clip == null) return;
                s_fades++;
                Plugin.Log.LogInfo("[audio investigation] RANDOM AMBIENT FADE " + src.clip.name +
                    " clip " + src.clip.length.ToString("F2") + "s, played " +
                    src.time.ToString("F2") + "s, fadeDuration " +
                    __instance.m_fadeOutDuration.ToString("F2") + "s");
            }
        }

        internal static string Snapshot(AudioMan man)
        {
            if (man == null) return "audio unavailable";
            AudioSource src = AccessTools.Field(typeof(AudioMan), "m_ambientLoopSource").GetValue(man) as AudioSource;
            AudioClip queued = AccessTools.Field(typeof(AudioMan), "m_queuedAmbientLoop").GetValue(man) as AudioClip;
            bool stop = (bool)AccessTools.Field(typeof(AudioMan), "m_stopAmbientLoop").GetValue(man);
            bool indoor = (bool)AccessTools.Field(typeof(AudioMan), "m_indoor").GetValue(man);
            float targetVol = (float)AccessTools.Field(typeof(AudioMan), "m_ambientVol").GetValue(man);
            string current = src != null && src.clip != null ? src.clip.name : "none";
            return "indoor=" + indoor + " ambient=" + current +
                " vol=" + (src != null ? src.volume.ToString("F2") : "-") +
                " targetVol=" + targetVol.ToString("F2") +
                " queued=" + (queued != null ? queued.name : "none") +
                " stop=" + stop + " fadeTime=" + man.m_ambientFadeTime.ToString("F2");
        }
    }
}
