using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    // My Little UI writes how many of each requirement you have, "(12)",
    // next to the amount needed. It counts only your own inventory: here it
    // counts the chests the station or hammer can use too, the same total
    // the requirement's own colour already uses.
    [HarmonyPatch]
    internal static class MyLittleUIAvailableAmountPatch
    {
        private static MethodBase _target;

        private static bool Prepare()
        {
            Type type = AccessTools.TypeByName(
                "MyLittleUI.MyLittleUI+InventoryGui_SetupRequirement_AddAvailableAmount");
            _target = type != null ? AccessTools.Method(type, "Postfix") : null;
            return _target != null;
        }

        private static MethodBase TargetMethod()
        {
            return _target;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return Redirect.Calls(
                instructions,
                Redirect.Swap(
                    MethodSwaps.CountItemsFrom,
                    MethodSwaps.CountItemsTo));
        }
    }
}
