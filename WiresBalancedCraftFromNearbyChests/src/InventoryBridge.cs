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

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    containers[i].GetInventory();

                if (chest != null)
                {
                    total += chest.CountItems(
                        name,
                        quality,
                        matchWorldLevel);
                }
            }

            return total;
        }

        public static bool HaveItem(
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
                    containers[i].GetInventory();

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
                    containers[i].GetInventory();

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
}
