using System;
using System.Collections.Generic;

namespace WiresForgeOfGoodtential
{
    internal static class ForgeTrophyEligibility
    {
        // Boss trophies belong on their stones, not in the Forge.
        // Named Hildir miniboss trophies are also excluded.
        // Exact prefab IDs are used; prestige is NOT an indicator
        // of boss status (rare ordinary enemies can be stronger).
        private static readonly HashSet<string> ForbiddenPrefabs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TrophyEikthyr",
                "TrophyTheElder",
                "TrophyBonemass",
                "TrophyDragonQueen",
                "TrophyGoblinKing",
                "TrophySeekerQueen",
                "TrophyFader",
                "TrophyYagluth",
                "TrophyModer",
                "TrophyQueen",
                "TrophyBarka",
                "TrophyEyeless",
                "TrophyFimbulbringer",
                "TrophyFrozenKing",
                "TrophySkeletonHildir",
                "TrophyCultist_Hildir",
                "TrophyGoblinBruteBrosBrute",
                "TrophyGoblinBruteBrosShaman",
                "TrophyElaking"
            };

        internal static bool IsBossTrophyPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;

            string normalized = prefabName.Trim();

            if (normalized.EndsWith(
                "(Clone)", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(
                    0, normalized.Length - 7).Trim();
            }

            return ForbiddenPrefabs.Contains(normalized);
        }

        internal static bool IsBossTrophy(
            ItemDrop.ItemData trophy)
        {
            if (trophy == null)
                return false;

            if (trophy.m_dropPrefab != null &&
                IsBossTrophyPrefab(trophy.m_dropPrefab.name))
            {
                return true;
            }

            // Some serialized/modded items have lost m_dropPrefab.
            // Fall back to their untranslated shared-name token.
            string sharedName =
                trophy.m_shared != null
                    ? trophy.m_shared.m_name
                    : "";

            if (string.IsNullOrEmpty(sharedName))
                return false;

            string name = sharedName
                .Replace("$", "")
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .ToLowerInvariant();

            // Only check recognizable boss-specific full identifiers;
            // don't accidentally exclude Seeker, Drake or ordinary
            // Goblin trophies.
            string[] bossIdentifiers =
            {
                "eikthyr", "theelder", "gdking", "bonemass",
                "dragonqueen", "moder", "goblinking", "yagluth",
                "seekerqueen", "thequeen", "fader", "frozenking",
                "fimbulbringer", "skeletonhildir", "cultisthildir",
                "goblinbrutebros", "elaking", "trophybarka",
                "trophyeyeless", "fimbulbringer"
            };

            foreach (string identifier in bossIdentifiers)
            {
                if (name.Contains(identifier))
                    return true;
            }

            return false;
        }
    }
}
