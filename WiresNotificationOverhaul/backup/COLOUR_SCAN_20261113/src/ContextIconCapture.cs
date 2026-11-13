using System;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // Context is captured at the actual event source, not guessed from
    // nearby objects or translated UI strings.
    internal static class ContextIconCapture
    {
        [ThreadStatic] private static Sprite _stationIcon;

        private static Sprite _fishIcon;
        private static string _fishRaw;
        private static string _fishLocalized;
        private static float _fishCapturedAt;

        internal static Sprite CurrentStationIcon => _stationIcon;

        internal static bool TryTakeFishIcon(
            string raw, string localized, out Sprite sprite)
        {
            sprite = null;
            if (_fishIcon == null || 
                Time.unscaledTime - _fishCapturedAt > 3f)
                return false;

            // Match one exact catch, even when the actual message has
            // already been localized by FishingFloat before ShowMessage.
            bool match =
                string.Equals(raw, _fishRaw, StringComparison.Ordinal) ||
                string.Equals(raw, _fishLocalized, StringComparison.Ordinal) ||
                string.Equals(localized, _fishLocalized,
                    StringComparison.Ordinal);

            if (!match)
                return false;

            sprite = _fishIcon;
            _fishIcon = null;
            _fishRaw = null;
            _fishLocalized = null;
            return true;
        }

        [HarmonyPatch(typeof(CraftingStation),
            nameof(CraftingStation.CheckUsable))]
        private static class StationUse
        {
            [HarmonyPrefix]
            private static void Prefix(
                CraftingStation __instance, out Sprite __state)
            {
                __state = _stationIcon;

                Sprite sprite = __instance != null
                    ? __instance.m_icon : null;
                if (sprite == null && __instance != null)
                {
                    Piece piece = __instance.GetComponent<Piece>();
                    if (piece != null)
                        sprite = piece.m_icon;
                }

                _stationIcon = sprite;
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(
                Exception __exception, Sprite __state)
            {
                _stationIcon = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(FishingFloat),
            nameof(FishingFloat.Catch))]
        private static class FishingCatch
        {
            [HarmonyPrefix]
            private static void Prefix(
                Fish fish, Character owner, out Sprite __state)
            {
                __state = null;
                if (fish == null || owner != Player.m_localPlayer)
                    return;

                ItemDrop drop = fish.GetComponent<ItemDrop>();
                if (drop == null && fish.m_pickupItem != null)
                    drop = fish.m_pickupItem.GetComponent<ItemDrop>();

                if (drop != null && drop.m_itemData != null)
                    __state = drop.m_itemData.GetIcon();
            }

            [HarmonyPostfix]
            private static void Postfix(Sprite __state, string __result)
            {
                if (__state == null || string.IsNullOrEmpty(__result))
                    return;

                _fishIcon = __state;
                _fishRaw = __result;
                _fishLocalized = Localization.instance != null
                    ? Localization.instance.Localize(__result)
                    : __result;
                _fishCapturedAt = Time.unscaledTime;
            }
        }
    }
}
