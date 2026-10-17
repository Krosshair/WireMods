using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace WiresBalancedCraftFromNearbyChests
{
    internal sealed class SharedRouteGroup
    {
        internal string Id;
        internal string ConfigKey;
        internal string[] RouteIds;
        internal ConfigEntry<string>[] KeywordEntries;

        internal string SyncId
        {
            get { return "shared:" + Id; }
        }

        internal string[] Keywords
        {
            get
            {
                var local = new string[3];

                if (KeywordEntries != null)
                {
                    for (int i = 0;
                         i < local.Length && i < KeywordEntries.Length;
                         i++)
                    {
                        local[i] =
                            KeywordEntries[i] != null
                                ? (KeywordEntries[i].Value ?? "").Trim()
                                : "";
                    }
                }

                return KeywordSync.GetKeywords(SyncId, local);
            }
        }
    }

    internal static class SharedRouteRegistry
    {
        private static readonly List<SharedRouteGroup> Groups =
            new List<SharedRouteGroup>();

        internal static IEnumerable<SharedRouteGroup> All
        {
            get { return Groups; }
        }

        internal static void SeedKnownGroups()
        {
            if (Groups.Count > 0)
                return;

            Seed(
                "smelting",
                "Smelting",
                new[]
                {
                    "refill:smelter",
                    "refill:blastfurnace"
                },
                "Smelting", "", "");

            Seed(
                "kitchen",
                "Kitchen",
                new[]
                {
                    "station:$piece_cauldron",
                    "station:$piece_preptable",
                    "station:$piece_meadcauldron"
                },
                "Kitchen", "", "");

            Seed(
                "forging",
                "Forging",
                new[]
                {
                    "station:$piece_forge",
                    "station:$piece_blackforge"
                },
                "Forging", "", "");

            Seed(
                "plainsprocessing",
                "Plains Processing",
                new[]
                {
                    "refill:windmill",
                    "refill:spinningwheel"
                },
                "Plains Processing", "", "");

            Seed(
                "frostprocessing",
                "Frost Processing",
                new[]
                {
                    "refill:frostkiln",
                    "refill:frostfoundry"
                },
                "Frost Processing", "", "");

            Seed(
                "siege",
                "Siege",
                new[]
                {
                    "refill:ballista",
                    "refill:catapult"
                },
                "Siege", "", "");
        }

        private static void Seed(
            string id,
            string configKey,
            string[] routeIds,
            params string[] defaultKeywords)
        {
            ConfigEntry<string>[] entries =
                ModConfig.BindSharedKeywords(
                    configKey,
                    defaultKeywords);

            var group = new SharedRouteGroup
            {
                Id = id,
                ConfigKey = configKey,
                RouteIds = routeIds,
                KeywordEntries = entries
            };

            Groups.Add(group);
            AttachKeywordHandlers(group);
        }

        internal static void AddMatchingRoutes(
            string title,
            HashSet<string> routeIds)
        {
            if (string.IsNullOrEmpty(title) ||
                routeIds == null)
                return;

            for (int g = 0; g < Groups.Count; g++)
            {
                SharedRouteGroup group = Groups[g];
                string[] keywords = group.Keywords;
                bool matches = false;

                for (int i = 0; i < keywords.Length; i++)
                {
                    string keyword =
                        (keywords[i] ?? "").Trim();

                    if (keyword.Length == 0)
                        continue;

                    if (title.IndexOf(
                            keyword,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                    continue;

                for (int i = 0; i < group.RouteIds.Length; i++)
                {
                    string routeId = group.RouteIds[i];

                    if (RouteRegistry.Get(routeId) != null)
                        routeIds.Add(routeId);
                }
            }
        }

        private static void AttachKeywordHandlers(
            SharedRouteGroup group)
        {
            if (group == null ||
                group.KeywordEntries == null)
                return;

            for (int i = 0;
                 i < group.KeywordEntries.Length;
                 i++)
            {
                ConfigEntry<string> entry =
                    group.KeywordEntries[i];

                if (entry == null)
                    continue;

                entry.SettingChanged += delegate
                {
                    ContainerRegistry.ReindexAll();
                    KeywordSync.NotifyLocalKeywordChanged();
                };
            }
        }
    }
}
