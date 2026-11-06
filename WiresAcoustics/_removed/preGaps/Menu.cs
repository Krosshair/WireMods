using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // The main menu's own scene sounds (the campfire and the rest) are plain sound sources, not the
    // game's sound effects, so they're picked up here when the menu starts: placed ones get the HRTF,
    // and the room measurement runs there like anywhere else. A first "it's on" while the menu plays.
    [HarmonyPatch(typeof(FejdStartup), "Start")]
    internal static class Menu
    {
        private static void Postfix()
        {
            if (!Plugin.Enabled.Value) return;
            int placed = 0;
            foreach (AudioSource src in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (src == null || src.spatialBlend < .5f || !src.gameObject.scene.IsValid()) continue;
                AcousticFilter f = src.GetComponent<AcousticFilter>() ?? src.gameObject.AddComponent<AcousticFilter>();
                if (Plugin.Hrtf.Value)
                {
                    if (f.BaseSpread < 0f) f.BaseSpread = src.spread;
                    src.spread = 180f;
                    f.Hrtf = true;
                }
                placed++;
            }
            Plugin.Log.LogInfo("[acoustics] main menu: " + placed + " placed sounds" + (Plugin.Hrtf.Value ? " on the HRTF" : ""));
        }
    }
}
