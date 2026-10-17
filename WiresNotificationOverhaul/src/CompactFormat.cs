using System;
using System.Text.RegularExpressions;

namespace WiresNotificationOverhaul
{
    // Compactness is purely a presentation choice. Keep the original
    // unshortened source text for merging and notification identity.
    internal static class CompactFormat
    {
        internal enum Kind
        {
            Other, Pickup, Removed, Dropped, Broken,
            Level, FortifiedLevel, Rested, Resting, Pin, AutoPickup, Walk,
            PotionExpired, PotionAvailable, NewUnlocks, InventoryFull,
            OutOf, MissingRequirement, NeedFire, NeedStation,
            StackAll, NothingToStack, SkillsLowered, JoinCodeCopied
        }

        private static readonly Regex LevelAtEnd = new Regex(
            @"(?:[: ]\s*)(\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex ComfortLevel = new Regex(
            @"(?:comfort\s*:\s*|:\s*)(\d+)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] SimpleStatusLabels = {
            "Freezing", "Cold", "Wet", "Poisoned", "Burning",
            "Warm", "Sheltered", "Smoked", "Exposed",
            "Comfortable", "Rested", "Hunted"
        };

        internal static Kind Detect(string raw, string localized)
        {
            string original = raw ?? "";
            string value = localized ?? "";
            if (original.IndexOf("$inventory_logs_new", StringComparison.Ordinal) >= 0)
                return Kind.NewUnlocks;
            if (original == "$inventory_full")
                return Kind.InventoryFull;
            if (original.StartsWith("$msg_outof ", StringComparison.Ordinal))
                return Kind.OutOf;
            if (original == "$msg_missingrequirement")
                return Kind.MissingRequirement;
            if (original == "$msg_needfire")
                return Kind.NeedFire;
            if (original == "$msg_missingstation")
                return Kind.NeedStation;
            if (original.StartsWith("$msg_stackall ", StringComparison.Ordinal))
                return Kind.StackAll;
            if (original == "$msg_stackall_none")
                return Kind.NothingToStack;
            if (original == "$msg_skills_lowered")
                return Kind.SkillsLowered;
            if (original == "$menu_joincode_copied")
                return Kind.JoinCodeCopied;
            if (original.StartsWith("$msg_added ", StringComparison.Ordinal))
                return Kind.Pickup;
            if (original.StartsWith("$msg_removed ", StringComparison.Ordinal))
                return Kind.Removed;
            if (original.StartsWith("$msg_dropped ", StringComparison.Ordinal))
                return Kind.Dropped;
            if (original.StartsWith("$msg_pin_added", StringComparison.Ordinal))
                return Kind.Pin;
            if (original.StartsWith("$hud_autopickup", StringComparison.Ordinal))
                return Kind.AutoPickup;
            if (original.StartsWith("$msg_walk ", StringComparison.Ordinal))
                return Kind.Walk;
            if (original.IndexOf("$msg_broke", StringComparison.Ordinal) >= 0)
                return Kind.Broken;
            if (original.StartsWith("$msg_skillup ", StringComparison.Ordinal))
                return Kind.Level;
            if (original.StartsWith("Fortified skill improved ", StringComparison.OrdinalIgnoreCase))
                return Kind.FortifiedLevel;
            if (original.IndexOf("$se_rested_start", StringComparison.Ordinal) >= 0 ||
                value.StartsWith("You are now feeling rested", StringComparison.OrdinalIgnoreCase))
                return Kind.Rested;
            if (original.IndexOf("$se_resting", StringComparison.Ordinal) >= 0 ||
                string.Equals(value.Trim(), "Resting", StringComparison.OrdinalIgnoreCase))
                return Kind.Resting;
            return Kind.Other;
        }

