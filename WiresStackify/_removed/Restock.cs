using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresStackify
{
    // Tops up your stacks from chests.
    internal static class Restock
    {
        private sealed class Want
        {
            public ItemDrop.ItemData Item;
            public int Have;
            public int Max;
        }

        private static int MaxFor(ItemDrop.ItemData.SharedData s)
        {
            int max = s.m_maxStackSize;
            int limit = 0;
            if (s.m_itemType == ItemDrop.ItemData.ItemType.Ammo) limit = StackifyPlugin.RestockLimitAmmo.Value;
            else if (s.m_itemType == ItemDrop.ItemData.ItemType.Consumable) limit = StackifyPlugin.RestockLimitConsumables.Value;
            if (limit <= 0) limit = StackifyPlugin.RestockLimitGeneral.Value;
            return limit > 0 ? Math.Min(max, limit) : max;
        }

        private static bool ShouldRestock(ItemDrop.ItemData item, Favorites fav, bool hotbar)
        {
            int max = MaxFor(item.m_shared);
            ItemDrop.ItemData.ItemType t = item.m_shared.m_itemType;
            return max > 1 && max > item.m_stack &&
                (item.m_customData == null || item.m_customData.Count == 0) &&
                (item.m_gridPos.y > 0 || hotbar) &&
                (!StackifyPlugin.RestockOnlyAmmoAndFood.Value ||
                    t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.Consumable) &&
                (!StackifyPlugin.RestockOnlyFavorites.Value || fav.IsFavorited(item));
        }

        internal static void Run(Player player, bool onlyThisChest)
        {
            if (player.IsTeleporting() || InventoryGui.instance == null || InventoryGui.instance.m_container == null) return;
            StackifyPlugin.DropDragItem();
            Favorites fav = Favorites.For(player);
            Inventory inv = player.GetInventory();
            bool hotbar = StackifyPlugin.RestockHotbar.Value;

            List<Want> wants = new List<Want>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
                if (ShouldRestock(it, fav, hotbar))
                    wants.Add(new Want { Item = it, Have = it.m_stack, Max = MaxFor(it.m_shared) });
            int total = wants.Count;
            if (total == 0)
            {
                if (StackifyPlugin.RestockMessage.Value) StackifyPlugin.Message(player, "Nothing to restock");
                return;
            }
            wants.Sort((a, b) => -StackifyPlugin.CompareSlotOrder(a.Item.m_gridPos, b.Item.m_gridPos));

            int full = 0;
            HashSet<Vector2i> touched = new HashSet<Vector2i>();
            Container current = StackifyPlugin.CurrentContainer(InventoryGui.instance);
            Container sound = current;
            if (current != null) full = FromChest(wants, inv, current.GetInventory(), touched, true);

            bool area = StackifyPlugin.RestockRange.Value > 0f &&
                (current == null || StackifyPlugin.RestockChestOpen.Value == StackifyPlugin.ChestOpen.AlsoNearbyChests) &&
                NearbyChests.AreaAllowed();
            if (!onlyThisChest && area)
            {
                List<Container> chests = NearbyChests.InRange(player.transform.position, StackifyPlugin.RestockRange.Value);
                bool single = NearbyChests.IsTrueSingleplayer();
                long id = player.GetPlayerID();
                foreach (Container c in chests)
                {
                    if (c == current || !NearbyChests.CanUse(c, id, single)) continue;
                    if (current == null) sound = StackifyPlugin.Closest(sound, c, player.transform.position);
                    if (StackifyPlugin.HasMuc)
                    {
                        full += FromChest(wants, inv, c.GetInventory(), touched, false);
                        continue;
                    }
                    NearbyChests.Claim(c, true);
                    full += FromChest(wants, inv, c.GetInventory(), touched, false);
                    NearbyChests.Claim(c, false);
                }
                StackifyPlugin.Changed(inv);
            }
            StackifyPlugin.ChestSound(sound);
            Report(player, full, touched.Count, total);
        }

        private static int FromChest(List<Want> wants, Inventory inv, Inventory chest, HashSet<Vector2i> touched, bool changed)
        {
            if (wants.Count == 0) return 0;
            int full = 0;
            List<ItemDrop.ItemData> inChest = chest.GetAllItems();
            for (int w = wants.Count - 1; w >= 0; w--)
            {
                Want want = wants[w];
                for (int i = inChest.Count - 1; i >= 0; i--)
                {
                    if (i >= inChest.Count) continue; // a stack was emptied and removed
                    ItemDrop.ItemData it = inChest[i];
                    if ((it.m_customData != null && it.m_customData.Count > 0) ||
                        it.m_shared.m_name != want.Item.m_shared.m_name || it.m_quality != want.Item.m_quality) continue;
                    int take = Math.Min(want.Max - want.Have, it.m_stack);
                    if (take > 0)
                    {
                        want.Have += take;
                        touched.Add(want.Item.m_gridPos);
                        inv.MoveItemToThis(chest, it, take, want.Item.m_gridPos.x, want.Item.m_gridPos.y);
                    }
                    if (want.Have == want.Max)
                    {
                        wants.RemoveAt(w);
                        full++;
                        break;
                    }
                }
            }
            if (changed) StackifyPlugin.Changed(inv);
            return full;
        }

        private static void Report(Player player, int full, int touched, int total)
        {
            if (!StackifyPlugin.RestockMessage.Value) return;
            string text;
            if (full == 0 && touched == 0) text = "Couldn't restock (0/" + total + ")";
            else if (full < total) text = "Partially restocked (" + touched + "/" + total + ")";
            else text = "Fully restocked (total: " + total + ")";
            StackifyPlugin.Message(player, text);
        }
    }
}
