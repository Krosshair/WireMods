using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    internal sealed class ForgeSelectionSnapshot
    {
        internal Recipe Recipe;
        internal ItemDrop.ItemData Item;
        internal int NativeTier;
        internal int RequiredTier;
        internal IdolFamily Family;
        internal int IdolTier;
        internal ItemDrop.ItemData Trophy;
        internal float ContinuousTier;
        internal float ItemReferencePrestige;
    }

    internal static class ForgeSelection
    {
        internal static Recipe CurrentRecipe;
        internal static ItemDrop.ItemData CurrentItem;
        internal static int NativeTier;
        internal static int RequiredTier;
        internal static IdolFamily Family;
        internal static int SelectedIdolTier;
        internal static ItemDrop.ItemData SelectedTrophy;
        internal static float ContinuousTier;
        internal static float ItemReferencePrestige;
        internal static int Potential;
        // The safe tier shown from 1 (Meadows), uncapped past the last idol.
        internal static int ShownTier;

        internal static ForgeSelectionSnapshot Pending;

        internal static bool Refresh(
            Recipe recipe,
            ItemDrop.ItemData item,
            Player player)
        {
            if (recipe == null ||
                item == null ||
                item.m_shared == null ||
                player == null)
            {
                ResetCurrent();
                return false;
            }

            int nativeTier;
            IdolFamily family;
            Piece.Requirement nativeRequirement;

            if (!ForgeData.TryGetNativeIdol(
                recipe,
                out nativeTier,
                out family,
                out nativeRequirement))
            {
                ResetCurrent();
                return false;
            }

            int targetQuality =
                Mathf.Max(1, item.m_quality + 1);

            int requiredTier =
                ForgeData.GetRequiredTier(
                    item,
                    recipe,
                    targetQuality,
                    nativeTier,
                    family);

            float continuousTier =
                ForgeData.GetContinuousTier(
                    item,
                    recipe,
                    item.m_quality,
                    nativeTier,
                    family);

            bool changed =
                CurrentRecipe != recipe ||
                CurrentItem != item ||
                Family != family ||
                NativeTier != nativeTier ||
                RequiredTier != requiredTier;

            ShownTier = ForgeData.GetPlayTier(item, recipe, targetQuality, nativeTier) + 1;

            Potential =
                Mathf.Max(
                    0,
                    item.m_quality -
                    Mathf.Max(1, recipe.m_item.m_itemData.m_shared.m_maxQuality));

            CurrentRecipe = recipe;
            CurrentItem = item;
            Family = family;
            NativeTier = nativeTier;
            RequiredTier = requiredTier;
            ContinuousTier = continuousTier;
            ItemReferencePrestige =
                ForgeData.GetItemReferencePrestige(
                    continuousTier);

            if (changed)
            {
                SelectedTrophy = null;
                SelectedIdolTier =
                    FindDefaultIdol(player);
            }
            else
            {
                ValidateIdol(player);
                ValidateTrophy(player);
            }

            return true;
        }

        internal static void ValidateIdol(Player player)
        {
            if (player == null)
                return;

            if (SelectedIdolTier < 0 ||
                SelectedIdolTier > ForgeData.MaxTier ||
                !PlayerHasIdol(
                    player,
                    Family,
                    SelectedIdolTier))
            {
                SelectedIdolTier =
                    FindDefaultIdol(player);
            }
        }

        // The weakest owned idol that can't actually cost the item a level
        // (the worst case is "nothing happens"). If every idol risks more
        // than that, the one with the best odds.
        internal static int FindDefaultIdol(Player player)
        {
            List<int> owned = GetOwnedValidIdolTiers(player);
            if (owned.Count == 0)
                return -1;

            foreach (int tier in owned)
            {
                ForgeOdds odds = ForgeRules.GetOdds(
                    Potential, RequiredTier, tier, null, ItemReferencePrestige);
                if (odds.FailureIsIgnore && odds.CompleteLoss <= 0f && odds.Destruction <= 0f)
                    return tier;
            }

            return owned[owned.Count - 1];
        }

        internal static void ValidateTrophy(Player player)
        {
            if (SelectedIdolTier < RequiredTier)
            {
                SelectedTrophy = null;
                return;
            }

            if (SelectedTrophy == null)
                return;

            if (ForgeTrophyEligibility.IsBossTrophy(SelectedTrophy))
            {
                SelectedTrophy = null;
                return;
            }

            if (player == null)
                return;

            Inventory inventory =
                player.GetInventory();

            if (inventory == null)
            {
                SelectedTrophy = null;
                return;
            }

            string sharedName =
                SelectedTrophy.m_shared != null
                    ? SelectedTrophy.m_shared.m_name
                    : null;

            if (string.IsNullOrEmpty(sharedName) ||
                inventory.CountItems(sharedName) <= 0)
            {
                SelectedTrophy = null;
            }
        }

        internal static bool PlayerHasIdol(
            Player player,
            IdolFamily family,
            int tier)
        {
            if (player == null ||
                tier < 0 ||
                tier > ForgeData.MaxTier)
                return false;

            ItemDrop idol =
                ForgeData.GetIdolDrop(
                    family,
                    tier);

            if (idol == null ||
                idol.m_itemData == null ||
                idol.m_itemData.m_shared == null)
            {
                return false;
            }

            return player.GetInventory().CountItems(
                idol.m_itemData.m_shared.m_name) > 0;
        }

        internal static int FindHighestOwnedIdol(
            Player player,
            IdolFamily family,
            int requiredTier)
        {
            for (int tier = ForgeData.MaxTier;
                 tier >= 0;
                 tier--)
            {
                if (PlayerHasIdol(
                    player,
                    family,
                    tier))
                {
                    return tier;
                }
            }

            // -1 explicitly means no compatible idol in inventory.
            // Never imply the player owns a missing safe-tier idol.
            return -1;
        }

        internal static List<int> GetOwnedValidIdolTiers(
            Player player)
        {
            List<int> tiers =
                new List<int>();

            if (player == null)
                return tiers;

            for (int tier = 0;
                 tier <= ForgeData.MaxTier;
                 tier++)
            {
                if (PlayerHasIdol(
                    player,
                    Family,
                    tier))
                {
                    tiers.Add(tier);
                }
            }

            return tiers;
        }

        internal static List<ItemDrop.ItemData> GetUniqueTrophies(
            Player player)
        {
            List<ItemDrop.ItemData> result =
                new List<ItemDrop.ItemData>();

            if (player == null ||
                player.GetInventory() == null)
            {
                return result;
            }

            List<ItemDrop.ItemData> items =
                player.GetInventory()
                    .GetAllItemsOfType(
                        ItemDrop.ItemData.ItemType.Trophy,
                        true);

            HashSet<string> seen =
                new HashSet<string>(
                    StringComparer.Ordinal);

            for (int i = 0;
                 i < items.Count;
                 i++)
            {
                ItemDrop.ItemData trophy =
                    items[i];

                if (trophy == null ||
                    trophy.m_shared == null ||
                    trophy.m_stack <= 0 ||
                    ForgeTrophyEligibility.IsBossTrophy(trophy))
                {
                    continue;
                }

                string key =
                    trophy.m_shared.m_name;

                if (seen.Add(key))
                    result.Add(trophy);
            }

            result.Sort(
                delegate(
                    ItemDrop.ItemData a,
                    ItemDrop.ItemData b)
                {
                    int prestige =
                        ForgeData
                            .GetTrophyPrestige(a)
                            .CompareTo(
                                ForgeData.GetTrophyPrestige(b));

                    if (prestige != 0)
                        return prestige;

                    return string.Compare(
                        ForgeData.LocalizeItemName(a),
                        ForgeData.LocalizeItemName(b),
                        StringComparison.CurrentCultureIgnoreCase);
                });

            return result;
        }

        internal static bool HasRequiredIdol(
            Player player)
        {
            return PlayerHasIdol(
                player,
                Family,
                SelectedIdolTier);
        }

        internal static bool HasSelectedTrophy(
            Player player)
        {
            if (SelectedTrophy == null)
                return true;

            if (SelectedIdolTier < RequiredTier)
                return false;

            if (player == null ||
                SelectedTrophy.m_shared == null ||
                ForgeTrophyEligibility.IsBossTrophy(SelectedTrophy))
            {
                return false;
            }

            return player.GetInventory().CountItems(
                SelectedTrophy.m_shared.m_name) > 0;
        }

        internal static bool LockForCraft(
            Player player)
        {
            if (CurrentRecipe == null ||
                CurrentItem == null ||
                player == null)
            {
                Pending = null;
                return false;
            }

            ValidateIdol(player);
            ValidateTrophy(player);

            bool noCost =
                player.NoCostCheat() ||
                (ZoneSystem.instance != null &&
                 ZoneSystem.instance.GetGlobalKey(
                     GlobalKeys.NoCraftCost));

            if (!noCost &&
                (!HasRequiredIdol(player) ||
                 !HasSelectedTrophy(player)))
            {
                Pending = null;
                return false;
            }

            Pending =
                new ForgeSelectionSnapshot
                {
                    Recipe = CurrentRecipe,
                    Item = CurrentItem,
                    NativeTier = NativeTier,
                    RequiredTier = RequiredTier,
                    Family = Family,
                    IdolTier = SelectedIdolTier,
                    Trophy = SelectedTrophy,
                    ContinuousTier = ContinuousTier,
                    ItemReferencePrestige =
                        ItemReferencePrestige
                };

            return true;
        }

        internal static ForgeSelectionSnapshot GetPending(
            Recipe recipe,
            ItemDrop.ItemData item)
        {
            if (Pending != null &&
                Pending.Recipe == recipe &&
                Pending.Item == item)
            {
                return Pending;
            }

            if (CurrentRecipe == recipe &&
                CurrentItem == item)
            {
                return new ForgeSelectionSnapshot
                {
                    Recipe = CurrentRecipe,
                    Item = CurrentItem,
                    NativeTier = NativeTier,
                    RequiredTier = RequiredTier,
                    Family = Family,
                    IdolTier = SelectedIdolTier,
                    Trophy = SelectedTrophy,
                    ContinuousTier = ContinuousTier,
                    ItemReferencePrestige =
                        ItemReferencePrestige
                };
            }

            return null;
        }

        internal static void AfterAttempt()
        {
            // Trophy offerings are deliberately opt-in for every single roll.
            SelectedTrophy = null;
            Pending = null;
        }

        internal static void CancelPending()
        {
            Pending = null;
        }

        internal static void ResetCurrent()
        {
            CurrentRecipe = null;
            CurrentItem = null;
            ShownTier = 0;
            NativeTier = 0;
            RequiredTier = 0;
            Family = IdolFamily.Battle;
            SelectedIdolTier = 0;
            SelectedTrophy = null;
            ContinuousTier = 0f;
            ItemReferencePrestige = 0f;
        }

        internal static void ResetAll()
        {
            ResetCurrent();
            Pending = null;
        }

    }
}