        internal static string Text(Kind kind, string original, string localized,
            int amount, bool compact, string fortifiedBlue = "#79B9F0")
        {
            // All compact replacements below are authored in English.
            // On other game languages, keep the localized sentence
            // unless the abbreviated form is language-neutral.
            if (compact && !CompactLocalization.IsEnglish)
            {
                if (kind == Kind.Pickup)
                    return amount > 0 ? "+" + amount : localized;
                if (kind == Kind.Dropped || kind == Kind.Removed)
                    return amount > 0 ? "-" + amount : localized;
                if (kind == Kind.Level || kind == Kind.FortifiedLevel)
                    return SkillLevelText(kind, original, localized,
                        false, fortifiedBlue);
                return amount > 1 ? localized + " x" + amount : localized;
            }

            // Level numbers are colored in both display modes.
            // Compact mode replaces the complete skill message with
            // "LVL. <colored number>" while verbose mode preserves
            // the skill's name and explanation.
            if (kind == Kind.Level || kind == Kind.FortifiedLevel)
                return SkillLevelText(kind, original, localized,
                    compact, fortifiedBlue);

            if (compact)
            {
                // These tokens identify the precise failed requirement,
                // regardless of the user's Valheim display language.
                switch ((original ?? "").Trim())
                {
                    case "$msg_stationneedroof":
                        return "Requires roof";
                    case "$msg_stationtooexposed":
                        return "Requires shelter";
                    case "$msg_missingstation":
                        return "Requires station";
                    case "$msg_needfire":
                        return "Requires nearby fire";
                }
            }

            if (!compact)
                return amount > 1
                    ? localized + " x" + amount : localized;

            switch (kind)
            {
                case Kind.NewUnlocks:
                    // Valheim sends "5 $inventory_logs_new" when the
                    // unlock pop-ups overflow. Change the notification
                    // only; do not alter the log or unlock queue.
                    Match count = Regex.Match(original ?? "", @"^\s*(\d+)\s+\$inventory_logs_new\s*$");
                    return count.Success
                        ? "+" + count.Groups[1].Value + " new unlocks"
                        : localized;
                case Kind.InventoryFull:
                    return "Inventory full";
                case Kind.OutOf:
                    // "Out of Arrows" is already concise. Keep the
                    // item name and the natural wording intact.
                    return localized;
                case Kind.MissingRequirement:
                    return "Missing requirement";
                case Kind.NeedFire:
                    // Retain "nearby" rather than silently changing
                    // the requirement into a vague "Need fire".
                    return Regex.IsMatch(localized ?? "", @"^You need a fire nearby[.!]?$", RegexOptions.IgnoreCase) ? "Need nearby fire" : localized;
                case Kind.NeedStation:
                    return ShortenMessage(localized);
                case Kind.StackAll:
                    return ShortenMessage(localized);
                case Kind.NothingToStack:
                    return "Nothing to stack";
                case Kind.SkillsLowered:
                    return "Skills lowered";
                case Kind.JoinCodeCopied:
                    return "Join code copied";
                case Kind.Pickup:
                    return amount > 0 ? "+" + amount : localized;
                case Kind.Dropped:
                case Kind.Removed:
                    return amount > 0 ? "-" + amount : localized;
                case Kind.Broken:
                    return "Broken";

                case Kind.Rested:
                    Match comfort = ComfortLevel.Match(original ?? "");
                    if (!comfort.Success)
                        comfort = ComfortLevel.Match(localized ?? "");
                    return comfort.Success ? "Rested (" +
                        comfort.Groups[1].Value + ")" : "Rested";
                case Kind.Resting:
                    return "Resting";
                case Kind.Pin:
                    return ShortenMessage(localized);
                case Kind.AutoPickup:
                    return ShortenMessage(localized);
                case Kind.Walk:
                    return ShortenMessage(localized);
                case Kind.PotionExpired:
                    return "Expired";
                case Kind.PotionAvailable:
                    return "Available";
                default:
                    string shortened = ShortenMessage(localized);
                    return amount > 1
                        ? shortened + " x" + amount : shortened;
            }
        }

