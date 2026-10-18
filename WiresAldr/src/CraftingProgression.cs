using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Reward only the results actually confirmed by vanilla's crafting
    // code, using native craft history and actual resulting item quality.
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class CraftingProgression
    {
        private static readonly AccessTools.FieldRef<InventoryGui, Recipe>
            RecipeField = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData>
            UpgradeField = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");

        private struct CraftState
        {
            internal Player Player;
            internal string ItemName;
            internal int IngredientTypes;
            internal float PreviousCraftCount;
            internal Skills.SkillType Skill;
            internal int PreviousQuality;
            internal bool Upgrade;
            internal HashSet<ItemDrop.ItemData> PreviousItems;
        }

        [HarmonyPrefix]
        private static void Prefix(InventoryGui __instance, Player player,
            out CraftState __state)
        {
            __state = default(CraftState);
            if (player == null || player != Player.m_localPlayer ||
                __instance == null || Game.instance == null ||
                Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.XpEnabled.Value || !Plugin.Rewards.Value) return;
            Recipe recipe = RecipeField(__instance);
            if (recipe == null || recipe.m_item == null ||
                recipe.m_item.m_itemData == null ||
                recipe.m_item.m_itemData.m_shared == null ||
                recipe.m_craftingStation == null ||
                recipe.m_craftingStation.m_craftingSkill == Skills.SkillType.None)
                return;
            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null || profile.m_playerStats == null ||
                profile.m_playerStats.Length == 0 ||
                profile.m_playerStats[0] == null) return;

            __state.Player = player;
            __state.ItemName = recipe.m_item.m_itemData.m_shared.m_name;
            __state.Skill = recipe.m_craftingStation.m_craftingSkill;
            var upgrade = UpgradeField(__instance);
            __state.Upgrade = upgrade != null;
            __state.PreviousQuality = upgrade == null ? 0 : upgrade.m_quality;
            if (recipe.m_resources != null)
                foreach (var requirement in recipe.m_resources)
                    if (requirement != null && requirement.m_resItem != null)
                        ++__state.IngredientTypes;
            profile.m_playerStats[0].m_itemCraftStats.TryGetValue(
                __state.ItemName, out __state.PreviousCraftCount);
            if (__state.Upgrade)
            {
                var inventory = player.GetInventory();
                if (inventory != null)
                    __state.PreviousItems = new HashSet<ItemDrop.ItemData>(
                        inventory.GetAllItems());
            }
        }

        [HarmonyPostfix]
        private static void Postfix(CraftState __state)
        {
            Player player = __state.Player;
            if (!player || Game.instance == null ||
                string.IsNullOrEmpty(__state.ItemName)) return;
            if (__state.Upgrade)
            {
                // Vanilla removes the old item and creates a new one.
                // Only a new item at a HIGHER quality level is success:
                // failed or destructive upgrader rolls earn no bonus.
                // Forge jackpots (e.g. Forge of Goodtential) can add
                // several levels at once; each gained level is rewarded.
                if (__state.PreviousItems == null) return;
                foreach (var item in player.GetInventory().GetAllItems())
                {
                    if (item == null || __state.PreviousItems.Contains(item) ||
                        item.m_shared == null ||
                        item.m_shared.m_name != __state.ItemName ||
                        item.m_quality <= __state.PreviousQuality)
                        continue;
                    int gained = Mathf.Clamp(item.m_quality - __state.PreviousQuality, 1, 5);
                    // 1 XP + 0.5 per quality level, for every level gained.
                    float xp = 0f;
                    for (int q = __state.PreviousQuality + 1; q <= __state.PreviousQuality + gained; ++q)
                        xp += 1f + 0.5f * q;
                    SkillReward.Grant(player, __state.Skill, xp,
                        "successful_upgrade_quality_" + item.m_quality);
                    break;
                }
                return;
            }

            // The permanent vanilla crafted-item stat changes only on
            // successful new crafts, not upgrades. No new history file.
            if (__state.PreviousCraftCount >= 1f ||
                __state.IngredientTypes < 2) return;
            var profile = Game.instance.GetPlayerProfile();
            if (profile == null || profile.m_playerStats == null ||
                profile.m_playerStats.Length == 0 ||
                profile.m_playerStats[0] == null) return;
            float after;
            profile.m_playerStats[0].m_itemCraftStats.TryGetValue(
                __state.ItemName, out after);
            if (after <= __state.PreviousCraftCount) return;
            // First craft of a multi-ingredient item: 1.5 XP + 0.5 per ingredient type (max 5).
            SkillReward.Grant(player, __state.Skill,
                Mathf.Min(5f, 1.5f + 0.5f * __state.IngredientTypes), "first_complex_craft");
        }
    }
}
