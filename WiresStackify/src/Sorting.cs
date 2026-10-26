using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WiresStackify
{
    // Sorting, from a pouch or a panel (see Actions for which click does
    // it). Filled from the bottom row up like Quick Stack Store, stacks
    // merged, by type (or by weight, heaviest first), ties broken by type,
    // name, quality and stack size. Favorites, the hotbar and Equipment and
    // Quick Slots' rows aren't part of it at all: they never move and
    // nothing is put on their slots.
    internal static class Sorting
    {
        internal enum Mode { Type, Weight }

        private const string RpcName = "WireStackify_RequestSort3";

        // Item type to sort group, as Quick Stack Store has it.
        private static readonly int[] TypeToCategory =
        {
            0, 2, 4, 7, 7, 8, 10, 10, 0, 6,
            0, 10, 10, 1, 7, 7, 0, 10, 9, 7,
            7, 3, 7, 5
        };

        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");

        // ---- The middle click (from the buttons) ----

        // chest: the open chest's buttons; otherwise your inventory's.
        // Returns whether anything moved (or a chest's owner was asked to sort).
        internal static bool Run(bool chest, Mode mode)
        {
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;
            if (gui == null || player == null || player.IsTeleporting()) return false;
            try
            {
                if (!chest) return SortInventory(gui, player, mode);
                Container c = StackifyPlugin.CurrentContainer(gui);
                return c != null && SortChest(gui, c, mode);
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Sorting failed: " + e); return false; }
        }

        // Your inventory and every chest given, in one go: one move sound,
        // and the items that moved pop in your inventory and the open chest.
        // Returns how many items moved.
        internal static int RunAll(IEnumerable<Container> chests, Mode mode)
        {
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;
            if (gui == null || player == null || player.IsTeleporting()) return 0;
            int moved = 0;
            try
            {
                StackifyPlugin.DropDragItem();
                Inventory inv = player.GetInventory();
                List<ItemDrop.ItemData> mine = Sort(inv, Favorites.For(player), StackifyPlugin.MainRows(inv, player), mode);
                moved += mine.Count;
                if (mine.Count > 0) StackifyEffects.Runner.Get().StartCoroutine(Ripple(gui.m_playerGrid, mine));
                Container open = StackifyPlugin.CurrentContainer(gui);
                foreach (Container c in chests)
                {
                    if (c == null) continue;
                    ZNetView nv = NearbyChests.NView(c);
                    if (nv == null || !nv.IsValid()) continue;
                    if (StackifyPlugin.HasMuc || nv.IsOwner())
                    {
                        List<ItemDrop.ItemData> m = Sort(c.GetInventory(), null, c.GetInventory().GetHeight(), mode, Favorites.For(player));
                        moved += m.Count;
                        if (m.Count > 0 && c == open) StackifyEffects.Runner.Get().StartCoroutine(Ripple(ContainerGrid(gui), m));
                    }
                    else nv.InvokeRPC(RpcName, (int)mode); // whoever has it sorts it
                }
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Sorting failed: " + e); }
            if (moved > 0) Feedback(null, null);
            return moved;
        }

        // ---- Sorting ----

        private static bool SortInventory(InventoryGui gui, Player player, Mode mode)
        {
            StackifyPlugin.DropDragItem();
            Inventory inv = player.GetInventory();
            List<ItemDrop.ItemData> moved = Sort(inv, Favorites.For(player), StackifyPlugin.MainRows(inv, player), mode);
            Feedback(gui.m_playerGrid, moved);
            return moved.Count > 0;
        }

        private static bool SortChest(InventoryGui gui, Container chest, Mode mode)
        {
            StackifyPlugin.DropDragItem();
            ZNetView nv = NearbyChests.NView(chest);
            if (nv == null || !nv.IsValid()) return false;
            if (StackifyPlugin.HasMuc || nv.IsOwner())
            {
                List<ItemDrop.ItemData> moved = Sort(chest.GetInventory(), null, chest.GetInventory().GetHeight(), mode,
                    Player.m_localPlayer != null ? Favorites.For(Player.m_localPlayer) : null);
                Feedback(ContainerGrid(gui), moved);
                return moved.Count > 0;
            }
            // Whoever has the chest sorts it (they need Stackify too).
            nv.InvokeRPC(RpcName, (int)mode);
            Feedback(null, null);
            return true;
        }

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class RpcPatch
        {
            private static void Postfix(Container __instance)
            {
                ZNetView nv = NearbyChests.NView(__instance);
                if (nv == null || nv.GetZDO() == null) return;
                Container c = __instance;
                nv.Register<int>(RpcName, (long sender, int mode) =>
                {
                    ZNetView own = NearbyChests.NView(c);
                    if (own != null && own.IsOwner()) Sort(c.GetInventory(), null, c.GetInventory().GetHeight(), (Mode)mode);
                });
            }
        }

        // favorites: the player's, for your own inventory; null for
        // containers. inContainer: the player's favorites again, for a
        // container: favorited items stay where they are there too
        // (favorited slots are only in your inventory). A container sorted
        // by its owner on request uses no favorites. Returns the items that
        // ended up somewhere else, in sorted order (stacks merged into
        // others count).
        private static List<ItemDrop.ItemData> Sort(Inventory inv, Favorites favorites, int rows, Mode mode,
            Favorites inContainer = null)
        {
            bool hotbar = favorites == null || StackifyPlugin.SortHotbar.Value;
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            HashSet<Vector2i> kept = new HashSet<Vector2i>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                bool sorted = it.m_gridPos.y < rows && (favorites == null ||
                    (!favorites.IsItemFavorited(it.m_shared) && (it.m_gridPos.y > 0 || hotbar) && !favorites.IsSlotFavorited(it.m_gridPos))) &&
                    (inContainer == null || !inContainer.IsItemFavorited(it.m_shared));
                if (sorted) items.Add(it);
                else kept.Add(it.m_gridPos); // never moves, and its slot stays its own
            }
            Dictionary<ItemDrop.ItemData, Vector2i> before = new Dictionary<ItemDrop.ItemData, Vector2i>();
            foreach (ItemDrop.ItemData it in items) before[it] = it.m_gridPos;

            List<ItemDrop.ItemData> merged = new List<ItemDrop.ItemData>();
            if (StackifyPlugin.SortMergeStacks.Value) MergeStacks(items, inv, merged);
            if (mode == Mode.Weight) items.Sort(WeightOrder); else items.Sort(TypeOrder);
            List<Vector2i> slots = Slots(inv, rows, hotbar, favorites, kept);
            for (int i = 0; i < items.Count && i < slots.Count; i++) items[i].m_gridPos = slots[i];

            List<ItemDrop.ItemData> moved = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData it in items)
                if (before[it] != it.m_gridPos) moved.Add(it);
            moved.AddRange(merged);
            if (moved.Count > 0) StackifyPlugin.Changed(inv);
            return moved;
        }

        // The free slots in the order items fill them: bottom row first,
        // left to right, like Quick Stack Store.
        private static List<Vector2i> Slots(Inventory inv, int h, bool hotbar, Favorites favorites, HashSet<Vector2i> kept)
        {
            int w = inv.GetWidth(), top = hotbar ? 0 : 1;
            List<Vector2i> order = new List<Vector2i>();
            for (int y = h - 1; y >= top; y--) for (int x = 0; x < w; x++) order.Add(new Vector2i(x, y));
            List<Vector2i> slots = new List<Vector2i>(order.Count);
            foreach (Vector2i v in order)
            {
                if (kept.Contains(v)) continue;
                if (favorites != null && favorites.IsSlotFavorited(v)) continue; // never sorted into
                slots.Add(v);
            }
            return slots;
        }

        private static int TypeGroup(ItemDrop.ItemData item)
        {
            int t = (int)item.m_shared.m_itemType;
            return t < 0 || t >= TypeToCategory.Length ? t : TypeToCategory[t];
        }

        // Quick Stack Store's order: type group, name, best quality, biggest stack.
        private static int TypeOrder(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int r = TypeGroup(a).CompareTo(TypeGroup(b));
            if (r == 0) r = string.CompareOrdinal(a.m_shared.m_name, b.m_shared.m_name);
            if (r == 0) r = -a.m_quality.CompareTo(b.m_quality);
            if (r == 0) r = -a.m_stack.CompareTo(b.m_stack);
            return r;
        }

        // Heaviest stack first; equal weights by type.
        private static int WeightOrder(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int r = -a.GetWeight().CompareTo(b.GetWeight());
            return r != 0 ? r : TypeOrder(a, b);
        }

        private static void MergeStacks(List<ItemDrop.ItemData> items, Inventory inv, List<ItemDrop.ItemData> merged)
        {
            Dictionary<string, List<ItemDrop.ItemData>> groups = new Dictionary<string, List<ItemDrop.ItemData>>();
            foreach (ItemDrop.ItemData it in items)
            {
                if (it.m_stack >= it.m_shared.m_maxStackSize || (it.m_customData != null && it.m_customData.Count > 0)) continue;
                string key = it.m_shared.m_name + "|" + it.m_quality;
                List<ItemDrop.ItemData> g;
                if (!groups.TryGetValue(key, out g)) groups[key] = g = new List<ItemDrop.ItemData>();
                g.Add(it);
            }
            foreach (List<ItemDrop.ItemData> g in groups.Values)
            {
                if (g.Count <= 1) continue;
                int total = 0;
                foreach (ItemDrop.ItemData it in g) total += it.m_stack;
                int max = g[0].m_shared.m_maxStackSize;
                foreach (ItemDrop.ItemData it in g)
                {
                    if (total <= 0)
                    {
                        it.m_stack = 0;
                        inv.RemoveItem(it);
                        items.Remove(it);
                        merged.Add(it);
                    }
                    else
                    {
                        it.m_stack = Math.Min(max, total);
                        total -= it.m_stack;
                    }
                }
            }
        }

        // ---- Sound and movement ----
        // The game's item move sound, played straight away on Stackify's own
        // UI sound player. Every item that moved pops into its new slot as a
        // ripple passes. Nothing moved: silence, nothing pops.
        private static void Feedback(InventoryGrid grid, List<ItemDrop.ItemData> moved)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || Player.m_localPlayer == null) return;
            if (moved != null && moved.Count == 0) return;
            GameObject move = MoveSound(gui);
            StackifyEffects.Runner.Get().StartCoroutine(Actions.After(Actions.ResultDelay, () => StackifyEffects.PlayUi(move)));
            if (moved != null) StackifyEffects.Runner.Get().StartCoroutine(Ripple(grid, moved)); // null: the chest's owner sorts it
        }

        // The game's item move sound itself (sfx_gui_moveitem), found once.
        // Its "move item" effect list can hold just the controller vibration
        // version, which makes no sound.
        private static GameObject _moveSound;
        private static bool _moveSearched;
        private static GameObject MoveSound(InventoryGui gui)
        {
            if (_moveSound != null || _moveSearched) return _moveSound;
            _moveSearched = true;
            if (gui.m_moveItemEffects != null)
                foreach (EffectList.EffectData d in gui.m_moveItemEffects.m_effectPrefabs)
                    if (d != null && d.m_prefab != null && d.m_prefab.name == "sfx_gui_moveitem") return _moveSound = d.m_prefab;
            foreach (ZSFX sfx in Resources.FindObjectsOfTypeAll<ZSFX>())
                if (sfx != null && sfx.gameObject.name == "sfx_gui_moveitem" && sfx.transform.parent == null) return _moveSound = sfx.gameObject;
            StackifyPlugin.Log.LogWarning("Couldn't find the game's item move sound.");
            return null;
        }

        private static IEnumerator Ripple(InventoryGrid grid, List<ItemDrop.ItemData> moved)
        {
            int n = moved.Count;
            if (n == 0) yield break;
            // Gaps of about 30 ms, uneven; a big sort squeezes them so the
            // whole ripple stays under about half a second.
            float gap = Mathf.Min(0.03f, 0.5f / n);
            float[] at = new float[n];
            for (int k = 1; k < n; k++) at[k] = at[k - 1] + gap * UnityEngine.Random.Range(0.6f, 1.4f);

            List<RectTransform> icons = new List<RectTransform>(n);
            List<InventoryElement> elements = grid != null ? Favorites.GridElements(grid) : null;
            Inventory inv = grid != null ? Favorites.GridInventory(grid) : null;
            int w = inv != null ? inv.GetWidth() : 0;
            foreach (ItemDrop.ItemData it in moved)
            {
                RectTransform icon = null;
                // Merged-away stacks have no slot: sound only.
                if (elements != null && inv != null && it.m_stack > 0)
                {
                    int i = it.m_gridPos.y * w + it.m_gridPos.x;
                    if (i >= 0 && i < elements.Count && elements[i] != null && elements[i].m_icon != null) icon = elements[i].m_icon.rectTransform;
                }
                icons.Add(icon);
            }

            const float pop = 0.22f;
            float t = 0f;
            while (t < at[n - 1] + pop)
            {
                for (int k = 0; k < n; k++)
                {
                    if (icons[k] == null) continue;
                    float u = Mathf.Clamp01((t - at[k]) / pop);
                    // in from slightly small, a touch past full size, settle
                    float s = u <= 0f ? 0.82f : 1f + Mathf.Sin(u * Mathf.PI) * 0.12f * (1f - u) - (1f - u) * (1f - u) * 0.18f;
                    icons[k].localScale = new Vector3(s, s, 1f);
                }
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            foreach (RectTransform r in icons) if (r != null) r.localScale = Vector3.one;
        }
    }
}
