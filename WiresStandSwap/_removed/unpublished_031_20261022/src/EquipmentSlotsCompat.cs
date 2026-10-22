using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace WiresStandSwap
{
    // EAQS keeps its equipment cells inside the player's Inventory,
    // beyond the ordinary backpack rows. Look up the ACTUAL live cell
    // positions rather than hardcoding a row or a chest-slot index.
    // Reflection keeps the dependency optional for vanilla players.
    internal static class EquipmentSlotsCompat
    {
        private static bool IsArmorType(
            ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return true;
                default:
                    return false;
            }
        }

        internal static ItemDrop.ItemData FindOccupiedSlot(
            Player player, ItemDrop.ItemData displayed)
        {
            if (player == null || displayed == null ||
                !IsArmorType(displayed.m_shared.m_itemType))
                return null;

            Type slotsType =
                AccessTools.TypeByName(
                    "EquipmentAndQuickSlots.Slots");
            if (slotsType == null)
                return null;

            MethodInfo getSlots =
                AccessTools.Method(slotsType,
                    "GetEquipmentSlots");
            MethodInfo allowed =
                AccessTools.Method(slotsType,
                    "WouldFitEquipmentSlot");
            if (getSlots == null)
                return null;

            try
            {
                IEnumerable slots = getSlots.Invoke(
                    null, new object[] { true }) as IEnumerable;
                Inventory inv = player.GetInventory();
                if (slots == null || inv == null)
                    return null;

                foreach (object slot in slots)
                {
                    if (slot == null) continue;
                    Type type = slot.GetType();
                    MethodInfo fits =
                        AccessTools.Method(type, "ItemFits");
                    PropertyInfo grid =
                        AccessTools.Property(type,
                            "GridPosition");
                    if (fits == null || grid == null ||
                        !(bool)fits.Invoke(
                            slot, new object[] { displayed }))
                        continue;
                    if (allowed != null &&
                        !(bool)allowed.Invoke(
                            null, new object[] { slot, displayed }))
                        continue;

                    Vector2i pos =
                        (Vector2i)grid.GetValue(slot, null);
                    ItemDrop.ItemData existing =
                        inv.GetItemAt(pos.x, pos.y);
                    if (existing == null ||
                        existing.m_shared.m_itemType !=
                            displayed.m_shared.m_itemType ||
                        player.IsEquipActionQueued(existing))
                        continue;

                    return existing;
                }
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning(
                    "EAQS occupied-slot lookup failed: " +
                    error);
            }
            return null;
        }

        internal static bool FindFreeSlot(
            Player player, ItemDrop.ItemData item,
            out Vector2i position, out bool hasMatchingCells,
            out bool lookupFailed)
        {
            position = Vector2i.zero;
            hasMatchingCells = false;
            lookupFailed = false;

            if (player == null || item == null ||
                !IsArmorType(item.m_shared.m_itemType))
                return false;

            Type slotsType =
                AccessTools.TypeByName(
                    "EquipmentAndQuickSlots.Slots");
            if (slotsType == null)
                return false;

            MethodInfo getSlots =
                AccessTools.Method(slotsType,
                    "GetEquipmentSlots");
            if (getSlots == null)
            {
                lookupFailed = true;
                return false;
            }

            try
            {
                IEnumerable slots = getSlots.Invoke(
                    null, new object[] { true }) as IEnumerable;
                if (slots == null)
                {
                    lookupFailed = true;
                    return false;
                }

                Inventory inv = player.GetInventory();
                if (inv == null) return false;
                foreach (object slot in slots)
                {
                    if (slot == null) continue;
                    Type t = slot.GetType();
                    MethodInfo fits =
                        AccessTools.Method(t, "ItemFits");
                    PropertyInfo grid =
                        AccessTools.Property(t,
                            "GridPosition");

                    if (fits == null || grid == null ||
                        !(bool)fits.Invoke(
                            slot, new object[] { item }))
                        continue;

                    // In particular, EAQS may restrict the extra
                    // utility cells depending on configuration.
                    MethodInfo allowed =
                        AccessTools.Method(slotsType,
                            "WouldFitEquipmentSlot");
                    if (allowed != null &&
                        !(bool)allowed.Invoke(
                            null, new object[] { slot, item }))
                        continue;

                    hasMatchingCells = true;
                    Vector2i cell =
                        (Vector2i)grid.GetValue(slot, null);
                    if (inv.GetItemAt(cell.x, cell.y) != null)
                        continue;

                    position = cell;
                    return true;
                }
            }
            catch (Exception error)
            {
                lookupFailed = true;
                Plugin.Log?.LogWarning(
                    "EAQS slot discovery failed: " + error);
            }

            return false;
        }
    }
}
