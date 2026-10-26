using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    // The armor number in the inventory pops when you put armor on or take it
    // off. Not on spawn or login, when the game equips your gear for you.
    internal static class ArmorPop
    {
        private const float SpawnQuiet = 2f;
        internal static float SpawnTime = -100f;

        internal static void EquipChanged(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared.m_armor <= 0f
                || !WiresSmoothUIPlugin.Enabled.Value || !WiresSmoothUIPlugin.ItemAnimations.Value
                || Time.unscaledTime - SpawnTime < SpawnQuiet)
                return;

            InventoryGui gui = InventoryGui.instance;
            if (gui != null && gui.m_armor != null)
                UiMotion.Pop(gui.m_armor.transform, 1f, 1.15f, 0.2f);
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class SmoothUISpawnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                ArmorPop.SpawnTime = Time.unscaledTime;
        }
    }
}
