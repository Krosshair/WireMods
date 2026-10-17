using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class MucActionPreflight
    {
        internal static readonly FieldInfo CraftRecipeField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_craftRecipe");

        internal static readonly FieldInfo CraftUpgradeItemField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_craftUpgradeItem");

        internal static readonly FieldInfo CraftVariantField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_craftVariant");

        internal static readonly FieldInfo MultiCraftingField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_multiCrafting");

        internal static readonly FieldInfo MultiCraftAmountField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_multiCraftAmount");

        internal static List<MultiUserChestCompat.Demand>
            RequirementDemands(
                Piece.Requirement[] requirements,
                int quality,
                int multiplier,
                CraftingStation station)
        {
            var demands =
                new List<MultiUserChestCompat.Demand>();

            if (requirements == null)
                return demands;

            var totals =
                new Dictionary<string, int>(
                    StringComparer.Ordinal);

            for (int i = 0; i < requirements.Length; i++)
            {
                Piece.Requirement requirement =
                    requirements[i];

                if (requirement == null ||
                    requirement.m_resItem == null)
                    continue;

                if (station != null &&
                    station.m_upgrader !=
                        requirement.m_upgraderResource)
                    continue;

                if (station == null &&
                    requirement.m_upgraderResource)
                    continue;

                int amount =
                    requirement.GetAmount(quality) *
                    multiplier;

                if (amount <= 0)
                    continue;

                string name =
                    requirement.m_resItem
                        .m_itemData.m_shared.m_name;

                int current;
                totals.TryGetValue(name, out current);
                totals[name] = current + amount;
            }

            foreach (KeyValuePair<string, int> pair
                     in totals)
            {
                demands.Add(
                    new MultiUserChestCompat.Demand
                    {
                        Name = pair.Key,
                        Amount = pair.Value,
                        Quality = -1,
                        MatchWorldLevel = true
                    });
            }

            return demands;
        }

        internal static void Missing(Player player)
        {
            if (player != null)
            {
                player.Message(
                    MessageHud.MessageType.Center,
                    "$msg_missingrequirement");
            }
        }
    }
    [HarmonyPatch(
        typeof(InventoryGui),
        "DoCrafting",
        new[] { typeof(Player) })]
    internal static class MucCraftActionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            InventoryGui __instance,
            Player player,
            MethodBase __originalMethod)
        {
            if (!MultiUserChestCompat.Active ||
                MultiUserChestCompat.Executing)
                return true;

            if (MultiUserChestCompat.Busy)
                return false;

            if (__instance == null ||
                player == null ||
                player.NoCostCheat() ||
                ZoneSystem.instance == null ||
                ZoneSystem.instance.GetGlobalKey(
                    GlobalKeys.NoCraftCost))
                return true;

            Recipe recipe =
                MucActionPreflight.CraftRecipeField
                    .GetValue(__instance)
                    as Recipe;

            if (recipe == null)
                return true;

            ItemDrop.ItemData upgrade =
                MucActionPreflight.CraftUpgradeItemField
                    .GetValue(__instance)
                    as ItemDrop.ItemData;

            int quality =
                upgrade == null
                    ? 1
                    : upgrade.m_quality + 1;

            bool multi =
                (bool)MucActionPreflight
                    .MultiCraftingField
                    .GetValue(__instance);

            int multiplier =
                multi
                    ? (int)MucActionPreflight
                        .MultiCraftAmountField
                        .GetValue(__instance)
                    : 1;

            int variant =
                (int)MucActionPreflight
                    .CraftVariantField
                    .GetValue(__instance);

            CraftingStation station =
                player.GetCurrentCraftingStation();

            RouteContext context;
            if (!RoutingContext.TryGet(out context))
                return true;

            MultiUserChestCompat.PendingAction pending;

            if (recipe.m_requireOnlyOneIngredient)
            {
                int need;
                ItemDrop.ItemData single;

                recipe.GetAmount(
                    quality,
                    out need,
                    out single,
                    multiplier);

                pending =
                    single != null && need > 0
                        ? MultiUserChestCompat
                            .PrepareExactItem(
                                context,
                                single,
                                need)
                        : null;
            }
            else
            {
                List<MultiUserChestCompat.Demand> demands =
                    MucActionPreflight.RequirementDemands(
                        recipe.m_resources,
                        quality,
                        multiplier,
                        station);

                pending =
                    MultiUserChestCompat.Prepare(
                        context,
                        demands);
            }

            if (pending == null)
                return true;

            Func<bool> stillValid =
                delegate
                {
                    if (__instance == null ||
                        player == null ||
                        player != Player.m_localPlayer ||
                        player.IsDead() ||
                        !InventoryGui.IsVisible())
                        return false;

                    if (!ReferenceEquals(
                            MucActionPreflight.CraftRecipeField
                                .GetValue(__instance),
                            recipe))
                        return false;

                    if (MucActionPreflight
                            .CraftUpgradeItemField
                            .GetValue(__instance) != upgrade)
                        return false;

                    if ((int)MucActionPreflight
                            .CraftVariantField
                            .GetValue(__instance) != variant)
                        return false;

                    bool currentMulti =
                        (bool)MucActionPreflight
                            .MultiCraftingField
                            .GetValue(__instance);

                    if (currentMulti != multi)
                        return false;

                    if (multi &&
                        (int)MucActionPreflight
                            .MultiCraftAmountField
                            .GetValue(__instance) != multiplier)
                        return false;

                    if (player.GetCurrentCraftingStation() !=
                        station)
                        return false;

                    return station == null ||
                           station.CheckUsable(
                               player,
                               false);
                };

            Action ready =
                delegate
                {
                    __originalMethod.Invoke(
                        __instance,
                        new object[] { player });
                };

            MultiUserChestCompat.Attach(
                pending,
                stillValid,
                ready,
                delegate
                {
                    MucActionPreflight.Missing(player);
                });

            return false;
        }
    }
    [HarmonyPatch(
        typeof(Player),
        nameof(Player.TryPlacePiece))]
    internal static class MucBuildActionPatch
    {
        private static readonly FieldInfo PlacementGhostField =
            AccessTools.Field(
                typeof(Player),
                "m_placementGhost");

        private static readonly FieldInfo LastToolUseField =
            AccessTools.Field(
                typeof(Player),
                "m_lastToolUseTime");

        private static readonly FieldInfo BuildRemoveDebtField =
            AccessTools.Field(
                typeof(Player),
                "m_buildRemoveDebt");

        private static readonly MethodInfo GetBuildStaminaMethod =
            AccessTools.Method(
                typeof(Player),
                "GetBuildStamina");

        private static readonly MethodInfo GetPlaceDurabilityMethod =
            AccessTools.Method(
                typeof(Player),
                "GetPlaceDurability",
                new[] { typeof(ItemDrop.ItemData) });

        [HarmonyPrefix]
        private static bool Prefix(
            Player __instance,
            Piece piece,
            ref bool __result)
        {
            if (!MultiUserChestCompat.Active ||
                MultiUserChestCompat.Executing)
                return true;

            if (MultiUserChestCompat.Busy)
            {
                __result = false;
                return false;
            }

            if (__instance == null ||
                __instance != Player.m_localPlayer ||
                piece == null ||
                __instance.NoCostCheat() ||
                ZoneSystem.instance == null ||
                ZoneSystem.instance.GetGlobalKey(
                    piece.FreeBuildKey()))
                return true;

            GameObject ghost =
                PlacementGhostField != null
                    ? PlacementGhostField.GetValue(
                        __instance) as GameObject
                    : null;

            if (ghost == null)
                return true;

            ItemDrop.ItemData tool =
                __instance.RightItem;

            if (tool == null ||
                tool.m_shared == null ||
                tool.m_shared.m_buildPieces == null)
                return true;

            Vector3 position =
                ghost.transform.position;

            Quaternion rotation =
                ghost.transform.rotation;

            RouteContext context;

            RoutingContext.EnterBuild();
            try
            {
                if (!RoutingContext.TryGet(out context))
                    return true;
            }
            finally
            {
                RoutingContext.ExitBuild();
            }

            List<MultiUserChestCompat.Demand> demands =
                MucActionPreflight.RequirementDemands(
                    piece.m_resources,
                    0,
                    1,
                    null);

            MultiUserChestCompat.PendingAction pending =
                MultiUserChestCompat.Prepare(
                    context,
                    demands);

            if (pending == null)
                return true;

            Func<bool> stillValid =
                delegate
                {
                    if (__instance == null ||
                        __instance != Player.m_localPlayer ||
                        __instance.IsDead() ||
                        !__instance.InPlaceMode() ||
                        __instance.GetSelectedPiece() != piece ||
                        __instance.RightItem != tool)
                        return false;

                    GameObject currentGhost =
                        PlacementGhostField != null
                            ? PlacementGhostField
                                .GetValue(__instance)
                                as GameObject
                            : null;

                    if (currentGhost == null)
                        return false;

                    Vector3 delta =
                        currentGhost.transform.position -
                        position;

                    if (delta.sqrMagnitude >= 0.01f ||
                        Quaternion.Angle(
                            currentGhost.transform.rotation,
                            rotation) >= 1f)
                        return false;

                    float stamina =
                        GetBuildStamina(__instance);

                    return __instance.HaveStamina(stamina);
                };

            Action ready =
                delegate
                {
                    CompleteBuild(
                        __instance,
                        piece,
                        tool);
                };

            MultiUserChestCompat.Attach(
                pending,
                stillValid,
                ready,
                delegate
                {
                    MucActionPreflight.Missing(
                        __instance);
                });

            __result = false;
            return false;
        }
        private static void CompleteBuild(
            Player player,
            Piece piece,
            ItemDrop.ItemData tool)
        {
            if (player == null ||
                piece == null ||
                tool == null)
                return;

            RoutingContext.EnterBuild();

            try
            {
                if (!player.HaveRequirements(
                        piece,
                        Player.RequirementMode.CanBuild))
                {
                    MucActionPreflight.Missing(player);
                    return;
                }

                if (!player.TryPlacePiece(piece))
                    return;

                if (Hud.instance != null &&
                    Hud.instance.m_buildUi != null)
                {
                    Hud.instance.m_buildUi
                        .AddRecentPiece(piece);
                }

                if (LastToolUseField != null)
                {
                    LastToolUseField.SetValue(
                        player,
                        Time.time);
                }

                if (!ZoneSystem.instance.GetGlobalKey(
                        piece.FreeBuildKey()))
                {
                    player.ConsumeResources(
                        piece.m_resources,
                        0);
                }

                player.UseStamina(
                    GetBuildStamina(player));

                PieceTable table =
                    tool.m_shared.m_buildPieces;

                if (table != null &&
                    table.m_skill !=
                        Skills.SkillType.None)
                {
                    if (table.m_skill ==
                        Skills.SkillType.Crafting)
                    {
                        Game.instance
                            .IncrementPlayerStat(
                                PlayerStatType.BuiltPieces);
                    }

                    int debt =
                        BuildRemoveDebtField != null
                            ? (int)BuildRemoveDebtField
                                .GetValue(player)
                            : 0;

                    if (debt > 0)
                    {
                        BuildRemoveDebtField.SetValue(
                            player,
                            debt - 1);
                    }
                    else
                    {
                        player.RaiseSkill(
                            table.m_skill);

                        if (table.m_skill ==
                            Skills.SkillType.Crafting)
                        {
                            Game.instance
                                .IncrementPlayerStat(
                                    PlayerStatType
                                        .BuiltPiecesNoDebt);
                        }
                    }
                }

                if (tool.m_shared.m_useDurability)
                {
                    tool.m_durability -=
                        GetPlaceDurability(
                            player,
                            tool) *
                        Game.m_durabilityRate;
                }

                tool.m_shared.m_buildEffect.Create(
                    player.transform.position,
                    Quaternion.identity,
                    null,
                    1f,
                    -1,
                    player.GetZDOID());
            }
            finally
            {
                RoutingContext.ExitBuild();
            }
        }

        private static float GetBuildStamina(
            Player player)
        {
            if (player == null ||
                GetBuildStaminaMethod == null)
                return 0f;

            object value =
                GetBuildStaminaMethod.Invoke(
                    player,
                    null);

            return value is float
                ? (float)value
                : 0f;
        }

        private static float GetPlaceDurability(
            Player player,
            ItemDrop.ItemData tool)
        {
            if (player == null ||
                tool == null ||
                GetPlaceDurabilityMethod == null)
                return 0f;

            object value =
                GetPlaceDurabilityMethod.Invoke(
                    player,
                    new object[] { tool });

            return value is float
                ? (float)value
                : 0f;
        }
    }
}
