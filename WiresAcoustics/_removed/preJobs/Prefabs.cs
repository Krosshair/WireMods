using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Every game sound gets its filter from its prefab, so a sound spawns with it instead of having one
    // added when it starts (AddComponent on each of ~50 sounds a second in a busy base was most of
    // this mod's main-thread time). Done once the game's prefabs are loaded; anything that turns up
    // later (other mods' prefabs) still gets one added on first play.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Prefabs
    {
        private static bool s_done;

        private static void Postfix()
        {
            if (s_done || !Plugin.Enabled.Value) return;
            s_done = true;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int n = 0;
            foreach (ZSFX sfx in Resources.FindObjectsOfTypeAll<ZSFX>())
            {
                if (sfx == null || sfx.gameObject.scene.IsValid()) continue; // prefabs only, not live sounds
                if (sfx.GetComponent<AudioSource>() == null || sfx.GetComponent<AcousticFilter>() != null) continue;
                sfx.gameObject.AddComponent<AcousticFilter>();
                n++;
            }
            Plugin.Log.LogInfo("[acoustics] filter added to " + n + " sound prefabs in " +
                ((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F1") + " ms");
        }
    }
}
