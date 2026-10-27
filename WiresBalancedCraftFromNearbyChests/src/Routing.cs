using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal enum RouteKind
    {
        Station,
        Tool,
        Refill
    }

    internal sealed class RouteDefinition
    {
        internal string Id;
        internal string ConfigKey;
        internal RouteKind Kind;
        internal ConfigEntry<string>[] KeywordEntries;
        // The station's crafts raise the Crafting skill (not Cooking):
        // part of the shared Crafting group.
        internal bool CraftingSkill;

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

                return KeywordSync.GetKeywords(Id, local);
            }
        }
    }

    internal sealed class RouteContext
    {
        internal RouteDefinition Definition;
        internal Vector3 Origin;
        internal float Range;
        internal CraftingStation Station;
        internal ItemDrop.ItemData Tool;
    }

    internal static class RouteRegistry
    {
        private static readonly Dictionary<string, RouteDefinition> Routes =
            new Dictionary<string, RouteDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<RouteDefinition> RouteOrder =
            new List<RouteDefinition>();

        private static bool _validatingAliases;

        private static readonly Dictionary<string, string[]> VanillaStationDefaults =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "$piece_workbench", new[] { "Work Bench", "Workbench", "" } },
                { "$piece_forge", new[] { "Forge", "", "" } },
                { "$piece_stonecutter", new[] { "Stonecutter", "Stone Cutter", "" } },
                { "$piece_artisanstation", new[] { "Artisantable", "Artisan Table", "Artisan" } },
                { "$piece_blackforge", new[] { "Blackforge", "Black Forge", "" } },
                { "$piece_magetable", new[] { "Galdr", "Galdr Table", "Magic" } },
                { "$piece_cauldron", new[] { "Food Cauldron", "Cauldron", "Cooking" } },
                { "$piece_preptable", new[] { "Food Preparation Table", "Food Table", "Food Preparation" } },
                { "$piece_meadcauldron", new[] { "Mead", "Mead Cauldron", "Brewing" } }
            };

        internal static IEnumerable<RouteDefinition> All
        {
            get { return RouteOrder; }
        }

        internal static void SeedKnownRoutes()
        {
            SeedStation("$piece_workbench", "Workbench", "Work Bench", "Workbench", "");
            SeedStation("$piece_forge", "Forge", "Forge", "", "");
            SeedStation("$piece_stonecutter", "Stonecutter", "Stonecutter", "Stone Cutter", "");
            SeedStation("$piece_artisanstation", "Artisan Table", "Artisantable", "Artisan Table", "Artisan");
            SeedStation("$piece_blackforge", "Black Forge", "Blackforge", "Black Forge", "");
            SeedStation("$piece_magetable", "Galdr Table", "Galdr", "Galdr Table", "Magic");
            SeedStation("$piece_cauldron", "Cauldron", "Food Cauldron", "Cauldron", "Cooking");
            SeedStation("$piece_preptable", "Food Preparation Table", "Food Preparation Table", "Food Table", "Food Preparation");
            SeedStation("$piece_meadcauldron", "Mead Cauldron", "Mead", "Mead Cauldron", "Brewing");

            SeedTool("Hammer", "Hammer", "Hammer", "Building", "");
            SeedTool("Hoe", "Hoe", "Hoe", "Landscaping", "");
            SeedTool("Cultivator", "Cultivator", "Cultivator", "Farming", "");
            SeedTool("Feaster", "Serving Tray", "Serving Tray", "Tray", "Feasts");

            SeedRefill("charcoalkiln", "Charcoal Kiln", "Charcoal Kiln", "Kiln", "");
            SeedRefill("frostkiln", "Frigid Kiln", "Frigid Kiln", "Frost Kiln", "Liquid Frost");
            SeedRefill("frostfoundry", "Frost Foundry", "Frost Foundry", "Foundry", "Casts");
            SeedRefill("smelter", "Smelter", "Smelter", "", "");
            SeedRefill("blastfurnace", "Blast Furnace", "Blast Furnace", "Furnace", "");
            SeedRefill("refuel", "Refuel", "Refuel", "Fuel", "");
            SeedRefill("eitrrefinery", "Eitr Refinery", "Eitr Refinery", "Refinery", "Eitr");
            SeedRefill("windmill", "Windmill", "Windmill", "Mill", "Barley");
            SeedRefill("spinningwheel", "Spinning Wheel", "Spinning Wheel", "Spinning", "Flax");
            SeedRefill("stoneoven", "Stone Oven", "Stone Oven", "Oven", "Baking");
            SeedRefill("cookingstation", "Cooking Station", "Cooking Station", "", "Cook");
            SeedRefill("fermenter", "Fermenter", "Fermenter", "Fermentation", "Mead Base");
            SeedRefill("ballista", "Ballista", "Ballista", "Turret", "Ammo");
            SeedRefill("shieldgenerator", "Shield Generator", "Shield Generator", "Shield", "");
            SeedRefill("catapult", "Catapult", "Catapult", "", "");

            ValidateLoadedAliases();
        }

        private static void SeedStation(
            string stationName,
            string configKey,
            params string[] defaultKeywords)
        {
            string id = "station:" + stationName;
            if (Routes.ContainsKey(id))
                return;

            ConfigEntry<string>[] entries =
                ModConfig.BindStationKeywords(
                    configKey,
                    defaultKeywords);

            var route = new RouteDefinition
            {
                Id = id,
                ConfigKey = configKey,
                Kind = RouteKind.Station,
                KeywordEntries = entries,
                CraftingSkill = !CookingStations.Contains(stationName)
            };

            Routes.Add(id, route);
            RouteOrder.Add(route);
            AttachKeywordHandlers(route);
        }

        // Vanilla stations whose crafts raise Cooking instead of Crafting.
        private static readonly HashSet<string> CookingStations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "$piece_cauldron", "$piece_preptable", "$piece_meadcauldron" };

        private static void SeedTool(
            string prefabName,
            string configKey,
            params string[] defaultKeywords)
        {
            string id = "tool:" + prefabName;
            if (Routes.ContainsKey(id))
                return;

            ConfigEntry<string>[] entries =
                ModConfig.BindToolKeywords(
                    configKey,
                    defaultKeywords);

            var route = new RouteDefinition
            {
                Id = id,
                ConfigKey = configKey,
                Kind = RouteKind.Tool,
                KeywordEntries = entries
            };

            Routes.Add(id, route);
            RouteOrder.Add(route);
            AttachKeywordHandlers(route);
        }

        private static void SeedRefill(
            string routeName,
            string configKey,
            params string[] defaultKeywords)
        {
            string id = "refill:" + routeName;
            if (Routes.ContainsKey(id))
                return;

            ConfigEntry<string>[] entries =
                ModConfig.BindRefillKeywords(
                    configKey,
                    defaultKeywords);

            var route = new RouteDefinition
            {
                Id = id,
                ConfigKey = configKey,
                Kind = RouteKind.Refill,
                KeywordEntries = entries
            };

            Routes.Add(id, route);
            RouteOrder.Add(route);
            AttachKeywordHandlers(route);
        }

        internal static RouteDefinition GetRefill(
            string routeName)
        {
            return Get("refill:" + routeName);
        }

        // Asked every frame while crafting or building: answered from a
        // cache per station and per tool, instead of building name strings.
        private static readonly Dictionary<int, RouteDefinition> StationCache =
            new Dictionary<int, RouteDefinition>();
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, RouteDefinition> ToolCache =
            new Dictionary<ItemDrop.ItemData.SharedData, RouteDefinition>();

        internal static void ClearInstanceCache()
        {
            StationCache.Clear();
            ToolCache.Clear();
        }

        internal static RouteDefinition EnsureStation(
            CraftingStation station,
            string prefabName = null)
        {
            if (station == null)
                return null;

            RouteDefinition cached;
            int stationId = station.GetInstanceID();
            if (prefabName == null && StationCache.TryGetValue(stationId, out cached))
                return cached;
            RouteDefinition resolved = EnsureStationUncached(station, prefabName);
            if (prefabName == null && resolved != null)
            {
                if (StationCache.Count > 1024)
                    StationCache.Clear();
                StationCache[stationId] = resolved;
            }
            return resolved;
        }

        private static RouteDefinition EnsureStationUncached(
            CraftingStation station,
            string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                prefabName = Utils.GetPrefabName(station.gameObject);

            string stationIdentity =
                !string.IsNullOrEmpty(station.m_name)
                    ? station.m_name
                    : prefabName;

            string id = "station:" + stationIdentity;
            RouteDefinition existing;
            if (Routes.TryGetValue(id, out existing))
                return existing;

            string[] defaultKeywords = DefaultStationKeywords(station);
            ConfigEntry<string>[] entries =
                ModConfig.BindStationKeywords(
                    prefabName,
                    defaultKeywords);

            var route = new RouteDefinition
            {
                Id = id,
                ConfigKey = prefabName,
                Kind = RouteKind.Station,
                KeywordEntries = entries,
                // Same rule as the game: crafts here raise Crafting.
                CraftingSkill = station.m_craftingSkill == Skills.SkillType.Crafting
            };

            Routes.Add(id, route);
            RouteOrder.Add(route);
            AttachKeywordHandlers(route);
            ValidateRouteAliases(route);
            // Chests already tagged for the Crafting group now include it.
            if (route.CraftingSkill)
                ContainerRegistry.ReindexAll();
            return route;
        }

        internal static RouteDefinition EnsureTool(
            ItemDrop.ItemData item,
            string prefabName = null)
        {
            if (item == null ||
                item.m_shared == null ||
                item.m_shared.m_buildPieces == null)
                return null;

            RouteDefinition cached;
            if (prefabName == null && ToolCache.TryGetValue(item.m_shared, out cached))
                return cached;
            RouteDefinition resolved = EnsureToolUncached(item, prefabName);
            if (prefabName == null && resolved != null)
                ToolCache[item.m_shared] = resolved;
            return resolved;
        }

        private static RouteDefinition EnsureToolUncached(
            ItemDrop.ItemData item,
            string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName =
                    item.m_dropPrefab != null
                        ? item.m_dropPrefab.name
                        : StripToken(item.m_shared.m_name);
            }

            string id = "tool:" + prefabName;
            RouteDefinition existing;
            if (Routes.TryGetValue(id, out existing))
                return existing;

            string[] defaultKeywords =
                DefaultToolKeywords(item, prefabName);

            ConfigEntry<string>[] entries =
                ModConfig.BindToolKeywords(
                    prefabName,
                    defaultKeywords);

            var route = new RouteDefinition
            {
                Id = id,
                ConfigKey = prefabName,
                Kind = RouteKind.Tool,
                KeywordEntries = entries
            };

            Routes.Add(id, route);
            RouteOrder.Add(route);
            AttachKeywordHandlers(route);
            ValidateRouteAliases(route);
            return route;
        }

        internal static string ClassifyTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;

            RouteDefinition best = null;
            int bestIndex = int.MaxValue;
            int bestLength = -1;

            foreach (RouteDefinition route in Routes.Values)
            {
                string[] keywords = route.Keywords;
                for (int i = 0; i < keywords.Length; i++)
                {
                    string keyword = keywords[i];
                    if (string.IsNullOrWhiteSpace(keyword))
                        continue;

                    keyword = keyword.Trim();
                    int index = title.IndexOf(
                        keyword,
                        StringComparison.OrdinalIgnoreCase);

                    if (index < 0)
                        continue;

                    if (index < bestIndex ||
                        (index == bestIndex && keyword.Length > bestLength) ||
                        (index == bestIndex &&
                         keyword.Length == bestLength &&
                         best != null &&
                         string.CompareOrdinal(route.Id, best.Id) < 0))
                    {
                        best = route;
                        bestIndex = index;
                        bestLength = keyword.Length;
                    }
                }
            }

            return best != null ? best.Id : null;
        }

        internal static RouteDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            RouteDefinition route;
            return Routes.TryGetValue(id, out route)
                ? route
                : null;
        }

        private static string[] DefaultStationKeywords(
            CraftingStation station)
        {
            string[] values;
            if (!string.IsNullOrEmpty(station.m_name) &&
                VanillaStationDefaults.TryGetValue(
                    station.m_name,
                    out values))
                return values;

            return new[]
            {
                StripToken(station.m_name),
                "",
                ""
            };
        }

        private static string[] DefaultToolKeywords(
            ItemDrop.ItemData item,
            string prefabName)
        {
            string fromName = StripToken(item.m_shared.m_name);
            string primary =
                !string.IsNullOrWhiteSpace(fromName)
                    ? fromName
                    : (prefabName ?? "").ToLowerInvariant();

            return new[] { primary, "", "" };
        }

        private static void AttachKeywordHandlers(
            RouteDefinition route)
        {
            if (route == null ||
                route.KeywordEntries == null)
                return;

            for (int i = 0;
                 i < route.KeywordEntries.Length;
                 i++)
            {
                ConfigEntry<string> entry =
                    route.KeywordEntries[i];

                if (entry == null)
                    continue;

                entry.SettingChanged += delegate
                {
                    if (_validatingAliases)
                        return;

                    ValidateChangedAlias(route, entry);
                    ContainerRegistry.ReindexAll();
                    KeywordSync.NotifyLocalKeywordChanged();
                };
            }
        }

        private static void ValidateChangedAlias(
            RouteDefinition route,
            ConfigEntry<string> entry)
        {
            string alias =
                entry != null
                    ? (entry.Value ?? "").Trim()
                    : "";

            if (alias.Length == 0)
                return;

            RouteDefinition owner =
                FindOtherAliasOwner(route, alias);

            if (owner == null)
                return;

            _validatingAliases = true;
            try
            {
                entry.Value = "";
            }
            finally
            {
                _validatingAliases = false;
            }

            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning(
                    "Chest alias '" + alias +
                    "' already belongs to '" +
                    owner.ConfigKey +
                    "'. Cleared duplicate from '" +
                    route.ConfigKey + "'.");
            }
        }

        private static RouteDefinition FindOtherAliasOwner(
            RouteDefinition route,
            string alias)
        {
            for (int r = 0; r < RouteOrder.Count; r++)
            {
                RouteDefinition candidate = RouteOrder[r];
                if (candidate == null ||
                    candidate == route ||
                    candidate.KeywordEntries == null)
                    continue;

                for (int i = 0;
                     i < candidate.KeywordEntries.Length;
                     i++)
                {
                    ConfigEntry<string> entry =
                        candidate.KeywordEntries[i];

                    string value =
                        entry != null
                            ? (entry.Value ?? "").Trim()
                            : "";

                    if (value.Length > 0 &&
                        string.Equals(
                            value,
                            alias,
                            StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            return null;
        }

        private static void ValidateRouteAliases(
            RouteDefinition route)
        {
            if (route == null ||
                route.KeywordEntries == null)
                return;

            for (int i = 0;
                 i < route.KeywordEntries.Length;
                 i++)
            {
                ConfigEntry<string> entry =
                    route.KeywordEntries[i];

                if (entry == null)
                    continue;

                ValidateChangedAlias(route, entry);
            }
        }

        private static void ValidateLoadedAliases()
        {
            var owners =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            _validatingAliases = true;
            try
            {
                for (int r = 0; r < RouteOrder.Count; r++)
                {
                    RouteDefinition route = RouteOrder[r];
                    if (route == null ||
                        route.KeywordEntries == null)
                        continue;

                    for (int i = 0;
                         i < route.KeywordEntries.Length;
                         i++)
                    {
                        ConfigEntry<string> entry =
                            route.KeywordEntries[i];

                        string alias =
                            entry != null
                                ? (entry.Value ?? "").Trim()
                                : "";

                        if (alias.Length == 0)
                            continue;

                        string ownerId;
                        if (owners.TryGetValue(
                                alias,
                                out ownerId) &&
                            !string.Equals(
                                ownerId,
                                route.Id,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Value = "";

                            if (Plugin.Log != null)
                            {
                                RouteDefinition owner = Get(ownerId);
                                Plugin.Log.LogWarning(
                                    "Chest alias '" + alias +
                                    "' was configured for both '" +
                                    (owner != null
                                        ? owner.ConfigKey
                                        : ownerId) +
                                    "' and '" + route.ConfigKey +
                                    "'. Kept the first and cleared the duplicate.");
                            }

                            continue;
                        }

                        owners[alias] = route.Id;
                    }
                }
            }
            finally
            {
                _validatingAliases = false;
            }
        }

        internal static string StripToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string text = value.Trim();
            if (text.StartsWith("$piece_", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(7);
            else if (text.StartsWith("$item_", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(6);
            else if (text.StartsWith("$", StringComparison.Ordinal))
            {
                int underscore = text.IndexOf('_');
                text = underscore >= 0 && underscore + 1 < text.Length
                    ? text.Substring(underscore + 1)
                    : text.Substring(1);
            }

            return text.Replace('_', ' ').Trim().ToLowerInvariant();
        }
    }

    internal static class RangeProvider
    {
        private static float _radius = -1f;

        // Resolved once per world: it's the workbench prefab's own value.
        internal static float GetSharedStationRadius()
        {
            if (_radius > 0f)
                return _radius;
            float radius = ResolveRadius();
            if (ZNetScene.instance != null)
                _radius = radius;
            return radius;
        }

        private static float ResolveRadius()
        {
            // Tool routes copy the configured workbench build radius itself.
            // They must not depend on which placed workbench happens to be
            // nearest, or on whether any workbench is placed at all.
            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                GameObject prefab =
                    scene.GetPrefab("piece_workbench");

                if (prefab != null)
                {
                    CraftingStation station =
                        prefab.GetComponentInChildren<CraftingStation>(
                            true);

                    if (station != null &&
                        station.m_rangeBuild > 0f)
                        return station.m_rangeBuild;
                }
            }

            // Defensive fallback for unusual modded startup order.
            foreach (CraftingStation updater in CraftingStation.Instances)
            {
                CraftingStation station = updater as CraftingStation;
                if (station != null &&
                    station.m_name == "$piece_workbench" &&
                    station.m_rangeBuild > 0f)
                    return station.m_rangeBuild;
            }

            return 10f;
        }

        internal static void Invalidate()
        {
            _radius = -1f;
        }
    }

    internal static class RoutingContext
    {
        private static int _buildDepth;
        private static int _refillDepth;
        private static RouteContext _refillContext;

        internal static bool EnterRefill(
            RouteDefinition route,
            Vector3 origin)
        {
            if (route == null)
                return false;

            if (_refillDepth == 0)
            {
                _refillContext = new RouteContext
                {
                    Definition = route,
                    Origin = origin,
                    Range = RangeProvider.GetSharedStationRadius()
                };
            }

            _refillDepth++;
            return true;
        }

        internal static void ExitRefill()
        {
            if (_refillDepth > 0)
                _refillDepth--;

            if (_refillDepth == 0)
                _refillContext = null;
        }

        internal static void EnterBuild()
        {
            _buildDepth++;
        }

        internal static void ExitBuild()
        {
            if (_buildDepth > 0)
                _buildDepth--;
        }

        internal static void Reset()
        {
            _buildDepth = 0;
            _refillDepth = 0;
            _refillContext = null;
        }

        // The game asks for item counts many times per frame while the
        // crafting panel or build menu is open. One context per frame and
        // kind instead of a new one for every call. A context handed out
        // is never changed afterwards (pending MUC actions keep theirs).
        private static int _toolFrame = -1, _stationFrame = -1;
        private static RouteContext _toolContext, _stationContext;

        internal static bool TryGet(out RouteContext context)
        {
            context = null;

            if (ModConfig.Enabled == null ||
                !ModConfig.Enabled.Value)
                return false;

            Player player = Player.m_localPlayer;
            if (player == null)
                return false;

            if (_refillDepth > 0 &&
                _refillContext != null)
            {
                context = _refillContext;
                return true;
            }

            if (_buildDepth > 0)
            {
                ItemDrop.ItemData tool = player.RightItem;
                if (tool != null &&
                    tool.m_shared != null &&
                    tool.m_shared.m_buildPieces != null)
                {
                    if (_toolFrame == Time.frameCount &&
                        _toolContext != null &&
                        _toolContext.Tool == tool)
                    {
                        context = _toolContext;
                        return true;
                    }

                    RouteDefinition route =
                        RouteRegistry.EnsureTool(tool);

                    if (route != null)
                    {
                        context = new RouteContext
                        {
                            Definition = route,
                            Origin = player.transform.position,
                            Range = RangeProvider.GetSharedStationRadius(),
                            Tool = tool
                        };
                        _toolContext = context;
                        _toolFrame = Time.frameCount;
                        return true;
                    }
                }
            }

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station == null)
                return false;

            if (_stationFrame == Time.frameCount &&
                _stationContext != null &&
                _stationContext.Station == station)
            {
                context = _stationContext;
                return true;
            }

            RouteDefinition stationRoute =
                RouteRegistry.EnsureStation(station);

            if (stationRoute == null)
                return false;

            context = new RouteContext
            {
                Definition = stationRoute,
                Origin = station.transform.position,
                Range = station.GetStationBuildRange(),
                Station = station
            };
            _stationContext = context;
            _stationFrame = Time.frameCount;

            return true;
        }
    }
}
