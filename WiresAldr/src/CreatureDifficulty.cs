using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    internal static class StarRules
    {
        [System.ThreadStatic] internal static bool EventSpawn;
        [System.ThreadStatic] internal static GameObject ActivePrefab;

        internal static bool Enabled =>
            Plugin.Enabled != null && Plugin.Enabled.Value &&
            Plugin.Stars.Value && WorldProgress.Ready;

        internal static float Adjust(float chance, float originalBase,
            int biome)
        {
            if (!Enabled || biome < 0 || biome >= 8) return chance;
            float added = WorldProgress.GetBonus(biome, Plugin.StarBonus.Value);
            float revised = (float)ProgressionMath.StarChance(
                chance, originalBase, added, Plugin.StarCap.Value);
            if (AldrDiagnostics.Active) AldrDiagnostics.Sample("STAR_ROLL", "native_biome_tier=" + (biome + 1) +
                " vanilla_chance=" + chance.ToString("0.##") +
                " new_chance=" + revised.ToString("0.##") +
                " added_points=" + added.ToString("0.##") +
                " (probability, not actual resulting stars)");
            return revised;
        }

        internal static bool HasNativeBiome(int tier, Heightmap.Biome mask)
        {
            if (tier < 0) return false;
            return (mask & BiomeCatalog.TierBiome(tier)) != 0;
        }
    }

    // Ignore raid enemies even when a Forsaken raid happens in an old
    // biome. Only normal creatures native to that biome progress.
    [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
    internal static class NaturalSpawnScope
    {
        [HarmonyPrefix]
        private static void Prefix(bool eventSpawner)
        {
            StarRules.EventSpawn = eventSpawner;
        }
        [HarmonyPostfix]
        private static void Postfix()
        {
            StarRules.EventSpawn = false;
        }
    }

    // After vanilla applies difficulty sliders, world-level scaling
    // and biome-sector modifiers; before the actual star dice rolls.
    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.GetLevelUpChance),
        new[] { typeof(Vector3), typeof(SpawnSystem.SpawnData) })]
    internal static class NormalSpawnStars
    {
        [HarmonyPostfix]
        private static void Postfix(SpawnSystem.SpawnData creature,
            ref float __result)
        {
            if (creature == null || creature.m_prefab == null ||
                creature.m_maxLevel <= 1 || StarRules.EventSpawn) return;

            // Native fish already get their ItemDrop quality from this
            // same vanilla roll. No custom species, item or rarity tags.
            if (FishProgression.IsFish(creature.m_prefab))
            {
                __result = FishProgression.QualityChance(creature, __result);
                return;
            }

            if (!StarRules.Enabled) return;
            int tier = BiomeCatalog.NaturalBiome(
                creature.m_prefab, creature.m_biome);
            if (!StarRules.HasNativeBiome(tier, creature.m_biome))
                return;

            float vanillaBase = creature.m_overrideLevelupChance > 0
                ? creature.m_overrideLevelupChance : 10f;
            __result = StarRules.Adjust(__result, vanillaBase, tier);
        }
    }

    // Local spawners such as Greydwarf nests: use the biome and only
    // native enemies from their own prefab list.
    [HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.GetLevelUpChance))]
    internal static class SpawnAreaStars
    {
        [HarmonyPostfix]
        private static void Postfix(SpawnArea __instance,
            ref float __result)
        {
            if (!StarRules.Enabled || __instance == null) return;
            int tier = BiomeCatalog.FromBiome(
                Heightmap.FindBiome(__instance.transform.position));
            if (tier < 0 || __instance.m_prefabs == null ||
                __instance.m_prefabs.Count == 0) return;
            foreach (SpawnArea.SpawnData spawn in __instance.m_prefabs)
            {
                if (spawn == null || spawn.m_prefab == null ||
                    BiomeCatalog.NativeCreature(spawn.m_prefab) != tier)
                    return; // mixed or unknown nest: don't guess
            }
            __result = StarRules.Adjust(
                __result, __instance.m_levelupChance, tier);
        }
    }

    // One-shot placed creature spawners use the float overload, not
    // SpawnSystem's SpawnData overload. Track only this specific call.
    [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
    internal static class CreatureSpawnerScope
    {
        [HarmonyPrefix]
        private static void Prefix(CreatureSpawner __instance)
        {
            StarRules.ActivePrefab = __instance != null
                ? __instance.m_creaturePrefab : null;
        }
        [HarmonyPostfix]
        private static void Postfix()
        {
            StarRules.ActivePrefab = null;
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.GetLevelUpChance),
        new[] { typeof(Vector3), typeof(float) })]
    internal static class CreatureSpawnerStars
    {
        [HarmonyPostfix]
        private static void Postfix(Vector3 position,
            float levelUpChanceOverride, ref float __result)
        {
            GameObject prefab = StarRules.ActivePrefab;
            if (!StarRules.Enabled || prefab == null) return;
            int tier = BiomeCatalog.NativeCreature(prefab);
            if (tier < 0 || BiomeCatalog.FromBiome(
                    Heightmap.FindBiome(position)) != tier)
                return;
            float baseline = levelUpChanceOverride > 0
                ? levelUpChanceOverride : 10f;
            __result = StarRules.Adjust(__result, baseline, tier);
        }
    }
}
