using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    // Small pops for things you do: using or equipping from the hotbar, putting
    // armor on or off, eating. Things that happen constantly on their own
    // (weight changing, getting hit, regen) aren't animated.
    internal static class MenuFeedbackSystem
    {
        private static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.HotbarSlotAnimations.Value;

        // Armor number pops when armor goes on or off; the hotbar slot pops too.
        internal static void EquipChanged(ItemDrop.ItemData item)
        {
            if (item == null)
                return;

            InventoryGui gui = InventoryGui.instance;
            if (On && item.m_shared.m_armor > 0f && gui != null && gui.m_armor != null
                && gui.m_armor.gameObject.activeInHierarchy)
                MicroMotionSystem.Pulse(gui.m_armor.transform, 1.15f, 0.2f);

            Player player = Player.m_localPlayer;
            if (player != null && item.m_gridPos.y == 0 && player.GetInventory().ContainsItem(item))
                HotbarUsed(item.m_gridPos.x + 1);
        }

        // The food you just ate pops into its spot.
        internal static void Ate(Player player, ItemDrop.ItemData item)
        {
            if (!On || Hud.instance == null || Hud.instance.m_foodIcons == null)
                return;

            List<Player.Food> foods = player.GetFoods();
            for (int i = 0; i < foods.Count && i < Hud.instance.m_foodIcons.Length; i++)
            {
                if (foods[i].m_item != null && foods[i].m_item.m_shared.m_name == item.m_shared.m_name)
                {
                    if (Hud.instance.m_foodIcons[i] != null)
                        MicroMotionSystem.Punch(Hud.instance.m_foodIcons[i].transform, 0.7f, 1.2f, 0.3f);
                    return;
                }
            }
        }

        internal static void HotbarUsed(int index)
        {
            if (!On || index < 1)
                return;

            foreach (HotkeyBar bar in HotbarAccess.Bars())
            {
                IList list = HotbarAccess.Elements(bar);
                if (list == null || index - 1 >= list.Count)
                    continue;

                GameObject slot = HotbarAccess.Slot(list[index - 1]);
                if (slot == null || !slot.activeInHierarchy)
                    continue;

                Transform icon = slot.transform.Find("icon");
                MicroMotionSystem.Punch(icon != null ? icon : slot.transform, 0.88f, 1.12f, 0.22f);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UseHotbarItem")]
    internal static class SmoothUIHotbarUsePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, int index)
        {
            if (__instance == Player.m_localPlayer && __instance.GetInventory().GetItemAt(index - 1, 0) != null)
                MenuFeedbackSystem.HotbarUsed(index);
        }
    }

    [HarmonyPatch(typeof(Player), "EatFood")]
    internal static class SmoothUIEatPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result && __instance == Player.m_localPlayer && item != null)
                MenuFeedbackSystem.Ate(__instance, item);
        }
    }

}
