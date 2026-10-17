using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // Vanilla calls InventoryGui.RepairOneItem, restores one worn item,
    // then shows a localized "Repaired [name]" center message without
    // an icon. Track the actual changed inventory item, not a guessed
    // item name or the first worn item.
    internal static class RepairMessageCapture
    {
        private sealed class Before
        {
            internal ItemDrop.ItemData Item;
            internal float Durability;
        }

        [ThreadStatic]
        private static List<Before> _before;

        internal static void Begin()
        {
            _before = null;

            Player player = Player.m_localPlayer;
            if (player == null || player.GetInventory() == null)
                return;

            var worn = new List<ItemDrop.ItemData>();
            player.GetInventory().GetWornItems(worn);

            var snapshot = new List<Before>(worn.Count);
            foreach (ItemDrop.ItemData item in worn)
            {
                if (item != null && item.m_shared != null)
                {
                    snapshot.Add(new Before
                    {
                        Item = item,
                        Durability = item.m_durability
                    });
                }
            }
            _before = snapshot;
        }

        internal static void End()
        {
            _before = null;
        }

        internal static bool TryGetRepairedIcon(
            MessageHud.MessageType type,
            string message,
            out Sprite icon)
        {
            icon = null;
            if (type != MessageHud.MessageType.Center ||
                string.IsNullOrEmpty(message) ||
                _before == null ||
                Localization.instance == null)
                return false;

            foreach (Before before in _before)
            {
                ItemDrop.ItemData item = before.Item;
                if (item == null || item.m_shared == null)
                    continue;

                float maximum = item.GetMaxDurability();
                if (maximum <= 0f ||
                    item.m_durability <= before.Durability + 0.001f ||
                    item.m_durability < maximum - 0.001f)
                    continue;

                string expected = Localization.instance.Localize(
                    "$msg_repaired", item.m_shared.m_name);

                if (!string.Equals(message, expected,
                    StringComparison.Ordinal))
                    continue;

                icon = item.GetIcon();
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
    internal static class RepairOneItemMessageCapturePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            RepairMessageCapture.Begin();
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            // Guaranteed cleanup, including repairs aborted by mods.
            RepairMessageCapture.End();
            return __exception;
        }
    }
}
