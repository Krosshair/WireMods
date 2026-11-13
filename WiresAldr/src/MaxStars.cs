using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Higher stars as the world is conquered: a biome's creatures can roll one star more for its own
    // boss and for every boss after it (Black Forest: 2 stars, 3 after the Elder, 4 after Bonemass;
    // Meadows up to 10 after the Deep North's). Each extra star still needs its own roll at the same
    // chance, as vanilla's stars do, so they stay rarer the higher they go. Only where a spawn can
    // already star (vanilla maximum above level 1), only creatures native to the biome, never raids,
    // never fish. The maximum is raised for the one spawn and put back straight after.
    internal static class MaxStars
    {
        // Bosses defeated from this biome's own on (biome tiers 0 Meadows .. 7 Deep North).
        internal static int Extra(int tier)
        {
            if (!StarRules.Enabled || tier < 0 || tier >= ProgressionMath.BiomeCount) return 0;
            bool[] keys = WorldProgress.ReadKeys();
            int n = 0;
            for (int i = tier; i < keys.Length; i++) if (keys[i]) n++;
            return n;
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
    internal static class MaxStarsSpawnSystem
    {
        [HarmonyPrefix]
        private static void Prefix(SpawnSystem.SpawnData critter, bool eventSpawner, out int __state)
        {
            __state = 0;
            if (eventSpawner || critter == null || critter.m_prefab == null || critter.m_maxLevel <= 1) return;
            if (FishProgression.IsFish(critter.m_prefab)) return;
            int tier = BiomeCatalog.NaturalBiome(critter.m_prefab, critter.m_biome);
            if (!StarRules.HasNativeBiome(tier, critter.m_biome)) return;
            __state = MaxStars.Extra(tier);
            critter.m_maxLevel += __state;
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(SpawnSystem.SpawnData critter, int __state, System.Exception __exception)
        {
            if (critter != null && __state > 0) critter.m_maxLevel -= __state;
            return __exception;
        }
    }

    // Nests: the same, when every creature it spawns is native to its biome (as SpawnAreaStars).
    [HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
    internal static class MaxStarsSpawnArea
    {
        [HarmonyPrefix]
        private static void Prefix(SpawnArea __instance, out int __state)
        {
            __state = 0;
            if (!StarRules.Enabled || __instance == null || __instance.m_prefabs == null || __instance.m_prefabs.Count == 0) return;
            int tier = BiomeCatalog.FromBiome(Heightmap.FindBiome(__instance.transform.position));
            if (tier < 0) return;
            foreach (SpawnArea.SpawnData s in __instance.m_prefabs)
                if (s == null || s.m_prefab == null || BiomeCatalog.NativeCreature(s.m_prefab) != tier) return;
            __state = MaxStars.Extra(tier);
            if (__state == 0) return;
            foreach (SpawnArea.SpawnData s in __instance.m_prefabs)
                if (s.m_maxLevel > 1) s.m_maxLevel += __state;
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(SpawnArea __instance, int __state, System.Exception __exception)
        {
            if (__state > 0 && __instance != null && __instance.m_prefabs != null)
                foreach (SpawnArea.SpawnData s in __instance.m_prefabs)
                    if (s != null && s.m_maxLevel > 1 + __state) s.m_maxLevel -= __state;
            return __exception;
        }
    }

    // Placed one-shot spawners (camps, dungeons). A location's own level override still wins.
    [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
    internal static class MaxStarsCreatureSpawner
    {
        [HarmonyPrefix]
        private static void Prefix(CreatureSpawner __instance, out int __state)
        {
            __state = 0;
            if (__instance == null || __instance.m_creaturePrefab == null || __instance.m_maxLevel <= 1) return;
            int tier = BiomeCatalog.NativeCreature(__instance.m_creaturePrefab);
            if (tier < 0 || BiomeCatalog.FromBiome(Heightmap.FindBiome(__instance.transform.position)) != tier) return;
            __state = MaxStars.Extra(tier);
            __instance.m_maxLevel += __state;
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(CreatureSpawner __instance, int __state, System.Exception __exception)
        {
            if (__instance != null && __state > 0) __instance.m_maxLevel -= __state;
            return __exception;
        }
    }
}
