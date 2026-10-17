using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class RefillSwaps
    {
        internal static readonly KeyValuePair<MethodInfo, MethodInfo>[] All =
            new[]
            {
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.CountItems),
                        new[] { typeof(string), typeof(int), typeof(bool) }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.CountItems))),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.CountItemsByName),
                        new[]
                        {
                            typeof(string[]),
                            typeof(int),
                            typeof(bool),
                            typeof(bool)
                        }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.CountItemsByName))),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.HaveItem),
                        new[] { typeof(string), typeof(bool) }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.HaveItem))),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.GetItem),
                        new[] { typeof(string), typeof(int), typeof(bool) }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.GetItem))),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.GetAmmoItem),
                        new[] { typeof(string), typeof(string) }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.GetAmmoItem))),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.RemoveItem),
                        new[]
                        {
                            typeof(string),
                            typeof(int),
                            typeof(int),
                            typeof(bool)
                        }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.RemoveItem),
                        new[]
                        {
                            typeof(Inventory),
                            typeof(string),
                            typeof(int),
                            typeof(int),
                            typeof(bool)
                        })),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.RemoveItem),
                        new[] { typeof(ItemDrop.ItemData), typeof(int) }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.RemoveItemData))),
                Swap(
                    AccessTools.Method(
                        typeof(Inventory),
                        nameof(Inventory.RemoveOneItem),
                        new[] { typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(
                        typeof(InventoryBridge),
                        nameof(InventoryBridge.RemoveOneItem)))
            };

        private static KeyValuePair<MethodInfo, MethodInfo> Swap(
            MethodInfo from,
            MethodInfo to)
        {
            return new KeyValuePair<MethodInfo, MethodInfo>(
                from,
                to);
        }
    }

    [HarmonyPatch]
    internal static class RefillInventoryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();

            Add(methods, typeof(Smelter), "OnAddFuel");
            Add(methods, typeof(Smelter), "OnAddOre");
            Add(methods, typeof(Smelter), "CanUseItems");

            Add(methods, typeof(Fireplace), "Interact");
            Add(methods, typeof(Fireplace), "CanUseItems");

            Add(methods, typeof(CookingStation), "OnAddFuelSwitch");
            Add(methods, typeof(CookingStation), "OnInteract");
            Add(methods, typeof(CookingStation), "CookItem");
            Add(methods, typeof(CookingStation), "CanUseItems");

            Add(methods, typeof(Fermenter), "Interact");
            Add(methods, typeof(Fermenter), "UseItem");
            Add(methods, typeof(Fermenter), "AddItem");

            Add(methods, typeof(Turret), "UseItem");
            Add(methods, typeof(Turret), "CanUseItems");

            Add(methods, typeof(ShieldGenerator), "OnAddFuel");
            Add(methods, typeof(ShieldGenerator), "CanUseItems");

            Add(methods, typeof(Catapult), "OnLoadPointUse");

            return methods;
        }

        private static void Add(
            List<MethodBase> methods,
            Type type,
            string name)
        {
            MethodInfo method =
                AccessTools.Method(type, name);

            if (method != null)
                methods.Add(method);
            else if (Plugin.Log != null)
                Plugin.Log.LogWarning(
                    "Refill patch target not found: " +
                    type.Name + "." + name);
        }

        private static void Prefix(
            object __instance,
            ref bool __state)
        {
            __state =
                RefillRouting.Enter(
                    __instance as UnityEngine.Component);
        }

        private static Exception Finalizer(
            Exception __exception,
            bool __state,
            MethodBase __originalMethod,
            object __instance,
            object[] __args)
        {
            if (__state)
                RoutingContext.ExitRefill();

            MucPendingException pending =
                __exception as MucPendingException;

            if (pending == null)
                return __exception;

            MultiUserChestCompat.Attach(
                pending.Pending,
                delegate
                {
                    return __instance != null;
                },
                delegate
                {
                    if (__originalMethod == null ||
                        __instance == null)
                        return;

                    object[] retryArgs =
                        MultiUserChestCompat.RefreshItemArgs(
                            __args);

                    __originalMethod.Invoke(
                        __instance,
                        retryArgs);
                },
                delegate
                {
                    Player player =
                        Player.m_localPlayer;

                    if (player != null)
                    {
                        player.Message(
                            MessageHud.MessageType.Center,
                            "$msg_missingrequirement");
                    }
                });

            return null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                for (int i = 0;
                     i < RefillSwaps.All.Length;
                     i++)
                {
                    KeyValuePair<MethodInfo, MethodInfo> swap =
                        RefillSwaps.All[i];

                    if (swap.Key != null &&
                        swap.Value != null &&
                        instruction.Calls(swap.Key))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = swap.Value;
                        break;
                    }
                }

                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(Catapult), "OnLoadPointUse")]
    internal static class CatapultAutoLoadPatch
    {
        private static readonly MethodInfo CanLoadMethod =
            AccessTools.Method(
                typeof(Catapult),
                "CanItemBeLoaded",
                new[] { typeof(ItemDrop.ItemData) });

        [HarmonyPrefix]
        private static void Prefix(
            Catapult __instance,
            Humanoid user,
            ref ItemDrop.ItemData item)
        {
            if (item != null ||
                __instance == null ||
                user == null ||
                CanLoadMethod == null)
                return;

            item = RefillSelection.FindFirstInChests(
                __instance,
                delegate(ItemDrop.ItemData candidate)
                {
                    object allowed =
                        CanLoadMethod.Invoke(
                            __instance,
                            new object[] { candidate });

                    return allowed is bool &&
                           (bool)allowed;
                });
        }

        private static ItemDrop.ItemData FindLoadable(
            Catapult catapult,
            Inventory inventory)
        {
            if (inventory == null)
                return null;

            List<ItemDrop.ItemData> items =
                inventory.GetAllItems();

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData candidate = items[i];
                object allowed =
                    CanLoadMethod.Invoke(
                        catapult,
                        new object[] { candidate });

                if (allowed is bool &&
                    (bool)allowed)
                    return candidate;
            }

            return null;
        }
    }
}
