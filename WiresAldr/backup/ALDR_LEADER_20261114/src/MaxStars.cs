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

        // The cap is soft: at night one more star is allowed (still its own roll, like every star).
        internal static int Raise(int tier, bool interior = false)
        {
            if (!StarRules.Enabled || tier < 0) return 0;
            return Extra(tier) + (!interior && EnvMan.IsNight() ? 1 : 0);
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
    internal static class MaxStarsSpawnSystem
    {
        [HarmonyPrefix]
        private static void Prefix(SpawnSystem.SpawnData critter, Vector3 spawnPoint, bool eventSpawner, out int __state)
        {
            __state = 0;
            Leaders.Skip();
            if (eventSpawner || critter == null || critter.m_prefab == null || critter.m_maxLevel <= 1) return;
            if (FishProgression.IsFish(critter.m_prefab)) return;
            int tier = BiomeCatalog.NaturalBiome(critter.m_prefab, critter.m_biome);
            if (!StarRules.HasNativeBiome(tier, critter.m_biome)) return;
            __state = MaxStars.Raise(tier);
            critter.m_maxLevel += __state;
            Leaders.Before(critter, spawnPoint);
        }

        [HarmonyPostfix]
        private static void Postfix(SpawnSystem.SpawnData critter) => Leaders.After(critter);

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
            __state = MaxStars.Raise(tier);
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
            // Dungeon and crypt interiors (high above the world) have no night.
            __state = MaxStars.Raise(tier, __instance.transform.position.y > 3000f);
            __instance.m_maxLevel += __state;
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(CreatureSpawner __instance, int __state, System.Exception __exception)
        {
            if (__instance != null && __state > 0) __instance.m_maxLevel -= __state;
            return __exception;
        }
    }

    // Group leaders: the first creature of a natural group of two or more gets one star more than it
    // rolled. Vanilla spawns a group right after finding its centre (FindBaseSpawnPoint), one Spawn
    // per member, and the group's size isn't known until the second member arrives, so the first
    // one is kept and given its star then. Same rules as above (native, not raids or fish, can star).
    internal static class Leaders
    {
        private static SpawnSystem.SpawnData s_group;
        private static Character s_first;
        private static int s_count;
        private static bool s_counts;

        internal static void NewGroup() { s_group = null; s_first = null; s_count = 0; }
        internal static void Skip() => s_counts = false;

        // Vanilla only levels a spawn past its centre distance (if it has one).
        internal static void Before(SpawnSystem.SpawnData critter, Vector3 spawnPoint) =>
            s_counts = critter.m_levelUpMinCenterDistance <= 0f || spawnPoint.magnitude > critter.m_levelUpMinCenterDistance;

        internal static void After(SpawnSystem.SpawnData critter)
        {
            if (!s_counts || critter == null || critter.m_prefab == null) return;
            if (s_group != critter) { s_group = critter; s_first = null; s_count = 0; }
            s_count++;
            if (s_count == 1)
            {
                // The one just made is the newest in the game's character list (added in Awake).
                var all = Character.GetAllCharacters();
                Character c = all.Count > 0 ? all[all.Count - 1] : null;
                s_first = c != null && Utils.GetPrefabName(c.gameObject) == critter.m_prefab.name ? c : null;
            }
            else if (s_count == 2 && s_first != null)
            {
                s_first.SetLevel(s_first.GetLevel() + 1);
                s_first = null;
            }
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), "FindBaseSpawnPoint")]
    internal static class LeadersGroupStart
    {
        private static void Prefix() => Leaders.NewGroup();
    }
}
