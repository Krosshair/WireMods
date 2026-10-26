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
                station.m_upgrader &&
                ForgeSelection.CurrentRecipe != null &&
                ForgeSelection.CurrentItem != null)
            {
                // Every Goodtential attempt preserves the actual item.
                // Even an under-tier idol may only lower its quality,
                // never invoke vanilla destruction/material returns.
                // The old four-slot reserve is therefore unnecessary.
                return int.MaxValue;
            }

            return inventory != null
                ? inventory.GetEmptySlots()
                : 0;
        }
    }
}
