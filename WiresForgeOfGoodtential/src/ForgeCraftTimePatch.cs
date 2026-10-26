using System;
using HarmonyLib;

namespace WiresForgeOfGoodtential
{
    // Halve only the Forge of Potential / Goodtential
    // upgrader duration, including its per-level component.
    // Normal crafting and other workstation timers are untouched.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class ForgeCraftTimePatch
    {
        private struct PreviousDurations
        {
            internal bool Changed;
            internal float Base;
            internal float PerLevel;
        }

        [HarmonyPrefix]
        private static void Prefix(
            InventoryGui __instance,
            Player player,
            ref PreviousDurations __state)
        {
            __state = new PreviousDurations();

            if (__instance == null || player == null)
                return;

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station == null || !station.m_upgrader)
                return;

            __state.Changed = true;
            __state.Base = __instance.m_upgraderDuration;
            __state.PerLevel =
                __instance.m_upgraderDurationPerLevel;

            __instance.m_upgraderDuration *= 0.5f;
            __instance.m_upgraderDurationPerLevel *= 0.5f;
        }

        [HarmonyPostfix]
        private static void Postfix(
            InventoryGui __instance,
            PreviousDurations __state)
        {
            Restore(__instance, __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            InventoryGui __instance,
            PreviousDurations __state,
            Exception __exception)
        {
            Restore(__instance, __state);
            return __exception;
        }

        private static void Restore(
            InventoryGui gui,
            PreviousDurations previous)
        {
            if (gui == null || !previous.Changed)
                return;

            gui.m_upgraderDuration = previous.Base;
            gui.m_upgraderDurationPerLevel = previous.PerLevel;
        }
    }
}
