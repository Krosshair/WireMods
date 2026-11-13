using System;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // Preserve incoming icons. Add an icon only when Valheim gives us
    // the exact referenced item/station, or its own Resting status icon.
    // No guesses, scans, new textures, or notification-frame work.
    internal static class ContextIcons
    {
        internal static Sprite Choose(
            string raw, string localized, string compact, Sprite provided)
        {
            if (provided != null)
                return provided;

            string token = raw ?? "";
            string message = compact ?? localized ?? "";

            // CheckUsable emits these synchronously. The station patch
            // provides the *specific* workbench/forge/cauldron sprite.
            if (token == "$msg_stationneedroof" ||
                token == "$msg_stationtooexposed" ||
                token == "$msg_needfire" ||
                string.Equals(message, "Requires roof", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, "Requires shelter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, "Requires nearby fire", StringComparison.OrdinalIgnoreCase))
                return ContextIconCapture.CurrentStationIcon;

            // The fish comes from the actual FishingFloat.Catch event.
            // Do not show a generic fishing rod for every catch.
            Sprite caught;
            if (ContextIconCapture.TryTakeFishIcon(token, localized, out caught))
                return caught;

            // Vanilla already uses the sleeping/resting Zzz sprite.
            // This is the specific status icon previously requested.
            if (token.IndexOf("$se_rested_start", StringComparison.Ordinal) >= 0 ||
                token.IndexOf("$se_resting", StringComparison.Ordinal) >= 0 ||
                message.StartsWith("Rested", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, "Resting", StringComparison.OrdinalIgnoreCase))
                return RestingIcon();

            // No icon for "Dry", "Warming up", generic warnings,
            // fishing failures, arbitrary mod messages, etc.
            return null;
        }

        private static Sprite RestingIcon()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
                return null;
            StatusEffect effect =
                db.GetStatusEffect("Resting".GetStableHashCode());
            if (effect == null)
                effect = db.GetStatusEffect("Rested".GetStableHashCode());
            return effect != null ? effect.m_icon : null;
        }
    }
}