        // One compact pipeline for ALL message types, including
        // vanilla saves, achievement/cheated pickups, ordinary
        // center warnings and notifications from other mods.
        // Preserve details when shortening would change their meaning.
        internal static string ShortenMessage(string original)
        {
            if (string.IsNullOrWhiteSpace(original))
                return original;

            string value = original.Trim();
            if (!CompactLocalization.IsEnglish)
                return value;

            // The input is already trimmed; remove the optional final
            // punctuation without running a regex for every message.
            string plain = value;
            char last = plain[plain.Length - 1];
            if (last == '.' || last == '!')
                plain = plain.Substring(0, plain.Length - 1);
            string status = SimplifyStatusSentence(value);
            if (status != value)
                return status;
            // All remaining captured messages pass through v1.1.0
            // rules, while unknown patterns retain their original text.
            if (CompactV11.TryFormat(value, out string v11))
                return v11;

            // Cheated pickups and other warnings about permanently
            // disabling achievements should not occupy multiple lines.
            bool achievements = plain.IndexOf(
                "achiev", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Regex.IsMatch(plain,
                @"achieve?ments?", RegexOptions.IgnoreCase);
            bool disabled = achievements && Regex.IsMatch(plain,
                @"disabl|no longer (?:earn|get)|cannot (?:earn|get)|won.t (?:earn|get)",
                RegexOptions.IgnoreCase);
            bool cheatItem = plain.IndexOf(
                "cheat", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Regex.IsMatch(plain,
                @"cheat(?:ed|ing)?\s+(?:item|pickup)|(?:item|pickup).*cheat",
                RegexOptions.IgnoreCase);
            if ((achievements && disabled) || cheatItem)
                return "Achievements disabled";

            if (plain.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0 &&
                plain.IndexOf("sav", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Regex.IsMatch(plain,
                    @"(?:world\s+(?:has\s+been\s+)?saved|saved\s+(?:the\s+)?world)",
                    RegexOptions.IgnoreCase))
            {
                Match duration = Regex.Match(plain,
                    @"\b\d+(?:[\.,]\d+)?\s*(?:ms|milliseconds?|s|sec|seconds?)\b",
                    RegexOptions.IgnoreCase);
                return duration.Success
                    ? "World saved (" + duration.Value.Replace(" ", "") + ")"
                    : "World saved";
            }

            // No other patterns below can match a message that
            // doesn't start with "You"/"Automatic pickup".
            if (!plain.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
            {
                if (plain.StartsWith("Automatic pickup", StringComparison.OrdinalIgnoreCase) &&
                    Regex.IsMatch(plain, @"^automatic pickup\s*:\s*", RegexOptions.IgnoreCase))
                    return Regex.Replace(plain, @"^automatic pickup", "Auto pickup",
                        RegexOptions.IgnoreCase);
                return value;
            }

            if (Regex.IsMatch(plain, @"^you (?:have )?(?:picked up|obtained|received) ",
                RegexOptions.IgnoreCase))
                return Regex.Replace(plain,
                    @"^you (?:have )?(?:picked up|obtained|received) ",
                    "", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(plain, @"^you (?:have )?(?:discovered|unlocked) ",
                RegexOptions.IgnoreCase))
                return Regex.Replace(plain,
                    @"^you (?:have )?(?:discovered|unlocked) ",
                    "Unlocked ", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(plain,
                @"^you (?:have )?(?:successfully )?(?:saved|restored|repaired) ",
                RegexOptions.IgnoreCase))
                return Regex.Replace(plain,
                    @"^you (?:have )?(?:successfully )?",
                    "", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(plain,
                @"^automatic pickup\s*:\s*", RegexOptions.IgnoreCase))
                return Regex.Replace(plain,
                    @"^automatic pickup", "Auto pickup",
                    RegexOptions.IgnoreCase);

            // Generic unknown messages remain complete rather than
            // being truncated. Compact Mode still processes them, but
            // never invents a shortened status with missing details.
            return value;
        }

        // Only shorten complete English status sentences with
        // unambiguous meaning. Don't blindly strip "You feel"
        // from a long instruction or unrelated mod message.
        internal static string SimplifyStatusSentence(string original)
        {
            if (string.IsNullOrWhiteSpace(original))
                return original;

            string input = original.Trim();
            int length = input.Length;
            char finalChar = input[length - 1];
            if (finalChar == '.' || finalChar == '!')
                length--;

            // Whole-sentence wording only: never guess at instructions
            // or modded sentences with additional information.
            int offset;
            if (input.StartsWith("You are ", StringComparison.OrdinalIgnoreCase))
                offset = 8;
            else if (input.StartsWith("You feel ", StringComparison.OrdinalIgnoreCase))
                offset = 9;
            else
                return original;

            if (length <= offset)
                return original;

            string status = input.Substring(offset, length - offset);
            if (status.StartsWith("now ", StringComparison.OrdinalIgnoreCase))
                status = status.Substring(4);

            if (string.Equals(status, "warmer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "warming up", StringComparison.OrdinalIgnoreCase))
                return "Warming up";

            if (string.Equals(status, "dry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "no longer wet", StringComparison.OrdinalIgnoreCase))
                return "Dry";

            if (string.Equals(status, "on fire", StringComparison.OrdinalIgnoreCase) &&
                input.StartsWith("You are ", StringComparison.OrdinalIgnoreCase) &&
                !input.StartsWith("You are now ", StringComparison.OrdinalIgnoreCase))
                return "Burning";

            for (int i = 0; i < SimpleStatusLabels.Length; i++)
                if (string.Equals(status, SimpleStatusLabels[i],
                    StringComparison.OrdinalIgnoreCase))
                    return SimpleStatusLabels[i];

            return original;
        }
        private static string SkillLevelText(
            Kind kind, string original, string localized,
            bool compact, string fortifiedBlue)
        {
            string message = localized ?? "";
            Match value = LevelAtEnd.Match(message);
            if (!value.Success)
                value = LevelAtEnd.Match(original ?? "");
            if (!value.Success)
                return message;

            string color = kind == Kind.FortifiedLevel
                ? fortifiedBlue : "#E7C745";
            string colored = "<color=" + color + ">" +
                value.Groups[1].Value + "</color>";

            if (compact)
                return "LVL. " + colored;

            // Preserve localization and punctuation in verbose mode,
            // replacing only the trailing number, not the skill name.
            Match ending = LevelAtEnd.Match(message);
            if (!ending.Success)
                return message + " LVL. " + colored;
            return message.Substring(0, ending.Groups[1].Index) +
                colored + message.Substring(
                    ending.Groups[1].Index +
                    ending.Groups[1].Length);
        }


    }
}
