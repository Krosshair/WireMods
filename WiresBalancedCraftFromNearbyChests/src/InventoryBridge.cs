using System.Collections.Generic;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class InventoryBridge
    {
        private static bool TryGetContext(
            Inventory inventory,
            out RouteContext context)
        {
            context = null;

            Player player = Player.m_localPlayer;
            if (player == null ||
                inventory == null ||
                !ReferenceEquals(
                    inventory,
                    player.GetInventory()))
                return false;

            return RoutingContext.TryGet(out context);
        }

        public static int CountItems(
            Inventory inventory,
            string name,
            int quality,
            bool matchWorldLevel)
        {
            Prof.Begin(Prof.CountItems);
            try { return CountItemsTimed(inventory, name, quality, matchWorldLevel); }
            finally { Prof.End(Prof.CountItems); }
        }

        private static int CountItemsTimed(
            Inventory inventory,
            string name,
            int quality,
            bool matchWorldLevel)
        {
            int total =
                inventory.CountItems(
                    name,
                    quality,
                    matchWorldLevel);

            RouteContext context;
            if (!TryGetContext(inventory, out context))
                return total;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            // While a Multi User Chest request is being carried out, the
            // items it reserved count, not what's left in the chests.
            if (MultiUserChestCompat.Executing)
            {
                int reserved = 0;
                for (int i = 0; i < containers.Count; i++)
                {
                    Inventory effective =
                        MultiUserChestCompat.GetEffectiveInventory(containers[i]);
                    if (effective != null)
                        reserved += effective.CountItems(name, quality, matchWorldLevel);
                }
                return total + reserved;
            }

            // The same item is asked about many times per frame (every
            // requirement of every recipe or piece): the containers' part
            // is counted again only if something in an inventory changed
            // (chests loading another player's changes included) or the
            // container list was rebuilt. Your own inventory is always
            // counted fresh.
            if (_cacheMatching != ContainerRegistry.MatchingVersion ||
                _cacheInventories != InventoryVersion ||
                !ReferenceEquals(_cacheRoute, context.Definition))
            {
                CountCache.Clear();
                _cacheMatching = ContainerRegistry.MatchingVersion;
                _cacheInventories = InventoryVersion;
                _cacheRoute = context.Definition;
            }

            CountKey key = new CountKey(name, quality, matchWorldLevel);
            int stored;
            if (CountCache.TryGetValue(key, out stored))
                return total + stored;

            Prof.Begin(Prof.CountChests);
            int inContainers = 0;
            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    containers[i].GetInventory();

                if (chest != null)
                {
                    inContainers += chest.CountItems(
                        name,
                        quality,
                        matchWorldLevel);
                }
            }
            Prof.End(Prof.CountChests);

            CountCache[key] = inContainers;
            return total + inContainers;
        }

        // Bumped by any inventory change (patch below).
        internal static int InventoryVersion;

        private static int _cacheMatching = -1;
        private static int _cacheInventories = -1;
        private static RouteDefinition _cacheRoute;

        private static readonly Dictionary<CountKey, int> CountCache =
            new Dictionary<CountKey, int>();

        private struct CountKey : System.IEquatable<CountKey>
        {
            private readonly string _name;
            private readonly int _quality;
            private readonly bool _world;

            internal CountKey(string name, int quality, bool world)
            {
                _name = name;
                _quality = quality;
                _world = world;
            }

            public bool Equals(CountKey other)
            {
                return _quality == other._quality &&
                       _world == other._world &&
                       string.Equals(_name, other._name);
            }

            public override bool Equals(object obj)
            {
                return obj is CountKey && Equals((CountKey)obj);
            }

            public override int GetHashCode()
            {
                int h = _name != null ? _name.GetHashCode() : 0;
                return (h * 397) ^ (_quality * 31) ^ (_world ? 1 : 0);
            }
        }

        public static bool HaveItem(
            Inventory inventory,
            string name,
            bool matchWorldLevel)
        {
            Prof.Begin(Prof.HaveItem);
            try { return HaveItemTimed(inventory, name, matchWorldLevel); }
            finally { Prof.End(Prof.HaveItem); }
        }

        private static bool HaveItemTimed(
            Inventory inventory,
            string name,
            bool matchWorldLevel)
        {
            if (inventory.HaveItem(
                    name,
                    matchWorldLevel))
                return true;

            RouteContext context;
            if (!TryGetContext(inventory, out context))
                return false;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        containers[i]);

                if (chest != null &&
                    chest.HaveItem(
                        name,
                        matchWorldLevel))
                    return true;
            }

            return false;
        }

        public static ItemDrop.ItemData GetItem(
            Inventory inventory,
            string name,
            int quality,
            bool isPrefabName)
        {
            Prof.Begin(Prof.GetItem);
            try { return GetItemTimed(inventory, name, quality, isPrefabName); }
            finally { Prof.End(Prof.GetItem); }
        }

        private static ItemDrop.ItemData GetItemTimed(
            Inventory inventory,
            string name,
            int quality,
            bool isPrefabName)
        {
            ItemDrop.ItemData item =
                inventory.GetItem(
                    name,
                    quality,
                    isPrefabName);

            if (item != null)
                return item;

            RouteContext context;
            if (!TryGetContext(inventory, out context))
                return null;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        containers[i]);

                if (chest == null)
                    continue;

                item = chest.GetItem(
                    name,
                    quality,
                    isPrefabName);

                if (item != null)
                    return item;
            }

            return null;
        }

        public static void RemoveItem(
            Inventory inventory,
            string name,
            int amount,
            int itemQuality,
            bool worldLevelBased)
        {
            Prof.Begin(Prof.RemoveItem);
            try { RemoveItemTimed(inventory, name, amount, itemQuality, worldLevelBased); }
            finally { Prof.End(Prof.RemoveItem); }
        }

        private static void RemoveItemTimed(
            Inventory inventory,
            string name,
            int amount,
            int itemQuality,
            bool worldLevelBased)
        {
            RouteContext context;
            if (!TryGetContext(inventory, out context))
            {
                inventory.RemoveItem(
                    name,
                    amount,
                    itemQuality,
                    worldLevelBased);
                return;
            }

            if (context.Definition != null &&
                context.Definition.Kind == RouteKind.Refill &&
                MultiUserChestCompat.Active)
            {
                MultiUserChestCompat.PendingAction batch =
                    MultiUserChestCompat.PrepareName(
                        context,
                        name,
                        amount,
                        itemQuality,
                        worldLevelBased);

                if (batch != null)
                    throw new MucPendingException(batch);
            }

            int remaining = amount;

            // Building as you walk: the chests in range right now, not
            // the list from a moment ago. Stations don't move.
            if (context.Tool != null)
                ContainerRegistry.Invalidate();

            int own =
                inventory.CountItems(
                    name,
                    itemQuality,
                    worldLevelBased);

            int fromPlayer =
                Mathf.Min(remaining, own);

            if (fromPlayer > 0)
            {
                inventory.RemoveItem(
                    name,
                    fromPlayer,
                    itemQuality,
                    worldLevelBased);

                remaining -= fromPlayer;
            }

            if (remaining <= 0)
                return;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0;
                 i < containers.Count && remaining > 0;
                 i++)
            {
                Container container = containers[i];
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        container);

                if (chest == null)
                    continue;

                int available =
                    chest.CountItems(
                        name,
                        itemQuality,
                        worldLevelBased);

                if (available <= 0)
                    continue;

                Inventory live =
                    container.GetInventory();

                bool reserved =
                    !ReferenceEquals(
                        chest,
                        live);

                if (!reserved &&
                    !ContainerRegistry.TryTakeOwnership(
                        container))
                    continue;

                int taken =
                    Mathf.Min(
                        available,
                        remaining);

                // Inventory.Changed makes the now-owned Container
                // save the new inventory back to its vanilla ZDO.
                chest.RemoveItem(
                    name,
                    taken,
                    itemQuality,
                    worldLevelBased);

                remaining -= taken;
            }

            if (remaining > 0 &&
                Plugin.Log != null)
            {
                Plugin.Log.LogWarning(
                    "Could not consume " +
                    remaining + "x " + name +
                    " after checking the player and " +
                    containers.Count +
                    " matching chest(s).");
            }
        }

        public static int CountItemsByName(
            Inventory inventory,
            string[] names,
            int quality,
            bool matchWorldLevel,
            bool stacksOnly)
        {
            int total = inventory.CountItemsByName(
                names,
                quality,
                matchWorldLevel,
                stacksOnly);

            RouteContext context;
            if (!TryGetContext(inventory, out context))
                return total;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        containers[i]);
                if (chest != null)
                {
                    total += chest.CountItemsByName(
                        names,
                        quality,
                        matchWorldLevel,
                        stacksOnly);
                }
            }

            return total;
        }

        public static ItemDrop.ItemData GetAmmoItem(
            Inventory inventory,
            string ammoName,
            string matchPrefabName)
        {
            ItemDrop.ItemData item =
                inventory.GetAmmoItem(
                    ammoName,
                    matchPrefabName);

            if (item != null)
                return item;

            RouteContext context;
            if (!TryGetContext(inventory, out context))
                return null;

            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        containers[i]);
                if (chest == null)
                    continue;

                item = chest.GetAmmoItem(
                    ammoName,
                    matchPrefabName);

                if (item != null)
                    return item;
            }

            return null;
        }

        public static bool RemoveItemData(
            Inventory inventory,
            ItemDrop.ItemData item,
            int amount)
        {
            Prof.Begin(Prof.RemoveItem);
            try { return RemoveItemDataTimed(inventory, item, amount); }
            finally { Prof.End(Prof.RemoveItem); }
        }

        private static bool RemoveItemDataTimed(
            Inventory inventory,
            ItemDrop.ItemData item,
            int amount)
        {
            if (inventory == null || item == null)
                return false;

            if (inventory.ContainsItem(item))
                return inventory.RemoveItem(item, amount);

            RouteContext context;
            if (!TryGetContext(inventory, out context))
                return inventory.RemoveItem(item, amount);

            if (context.Definition != null &&
                context.Definition.Kind == RouteKind.Refill &&
                MultiUserChestCompat.Active)
            {
                MultiUserChestCompat.PendingAction batch =
                    MultiUserChestCompat.PrepareExactItem(
                        context,
                        item,
                        amount);

                if (batch != null)
                    throw new MucPendingException(batch);
            }

            if (context.Tool != null)
                ContainerRegistry.Invalidate();
            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0; i < containers.Count; i++)
            {
                Container container = containers[i];
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        container);

                if (chest == null ||
                    !chest.ContainsItem(item))
                    continue;

                Inventory live =
                    container.GetInventory();

                bool reserved =
                    !ReferenceEquals(
                        chest,
                        live);

                if (!reserved &&
                    !ContainerRegistry.TryTakeOwnership(
                        container))
                    return false;

                return chest.RemoveItem(item, amount);
            }

            return false;
        }

        public static bool RemoveOneItem(
            Inventory inventory,
            ItemDrop.ItemData item)
        {
            return RemoveItemData(
                inventory,
                item,
                1);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Inventory), "Changed")]
    internal static class InventoryChangedVersionPatch
    {
        private static void Postfix()
        {
            Prof.Begin(Prof.InventoryChanged);
            InventoryBridge.InventoryVersion++;
            Prof.End(Prof.InventoryChanged);
        }
    }
}
