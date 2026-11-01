using System;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // Valheim emits "$msg_snapping <snap type>" from Player's
    // placement controls. Resolve the actual selected build piece
    // at that message event, not by polling the placement ghost.
    internal static class SnapMessageCapture
    {
        private const string Prefix = "$msg_snapping ";

        internal static bool TryResolve(
            MessageHud.MessageType type, string raw,
            out Sprite icon, out string compact)
        {
            icon = null;
            compact = null;

            if (type != MessageHud.MessageType.Center ||
                string.IsNullOrEmpty(raw) ||
                !raw.StartsWith(Prefix, StringComparison.Ordinal))
                return false;

            string snapType = raw.Substring(Prefix.Length).Trim();
            if (snapType.Length == 0)
                return false;

            // Both the normal snap point name and $msg_snapping_auto
            // are supplied by Valheim; retain exactly that information.
            compact = Localization.instance != null
                ? Localization.instance.Localize(snapType)
                : snapType;
            if (string.IsNullOrWhiteSpace(compact))
                compact = snapType;

            Player player = Player.m_localPlayer;
            Piece selected = player != null ? player.GetSelectedPiece() : null;
            if (selected != null)
                icon = selected.m_icon;
            return true;
        }
    }
}