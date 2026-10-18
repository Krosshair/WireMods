using System;
using System.Collections.Generic;

namespace WiresAldr
{
    // One resource can have different *location-specific* bonuses.
    // Biome is determined where the Pickable exists, not by inventory.
    internal enum WildResource
    {
        None, Raspberry, Mushroom, Dandelion, Blueberry, Thistle,
        CarrotSeeds, TurnipSeeds, YellowMushroom, Cloudberry,
        JotunPuffs, Magecap, Fiddlehead, Smokepuff, Lingonberry,
        OnionSeeds, Barley, Flax
    }

    internal static class ForageRules
    {
        internal static WildResource FromPrefab(string name)
        {
            if (String.IsNullOrEmpty(name)) return WildResource.None;
            int clone = name.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (clone >= 0) name = name.Substring(0, clone);
            string n = name.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
            switch (n)
            {
                case "raspberry": return WildResource.Raspberry;
                case "mushroom": case "mushroomred": return WildResource.Mushroom;
                case "dandelion": return WildResource.Dandelion;
                case "blueberries": case "blueberry": return WildResource.Blueberry;
                case "thistle": return WildResource.Thistle;
                case "carrotseeds": return WildResource.CarrotSeeds;
                case "turnipseeds": return WildResource.TurnipSeeds;
                case "mushroomyellow": return WildResource.YellowMushroom;
                case "cloudberry": case "cloudberries": return WildResource.Cloudberry;
                case "mushroomjotunpuffs": case "jotunpuffs": return WildResource.JotunPuffs;
                case "mushroommagecap": case "magecap": return WildResource.Magecap;
                case "fiddlehead": return WildResource.Fiddlehead;
                case "smokepuff": return WildResource.Smokepuff;
                case "lingonberry": case "lingonberries": return WildResource.Lingonberry;
                case "onionseeds": return WildResource.OnionSeeds;
                case "barley": return WildResource.Barley;
                case "flax": return WildResource.Flax;
                default: return WildResource.None;
            }
        }

        // Prefab name first, then the item's vanilla shared name, so a
        // resource is recognized even if its prefab name differs.
        internal static WildResource FromItem(UnityEngine.GameObject itemPrefab)
        {
            if (itemPrefab == null) return WildResource.None;
            WildResource r = FromPrefab(itemPrefab.name);
            if (r != WildResource.None) return r;
            ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
            string token = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_name : null;
            if (string.IsNullOrEmpty(token)) return WildResource.None;
            foreach (WildResource candidate in (WildResource[])Enum.GetValues(typeof(WildResource)))
                if (candidate != WildResource.None && Token(candidate) == token) return candidate;
            return WildResource.None;
        }

        // Read-only, shared: never modify the returned array.
        private static readonly WildResource[][] BiomeResources =
        {
            new[] { WildResource.Raspberry, WildResource.Mushroom, WildResource.Dandelion },
            new[] { WildResource.Mushroom, WildResource.Blueberry,
                WildResource.Thistle, WildResource.CarrotSeeds, WildResource.YellowMushroom },
            new[] { WildResource.Thistle, WildResource.TurnipSeeds },
            new[] { WildResource.OnionSeeds },
            new[] { WildResource.Cloudberry, WildResource.Barley, WildResource.Flax },
            new[] { WildResource.JotunPuffs, WildResource.Magecap },
            new[] { WildResource.Fiddlehead, WildResource.Smokepuff },
            new[] { WildResource.Lingonberry }
        };
        internal static WildResource[] ResourcesInBiome(int tier) =>
            tier >= 0 && tier < BiomeResources.Length ? BiomeResources[tier] : Array.Empty<WildResource>();
        internal static string Name(WildResource id)
        {
            switch (id)
            {
                case WildResource.Raspberry: return "Raspberries";
                case WildResource.Mushroom: return "Mushrooms";
                case WildResource.Dandelion: return "Dandelions";
                case WildResource.Blueberry: return "Blueberries";
                case WildResource.Thistle: return "Thistle";
                case WildResource.CarrotSeeds: return "Wild carrot seeds";
                case WildResource.TurnipSeeds: return "Wild turnip seeds";
                case WildResource.YellowMushroom: return "Yellow mushrooms";
                case WildResource.Cloudberry: return "Cloudberries";
                case WildResource.JotunPuffs: return "Jotun puffs";
                case WildResource.Magecap: return "Magecaps";
                case WildResource.Fiddlehead: return "Fiddleheads";
                case WildResource.Smokepuff: return "Smokepuffs";
                case WildResource.Lingonberry: return null; // shown by its localized name once discovered
                case WildResource.OnionSeeds: return "Wild onion seeds";
                case WildResource.Barley: return "Wild barley";
                case WildResource.Flax: return "Wild flax";
                default: return "";
            }
        }

