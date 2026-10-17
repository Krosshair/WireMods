using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // All data comes from existing biome spawn entries, native Fish
    // components, and Valheim's actual Forsaken progression keys.
    // Temporary values are restored even if the spawn routine throws.
    internal static class FishProgression
    {
        internal static bool Enabled => Plugin.Enabled != null &&
            Plugin.Enabled.Value && Plugin.FishYields.Value &&
            WorldProgress.Ready;

        internal static int SingleBiome(Heightmap.Biome mask)
        {
            int result = -1;
            Heightmap.Biome recognized = 0;
            for (int i = 0; i < 8; ++i)
            {
                Heightmap.Biome current = BiomeCatalog.TierBiome(i);
                recognized |= current;
                if ((mask & current) == 0) continue;
                if (result >= 0) return -1;
                result = i;
            }
            // A mixed ocean/land spawn isn't exclusively native to
            // one progression biome. Leave it fully vanilla.
            return (mask & ~recognized) == 0 ? result : -1;
        }

        internal static float Growth(int biome)
        {
            if (!Enabled || biome < 0) return 0f;
            return Mathf.Clamp(WorldProgress.GetBonus(biome,
                Plugin.FishYieldBonus.Value), 0f, 80f);
        }

        internal struct Original
        {
            internal SpawnSystem.SpawnData Spawn;
            internal float Chance;
            internal float Interval;
            internal int Maximum;
        }

        [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawnList")]
        private static class NativeFishAbundance
        {
            [HarmonyPrefix]
            private static void Prefix(List<SpawnSystem.SpawnData> spawners,
                bool eventSpawners, out List<Original> __state)
            {
                __state = null;
                if (eventSpawners || spawners == null || !Enabled) return;
                foreach (var spawn in spawners)
                {
                    if (spawn == null || !spawn.m_enabled || spawn.m_prefab == null ||
                        spawn.m_prefab.GetComponent<Fish>() == null ||
                        spawn.m_spawnInterval <= 0f || spawn.m_maxSpawned <= 0) continue;

                    int biome = SingleBiome(spawn.m_biome);
                    float bonus = Growth(biome);
                    if (bonus <= 0f) continue;

                    if (__state == null) __state = new List<Original>(4);
                    __state.Add(new Original {
                        Spawn = spawn, Chance = spawn.m_spawnChance,
                        Maximum = spawn.m_maxSpawned,
                        Interval = spawn.m_spawnInterval
                    });

                    // Never bypass the original spawner's terrain, bait,
                    // environment, depth, daylight or other spawn restrictions.
                    spawn.m_spawnChance = Mathf.Min(100f,
                        spawn.m_spawnChance * (1f + bonus / 100f));
                    spawn.m_spawnInterval /= 1f + bonus / 100f;
                    // Population cap is an integer in vanilla. At least one
                    // additional fish slot is needed for progress to matter.
                    spawn.m_maxSpawned = Mathf.Min(100, spawn.m_maxSpawned +
                        Mathf.Max(1, Mathf.RoundToInt(
                            spawn.m_maxSpawned * bonus / 100f)));
                }
            }

            private static void Restore(List<Original> old)
            {
                if (old == null) return;
                foreach (var state in old)
                {
                    if (state.Spawn == null) continue;
                    state.Spawn.m_spawnChance = state.Chance;
                    state.Spawn.m_spawnInterval = state.Interval;
                    state.Spawn.m_maxSpawned = state.Maximum;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(List<Original> __state) => Restore(__state);

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, List<Original> __state)
            {
                Restore(__state);
                return __exception;
            }
        }

        // Purely modifies the existing native fish roll. Valheim itself
        // sets the spawned Fish's ItemDrop quality from the rolled level.
        internal static float QualityChance(SpawnSystem.SpawnData spawn,
            float vanillaChance)
        {
            if (!Enabled || spawn == null || spawn.m_prefab == null ||
                spawn.m_prefab.GetComponent<Fish>() == null ||
                spawn.m_maxLevel <= 1) return vanillaChance;
            int biome = SingleBiome(spawn.m_biome);
            float progress = Growth(biome);
            if (progress <= 0f) return vanillaChance;
            // +5.25 percentage points with a 15% native-fish progression.
            // Never lower unusually high vanilla/world-difficulty rolls.
            return Mathf.Max(vanillaChance,
                Mathf.Min(100f, vanillaChance + progress * 0.35f));
        }
    }
}
