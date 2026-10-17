using BepInEx.Configuration;

namespace WiresBalancedCraftFromNearbyChests
{
    internal enum ChestNameHover
    {
        Vanilla,
        CustomName,
        TypeThenCustomName,
        CustomNameThenType
    }

    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowConnections;

        internal static ConfigEntry<bool> EnableCustomNames;
        internal static ConfigEntry<ChestNameHover> HoverNameFormat;
        internal static ConfigEntry<bool> ShowRenameHint;

        internal static ConfigFile File;

        internal static void Bind(ConfigFile config)
        {
            File = config;

            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Enable crafting and building from correctly named nearby chests.");

            ShowConnections = config.Bind(
                "Visuals",
                "Show connections",
                true,
                "Show vanilla-style connection effects for linked chests.");

            EnableCustomNames = config.Bind(
                "Chest Renaming",
                "Enable custom names",
                true,
                "Enable the standalone chest renaming fallback when another compatible chest renamer is not installed.");

            HoverNameFormat = config.Bind(
                "Chest Renaming",
                "Hover name format",
                ChestNameHover.TypeThenCustomName,
                "Chest name format shown in hover when the standalone rename fallback is active.");

            ShowRenameHint = config.Bind(
                "Chest Renaming",
                "Show rename hint in hover",
                true,
                "Show the Alt+Use rename hint when the standalone rename fallback is active.");
        }

        internal static ConfigEntry<string>[] BindStationKeywords(
            string key,
            params string[] defaults)
        {
            return BindKeywordSlots(
                "Station Keywords",
                key,
                defaults,
                "Chest titles containing this alternative are assigned to this crafting station. Matching is case-insensitive; spaces are literal. Leave blank to disable this alternative. If another route already uses the same alias, this field is automatically cleared.");
        }

        internal static ConfigEntry<string>[] BindToolKeywords(
            string key,
            params string[] defaults)
        {
            return BindKeywordSlots(
                "Build Tool Keywords",
                key,
                defaults,
                "Chest titles containing this alternative are assigned to this build tool. Matching is case-insensitive; spaces are literal. Leave blank to disable this alternative. If another route already uses the same alias, this field is automatically cleared.");
        }

        internal static ConfigEntry<string>[] BindRefillKeywords(
            string key,
            params string[] defaults)
        {
            return BindKeywordSlots(
                "Refill Keywords",
                key,
                defaults,
                "Chest titles containing this alternative are assigned to this refill/feed route. Matching is case-insensitive; spaces are literal. Leave blank to disable this alternative. If another route already uses the same alias, this field is automatically cleared.");
        }

        internal static ConfigEntry<string>[] BindSharedKeywords(
            string key,
            params string[] defaults)
        {
            return BindKeywordSlots(
                "Shared Keywords",
                key,
                defaults,
                "Chest titles containing this alternative are shared by all routes in this group. Matching is case-insensitive; spaces are literal. Leave blank to disable this alternative.");
        }

        private static ConfigEntry<string>[] BindKeywordSlots(
            string section,
            string key,
            string[] defaults,
            string description)
        {
            var entries = new ConfigEntry<string>[3];

            for (int i = 0; i < entries.Length; i++)
            {
                string defaultValue =
                    defaults != null && i < defaults.Length
                        ? defaults[i] ?? ""
                        : "";

                entries[i] = File.Bind(
                    section,
                    key + " " + (i + 1),
                    defaultValue,
                    description);
            }

            return entries;
        }
    }
}
