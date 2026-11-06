using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Every game sound gets its filter from its prefab, so a sound spawns with it instead of having one
    // added when it starts (AddComponent on each of ~50 sounds a second in a busy base was most of
    // this mod's main-thread time). Done once the game's prefabs are loaded; anything that turns up
    // later (other mods' prefabs) still gets one added on first play. Placed sources outside the
    // sound-effect system (Others: ship wake, shield hum) get theirs here too, not from a scan.
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
            int others = 0;
            AudioMan man = AudioMan.instance;
            foreach (AudioSource src in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (src == null || src.gameObject.scene.IsValid() || src.spatialBlend < .5f) continue;
                if (man != null && man.m_guiMixer != null && src.outputAudioMixerGroup == man.m_guiMixer) continue;
                if (src.GetComponent<ZSFX>() != null || src.GetComponent<AcousticFilter>() != null) continue;
                src.gameObject.AddComponent<AcousticFilter>();
                others++;
            }
            Plugin.Log.LogInfo("[acoustics] filter added to " + n + " sound prefabs and " + others + " other placed sources in " +
                ((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F1") + " ms");
        }
    }
}
