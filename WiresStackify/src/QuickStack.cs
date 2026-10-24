using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresStackify
{
    // Moves your items into chests that already hold the same item.
    internal static class QuickStack
    {
        private static readonly HashSet<string> Names = new HashSet<string>();

        private static bool ShouldStack(ItemDrop.ItemData item, Favorites fav, bool hotbar, int rows)
        {
            return item.m_shared.m_maxStackSize > 1 && (item.m_gridPos.y > 0 || hotbar) && item.m_gridPos.y < rows && !fav.IsFavorited(item);
        }

        // What a quick stack would take from your inventory. only: just
        // this kind (an item dropped on the pouch): every stack of it in
        // your inventory, the one you hold even if it's favorited or on the
        // hotbar.
        internal static List<ItemDrop.ItemData> Candidates(Player player, ItemDrop.ItemData only = null)
        {
            Favorites fav = Favorites.For(player);
            Inventory inv = player.GetInventory();
            bool hotbar = StackifyPlugin.QuickStackHotbar.Value;
            int rows = StackifyPlugin.MainRows(inv, player);
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                if (only == null) { if (ShouldStack(it, fav, hotbar, rows)) items.Add(it); }
                else if (it == only || (it.m_shared.m_name == only.m_shared.m_name && it.m_gridPos.y < rows &&
                         (it.m_gridPos.y > 0 || hotbar) && !fav.IsSlotFavorited(it.m_gridPos)))
                    items.Add(it);
            }
            return items;
        }

        // Hover preview: which of your stacks would go, and into how many
        // chests, without moving anything.
        internal static List<ItemDrop.ItemData> Preview(Player player, bool onlyThisChest, out int chests, ItemDrop.ItemData only = null)
        {
            chests = 0;
            List<ItemDrop.ItemData> result = new List<ItemDrop.ItemData>();
            if (InventoryGui.instance == null) return result;
            Container current = StackifyPlugin.CurrentContainer(InventoryGui.instance);
            if (onlyThisChest && current == null) return result;
            if (only != null && !player.GetInventory().ContainsItem(only)) return result;
            List<ItemDrop.ItemData> items = Candidates(player, only);
            if (items.Count == 0) return result;
            HashSet<Container> used = new HashSet<Container>();
            foreach (Container c in NearbyChests.ForPouch(player, onlyThisChest, current, StackifyPlugin.QuickStackRange.Value))
            {
                Inventory chest = c.GetInventory();
                Names.Clear();
                bool trophy = false;
                foreach (ItemDrop.ItemData it in chest.GetAllItems())
                {
                    Names.Add(it.m_shared.m_name);
                    if (it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) trophy = true;
                }
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData it = items[i];
                    bool match = Names.Contains(it.m_shared.m_name) ||
                        (trophy && StackifyPlugin.TrophiesTogether.Value && it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy);
                    if (!match || !chest.CanAddItem(it)) continue;
                    result.Add(it);
                    items.RemoveAt(i);
                    used.Add(c);
                }
            }
            chests = used.Count;
            return result;
        }

        // onlyThisChest: the chest's own button, or Place stacks.
        // Returns how many stacks moved.
        internal static int Run(Player player, bool onlyThisChest, Container chestOverride = null, ItemDrop.ItemData only = null)
        {
            if (player.IsTeleporting() || InventoryGui.instance == null || InventoryGui.instance.m_container == null) return 0;
            if (only == null) StackifyPlugin.DropDragItem();
            Inventory inv = player.GetInventory();
            List<ItemDrop.ItemData> items = Candidates(player, only);
            if (items.Count == 0) return 0;
            items.Sort((a, b) => -StackifyPlugin.CompareSlotOrder(a.m_gridPos, b.m_gridPos));

            List<ItemDrop.ItemData> trophies = null;
            if (StackifyPlugin.TrophiesTogether.Value)
            {
                trophies = new List<ItemDrop.ItemData>();
                for (int i = items.Count - 1; i >= 0; i--)
                    if (items[i].m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy)
                    {
                        trophies.Insert(0, items[i]);
                        items.RemoveAt(i);
                    }
            }

            int moved = 0;
            Container current = chestOverride != null ? chestOverride : StackifyPlugin.CurrentContainer(InventoryGui.instance);
            List<StackifyEffects.Hit> hits = new List<StackifyEffects.Hit>();
            foreach (Container c in NearbyChests.ForPouch(player, onlyThisChest, current, StackifyPlugin.QuickStackRange.Value))
            {
                int n;
                // The open chest is already yours; others are claimed while
                // items go in, unless Multi User Chest looks after that.
                if (c == current || StackifyPlugin.HasMuc) n = IntoChest(trophies, items, inv, c.GetInventory(), false);
                else
                {
                    NearbyChests.Claim(c, true);
                    n = IntoChest(trophies, items, inv, c.GetInventory(), false);
                    NearbyChests.Claim(c, false);
                }
                if (n > 0) { moved += n; hits.Add(new StackifyEffects.Hit { Chest = c, Count = n }); }
            }
            if (moved > 0) StackifyPlugin.Changed(inv);
            if (moved > 0) StackifyButtons.Feedback(StackifyEffects.Kind.Stack, moved, onlyThisChest);
            StackifyEffects.Play(StackifyEffects.Kind.Stack, hits, current, player, onlyThisChest);
            return moved;
        }

        private static int IntoChest(List<ItemDrop.ItemData> trophies, List<ItemDrop.ItemData> items,
            Inventory inv, Inventory chest, bool changed)
        {
            int moved = 0;
            List<ItemDrop.ItemData> inChest = chest.GetAllItems();
            if (trophies != null && trophies.Count > 0)
            {
                bool anyTrophy = false;
                foreach (ItemDrop.ItemData it in inChest)
                    if (it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) { anyTrophy = true; break; }
                if (anyTrophy)
                    for (int i = trophies.Count - 1; i >= 0; i--)
                        if (chest.AddItem(trophies[i]))
                        {
                            inv.RemoveItem(trophies[i]);
                            trophies.RemoveAt(i);
                            moved++;
                        }
            }
            if (items.Count > 0 && inChest.Count > 0)
            {
                // What the chest held before anything is added to it, then one
                // pass over your items.
                Names.Clear();
                foreach (ItemDrop.ItemData it in inChest) Names.Add(it.m_shared.m_name);
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData it = items[i];
                    if (!Names.Contains(it.m_shared.m_name) || !chest.AddItem(it)) continue;
                    inv.RemoveItem(it);
                    items.RemoveAt(i);
                    moved++;
                }
            }
            if (changed) StackifyPlugin.Changed(inv);
            return moved;
        }

        // Place stacks (the game's button and holding use on a chest).
        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        private static class PlaceStacksPatch
        {
            private static bool Prefix(Container __instance, bool granted)
            {
                if (!StackifyPlugin.HoldToStack.Value) return true;
                if (Player.m_localPlayer == null) return false;
                if (granted) Run(Player.m_localPlayer, true, __instance);
                else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return false;
            }
        }
    }
}
