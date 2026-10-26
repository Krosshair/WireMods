using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    internal enum IdolFamily
    {
        Battle,
        Protection
    }

    internal enum ForgeOutcome
    {
        None,
        Success,
        Jackpot,
        Ignore,
        Loss,
        CompleteLoss,
        Destroyed
    }

    internal enum GearArchetype
    {
        Sword,
        Mace,
        Sledge,
        Spear,
        Polearm,
        Knife,
        Bow,
        Crossbow,
        Axe,
        HeavyAxe,
        Greatsword,
        Fists,
        Pickaxe,
        Magic,
        Armor,
        RoundShield,
        TowerShield,
        Buckler,
        GenericBattle,
        GenericProtection
    }

    internal sealed class ForgeOdds
    {
        internal float Success;
        internal float OrdinaryFailure;
        internal float CompleteLoss;
        internal float Destruction;
        internal float JackpotChance = 0.20f;
        internal bool FailureIsIgnore;
        internal float CascadeChance;

        internal float TotalBad
        {
            get { return OrdinaryFailure + CompleteLoss + Destruction; }
        }
    }

    internal static class ForgeData
    {
        internal const int MaxTier = 7;

        internal static readonly string[] TierNames =
        {
            "Wooden",
            "Bronze",
            "Iron",
            "Silver",
            "Black Metal",
            "Black Marble",
            "Flametal",
            "Bloodgold"
        };

        // Internal reference prestige for an item sitting in each biome band.
        // Trophy prestige is compared against this continuously.
        internal static readonly float[] TierPrestige =
        {
            6f, 20f, 30f, 40f, 48f, 58f, 68f, 82f
        };

        private static readonly Dictionary<GearArchetype, float[]> Thresholds =
            new Dictionary<GearArchetype, float[]>
            {
                { GearArchetype.Sword,       new[] { 0f, 35f, 55f, 75f, 95f, 115f, 135f, 170f } },
                { GearArchetype.Mace,        new[] { 0f, 35f, 55f, 75f, 95f, 115f, 135f, 170f } },
                { GearArchetype.Spear,       new[] { 0f, 35f, 55f, 75f, 95f, 115f, 135f, 170f } },
                { GearArchetype.Axe,         new[] { 0f, 40f, 60f, 80f, 100f, 120f, 140f, 176f } },
                { GearArchetype.Polearm,     new[] { 0f, 45f, 65f, 85f, 105f, 125f, 145f, 182f } },
                { GearArchetype.Knife,       new[] { 0f, 12f, 20f, 25f, 34f, 45f, 60f, 75f } },
                { GearArchetype.Bow,         new[] { 0f, 32f, 42f, 52f, 62f, 72f, 82f, 100f } },
                { GearArchetype.Crossbow,    new[] { 0f, 0f, 0f, 0f, 0f, 200f, 220f, 264f } },
                { GearArchetype.HeavyAxe,    new[] { 0f, 50f, 70f, 90f, 110f, 130f, 150f, 188f } },
                { GearArchetype.Greatsword,  new[] { 0f, 70f, 90f, 110f, 130f, 150f, 170f, 210f } },
                { GearArchetype.Sledge,      new[] { 0f, 20f, 55f, 85f, 115f, 145f, 175f, 225f } },
                { GearArchetype.Fists,       new[] { 0f, 25f, 42f, 60f, 80f, 95f, 105f, 114f } },
                { GearArchetype.Pickaxe,     new[] { 0f, 25f, 33f, 41f, 49f, 57f, 65f, 73f } },
                { GearArchetype.Armor,       new[] { 0f, 8f, 14f, 20f, 26f, 32f, 38f, 44f } },
                { GearArchetype.RoundShield, new[] { 6f, 24f, 42f, 60f, 78f, 96f, 114f, 132f } },
                { GearArchetype.TowerShield, new[] { 10f, 32f, 52f, 72f, 104f, 122f, 140f, 158f } },
                { GearArchetype.Buckler,     new[] { 0f, 16f, 28f, 40f, 62f, 78f, 83f, 88f } }
            };

        private sealed class TrophyRule
        {
            internal readonly string[] Patterns;
            internal readonly float Prestige;

            internal TrophyRule(float prestige, params string[] patterns)
            {
                Prestige = prestige;
                Patterns = patterns;
            }
        }

        private static readonly Dictionary<string, float>
            VanillaTrophyPrestige =
                new Dictionary<string, float>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "TrophyAbomination", 42f },
                    { "TrophyAsksvin", 66f },
                    { "TrophyBarka", 100f },
                    { "TrophyBjorn", 24f },
                    { "TrophyBjornUndead", 58f },
                    { "TrophyBlob", 22f },
                    { "TrophyBlob_Frost", 34f },
                    { "TrophyBlob_Lava", 55f },
                    { "TrophyBlob_Morkhalla", 58f },
                    { "TrophyBoar", 6f },
                    { "TrophyBonemass", 48f },
                    { "TrophyBonemawSerpent", 68f },
                    { "TrophyCharredArcher", 52f },
                    { "TrophyCharredMage", 72f },
                    { "TrophyCharredMelee", 60f },
                    { "TrophyCultist", 38f },
                    { "TrophyCultist_Hildir", 50f },
                    { "TrophyDeathsquito", 35f },
                    { "TrophyDeer", 2f },
                    { "TrophyDeerWhite", 10f },
                    { "TrophyDragonQueen", 54f },
                    { "TrophyDraugr", 28f },
                    { "TrophyDraugrElite", 34f },
                    { "TrophyDraugrFem", 28f },
                    { "TrophyDvergr", 55f },
                    { "TrophyEikthyr", 18f },
                    { "TrophyElaking", 60f },
                    { "TrophyFader", 84f },
                    { "TrophyFallenValkyrie", 72f },
                    { "TrophyFenring", 40f },
                    { "TrophyForestTroll", 28f },
                    { "TrophyFrostTroll", 28f },
                    { "TrophyGhost", 14f },
                    { "TrophyGjall", 64f },
                    { "TrophyGoblin", 42f },
                    { "TrophyGoblinBrute", 48f },
                    { "TrophyGoblinBruteBrosBrute", 58f },
                    { "TrophyGoblinBruteBrosShaman", 58f },
                    { "TrophyGoblinKing", 66f },
                    { "TrophyGoblinShaman", 38f },
                    { "TrophyGreydwarf", 12f },
                    { "TrophyGreydwarfBrute", 20f },
                    { "TrophyGreydwarfShaman", 16f },
                    { "TrophyGrowth", 40f },
                    { "TrophyHare", 8f },
                    { "TrophyHatchling", 34f },
                    { "TrophyJotunWarrior", 78f },
                    { "TrophyJotunWitch", 70f },
                    { "TrophyKvastur", 44f },
                    { "TrophyLeech", 24f },
                    { "TrophyLox", 50f },
                    { "TrophyMole", 82f },
                    { "TrophyMoose", 65f },
                    { "TrophyMorgen", 76f },
                    { "TrophyNeck", 4f },
                    { "TrophySeal", 24f },
                    { "TrophySeeker", 52f },
                    { "TrophySeekerBrute", 60f },
                    { "TrophySeekerQueen", 72f },
                    { "TrophySerpent", 45f },
                    { "TrophySGolem", 44f },
                    { "TrophySkeleton", 13f },
                    { "TrophySkeletonHildir", 34f },
                    { "TrophySkeletonPoison", 18f },
                    { "TrophySurtling", 18.5f },
                    { "TrophyTheElder", 34f },
                    { "TrophyTick", 42f },
                    { "TrophyUlv", 36f },
                    { "TrophyVolture", 45f },
                    { "TrophyWolf", 32f },
                    { "TrophyWraith", 31f },
                    { "TrophyWrithan", 45f }
                };

        // Fuzzy rules are kept only as a fallback for modded trophies.
        private static readonly TrophyRule[] TrophyRules =
        {
            new TrophyRule(100f, "barka"),
            new TrophyRule(84f, "fader"),
            new TrophyRule(82f, "eyeless"),
            new TrophyRule(78f, "krigen"),
            new TrophyRule(76f, "morgen"),
            new TrophyRule(72f, "fallenvalkyrie", "valkyrie"),
            new TrophyRule(72f, "queen"),
            new TrophyRule(70f, "hexen"),
            new TrophyRule(68f, "bonemaw"),
            new TrophyRule(66f, "asksvin"),
            new TrophyRule(66f, "yagluth"),
            new TrophyRule(65f, "moose"),
            new TrophyRule(64f, "gjall"),
            new TrophyRule(72f, "trophycharredmage", "charredmage", "charredwarlock", "warlock"),
            new TrophyRule(60f, "seekersoldier", "soldier"),
            new TrophyRule(60f, "trophycharredmelee", "charredmelee", "charredwarrior"),
            new TrophyRule(60f, "elaking"),
            new TrophyRule(58f, "zil", "thungr"),
            new TrophyRule(56f, "dvergrmage"),
            new TrophyRule(55f, "dvergrrogue", "dvergr"),
            new TrophyRule(55f, "lavablob"),
            new TrophyRule(54f, "moder"),
            new TrophyRule(52f, "trophycharredarcher", "charredarcher", "charredmarksman", "marksman"),
            new TrophyRule(52f, "seeker"),
            new TrophyRule(50f, "lox"),
            new TrophyRule(50f, "geirrhafa"),
            new TrophyRule(48f, "fulingberserker", "berserker"),
            new TrophyRule(48f, "bonemass"),
            new TrophyRule(45f, "volture"),
            new TrophyRule(45f, "serpent"),
            new TrophyRule(44f, "stonegolem", "golem"),
            new TrophyRule(42f, "abomination"),
            new TrophyRule(42f, "tick"),
            new TrophyRule(42f, "fuling"),
            new TrophyRule(40f, "growth"),
            new TrophyRule(40f, "fenring"),
            new TrophyRule(38f, "cultist"),
            new TrophyRule(38f, "pulp"),
            new TrophyRule(38f, "fulingshaman"),
            new TrophyRule(35f, "deathsquito"),
            new TrophyRule(34f, "draugrelite"),
            new TrophyRule(34f, "drake"),
            new TrophyRule(34f, "elder"),
            new TrophyRule(32f, "wolf"),
            new TrophyRule(31f, "wraith"),
            new TrophyRule(28f, "draugr"),
            new TrophyRule(28f, "frosttroll", "troll"),
            new TrophyRule(24f, "seal"),
            new TrophyRule(24f, "leech"),
            new TrophyRule(24f, "bear"),
            new TrophyRule(22f, "blob"),
            new TrophyRule(20f, "greydwarfbrute", "brute"),
            new TrophyRule(18.5f, "surtling"),
            new TrophyRule(18f, "rancid"),
            new TrophyRule(18f, "eikthyr"),
            new TrophyRule(16f, "greydwarfshaman", "shaman"),
            new TrophyRule(13f, "skeleton"),
            new TrophyRule(12f, "greydwarf"),
            new TrophyRule(10f, "deerwhite", "whitedeer"),
            new TrophyRule(8f, "hare"),
            new TrophyRule(6f, "boar"),
            new TrophyRule(4f, "neck"),
            new TrophyRule(2f, "deer")
        };

        internal static string IdolPrefabName(IdolFamily family, int tier)
        {
            return "Upgrader" + Mathf.Clamp(tier, 0, MaxTier) +
                (family == IdolFamily.Battle ? "Weapon" : "Armor");
        }

        internal static ItemDrop GetIdolDrop(IdolFamily family, int tier)
        {
            if (ObjectDB.instance == null)
                return null;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(
                IdolPrefabName(family, tier));

            return prefab != null
                ? prefab.GetComponent<ItemDrop>()
                : null;
        }

        internal static bool TryParseIdol(
            ItemDrop idol,
            out int tier,
            out IdolFamily family)
        {
            tier = -1;
            family = IdolFamily.Battle;

            if (idol == null)
                return false;

            string prefabName = idol.gameObject != null
                ? idol.gameObject.name
                : "";

            for (int i = 0; i <= MaxTier; i++)
            {
                if (string.Equals(
                    prefabName,
                    IdolPrefabName(IdolFamily.Battle, i),
                    StringComparison.OrdinalIgnoreCase))
                {
                    tier = i;
                    family = IdolFamily.Battle;
                    return true;
                }

                if (string.Equals(
                    prefabName,
                    IdolPrefabName(IdolFamily.Protection, i),
                    StringComparison.OrdinalIgnoreCase))
                {
                    tier = i;
                    family = IdolFamily.Protection;
                    return true;
                }
            }

            return false;
        }

        internal static bool TryGetNativeIdol(
            Recipe recipe,
            out int tier,
            out IdolFamily family,
            out Piece.Requirement requirement)
        {
            tier = -1;
            family = IdolFamily.Battle;
            requirement = null;

            if (recipe == null || recipe.m_resources == null)
                return false;

            for (int i = 0; i < recipe.m_resources.Length; i++)
            {
                Piece.Requirement req = recipe.m_resources[i];

                if (req == null ||
                    !req.m_upgraderResource ||
                    req.m_resItem == null)
                {
                    continue;
                }

                int parsedTier;
                IdolFamily parsedFamily;

                if (!TryParseIdol(
                    req.m_resItem,
                    out parsedTier,
                    out parsedFamily))
                {
                    continue;
                }

                tier = parsedTier;
                family = parsedFamily;
                requirement = req;
                return true;
            }

            return false;
        }

        internal static GearArchetype GetArchetype(
            ItemDrop.ItemData item,
            IdolFamily family)
        {
            if (item == null || item.m_shared == null)
            {
                return family == IdolFamily.Battle
                    ? GearArchetype.GenericBattle
                    : GearArchetype.GenericProtection;
            }

            ItemDrop.ItemData.SharedData s = item.m_shared;

            if (family == IdolFamily.Protection)
            {
                if (s.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                {
                    if (s.m_timedBlockBonus <= 1.01f)
                        return GearArchetype.TowerShield;

                    if (s.m_timedBlockBonus >= 2.0f)
                        return GearArchetype.Buckler;

                    return GearArchetype.RoundShield;
                }

                if (s.m_itemType == ItemDrop.ItemData.ItemType.Helmet ||
                    s.m_itemType == ItemDrop.ItemData.ItemType.Chest ||
                    s.m_itemType == ItemDrop.ItemData.ItemType.Legs ||
                    s.m_itemType == ItemDrop.ItemData.ItemType.Shoulder)
                {
                    return GearArchetype.Armor;
                }

                return GearArchetype.GenericProtection;
            }

            switch (s.m_skillType)
            {
                case Skills.SkillType.Swords:
                    return s.m_animationState ==
                        ItemDrop.ItemData.AnimationState.Greatsword
                        ? GearArchetype.Greatsword
                        : GearArchetype.Sword;

                case Skills.SkillType.Clubs:
                    return s.m_animationState ==
                        ItemDrop.ItemData.AnimationState.TwoHandedClub
                        ? GearArchetype.Sledge
                        : GearArchetype.Mace;

                case Skills.SkillType.Polearms:
                    return GearArchetype.Polearm;

                case Skills.SkillType.Spears:
                    return GearArchetype.Spear;

                case Skills.SkillType.Knives:
                    return GearArchetype.Knife;

                case Skills.SkillType.Bows:
                    return GearArchetype.Bow;

                case Skills.SkillType.Crossbows:
                    return GearArchetype.Crossbow;

                case Skills.SkillType.Axes:
                    return s.m_animationState ==
                        ItemDrop.ItemData.AnimationState.TwoHandedAxe
                        ? GearArchetype.HeavyAxe
                        : GearArchetype.Axe;

                case Skills.SkillType.Unarmed:
                    return GearArchetype.Fists;

                case Skills.SkillType.Pickaxes:
                    return GearArchetype.Pickaxe;

                case Skills.SkillType.ElementalMagic:
                case Skills.SkillType.BloodMagic:
                    return GearArchetype.Magic;
            }

            if (s.m_itemType == ItemDrop.ItemData.ItemType.Hands)
                return GearArchetype.Fists;

            return GearArchetype.GenericBattle;
        }

        internal static float GetPowerScore(
            ItemDrop.ItemData item,
            int quality,
            GearArchetype archetype)
        {
            if (item == null || item.m_shared == null)
                return 0f;

            ItemDrop.ItemData.SharedData s = item.m_shared;
            int q = Mathf.Max(1, quality);

            if (archetype == GearArchetype.Armor ||
                archetype == GearArchetype.GenericProtection)
            {
                return s.m_armor +
                    Mathf.Max(0, q - 1) * s.m_armorPerLevel;
            }

            if (archetype == GearArchetype.RoundShield ||
                archetype == GearArchetype.TowerShield ||
                archetype == GearArchetype.Buckler)
            {
                return s.m_blockPower +
                    Mathf.Max(0, q - 1) * s.m_blockPowerPerLevel;
            }

            HitData.DamageTypes d = item.GetDamage(q, 0f);

            float score =
                d.m_damage +
                d.m_blunt +
                d.m_slash +
                d.m_pierce +
                d.m_fire +
                d.m_frost +
                d.m_lightning +
                d.m_poison * 0.5f;

            if (archetype == GearArchetype.Pickaxe)
                score += d.m_pickaxe;

            return score;
        }

        internal static int GetRequiredTier(
            ItemDrop.ItemData item,
            Recipe recipe,
            int targetQuality,
            int nativeTier,
            IdolFamily family)
        {
            nativeTier = Mathf.Clamp(nativeTier, 0, MaxTier);

            int naturalMax =
                recipe != null &&
                recipe.m_item != null &&
                recipe.m_item.m_itemData != null &&
                recipe.m_item.m_itemData.m_shared != null
                    ? Mathf.Max(
                        1,
                        recipe.m_item.m_itemData.m_shared.m_maxQuality)
                    : Mathf.Max(1, item.m_shared.m_maxQuality);

            int extraTarget =
                Mathf.Max(
                    0,
                    targetQuality - naturalMax);

            // The item's native idol remains enough for its first three
            // refinements beyond the normal cap. Every three further quality
            // levels raises the minimum idol by one biome.
            //
            // Example for a Q4 Iron item:
            // Q5-Q7 -> Iron, Q8-Q10 -> Silver, Q11-Q13 -> Black Metal,
            // Q14-Q16 -> Black Marble, Q17-Q19 -> Flametal.
            int tierGain =
                extraTarget <= 0
                    ? 0
                    : (extraTarget - 1) / 3;

            return Mathf.Clamp(
                nativeTier + tierGain,
                nativeTier,
                MaxTier);
        }

        internal static float GetContinuousTier(
            ItemDrop.ItemData item,
            Recipe recipe,
            int quality,
            int nativeTier,
            IdolFamily family)
        {
            nativeTier =
                Mathf.Clamp(
                    nativeTier,
                    0,
                    MaxTier);

            int naturalMax =
                recipe != null &&
                recipe.m_item != null &&
                recipe.m_item.m_itemData != null &&
                recipe.m_item.m_itemData.m_shared != null
                    ? Mathf.Max(
                        1,
                        recipe.m_item.m_itemData.m_shared.m_maxQuality)
                    : Mathf.Max(
                        1,
                        item.m_shared.m_maxQuality);

            quality = Mathf.Max(1, quality);

            // Trophy favor follows where the item actually sits in its
            // progression, not a raw-damage comparison against Q1 gear from
            // the next biome. A fully upgraded item from the previous biome
            // is still previous-biome gear, and defeating later foes with it
            // should remain impressive.
            //
            // Normal qualities smoothly approach the item's native biome.
            // Beyond the natural cap, each three gained quality levels move
            // the reference by one full biome, matching the refinement pace.
            float continuous;

            if (quality <= naturalMax)
            {
                if (naturalMax <= 1)
                {
                    continuous = nativeTier;
                }
                else
                {
                    float normalProgress =
                        Mathf.InverseLerp(
                            1f,
                            naturalMax,
                            quality);

                    continuous =
                        nativeTier -
                        0.5f +
                        normalProgress * 0.5f;
                }
            }
            else
            {
                int extra =
                    quality - naturalMax;

                continuous =
                    nativeTier +
                    extra / 3f;
            }

            return Mathf.Clamp(
                continuous,
                Mathf.Max(0f, nativeTier - 0.5f),
                MaxTier);
        }

        internal static float GetItemReferencePrestige(
            float continuousTier)
        {
            float t = Mathf.Clamp(
                continuousTier,
                0f,
                MaxTier);

            int lo = Mathf.FloorToInt(t);
            int hi = Mathf.Min(MaxTier, lo + 1);

            if (lo == hi)
                return TierPrestige[lo];

            return Mathf.Lerp(
                TierPrestige[lo],
                TierPrestige[hi],
                t - lo);
        }

        internal static float GetTrophyPrestige(
            ItemDrop.ItemData trophy)
        {
            if (trophy == null || trophy.m_shared == null)
                return 0f;

            if (trophy.m_dropPrefab != null)
            {
                float vanillaPrestige;

                if (VanillaTrophyPrestige.TryGetValue(
                    trophy.m_dropPrefab.name,
                    out vanillaPrestige))
                {
                    return vanillaPrestige;
                }
            }

            string source = "";

            if (trophy.m_dropPrefab != null)
                source += trophy.m_dropPrefab.name;

            source += "|" + trophy.m_shared.m_name;

            string normalized =
                source
                    .Replace("_", "")
                    .Replace("-", "")
                    .Replace(" ", "")
                    .Replace("$", "")
                    .ToLowerInvariant();

            for (int i = 0;
                 i < TrophyRules.Length;
                 i++)
            {
                TrophyRule rule = TrophyRules[i];

                for (int p = 0;
                     p < rule.Patterns.Length;
                     p++)
                {
                    if (normalized.Contains(
                        rule.Patterns[p]))
                    {
                        return rule.Prestige;
                    }
                }
            }

            // Unknown/modded trophies still function as a middling offering.
            return 40f;
        }

        internal static float GetTrophyBadOutcomeMultiplier(
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            if (trophy == null)
                return 1f;

            float delta =
                GetTrophyPrestige(trophy) -
                itemReferencePrestige;

            // A trophy does not need to outclass the item to matter.
            // Bringing down a dangerous foe from the item's own biome is
            // already a worthy deed; later-biome trophies scale much harder,
            // while obsolete trophies fade toward negligible value.
            float multiplier =
                Mathf.Exp(-0.11f * (delta + 9f));

            return Mathf.Clamp(
                multiplier,
                0.03f,
                0.98f);
        }

        internal static string GetFavorDescription(
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            if (trophy == null)
                return "No trophy is offered.";

            float m =
                GetTrophyBadOutcomeMultiplier(
                    trophy,
                    itemReferencePrestige);

            if (m >= 0.90f)
                return "Meagre offering.";

            if (m >= 0.70f)
                return "Modest offering.";

            if (m >= 0.45f)
                return "Worthy offering.";

            if (m >= 0.22f)
                return "Strong offering.";

            if (m >= 0.08f)
                return "Great offering.";

            return "The gods are greatly pleased.";
        }

        internal static string LocalizeItemName(
            ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
                return "";

            return Localization.instance != null
                ? Localization.instance.Localize(
                    item.m_shared.m_name)
                : item.m_shared.m_name;
        }

        internal static string LocalizeDropName(
            ItemDrop drop)
        {
            return drop != null
                ? LocalizeItemName(drop.m_itemData)
                : "";
        }
    }
}
