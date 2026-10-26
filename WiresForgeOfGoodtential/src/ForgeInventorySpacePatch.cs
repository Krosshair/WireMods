using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    internal static class ForgeInventorySpacePatch
    {
        private static readonly MethodInfo GetEmptySlotsMethod =
            AccessTools.Method(
                typeof(Inventory),
                nameof(Inventory.GetEmptySlots));

        private static readonly MethodInfo GetEmptySlotsForCraftMethod =
            AccessTools.Method(
                typeof(ForgeInventorySpacePatch),
                nameof(GetEmptySlotsForCraft));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(GetEmptySlotsMethod))
                {
                    yield return new CodeInstruction(
                        System.Reflection.Emit.OpCodes.Call,
                        GetEmptySlotsForCraftMethod);

                    continue;
                }

                yield return instruction;
            }
        }

        private static int GetEmptySlotsForCraft(
            Inventory inventory)
        {
            Player player = Player.m_localPlayer;

            CraftingStation station =
                player != null
                    ? player.GetCurrentCraftingStation()
                    : null;

            if (station != null &&
                station.m_upgrader &&
                ForgeSelection.CurrentRecipe != null &&
                ForgeSelection.CurrentItem != null)
            {
                int potential =
                    ForgeSelection.CurrentItem.m_quality -
                    Mathf.Max(1, ForgeSelection.CurrentRecipe
                        .m_item.m_itemData.m_shared.m_maxQuality);

                ForgeOdds odds = ForgeRules.GetOdds(
                    Mathf.Max(0, potential),
                    ForgeSelection.RequiredTier,
                    ForgeSelection.SelectedIdolTier,
                    ForgeSelection.SelectedTrophy,
                    ForgeSelection.ItemReferencePrestige);

                // Preserve vanilla's inventory-space reserve only when
                // the chosen offering can actually destroy equipment.
                if (odds.Destruction <= 0f)
                    return int.MaxValue;
            }

            return inventory != null
                ? inventory.GetEmptySlots()
                : 0;
        }
    }
}
