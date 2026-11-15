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

        // ---- Holding an item over the chest's pouch ----
        // Tops up every stack of that kind you carry, to a full stack, from
        // that chest. Any item, whatever the restock settings say.
        private static List<Want> WantsFor(Player player, ItemDrop.ItemData kind)
        {
            List<Want> wants = new List<Want>();
            foreach (ItemDrop.ItemData it in player.GetInventory().GetAllItems())
                if (it.m_shared.m_name == kind.m_shared.m_name && it.m_quality == kind.m_quality &&
                    it.m_shared.m_maxStackSize > 1 && it.m_stack < it.m_shared.m_maxStackSize &&
                    (it.m_customData == null || it.m_customData.Count == 0))
                    wants.Add(new Want { Item = it, Have = it.m_stack, Max = it.m_shared.m_maxStackSize });
            return wants;
        }

        // Never past what you can carry: how much weight is left before
        // you'd be over (nothing if you already are).
        private static float Room(Player player)
        {
            return Math.Max(0f, player.GetMaxCarryWeight() - player.GetInventory().GetTotalWeight());
        }

        // How many of a want fit in the weight that's left.
        private static int Fits(Want want, int take, float room)
        {
            float unit = want.Item.GetWeight(1);
            if (unit <= 0f) return take;
            return Math.Min(take, (int)Math.Floor(room / unit + 0.0001f));
        }

        private static List<Want> NormalWants(Player player)
        {
            Favorites fav = Favorites.For(player);
            bool hotbar = StackifyPlugin.RestockHotbar.Value;
            List<Want> wants = new List<Want>();
            foreach (ItemDrop.ItemData it in player.GetInventory().GetAllItems())
                if (ShouldRestock(it, fav, hotbar))
                    wants.Add(new Want { Item = it, Have = it.m_stack, Max = MaxFor(it.m_shared) });
            wants.Sort((a, b) => -StackifyPlugin.CompareSlotOrder(a.Item.m_gridPos, b.Item.m_gridPos));
            return wants;
        }

        internal static List<ItemDrop.ItemData> PreviewOnly(Player player, Container chest, ItemDrop.ItemData kind, out float weight)
        {
            weight = 0f;
            if (player == null || chest == null || kind == null) return new List<ItemDrop.ItemData>();
            List<Want> wants = WantsFor(player, kind);
            wants.Sort((a, b) => -StackifyPlugin.CompareSlotOrder(a.Item.m_gridPos, b.Item.m_gridPos));
            int n;
            return Simulate(wants, new List<Container> { chest }, Room(player), out n, out weight);
        }

        // Returns how many stacks were topped up.
        internal static int RunOnly(Player player, Container chest, ItemDrop.ItemData kind)
        {
            if (player == null || chest == null || kind == null || player.IsTeleporting()) return 0;
            Inventory inv = player.GetInventory();
            List<Want> wants = WantsFor(player, kind);
            if (wants.Count == 0) return 0;
            wants.Sort((a, b) => -StackifyPlugin.CompareSlotOrder(a.Item.m_gridPos, b.Item.m_gridPos));
            HashSet<Vector2i> touched = new HashSet<Vector2i>();
            float room = Room(player);
            bool took;
            FromChest(wants, inv, chest.GetInventory(), touched, ref room, out took);
            if (touched.Count > 0) StackifyPlugin.Changed(inv);
            List<StackifyEffects.Hit> hits = new List<StackifyEffects.Hit>();
            if (took) hits.Add(new StackifyEffects.Hit { Chest = chest, Count = touched.Count });
            if (touched.Count > 0) StackifyButtons.Feedback(StackifyEffects.Kind.Restock, touched.Count, true);
            StackifyEffects.Play(StackifyEffects.Kind.Restock, hits, chest, player, true);
            return touched.Count;
        }

        // Returns how many stacks were topped up.
        internal static int Run(Player player, bool onlyThisChest)
        {
            if (player.IsTeleporting() || InventoryGui.instance == null || InventoryGui.instance.m_container == null) return 0;
            StackifyPlugin.DropDragItem();
            Inventory inv = player.GetInventory();
            List<Want> wants = NormalWants(player);
            if (wants.Count == 0) return 0;
            float room = Room(player);

            HashSet<Vector2i> touched = new HashSet<Vector2i>();
            Container current = StackifyPlugin.CurrentContainer(InventoryGui.instance);
            List<StackifyEffects.Hit> hits = new List<StackifyEffects.Hit>();
            foreach (Container c in NearbyChests.ForPouch(player, onlyThisChest, current, StackifyPlugin.RestockRange.Value))
            {
                if (wants.Count == 0 || room <= 0f) break;
                bool took;
                int before = touched.Count;
                if (c == current || StackifyPlugin.HasMuc) FromChest(wants, inv, c.GetInventory(), touched, ref room, out took);
                else
                {
                    NearbyChests.Claim(c, true);
                    FromChest(wants, inv, c.GetInventory(), touched, ref room, out took);
                    NearbyChests.Claim(c, false);
                }
                if (took) hits.Add(new StackifyEffects.Hit { Chest = c, Count = touched.Count - before });
            }
            if (touched.Count > 0) StackifyPlugin.Changed(inv);
            if (touched.Count > 0) StackifyButtons.Feedback(StackifyEffects.Kind.Restock, touched.Count, onlyThisChest);
            StackifyEffects.Play(StackifyEffects.Kind.Restock, hits, current, player, onlyThisChest);
            return touched.Count;
        }

        private static void FromChest(List<Want> wants, Inventory inv, Inventory chest, HashSet<Vector2i> touched, ref float room, out bool took)
        {
            took = false;
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
                    int take = Fits(want, Math.Min(want.Max - want.Have, it.m_stack), room);
                    if (take <= 0) break; // full, or too heavy to carry any more of
                    want.Have += take;
                    room -= want.Item.GetWeight(take);
                    took = true;
                    touched.Add(want.Item.m_gridPos);
                    inv.MoveItemToThis(chest, it, take, want.Item.m_gridPos.x, want.Item.m_gridPos.y);
                    if (want.Have >= want.Max) { wants.RemoveAt(w); break; }
                }
            }
        }

        // Hover preview: which of your stacks a restock would top up, from
        // how many containers and how much heavier you'd be, without moving
        // anything.
        internal static List<ItemDrop.ItemData> Preview(Player player, bool onlyThisChest, out int containers, out float weight)
        {
            containers = 0;
            weight = 0f;
            if (InventoryGui.instance == null) return new List<ItemDrop.ItemData>();
            Container current = StackifyPlugin.CurrentContainer(InventoryGui.instance);
            if (onlyThisChest && current == null) return new List<ItemDrop.ItemData>();
            List<Container> chests = NearbyChests.ForPouch(player, onlyThisChest, current, StackifyPlugin.RestockRange.Value);
            return Simulate(NormalWants(player), chests, Room(player), out containers, out weight);
        }

        // A restock on paper, the same way Run does it: container by
        // container, each stack topped up as far as there's some and you can
        // carry it.
        private static List<ItemDrop.ItemData> Simulate(List<Want> wants, List<Container> chests, float room,
            out int containers, out float weight)
        {
            containers = 0;
            weight = 0f;
            List<ItemDrop.ItemData> result = new List<ItemDrop.ItemData>();
            if (wants.Count == 0) return result;
            foreach (Container c in chests)
            {
                if (wants.Count == 0 || room <= 0f) break;
                Dictionary<string, int> count = new Dictionary<string, int>();
                foreach (ItemDrop.ItemData s in c.GetInventory().GetAllItems())
                {
                    if (s.m_customData != null && s.m_customData.Count > 0) continue;
                    string k = s.m_shared.m_name + "|" + s.m_quality;
                    int v;
                    count.TryGetValue(k, out v);
                    count[k] = v + s.m_stack;
                }
                bool used = false;
                for (int w = wants.Count - 1; w >= 0; w--)
                {
                    Want want = wants[w];
                    string key = want.Item.m_shared.m_name + "|" + want.Item.m_quality;
                    int avail;
                    if (!count.TryGetValue(key, out avail) || avail <= 0) continue;
                    int take = Fits(want, Math.Min(want.Max - want.Have, avail), room);
                    if (take <= 0) continue;
                    count[key] = avail - take;
                    want.Have += take;
                    float add = want.Item.GetWeight(take);
                    room -= add;
                    weight += add;
                    used = true;
                    if (!result.Contains(want.Item)) result.Add(want.Item);
                    if (want.Have >= want.Max) wants.RemoveAt(w);
                }
                if (used) containers++;
            }
            return result;
        }
    }
}
