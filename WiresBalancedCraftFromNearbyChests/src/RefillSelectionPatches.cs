using System.Collections.Generic;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    [HarmonyPatch(typeof(Smelter), "FindCookableItem")]
    internal static class SmelterChestSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Smelter __instance,
            Inventory inventory,
            ref ItemDrop.ItemData __result)
        {
            if (!RefillSelection.IsLocalPlayerInventory(inventory))
                return true;

            foreach (Smelter.ItemConversion conversion
                     in __instance.m_conversion)
            {
                if (conversion.m_from == null)
                    continue;

                if (inventory.GetItem(
                        conversion.m_from.m_itemData.m_shared.m_name) != null)
                    return true;
            }
            __result =
                RefillSelection.FindFirstInChests(
                    __instance,
                    delegate(ItemDrop.ItemData candidate)
                    {
                        if (candidate == null ||
                            candidate.m_dropPrefab == null)
                            return false;

                        foreach (Smelter.ItemConversion conversion
                                 in __instance.m_conversion)
                        {
                            if (conversion.m_from != null &&
                                conversion.m_from.gameObject.name ==
                                candidate.m_dropPrefab.name)
                                return true;
                        }

                        return false;
                    });

            return false;
        }
    }

    [HarmonyPatch(typeof(CookingStation), "FindCookableItem")]
    internal static class CookingStationChestSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            CookingStation __instance,
            Inventory inventory,
            ref ItemDrop.ItemData __result)
        {
            if (!RefillSelection.IsLocalPlayerInventory(inventory))
                return true;

            foreach (CookingStation.ItemConversion conversion
                     in __instance.m_conversion)
            {
                if (inventory.GetItem(
                        conversion.m_from.m_itemData.m_shared.m_name) != null)
                    return true;
            }

            __result =
                RefillSelection.FindFirstInChests(
                    __instance,
                    delegate(ItemDrop.ItemData candidate)
                    {
                        if (candidate == null)
                            return false;

                        foreach (CookingStation.ItemConversion conversion
                                 in __instance.m_conversion)
                        {
                            if (candidate.m_shared.m_name ==
                                conversion.m_from.m_itemData.m_shared.m_name)
                                return true;
                        }

                        return false;
                    });

            return false;
        }
    }

    [HarmonyPatch(typeof(Fermenter), "FindCookableItem")]
    internal static class FermenterChestSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Fermenter __instance,
            Inventory inventory,
            ref ItemDrop.ItemData __result)
        {
            if (!RefillSelection.IsLocalPlayerInventory(inventory))
                return true;

            foreach (Fermenter.ItemConversion conversion
                     in __instance.m_conversion)
            {
                if (inventory.GetItem(
                        conversion.m_from.m_itemData.m_shared.m_name) != null)
                    return true;
            }

            __result =
                RefillSelection.FindFirstInChests(
                    __instance,
                    delegate(ItemDrop.ItemData candidate)
                    {
                        if (candidate == null)
                            return false;

                        foreach (Fermenter.ItemConversion conversion
                                 in __instance.m_conversion)
                        {
                            if (candidate.m_shared.m_name ==
                                conversion.m_from.m_itemData.m_shared.m_name)
                                return true;
                        }

                        return false;
                    });

            return false;
        }
    }

    [HarmonyPatch(typeof(Turret), "FindAmmoItem")]
    internal static class TurretChestSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Turret __instance,
            Inventory inventory,
            bool onlyCurrentlyLoadableType,
            ref ItemDrop.ItemData __result)
        {
            if (!RefillSelection.IsLocalPlayerInventory(inventory))
                return true;

            string matchPrefabName =
                onlyCurrentlyLoadableType &&
                __instance.HasAmmo()
                    ? __instance.GetAmmoType()
                    : null;

            if (inventory.GetAmmoItem(
                    __instance.m_ammoType,
                    matchPrefabName) != null)
                return true;

            __result =
                RefillSelection.FindFirstInChests(
                    __instance,
                    delegate(ItemDrop.ItemData candidate)
                    {
                        if (candidate == null ||
                            candidate.m_shared == null)
                            return false;

                        ItemDrop.ItemData.ItemType type =
                            candidate.m_shared.m_itemType;

                        bool usableType =
                            type == ItemDrop.ItemData.ItemType.Ammo ||
                            type == ItemDrop.ItemData.ItemType.AmmoNonEquipable ||
                            type == ItemDrop.ItemData.ItemType.Consumable;

                        return usableType &&
                               candidate.m_shared.m_ammoType ==
                                   __instance.m_ammoType &&
                               (matchPrefabName == null ||
                                (candidate.m_dropPrefab != null &&
                                 candidate.m_dropPrefab.name ==
                                     matchPrefabName));
                    });

            return false;
        }
    }

    [HarmonyPatch(typeof(ShieldGenerator), "OnAddFuel")]
    internal static class ShieldGeneratorChestSelectionPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            ShieldGenerator __instance,
            Humanoid user,
            ref ItemDrop.ItemData item)
        {
            if (item != null ||
                __instance == null ||
                user == null)
                return;

            Inventory inventory = user.GetInventory();
            foreach (ItemDrop fuel in __instance.m_fuelItems)
            {
                if (fuel != null &&
                    inventory.HaveItem(
                        fuel.m_itemData.m_shared.m_name))
                    return;
            }

            item =
                RefillSelection.FindFirstInChests(
                    __instance,
                    delegate(ItemDrop.ItemData candidate)
                    {
                        if (candidate == null)
                            return false;

                        foreach (ItemDrop fuel
                                 in __instance.m_fuelItems)
                        {
                            if (fuel != null &&
                                candidate.m_shared.m_name ==
                                fuel.m_itemData.m_shared.m_name)
                                return true;
                        }

                        return false;
                    });
        }
    }
}
