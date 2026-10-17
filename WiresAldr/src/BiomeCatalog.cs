using System;
using UnityEngine;

namespace WiresAldr
{
    internal static class BiomeCatalog
    {
        internal static int FromBiome(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return 0;
                case Heightmap.Biome.BlackForest: return 1;
                case Heightmap.Biome.Swamp: return 2;
                case Heightmap.Biome.Mountain: return 3;
                case Heightmap.Biome.Plains: return 4;
                case Heightmap.Biome.Mistlands: return 5;
                case Heightmap.Biome.AshLands: return 6;
                case Heightmap.Biome.DeepNorth: return 7;
                default: return -1;
            }
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int clone = s.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (clone >= 0) s = s.Substring(0, clone);
            return s.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
        }

        internal static int NativeCreature(GameObject prefab)
        {
            if (prefab == null) return -1;
            // Valheim's own world spawn lists decide first. This also
            // covers creatures from later content without a name list.
            int fromSpawns = CreatureTiers.Native(prefab.name);
            if (fromSpawns >= 0) return fromSpawns;
            string n = Normalize(prefab.name);
            // Fallback for creatures outside the world spawn lists.
            // Specific native enemies first. Never use the current
            // biome to categorize post-boss guest spawn types.
            if (Has(n,"eikthyr","gdking","bonemass","dragon","goblinking",
                "thequeen","fader","frozenking")) return -1;
            if (Has(n,"boar","deer","neck","greyling")) return 0;
            if (Has(n,"greydwarf","troll","ghost","skeleton","rancidremains")) return 1;
            if (Has(n,"draugr","blob","oozer","leech","wraith","abomination","surtling")) return 2;
            if (Has(n,"wolf","fenring","ulv","stonegolem","hatchling","bat","cultist")) return 3;
            if (Has(n,"goblin","lox","deathsquito","growth","tarblob")) return 4;
            if (Has(n,"seeker","gjall","tick","dverger","hare")) return 5;
            if (Has(n,"charred","asksvin","morgen","fallenvalkyrie","volture","bonemaw","lavaeye")) return 6;
            // Deep North prefabs may differ across content updates.
            return -1;
        }

        private static bool Has(string n, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (n.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static int NaturalBiome(GameObject prefab,
            Heightmap.Biome spawnBiome)
        {
            int native = NativeCreature(prefab);
            if (native >= 0) return native;
            if (prefab == null || prefab.GetComponent<Character>() == null ||
                prefab.GetComponent<BaseAI>() == null)
                return -1;
            // For unknown/modded native mobs, allow only a single
            // explicitly selected spawning biome.
            int count = 0, tier = -1;
            for (int i = 0; i < 8; ++i)
            {
                Heightmap.Biome bit = TierBiome(i);
                if ((spawnBiome & bit) != 0)
                {
                    count++; tier = i;
                }
            }
            return count == 1 ? tier : -1;
        }

        internal static Heightmap.Biome TierBiome(int tier)
        {
            switch(tier)
            {
                case 0: return Heightmap.Biome.Meadows;
                case 1: return Heightmap.Biome.BlackForest;
                case 2: return Heightmap.Biome.Swamp;
                case 3: return Heightmap.Biome.Mountain;
                case 4: return Heightmap.Biome.Plains;
                case 5: return Heightmap.Biome.Mistlands;
                case 6: return Heightmap.Biome.AshLands;
                case 7: return Heightmap.Biome.DeepNorth;
                default: return 0;
            }
        }

        private static string Token(GameObject prefab)
        {
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_name : null;
        }

        internal static GlobalResource SharedMaterial(GameObject prefab)
        {
            if (prefab == null) return GlobalResource.None;
            switch (Token(prefab))
            {
                case "$item_wood": return GlobalResource.Wood;
                case "$item_stone": return GlobalResource.Stone;
                case "$item_roundlog": return GlobalResource.CoreWood;
                case "$item_finewood": return GlobalResource.FineWood;
                case "$item_blackwood": return GlobalResource.Ashwood;
            }
            string n = Normalize(prefab.name);
            if (Has(n,"wood","wooditem")) return GlobalResource.Wood;
            if (Has(n,"stone","rock")) return GlobalResource.Stone;
            if (Has(n,"roundlog","corewood")) return GlobalResource.CoreWood;
            if (Has(n,"finewood")) return GlobalResource.FineWood;
            if (Has(n,"ashwood")) return GlobalResource.Ashwood;
            return GlobalResource.None;
        }

        internal static int RegionalMaterialTier(GameObject prefab)
        {
            if (prefab == null || SharedMaterial(prefab) != GlobalResource.None)
                return -1;
            // Vanilla shared item names (verified against game data).
            switch (Token(prefab))
            {
                case "$item_copperore": case "$item_tinore": return 1;
                case "$item_elderbark": case "$item_ironscrap": case "$item_ironore": return 2;
                case "$item_silverore": case "$item_obsidian": return 3;
                case "$item_tar": return 4;
                case "$item_yggdrasilwood": case "$item_blackmarble": case "$item_softtissue": return 5;
                case "$item_grausten": case "$item_flametalore": return 6;
                case "$item_frostwood": case "$item_goldore": return 7;
            }
            string n = Normalize(prefab.name);
            if (Has(n,"copperore","tinore","tin")) return 1;
            if (Has(n,"elderbark","ancientbark","ironscrap","ironore")) return 2;
            if (Has(n,"silverore","obsidian")) return 3;
            if (Has(n,"tar")) return 4;
            if (Has(n,"yggdrasilwood","blackmarble","softtissue")) return 5;
            if (Has(n,"grausten","flametalore")) return 6;
            // Later regional materials are recognized by item name above.
            return -1;
        }
    }
}
