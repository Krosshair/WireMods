using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Locations and dungeons aren't in the game's prefab list (Prefabs), they're loaded when you get
    // near one. When one is placed, its own sounds get their filter (so nothing is added later while
    // they play) and its reverb zones are removed (Room's measured space does that job). Looking only
    // inside what was just placed, instead of searching the whole scene every few seconds.
    internal static class Spawned
    {
        private static int s_sources, s_zones;

        internal static void Take(GameObject root)
        {
            if (!Plugin.Enabled.Value || root == null) return;
            AudioMan man = AudioMan.instance;
            foreach (AudioSource src in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (src == null || src.GetComponent<AcousticFilter>() != null) continue;
                if (man != null && man.m_guiMixer != null && src.outputAudioMixerGroup == man.m_guiMixer) continue;
                src.gameObject.AddComponent<AcousticFilter>().Active = false; // switched on by Budget while it matters
                s_sources++;
            }
            foreach (AudioReverbZone z in root.GetComponentsInChildren<AudioReverbZone>(true))
                if (Room.RemoveZone(z)) s_zones++;
        }

        [HarmonyPatch(typeof(Location), "Awake")]
        private static class AtLocation
        {
            private static void Postfix(Location __instance) => Take(__instance.gameObject);
        }

        [HarmonyPatch(typeof(DungeonGenerator), "Spawn")]
        private static class AtDungeon
        {
            private static void Postfix(DungeonGenerator __instance) => Take(__instance.gameObject);
        }

        [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Generate), typeof(int), typeof(ZoneSystem.SpawnMode))]
        private static class AtDungeonGenerated
        {
            private static void Postfix(DungeonGenerator __instance) => Take(__instance.gameObject);
        }

        internal static string Summary()
        {
            string s = "placed with locations: " + s_sources + " sounds, " + s_zones + " reverb zones removed";
            s_sources = s_zones = 0;
            return s;
        }
    }
}
