using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class MethodSwaps
    {
        internal static readonly MethodInfo CountItemsFrom =
            AccessTools.Method(
                typeof(Inventory),
                nameof(Inventory.CountItems),
                new[]
                {
                    typeof(string),
                    typeof(int),
                    typeof(bool)
                });

        internal static readonly MethodInfo CountItemsTo =
            AccessTools.Method(
                typeof(InventoryBridge),
                nameof(InventoryBridge.CountItems));

        internal static readonly MethodInfo HaveItemFrom =
            AccessTools.Method(
                typeof(Inventory),
                nameof(Inventory.HaveItem),
                new[]
                {
                    typeof(string),
                    typeof(bool)
                });

        internal static readonly MethodInfo HaveItemTo =
            AccessTools.Method(
                typeof(InventoryBridge),
                nameof(InventoryBridge.HaveItem));

        internal static readonly MethodInfo GetItemFrom =
            AccessTools.Method(
                typeof(Inventory),
                nameof(Inventory.GetItem),
                new[]
                {
                    typeof(string),
                    typeof(int),
                    typeof(bool)
                });

        internal static readonly MethodInfo GetItemTo =
            AccessTools.Method(
                typeof(InventoryBridge),
                nameof(InventoryBridge.GetItem));

        internal static readonly MethodInfo RemoveItemFrom =
            AccessTools.Method(
                typeof(Inventory),
                nameof(Inventory.RemoveItem),
                new[]
                {
                    typeof(string),
                    typeof(int),
                    typeof(int),
                    typeof(bool)
                });

        internal static readonly MethodInfo RemoveItemTo =
            AccessTools.Method(
                typeof(InventoryBridge),
                nameof(InventoryBridge.RemoveItem));
    }

    internal static class Redirect
    {
        internal static IEnumerable<CodeInstruction> Calls(
            IEnumerable<CodeInstruction> instructions,
            params KeyValuePair<MethodInfo, MethodInfo>[] swaps)
        {
            foreach (CodeInstruction instruction
                     in instructions)
            {
                for (int i = 0; i < swaps.Length; i++)
                {
                    if (instruction.Calls(
                            swaps[i].Key))
                    {
                        instruction.opcode =
                            OpCodes.Call;
                        instruction.operand =
                            swaps[i].Value;
                        break;
                    }
                }

                yield return instruction;
            }
        }

        internal static KeyValuePair<MethodInfo, MethodInfo> Swap(
            MethodInfo from,
            MethodInfo to)
        {
            return new KeyValuePair<MethodInfo, MethodInfo>(
                from,
                to);
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Container __instance)
        {
            ContainerRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(
        typeof(Player),
        "HaveRequirementItems",
        new[]
        {
            typeof(Recipe),
            typeof(bool),
            typeof(int),
            typeof(int)
        })]
    internal static class HaveRequirementItemsPatch
    {
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

    [HarmonyPatch]
    internal static class GetFirstRequiredItemPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Player),
                nameof(Player.GetFirstRequiredItem));
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return Redirect.Calls(
                instructions,
                Redirect.Swap(
                    MethodSwaps.CountItemsFrom,
                    MethodSwaps.CountItemsTo),
                Redirect.Swap(
                    MethodSwaps.GetItemFrom,
                    MethodSwaps.GetItemTo));
        }
    }

    [HarmonyPatch(
        typeof(Player),
        nameof(Player.ConsumeResources))]
    internal static class ConsumeResourcesPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return Redirect.Calls(
                instructions,
                Redirect.Swap(
                    MethodSwaps.RemoveItemFrom,
                    MethodSwaps.RemoveItemTo));
        }
    }

    [HarmonyPatch(
        typeof(InventoryGui),
        "DoCrafting",
        new[] { typeof(Player) })]
    internal static class DoCraftingPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return Redirect.Calls(
                instructions,
                Redirect.Swap(
                    MethodSwaps.RemoveItemFrom,
                    MethodSwaps.RemoveItemTo));
        }
    }

    [HarmonyPatch(
        typeof(InventoryGui),
        nameof(InventoryGui.SetupRequirement))]
    internal static class SetupRequirementPatch
    {
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

    [HarmonyPatch(
        typeof(Player),
        nameof(Player.HaveRequirements),
        new[]
        {
            typeof(Piece),
            typeof(Player.RequirementMode)
        })]
    internal static class HavePieceRequirementsPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            RoutingContext.EnterBuild();
        }

        [HarmonyFinalizer]
        private static void Finalizer()
        {
            RoutingContext.ExitBuild();
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return Redirect.Calls(
                instructions,
                Redirect.Swap(
                    MethodSwaps.HaveItemFrom,
                    MethodSwaps.HaveItemTo),
                Redirect.Swap(
                    MethodSwaps.CountItemsFrom,
                    MethodSwaps.CountItemsTo));
        }
    }

    [HarmonyPatch(
        typeof(Player),
        "UpdatePlacement",
        new[] { typeof(bool), typeof(float) })]
    internal static class UpdatePlacementPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            RoutingContext.EnterBuild();
        }

        [HarmonyFinalizer]
        private static void Finalizer()
        {
            RoutingContext.ExitBuild();
        }
    }

    [HarmonyPatch(
        typeof(Hud),
        "SetupPieceInfo",
        new[] { typeof(Piece) })]
    internal static class SetupPieceInfoPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            RoutingContext.EnterBuild();
        }

        [HarmonyFinalizer]
        private static void Finalizer()
        {
            RoutingContext.ExitBuild();
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

    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    internal static class ZNetShutdownPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            RefillTargetRegistry.Clear();
            ContainerRegistry.Clear();
            RoutingContext.Reset();
            ConnectionVisuals.Cleanup();
            KeywordSync.Reset();
            MultiUserChestCompat.Reset();
            RangeProvider.Invalidate();
        }
    }
}
