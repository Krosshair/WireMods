using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class RefillSelection
    {
        internal static ItemDrop.ItemData FindFirstInChests(
            Component component,
            Func<ItemDrop.ItemData, bool> accepts)
        {
            Prof.Begin(Prof.FindInChests);
            try { return FindFirstInChestsTimed(component, accepts); }
            finally { Prof.End(Prof.FindInChests); }
        }

        private static ItemDrop.ItemData FindFirstInChestsTimed(
            Component component,
            Func<ItemDrop.ItemData, bool> accepts)
        {
            if (component == null || accepts == null)
                return null;

            RouteDefinition route =
                RefillRouting.Resolve(component);

            if (route == null)
                return null;

            var context = new RouteContext
            {
                Definition = route,
                Origin = component.transform.position,
                Range = RangeProvider.GetSharedStationRadius()
            };
            List<Container> containers =
                ContainerRegistry.GetMatching(context);

            for (int i = 0; i < containers.Count; i++)
            {
                Inventory chest =
                    MultiUserChestCompat.GetEffectiveInventory(
                        containers[i]);

                if (chest == null)
                    continue;

                // The bottom-left-most item it takes: one pass, no copy
                // of the chest's items and no sorting.
                ItemDrop.ItemData best = null;
                List<ItemDrop.ItemData> items = chest.GetAllItems();
                for (int j = 0; j < items.Count; j++)
                {
                    ItemDrop.ItemData item = items[j];
                    if (item == null ||
                        (best != null &&
                         (item.m_gridPos.y < best.m_gridPos.y ||
                          (item.m_gridPos.y == best.m_gridPos.y &&
                           item.m_gridPos.x >= best.m_gridPos.x))))
                        continue;
                    if (accepts(item))
                        best = item;
                }

                if (best != null)
                    return best;
            }

            return null;
        }

        internal static List<ItemDrop.ItemData> BottomLeftFirst(
            Inventory inventory)
        {
            var items =
                inventory != null
                    ? new List<ItemDrop.ItemData>(
                        inventory.GetAllItems())
                    : new List<ItemDrop.ItemData>();

            items.Sort(
                delegate(
                    ItemDrop.ItemData a,
                    ItemDrop.ItemData b)
                {
                    int byRow =
                        b.m_gridPos.y.CompareTo(
                            a.m_gridPos.y);

                    if (byRow != 0)
                        return byRow;

                    return a.m_gridPos.x.CompareTo(
                        b.m_gridPos.x);
                });

            return items;
        }

        internal static bool IsLocalPlayerInventory(
            Inventory inventory)
        {
            Player player = Player.m_localPlayer;
            return player != null &&
                   inventory != null &&
                   ReferenceEquals(
                       inventory,
                       player.GetInventory());
        }
    }
}
