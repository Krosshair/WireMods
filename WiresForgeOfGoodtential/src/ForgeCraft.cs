using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    internal static class ForgeContext
    {
        internal static bool Active;
        internal static Player Player;
        internal static ItemDrop.ItemData OriginalItem;
        internal static ItemDrop.ItemData UpgraderResource;
        internal static Recipe Recipe;
        internal static Piece.Requirement[] OriginalResources;
        internal static bool OriginalRequireOnlyOneIngredient;
        internal static Vector2i OriginalPosition;
        internal static string CraftedPrefabName;
        internal static string SharedItemName;
        internal static int NaturalMax;
        internal static int OriginalQuality;
        internal static int AttemptedQuality;
        internal static int TargetQuality;
        internal static int Delta;
        internal static ForgeOutcome Outcome;
        internal static ForgeOdds Odds;
        internal static ItemDrop.ItemData OfferedTrophy;
        internal static bool ForceConsumeIdol;
        internal static float IdolKeepChance;
        internal static bool IdolSpared;
        internal static float OldUpgradeChance;
        internal static float OldBreakChance;
        internal static CraftingStation Station;
        internal static EffectList OldFailEffects;
        internal static bool AttemptExecuted;

        internal static void Restore()
        {
            if (UpgraderResource != null &&
                UpgraderResource.m_shared != null)
            {
                UpgraderResource.m_shared.m_upgradeChance =
                    OldUpgradeChance;

                UpgraderResource.m_shared.m_breakChance =
                    OldBreakChance;
            }

            if (Recipe != null &&
                OriginalResources != null)
            {
                Recipe.m_resources =
                    OriginalResources;

                Recipe.m_requireOnlyOneIngredient =
                    OriginalRequireOnlyOneIngredient;
            }

            if (Station != null &&
                OldFailEffects != null)
            {
                Station.m_craftItemDoneFailEffects =
                    OldFailEffects;
            }

            ClearFields();
        }

        internal static void ClearFields()
        {
            Active = false;
            Player = null;
            OriginalItem = null;
            UpgraderResource = null;
            Recipe = null;
            OriginalResources = null;
            OriginalRequireOnlyOneIngredient = false;
            CraftedPrefabName = null;
            SharedItemName = null;
            NaturalMax = 0;
            OriginalQuality = 0;
            AttemptedQuality = 0;
            TargetQuality = 0;
            Delta = 0;
            Outcome = ForgeOutcome.None;
            Odds = null;
            OfferedTrophy = null;
            ForceConsumeIdol = false;
            IdolKeepChance = 0f;
            IdolSpared = false;
            Station = null;
            OldFailEffects = null;
            AttemptExecuted = false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class ForgeCraftPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Recipe ___m_craftRecipe,
            ItemDrop.ItemData ___m_craftUpgradeItem)
        {
            ForgeContext.Restore();

            Player player =
                Player.m_localPlayer;

            if (player == null ||
                ___m_craftRecipe == null ||
                ___m_craftUpgradeItem == null)
            {
                return;
            }

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station == null ||
                !station.m_upgrader)
            {
                return;
            }

            ForgeSelectionSnapshot selection =
                ForgeSelection.GetPending(
                    ___m_craftRecipe,
                    ___m_craftUpgradeItem);

            if (selection == null ||
                selection.IdolTier < 0 ||
                !ForgeSelection.PlayerHasIdol(
                    player,
                    selection.Family,
                    selection.IdolTier))
                return;

            ItemDrop selectedIdol =
                ForgeData.GetIdolDrop(
                    selection.Family,
                    selection.IdolTier);

            if (selectedIdol == null ||
                selectedIdol.m_itemData == null ||
                selectedIdol.m_itemData.m_shared == null)
            {
                return;
            }

            ItemDrop.ItemData selectedTrophy =
                selection.Trophy;

            // An under-tier idol never sacrifices a trophy, even
            // if a stale selection was locked externally. Do not
            // abort the Goodtential interception and fall back to
            // vanilla crafting; simply discard the invalid offering.
            if (selection.IdolTier < selection.RequiredTier)
                selectedTrophy = null;

            // Boss trophies can't be offered: drop a stale or injected
            // one and carry on with the refinement without it.
            if (ForgeTrophyEligibility.IsBossTrophy(selectedTrophy))
                selectedTrophy = null;

            Piece.Requirement[] originalResources =
                ___m_craftRecipe.m_resources;

            Piece.Requirement[] attemptResources =
                BuildAttemptRequirements(
                    originalResources,
                    selectedIdol,
                    selectedTrophy);

            if (attemptResources == null)
                return;

            int naturalMax =
                Mathf.Max(
                    1,
                    ___m_craftRecipe.m_item
                        .m_itemData.m_shared.m_maxQuality);

            int originalQuality =
                ___m_craftUpgradeItem.m_quality;

            int potential =
                Mathf.Max(
                    0,
                    originalQuality - naturalMax);

            ForgeOdds odds =
                ForgeRules.GetOdds(
                    potential,
                    selection.RequiredTier,
                    selection.IdolTier,
                    selectedTrophy,
                    selection.ItemReferencePrestige);

            ForgeOutcome outcome =
                ForgeRules.RollOutcome(odds);

            int targetQuality =
                originalQuality;

            int delta = 0;

            if (outcome == ForgeOutcome.Success)
            {
                int gain =
                    ForgeRules.RollSuccessGain(
                        odds.JackpotChance);

                targetQuality =
                    originalQuality + gain;

                delta = gain;

                if (gain > 1)
                    outcome = ForgeOutcome.Jackpot;
            }
            else if (outcome == ForgeOutcome.Ignore)
            {
                targetQuality =
                    originalQuality;
            }
            else if (outcome == ForgeOutcome.Loss)
            {
                int lossLevels =
                    ForgeRules.RollLossLevels(
                        potential,
                        odds.CascadeChance);

                targetQuality =
                    Mathf.Max(
                        naturalMax,
                        originalQuality - lossLevels);

                delta =
                    targetQuality -
                    originalQuality;
            }
            else if (outcome ==
                     ForgeOutcome.CompleteLoss)
            {
                targetQuality =
                    originalQuality < naturalMax
                        ? 1
                        : naturalMax;

                delta =
                    targetQuality -
                    originalQuality;
            }

            ForgeContext.Active = true;
            ForgeContext.Player = player;
            ForgeContext.OriginalItem =
                ___m_craftUpgradeItem;
            ForgeContext.UpgraderResource =
                selectedIdol.m_itemData;
            ForgeContext.Recipe =
                ___m_craftRecipe;
            ForgeContext.OriginalResources =
                originalResources;
            ForgeContext.OriginalRequireOnlyOneIngredient =
                ___m_craftRecipe.m_requireOnlyOneIngredient;
            ForgeContext.OriginalPosition =
                ___m_craftUpgradeItem.m_gridPos;
            ForgeContext.CraftedPrefabName =
                ___m_craftRecipe.m_item
                    .gameObject.name;
            ForgeContext.SharedItemName =
                ___m_craftUpgradeItem
                    .m_shared.m_name;
            ForgeContext.NaturalMax =
                naturalMax;
            ForgeContext.OriginalQuality =
                originalQuality;
            ForgeContext.AttemptedQuality =
                originalQuality + 1;
            ForgeContext.TargetQuality =
                targetQuality;
            ForgeContext.Delta =
                delta;
            ForgeContext.Outcome =
                outcome;
            ForgeContext.Odds =
                odds;
            ForgeContext.OfferedTrophy =
                selectedTrophy;
            ForgeContext.ForceConsumeIdol =
                outcome == ForgeOutcome.CompleteLoss &&
                originalQuality < naturalMax;
            ForgeContext.IdolKeepChance =
                ForgeRules.GetIdolKeepChance(
                    outcome,
                    selection.IdolTier,
                    selection.RequiredTier,
                    selectedTrophy,
                    selection.ItemReferencePrestige);
            ForgeContext.OldUpgradeChance =
                selectedIdol.m_itemData
                    .m_shared.m_upgradeChance;
            ForgeContext.OldBreakChance =
                selectedIdol.m_itemData
                    .m_shared.m_breakChance;
            ForgeContext.Station =
                station;

            // Swap in exactly the chosen catalyst and optional offering
            // for the duration of this one synchronous vanilla craft.
            ___m_craftRecipe.m_resources =
                attemptResources;

            // The chosen idol and optional trophy are one combined
            // offering, never alternate OR ingredients.
            ___m_craftRecipe.m_requireOnlyOneIngredient =
                false;

            if (outcome == ForgeOutcome.Success ||
                outcome == ForgeOutcome.Jackpot)
            {
                selectedIdol.m_itemData
                    .m_shared.m_upgradeChance = 2f;

                selectedIdol.m_itemData
                    .m_shared.m_breakChance = -1f;
            }
            else if (outcome == ForgeOutcome.Destroyed)
            {
                // Use Valheim's real destructive upgrader branch,
                // including its normal item-loss/material-return logic.
                selectedIdol.m_itemData
                    .m_shared.m_upgradeChance = -1f;

                selectedIdol.m_itemData
                    .m_shared.m_breakChance = 2f;
            }
            else
            {
                ForgeContext.OldFailEffects =
                    station.m_craftItemDoneFailEffects;

                station.m_craftItemDoneFailEffects =
                    new EffectList();

                selectedIdol.m_itemData
                    .m_shared.m_upgradeChance = -1f;

                selectedIdol.m_itemData
                    .m_shared.m_breakChance = -1f;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            EffectList ___m_craftBonusEffect)
        {
            if (!ForgeContext.Active)
                return;

            bool executed = false;

            try
            {
                Player player =
                    ForgeContext.Player;

                if (player == null ||
                    ForgeContext.OriginalItem == null)
                {
                    return;
                }

                executed =
                    !player.GetInventory()
                        .ContainsItem(
                            ForgeContext.OriginalItem);

                ForgeContext.AttemptExecuted =
                    executed;

                if (!executed)
                    return;

                if (ForgeContext.Outcome ==
                        ForgeOutcome.Ignore ||
                    ForgeContext.Outcome ==
                        ForgeOutcome.Loss ||
                    ForgeContext.Outcome ==
                        ForgeOutcome.CompleteLoss)
                {
                    PlaySafeFailureEffect(player);
                }

                if (ForgeContext.Outcome ==
                        ForgeOutcome.Jackpot &&
                    ___m_craftBonusEffect != null)
                {
                    ___m_craftBonusEffect.Create(
                        player.transform.position +
                            Vector3.up,
                        Quaternion.identity);
                }

                // Separate, gentle reward cue for a spared idol.
                // The jackpot effect above remains unchanged.
                if (ForgeContext.IdolSpared)
                    ForgeIdolSound.Play(player);

                // A forced loss (the below-cap reset) isn't bad luck.
                if (!ForgeContext.ForceConsumeIdol)
                    IdolLuck.Record(ForgeContext.IdolSpared);

                ShowResult(player);
            }
            finally
            {
                // Only completed crafts count. Always restore the
                // transient recipe/upgrade settings, even if persistence
                // encounters an unexpected save-data exception.
                try
                {
                    if (executed &&
                        ForgeContext.OfferedTrophy != null)
                    {
                        TrophyFatigue.RecordUse(
                            ForgeContext.Player,
                            ForgeContext.OfferedTrophy);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning(
                        "Goodtential: trophy fatigue save failed: " +
                        ex.Message);
                }
                finally
                {
                    ForgeContext.Restore();

                    if (executed)
                        ForgeSelection.AfterAttempt();
                    else
                        ForgeSelection.CancelPending();
                }
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception)
        {
            if (__exception != null &&
                ForgeContext.Active)
            {
                ForgeContext.Restore();
                ForgeSelection.CancelPending();
            }

            return __exception;
        }

        private static Piece.Requirement[]
            BuildAttemptRequirements(
                Piece.Requirement[] original,
                ItemDrop selectedIdol,
                ItemDrop.ItemData trophy)
        {
            if (original == null)
                return null;

            List<Piece.Requirement> result =
                new List<Piece.Requirement>();

            bool replacedIdol = false;

            for (int i = 0;
                 i < original.Length;
                 i++)
            {
                Piece.Requirement req =
                    original[i];

                if (req == null)
                    continue;

                int tier;
                IdolFamily family;

                if (!replacedIdol &&
                    req.m_upgraderResource &&
                    req.m_resItem != null &&
                    ForgeData.TryParseIdol(
                        req.m_resItem,
                        out tier,
                        out family))
                {
                    result.Add(
                        CloneRequirement(
                            req,
                            selectedIdol));

                    replacedIdol = true;
                }
                else
                {
                    result.Add(req);
                }
            }

            if (!replacedIdol)
                return null;

            if (trophy != null)
            {
                ItemDrop trophyDrop =
                    ResolveDrop(trophy);

                if (trophyDrop == null)
                    return null;

                Piece.Requirement offering =
                    new Piece.Requirement();

                offering.m_resItem =
                    trophyDrop;
                offering.m_amount = 1;
                offering.m_amountPerLevel = 0;
                offering.m_upgraderResource = true;
                offering.m_recover = false;

                result.Add(offering);
            }

            return result.ToArray();
        }

        private static Piece.Requirement
            CloneRequirement(
                Piece.Requirement original,
                ItemDrop replacement)
        {
            Piece.Requirement copy =
                new Piece.Requirement();

            copy.m_resItem =
                replacement;
            copy.m_amount =
                original.m_amount;
            copy.m_extraAmountOnlyOneIngredient =
                original.m_extraAmountOnlyOneIngredient;
            copy.m_amountPerLevel =
                original.m_amountPerLevel;
            copy.m_upgraderResource =
                original.m_upgraderResource;
            copy.m_recover =
                original.m_recover;

            return copy;
        }

        private static ItemDrop ResolveDrop(
            ItemDrop.ItemData item)
        {
            if (item == null)
                return null;

            if (item.m_dropPrefab != null)
            {
                ItemDrop fromPrefab =
                    item.m_dropPrefab
                        .GetComponent<ItemDrop>();

                if (fromPrefab != null)
                    return fromPrefab;
            }

            if (ObjectDB.instance != null &&
                item.m_shared != null)
            {
                GameObject prefab =
                    ObjectDB.instance
                        .GetItemPrefab(
                            item.m_shared);

                if (prefab != null)
                    return prefab
                        .GetComponent<ItemDrop>();
            }

            return null;
        }

        private static void PlaySafeFailureEffect(
            Player player)
        {
            if (player == null ||
                ForgeContext.OldFailEffects == null)
            {
                return;
            }

            GameObject[] spawned =
                ForgeContext.OldFailEffects.Create(
                    player.transform.position,
                    Quaternion.identity);

            for (int i = 0;
                 i < spawned.Length;
                 i++)
            {
                GameObject root =
                    spawned[i];

                if (root == null)
                    continue;

                MonoBehaviour[] behaviours =
                    root.GetComponentsInChildren<
                        MonoBehaviour>(true);

                for (int j = 0;
                     j < behaviours.Length;
                     j++)
                {
                    MonoBehaviour behaviour =
                        behaviours[j];

                    if (behaviour is IProjectile)
                    {
                        behaviour.enabled = false;
                        UnityEngine.Object.Destroy(
                            behaviour);
                    }
                }
            }
        }

        private static void ShowResult(
            Player player)
        {
            string itemName =
                Localization.instance != null
                    ? Localization.instance.Localize(
                        ForgeContext.SharedItemName)
                    : ForgeContext.SharedItemName;

            switch (ForgeContext.Outcome)
            {
                case ForgeOutcome.Jackpot:
                    player.Message(
                        MessageHud.MessageType.Center,
                        "Exceptional refinement! " +
                        itemName +
                        " reached quality " +
                        ForgeContext.TargetQuality +
                        ".",
                        0,
                        null,
                        true);
                    break;

                case ForgeOutcome.Ignore:
                    player.Message(
                        MessageHud.MessageType.Center,
                        "Refinement failed. " +
                        itemName +
                        " remains at quality " +
                        ForgeContext.OriginalQuality +
                        ".",
                        0,
                        null,
                        true);
                    break;

                case ForgeOutcome.Loss:
                    player.Message(
                        MessageHud.MessageType.Center,
                        "Refinement failed. " +
                        itemName +
                        " was reduced to quality " +
                        ForgeContext.TargetQuality +
                        ".",
                        0,
                        null,
                        true);
                    break;

                case ForgeOutcome.Destroyed:
                    player.Message(
                        MessageHud.MessageType.Center,
                        "Refinement failed. " +
                        itemName + " was destroyed.",
                        0, null, true);
                    break;

                case ForgeOutcome.CompleteLoss:
                    player.Message(
                        MessageHud.MessageType.Center,
                        "Refinement failed. " +
                        itemName +
                        " was reduced back to its base form at quality " +
                        ForgeContext.TargetQuality +
                        ".",
                        0,
                        null,
                        true);
                    break;
            }
        }
    }

    [HarmonyPatch(
        typeof(Inventory),
        nameof(Inventory.AddItem),
        new Type[]
        {
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(long),
            typeof(string),
            typeof(Vector2i),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    internal static class ForgeReturnedItemQualityPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            string name,
            ref int quality,
            Vector2i position)
        {
            if (!ForgeContext.Active ||
                name !=
                    ForgeContext.CraftedPrefabName ||
                position.x !=
                    ForgeContext.OriginalPosition.x ||
                position.y !=
                    ForgeContext.OriginalPosition.y)
            {
                return;
            }

            quality =
                ForgeContext.TargetQuality;
        }
    }

    [HarmonyPatch(
        typeof(Player),
        nameof(Player.Message))]
    internal static class ForgeVanillaMessagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            MessageHud.MessageType type,
            string msg)
        {
            if (!ForgeContext.Active ||
                type !=
                    MessageHud.MessageType.Center ||
                Localization.instance == null)
            {
                return true;
            }

            string success =
                Localization.instance.Localize(
                    "$msg_upgrader_success",
                    ForgeContext.SharedItemName,
                    ForgeContext.AttemptedQuality
                        .ToString());

            string failed =
                Localization.instance.Localize(
                    "$msg_upgrader_failed",
                    ForgeContext.SharedItemName,
                    (ForgeContext.AttemptedQuality - 2)
                        .ToString());

            string broke =
                Localization.instance.Localize(
                    "$msg_upgrader_broke",
                    ForgeContext.SharedItemName,
                    (ForgeContext.AttemptedQuality - 1)
                        .ToString());

            if (msg == success)
            {
                return ForgeContext.Outcome ==
                    ForgeOutcome.Success;
            }

            if (msg == failed)
                return false;

            if (msg == broke)
                return false;

            return true;
        }
    }
}
