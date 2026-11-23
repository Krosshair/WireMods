using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Only the actual menu scene's spatial sound sources get menu HRTF.
    // The global scan also finds persistent AudioMan ambient/ocean sources
    // and old scene objects, whose acoustic ownership must not be overwritten.
    [HarmonyPatch(typeof(FejdStartup), "Start")]
    internal static class Menu
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (!Plugin.Enabled.Value || __instance == null) return;
            var scene = __instance.gameObject.scene;
            if (!scene.IsValid()) return;
            int placed = 0;
            foreach (AudioSource src in Object.FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (src == null) continue;
                // Diagnostic for the specific clip-less source observed on
                // scene entry, including its native play-on-awake setting.
                if (src.gameObject.name == "Amb_MainMenu")
                    Plugin.Log.LogInfo("[acoustics-menu] Amb_MainMenu: clip " +
                        (src.clip != null ? src.clip.name : "NULL") +
                        ", scene " + src.gameObject.scene.name +
                        ", menu scene " + scene.name +
                        ", spatial " + src.spatialBlend.ToString("F2") +
                        ", playOnAwake " + src.playOnAwake);
                if (src.gameObject.scene != scene ||
                    src.spatialBlend < .5f || src.clip == null) continue;
                AcousticFilter existing = src.GetComponent<AcousticFilter>();
                if (existing != null && (existing.Voice ||
                    existing.Kind == AcousticFilter.Kinds.Ocean ||
                    existing.Kind == AcousticFilter.Kinds.Ambient ||
                    existing.Kind == AcousticFilter.Kinds.Outside ||
                    existing.WorldRandomAmbient)) continue;
                AcousticFilter f = existing ?? AcousticFilter.For(src);
                f.Kind = AcousticFilter.Kinds.Menu;
                if (Plugin.Hrtf.Value)
                {
                    f.TakePanning(src.spread);
                    f.Hrtf = true;
                }
                placed++;
            }
            Plugin.Log.LogInfo("[acoustics] main menu: " + placed +
                " placed menu-scene sounds" +
                (Plugin.Hrtf.Value ? " on the HRTF" : ""));
        }
    }
}
