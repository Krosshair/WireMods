using System;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // The hammer's Player.Repair shows its message synchronously.
    // Capture the targeted piece only for that call, so its actual
    // icon is available to MessageHud. Never scan the world or UI.
    internal static class BuildingRepairCapture
    {
        [ThreadStatic] private static Piece _target;

        internal static void Begin(Player player)
        {
            _target = player != null && player == Player.m_localPlayer
                ? player.GetHoveringPiece() : null;
        }

        internal static void End()
        {
            _target = null;
        }

        internal static bool TryGetMessage(
            MessageHud.MessageType type, string text,
            out Sprite icon, out string shortText)
        {
            icon = null;
            shortText = null;
            Piece piece = _target;
            if (type != MessageHud.MessageType.TopLeft ||
                piece == null || string.IsNullOrEmpty(text) ||
                Localization.instance == null)
                return false;

            string repaired = Localization.instance.Localize(
                "$msg_repaired", piece.m_name);
            // Unlike success, vanilla sends the failure as a tokenized
            // string with the piece name at its beginning.
            string unneeded = piece.m_name + " $msg_doesnotneedrepair";
            string localizedUnneeded = Localization.instance.Localize(unneeded);
            if (string.Equals(text, repaired, StringComparison.Ordinal))
                shortText = "Repaired";
            else if (string.Equals(text, unneeded, StringComparison.Ordinal) ||
                     string.Equals(text, localizedUnneeded, StringComparison.Ordinal))
                shortText = "No repair needed";
            else
                return false;

            icon = piece.m_icon;
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "Repair")]
    internal static class BuildingRepairCapturePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance)
        {
            BuildingRepairCapture.Begin(__instance);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            BuildingRepairCapture.End();
            return __exception;
        }
    }
}