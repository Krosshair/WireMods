using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // This path executes ONLY when the game discovers new content.
    // Vanilla's Player known-item/recipe and compendium data are untouched.
    internal static class UnlockCapture
    {
        [ThreadStatic] private static string _category;

        internal static bool Enabled =>
            WiresNotificationOverhaulPlugin.GroupUnlockNotifications != null &&
            WiresNotificationOverhaulPlugin.GroupUnlockNotifications.Value ==
                WiresNotificationOverhaulPlugin.Toggle.On;

        private static string Localize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Discovery";
            string value = Localization.instance != null
                ? Localization.instance.Localize(name)
                : name.TrimStart('$');
            return string.IsNullOrWhiteSpace(value) ? "Discovery" : value.Trim();
        }

        private static string Category(string topic)
        {
            if (!string.IsNullOrWhiteSpace(_category))
                return _category;

            if (!CompactLocalization.IsEnglish)
            {
                string nativeTopic = CompactLocalization.NativeToken(
                    topic, null);
                if (!string.IsNullOrWhiteSpace(nativeTopic))
                    return nativeTopic;
            }

            switch (topic)
            {
                case "$msg_newrecipe": return "Crafting";
                case "$msg_newpiece": return "Hammer";
                case "$msg_newdish": return "Cooking";
                case "$msg_newstation":
                case "$msg_newstation_level": return "Stations";
                case "$msg_newmaterial": return "Materials";
                case "$msg_newtrophy": return "Trophies";
                case "$msg_newitem": return "Items";
                default: return "Discoveries";
            }
        }

        [HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        private static class RecipeContext
        {
            [HarmonyPrefix]
            private static void Prefix(Recipe recipe, out string __state)
            {
                __state = _category;
                _category = recipe != null && recipe.m_craftingStation != null
                    ? Localize(recipe.m_craftingStation.m_name)
                    : CompactLocalization.NativeToken("$msg_newrecipe", "Crafting");
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, string __state)
            {
                _category = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Player), "AddKnownPiece")]
        private static class PieceContext
        {
            [HarmonyPrefix]
            private static void Prefix(Piece piece, out string __state)
            {
                __state = _category;
                bool dish = piece != null && (
                    piece.m_category == Piece.PieceCategory.Feasts ||
                    piece.m_category == Piece.PieceCategory.Food ||
                    piece.m_category == Piece.PieceCategory.Meads);
                _category = dish
                    ? CompactLocalization.NativeToken("$msg_newdish", "Cooking")
                    : CompactLocalization.NativeToken("$msg_newpiece", "Hammer");
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, string __state)
            {
                _category = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownStation))]
        private static class StationContext
        {
            [HarmonyPrefix]
            private static void Prefix(CraftingStation station, out string __state)
            {
                __state = _category;
                _category = station != null
                    ? Localize(station.m_name)
                    : CompactLocalization.NativeToken("$msg_newstation", "Stations");
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception, string __state)
            {
                _category = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.QueueUnlockMsg))]
        private static class QueueUnlockPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(MessageHud __instance,
                Sprite icon, string topic, string description)
            {
                if (!Enabled || __instance == null ||
                    __instance.GetLog() == null)
                    return true;

                // Capture the actual unlock icon; if the UI cannot accept
                // it, let Valheim's original method handle everything.
                if (!NotificationSystem.TryShowGroupedUnlock(
                    __instance, Category(topic), icon))
                    return true;

                // Vanilla QueueUnlockMsg also calls AddLog, separately
                // from the known-item/recipe updates. Preserve precisely
                // that log entry, and its normal 50-line cap.
                List<string> log = __instance.GetLog();
                if (log != null)
                {
                    log.Add(topic + ": " + description);
                    while (log.Count > __instance.m_maxLogMessages)
                        log.RemoveAt(0);
                }

                return false;
            }
        }


    }
}
