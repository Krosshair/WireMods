using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

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
                station.m_upgrader)
            {
                bool riskyUnderTier =
                    ForgeSelection.SelectedIdolTier <
                    ForgeSelection.RequiredTier;

                // Safe Goodtential refinements cannot destroy the item, so
                // vanilla's ingredient-return reserve is unnecessary.
                // Under-tier idols can genuinely destroy it, so those retain
                // the original vanilla empty-slot safeguard.
                if (!riskyUnderTier)
                    return int.MaxValue;
            }

            return inventory != null
                ? inventory.GetEmptySlots()
                : 0;
        }
    }
}