        // Vanilla shared item name, used only to check discovery.
        internal static string Token(WildResource id)
        {
            switch (id)
            {
                case WildResource.Raspberry: return "$item_raspberries";
                case WildResource.Mushroom: return "$item_mushroomcommon";
                case WildResource.Dandelion: return "$item_dandelion";
                case WildResource.Blueberry: return "$item_blueberries";
                case WildResource.Thistle: return "$item_thistle";
                case WildResource.CarrotSeeds: return "$item_carrotseeds";
                case WildResource.TurnipSeeds: return "$item_turnipseeds";
                case WildResource.YellowMushroom: return "$item_mushroomyellow";
                case WildResource.Cloudberry: return "$item_cloudberries";
                case WildResource.JotunPuffs: return "$item_jotunpuffs";
                case WildResource.Magecap: return "$item_magecap";
                case WildResource.Fiddlehead: return "$item_fiddleheadfern";
                case WildResource.Smokepuff: return "$item_smokepuff";
                case WildResource.Lingonberry: return "$item_lingonberries";
                case WildResource.OnionSeeds: return "$item_onionseeds";
                case WildResource.Barley: return "$item_barley";
                case WildResource.Flax: return "$item_flax";
                default: return null;
            }
        }

        internal static int HomeTier(WildResource r)
        {
            switch (r)
            {
                case WildResource.Raspberry: case WildResource.Mushroom:
                case WildResource.Dandelion: return 0;
                case WildResource.Blueberry: case WildResource.Thistle:
                case WildResource.CarrotSeeds: case WildResource.YellowMushroom: return 1;
                case WildResource.TurnipSeeds: return 2;
                case WildResource.OnionSeeds: return 3;
                case WildResource.Cloudberry: case WildResource.Barley: case WildResource.Flax: return 4;
                case WildResource.JotunPuffs: case WildResource.Magecap: return 5;
                case WildResource.Fiddlehead: case WildResource.Smokepuff: return 6;
                case WildResource.Lingonberry: return 7;
                default: return -1;
            }
        }

        internal static bool ExistsIn(WildResource r, int biome)
        {
            foreach (WildResource item in ResourcesInBiome(biome))
                if (item == r) return true;
            return false;
        }

        internal static List<BonusBreakdown.Portion> Parts(
            WildResource resource, int sourceBiome, bool[] defeated,
            double ownBase, double laterFraction, double skippedFraction,
            double blackForestMushroomPremium,
            double swampThistleElderPremium,
            double swampThistleBonemassPremium)
        {
            var parts = new List<BonusBreakdown.Portion>();
            int home = HomeTier(resource);
            if (home < 0 || !ExistsIn(resource, sourceBiome) ||
                defeated == null || defeated.Length != ProgressionMath.BiomeCount)
                return parts;

            parts.AddRange(BonusBreakdown.ForBiome(home, defeated,
                ownBase, laterFraction, skippedFraction));

            // More dangerous gathering locations add separate, earned
            // contributions without changing the home resource's rules.
            // E.g. Swamp thistle already improves from Elder, *before*
            // Bonemass, and gains a further premium with each later boss.
            if (resource == WildResource.Thistle && sourceBiome == 2)
            {
                AddPremium(parts, defeated, 1, swampThistleElderPremium);
                for (int i = 2; i < 8; ++i)
                    AddPremium(parts, defeated, i, swampThistleBonemassPremium
                        * Math.Pow(Math.Max(0, Math.Min(1, laterFraction)), i - 2));
            }
            else if (resource == WildResource.Mushroom && sourceBiome == 1)
            {
                for (int i = 1; i < 8; ++i)
                    AddPremium(parts, defeated, i, blackForestMushroomPremium
                        * Math.Pow(Math.Max(0, Math.Min(1, laterFraction)), i - 1));
            }
            return Merge(parts);
        }

        private static void AddPremium(List<BonusBreakdown.Portion> parts,
            bool[] defeated, int tier, double percent)
        {
            if (defeated[tier] && percent > 0)
                parts.Add(new BonusBreakdown.Portion(tier, percent));
        }

        private static List<BonusBreakdown.Portion> Merge(List<BonusBreakdown.Portion> parts)
        {
            parts.Sort((a, b) => a.BossTier.CompareTo(b.BossTier));
            var merged = new List<BonusBreakdown.Portion>();
            foreach (BonusBreakdown.Portion p in parts)
            {
                if (merged.Count > 0 &&
                    merged[merged.Count - 1].BossTier == p.BossTier)
                {
                    int index = merged.Count - 1;
                    merged[index] = new BonusBreakdown.Portion(
                        p.BossTier, merged[index].Percent + p.Percent);
                }
                else merged.Add(p);
            }
            return merged;
        }

        // Extra yield, in percent of the pick's normal amount, from bosses
        // defeated AFTER the resource's own biome boss. Unlike the chance
        // bonus, this does not shrink with distance: the further the world
        // has progressed past a biome, the quicker its resources are to gather.
        internal static List<BonusBreakdown.Portion> VeteranParts(
            WildResource resource, int sourceBiome, bool[] defeated,
            double perLaterBoss, double skippedFraction)
        {
            var parts = new List<BonusBreakdown.Portion>();
            int home = HomeTier(resource);
            if (home < 0 || perLaterBoss <= 0 || !ExistsIn(resource, sourceBiome) ||
                defeated == null || defeated.Length != ProgressionMath.BiomeCount)
                return parts;
            double scale = defeated[home] ? 1 : Math.Max(0, Math.Min(1, skippedFraction));
            for (int i = home + 1; i < defeated.Length; ++i)
                if (defeated[i] && scale > 0)
                    parts.Add(new BonusBreakdown.Portion(i, perLaterBoss * scale));
            return parts;
        }

        internal static double Total(List<BonusBreakdown.Portion> parts)
        {
            double total = 0;
            foreach (BonusBreakdown.Portion p in parts) total += p.Percent;
            return total;
        }
    }
}
