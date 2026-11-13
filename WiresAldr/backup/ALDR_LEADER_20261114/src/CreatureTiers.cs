using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresAldr
{
    // Native creature biome read from Valheim's own world spawn lists, so
    // every naturally spawning creature (including content from future game
    // updates) is classified from real game data instead of a name list.
    // Result is a 0-based progression index (Meadows 0 ... Deep North 7),
    // or -1 when the game data does not identify one native land biome.
    internal static class CreatureTiers
    {
        private static Dictionary<string, int> map;
        private static float nextAttempt = -999f;
        internal static bool Loaded => map != null;

        private static string Key(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return "";
            int clone = prefabName.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) prefabName = prefabName.Substring(0, clone);
            return prefabName.Trim();
        }

        // Lowest progression biome in a spawn mask. Ocean is not a
        // progression biome, so an ocean-only spawn gives -1.
        private static int LowestBiome(Heightmap.Biome mask)
        {
            for (int i = 0; i < ProgressionMath.BiomeCount; ++i)
                if ((mask & BiomeCatalog.TierBiome(i)) != 0) return i;
            return -1;
        }

        private static bool Build()
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || zone.m_zoneCtrlPrefab == null) return false;
            SpawnSystem system = zone.m_zoneCtrlPrefab.GetComponent<SpawnSystem>();
            if (system == null || system.m_spawnLists == null) return false;

            // Ungated spawns define where a creature natively lives.
            // Spawns that only appear after a boss key or during a
            // persistent event are guests in older biomes and are used
            // only when a creature has no ungated entry at all.
            var native = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var gated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (SpawnSystemList list in system.m_spawnLists)
            {
                if (list == null || list.m_spawners == null) continue;
                foreach (SpawnSystem.SpawnData spawn in list.m_spawners)
                {
                    if (spawn == null || spawn.m_prefab == null || spawn.m_devDisabled) continue;
                    if (spawn.m_prefab.GetComponent<Character>() == null &&
                        spawn.m_prefab.GetComponent<Fish>() == null) continue;
                    int tier = LowestBiome(spawn.m_biome);
                    if (tier < 0) continue;
                    bool isGated = !string.IsNullOrEmpty(spawn.m_requiredGlobalKey) ||
                        !string.IsNullOrEmpty(spawn.m_requiredPersistentEvent);
                    var target = isGated ? gated : native;
                    string key = Key(spawn.m_prefab.name);
                    int existing;
                    if (!target.TryGetValue(key, out existing) || tier < existing)
                        target[key] = tier;
                }
            }
            foreach (var kv in gated)
                if (!native.ContainsKey(kv.Key)) native[kv.Key] = kv.Value;
            if (native.Count == 0) return false;
            map = native;
            if (AldrDiagnostics.Active)
                AldrDiagnostics.Log("CREATURE_TIERS", "native_spawn_entries=" + map.Count);
            return true;
        }

        // 0-based native biome from world spawn data, or -1.
        internal static int Native(string prefabName)
        {
            if (map == null)
            {
                // Bounded retry before the world has loaded; no per-frame scans.
                if (Time.time < nextAttempt) return -1;
                nextAttempt = Time.time + 10f;
                if (!Build()) return -1;
            }
            int tier;
            return map.TryGetValue(Key(prefabName), out tier) ? tier : -1;
        }

        // Game.Start creates a fresh world; spawn lists may differ per world
        // modding setup, so rebuild lazily after each world load.
        internal static void Reset() { map = null; nextAttempt = -999f; }
    }

    [HarmonyLib.HarmonyPatch(typeof(Game), "Start")]
    internal static class CreatureTiersReset
    {
        private static void Postfix() { CreatureTiers.Reset(); DynamicExperience.ResetWorld(); }
    }
}
