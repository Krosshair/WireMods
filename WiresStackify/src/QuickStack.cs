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

        // onlyThisChest: the chest's own button, or Place stacks.
        // Returns how many stacks moved.
        internal static int Run(Player player, bool onlyThisChest, Container chestOverride = null, bool quiet = false)
        {
            if (player.IsTeleporting() || InventoryGui.instance == null || InventoryGui.instance.m_container == null) return 0;
            StackifyPlugin.DropDragItem();
            Favorites fav = Favorites.For(player);
            Inventory inv = player.GetInventory();
            bool hotbar = StackifyPlugin.QuickStackHotbar.Value;

            int rows = StackifyPlugin.MainRows(inv, player);
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
                if (ShouldStack(it, fav, hotbar, rows)) items.Add(it);
            if (items.Count == 0)
            {
                if (!quiet && StackifyPlugin.QuickStackMessage.Value) StackifyPlugin.Message(player, "Nothing to quickstack");
                if (!quiet) StackifyButtons.Feedback(StackifyEffects.Kind.Stack, 0, onlyThisChest);
                return 0;
            }
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
            if (current != null) { moved = IntoChest(trophies, items, inv, current.GetInventory(), true); if (moved > 0) hits.Add(new StackifyEffects.Hit { Chest = current, Count = moved }); }

            bool area = StackifyPlugin.QuickStackRange.Value > 0f &&
                (current == null || StackifyPlugin.QuickStackChestOpen.Value == StackifyPlugin.ChestOpen.AlsoNearbyChests) &&
                NearbyChests.AreaAllowed();
            if (!onlyThisChest && area)
            {
                List<Container> chests = NearbyChests.InRange(player.transform.position, StackifyPlugin.QuickStackRange.Value);
                bool single = NearbyChests.IsTrueSingleplayer();
                long id = player.GetPlayerID();
                foreach (Container c in chests)
                {
                    if (c == current || !NearbyChests.CanUse(c, id, single)) continue;
                    int n;
                    if (StackifyPlugin.HasMuc) n = IntoChest(trophies, items, inv, c.GetInventory(), false);
                    else
                    {
                        NearbyChests.Claim(c, true);
                        n = IntoChest(trophies, items, inv, c.GetInventory(), false);
                        NearbyChests.Claim(c, false);
                    }
                    if (n > 0) { moved += n; hits.Add(new StackifyEffects.Hit { Chest = c, Count = n }); }
                }
                StackifyPlugin.Changed(inv);
            }
            if (!quiet || moved > 0) StackifyButtons.Feedback(StackifyEffects.Kind.Stack, moved, onlyThisChest);
            StackifyEffects.Play(StackifyEffects.Kind.Stack, hits, current, player, onlyThisChest);
            if (!quiet) Report(player, moved);
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

        private static void Report(Player player, int moved)
        {
            if (!StackifyPlugin.QuickStackMessage.Value) return;
            StackifyPlugin.Message(player, moved == 0 ? "Nothing to quickstack" : "Quickstacked " + moved);
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
