using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WiresStackify
{
    // Middle click on your inventory (or around it, out to the end of the
    // armor and weight tabs) sorts it; on an open chest (out to the end of
    // its buttons and weight) sorts the chest. Sorted the same way as Quick
    // Stack Store: by type, then name, quality and stack size, filling from
    // the bottom row up, stacks merged. Favorites and the hotbar aren't part
    // of it at all: they never move and nothing is put on their slots.
    internal static class Sorting
    {
        private enum Target { None, Inventory, Chest }

        private const float Margin = 16f;   // around the panel, in UI units
        private const string RpcName = "WireStackify_RequestSort2";

        // Item type to sort group, as Quick Stack Store has it.
        private static readonly int[] TypeToCategory =
        {
            0, 2, 4, 7, 7, 8, 10, 10, 0, 6,
            0, 10, 10, 1, 7, 7, 0, 10, 9, 7,
            7, 3, 7, 5
        };

        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");

        // ---- The middle click ----

        // Only a mouse button check each frame; the rest happens on the
        // click, the moment the button goes down.
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static class ClickPatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (Input.GetMouseButtonDown(2)) Click(__instance);
            }
        }

        private static void Click(InventoryGui gui)
        {
            if (!StackifyPlugin.MiddleClickSort.Value || !InventoryGui.IsVisible()) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || StackifyPlugin.DragGo(gui) != null) return;
            try
            {
                Target target = Under(gui, Input.mousePosition);
                if (target == Target.Inventory) SortInventory(gui, player);
                else if (target == Target.Chest)
                {
                    Container chest = StackifyPlugin.CurrentContainer(gui);
                    if (chest != null) SortChest(gui, chest);
                }
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Sorting failed: " + e); }
        }

        private static Target Under(InventoryGui gui, Vector2 mouse)
        {
            RectTransform playerPanel = gui.m_player;
            RectTransform chestPanel = gui.m_container;
            Container chest = StackifyPlugin.CurrentContainer(gui);
            bool chestOpen = chest != null && chestPanel != null && chestPanel.gameObject.activeInHierarchy;
            // On the inventory itself always means the inventory; otherwise
            // the chest's area comes before the inventory's margin.
            if (playerPanel != null && Inside(playerPanel, mouse, 0f, chestPanel)) return Target.Inventory;
            if (chestOpen && Inside(chestPanel, mouse, Margin, null)) return Target.Chest;
            if (playerPanel != null && Inside(playerPanel, mouse, Margin, chestPanel)) return Target.Inventory;
            return Target.None;
        }

        // Inside the panel or anything sticking out of it (tabs, buttons),
        // plus a margin all around. Text and tiny anchors don't count, and
        // neither does the chest panel when checking the inventory.
        private static bool Inside(RectTransform panel, Vector2 screen, float margin, RectTransform skip)
        {
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, screen, cam, out local)) return false;
            Rect area = panel.rect;
            foreach (Transform child in panel)
            {
                RectTransform r = child as RectTransform;
                if (r == null || r == skip || !r.gameObject.activeInHierarchy) continue;
                if (r.GetComponent<TMP_Text>() != null || r.name == "TooltipAnchor") continue;
                if (r.rect.width < 10f || r.rect.height < 10f) continue;
                area = Union(area, RectIn(panel, r));
            }
            area.xMin -= margin; area.yMin -= margin; area.xMax += margin; area.yMax += margin;
            return area.Contains(local);
        }

        private static Rect RectIn(RectTransform panel, RectTransform r)
        {
            Vector3[] c = new Vector3[4];
            r.GetWorldCorners(c);
            Vector2 min = panel.InverseTransformPoint(c[0]), max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 p = panel.InverseTransformPoint(c[i]);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        // ---- Sorting ----

        private static void SortInventory(InventoryGui gui, Player player)
        {
            StackifyPlugin.DropDragItem();
            List<ItemDrop.ItemData> moved = Sort(player.GetInventory(), Favorites.For(player));
            Feedback(gui.m_playerGrid, moved);
        }

        private static void SortChest(InventoryGui gui, Container chest)
        {
            StackifyPlugin.DropDragItem();
            ZNetView nv = NearbyChests.NView(chest);
            if (nv == null || !nv.IsValid()) return;
            if (StackifyPlugin.HasMuc || nv.IsOwner())
                Feedback(ContainerGrid(gui), Sort(chest.GetInventory(), null));
            else
            {
                // Whoever has the chest sorts it (they need Stackify too).
                nv.InvokeRPC(RpcName);
                Feedback(null, null);
            }
        }

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class RpcPatch
        {
            private static void Postfix(Container __instance)
            {
                ZNetView nv = NearbyChests.NView(__instance);
                if (nv == null || nv.GetZDO() == null) return;
                Container c = __instance;
                nv.Register(RpcName, (long sender) =>
                {
                    ZNetView own = NearbyChests.NView(c);
                    if (own != null && own.IsOwner()) Sort(c.GetInventory(), null);
                });
            }
        }

        // favorites: the player's, for your own inventory; null for chests
        // (everything in a chest is sorted). Returns the items that ended up
        // somewhere else, in sorted order (stacks merged into others count).
        private static List<ItemDrop.ItemData> Sort(Inventory inv, Favorites favorites)
        {
            bool hotbar = favorites == null || StackifyPlugin.SortHotbar.Value;
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            HashSet<Vector2i> kept = new HashSet<Vector2i>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                bool sorted = favorites == null ||
                    (!favorites.IsItemFavorited(it.m_shared) && (it.m_gridPos.y > 0 || hotbar) && !favorites.IsSlotFavorited(it.m_gridPos));
                if (sorted) items.Add(it);
                else kept.Add(it.m_gridPos); // never moves, and its slot stays its own
            }
            Dictionary<ItemDrop.ItemData, Vector2i> before = new Dictionary<ItemDrop.ItemData, Vector2i>();
            foreach (ItemDrop.ItemData it in items) before[it] = it.m_gridPos;

            List<ItemDrop.ItemData> merged = new List<ItemDrop.ItemData>();
            if (StackifyPlugin.SortMergeStacks.Value) MergeStacks(items, inv, merged);
            items.Sort(TypeOrder);
            List<Vector2i> slots = Slots(inv, hotbar, favorites, kept);
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
        private static List<Vector2i> Slots(Inventory inv, bool hotbar, Favorites favorites, HashSet<Vector2i> kept)
        {
            int w = inv.GetWidth(), h = inv.GetHeight(), top = hotbar ? 0 : 1;
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
        // A sort sounds like reaching into the bag and putting things in
        // order, all made from the game's own sounds:
        //  the rummage   the bag rustle of opening the inventory, a touch
        //                higher so it isn't mistaken for opening it
        //  the shuffle   a few quick item move clicks while the items ripple
        //                into place, more for a bigger sort (1 to 5), each a
        //                little higher than the last with some wobble
        //  the settle    the put-down sound once the last item lands
        // Every item that moved pops into its new slot as the ripple passes.
        // Nothing moved: silence, nothing pops.
        private static void Feedback(InventoryGrid grid, List<ItemDrop.ItemData> moved)
        {
            if (InventoryGui.instance == null || Player.m_localPlayer == null) return;
            if (moved != null && moved.Count == 0) return;
            Play(SortSounds.Rummage, 0f);
            // null: the chest's owner sorts it; just the rummage and settle.
            StackifyEffects.Runner.Get().StartCoroutine(Ripple(grid, moved ?? new List<ItemDrop.ItemData>()));
        }

        private enum SortSounds { Rummage, Shuffle, Settle }

        private static void Play(SortSounds which, float progress)
        {
            InventoryGui gui = InventoryGui.instance;
            Player p = Player.m_localPlayer;
            if (gui == null || p == null) return;
            Vector3 ear = StackifyEffects.Ear();
            switch (which)
            {
                case SortSounds.Rummage:
                    StackifyEffects.SpawnLocal(gui.m_openInventoryEffects, ear, Quaternion.identity, null, 1.08f);
                    break;
                case SortSounds.Shuffle:
                    float pitch = Mathf.Lerp(0.95f, 1.15f, progress) * UnityEngine.Random.Range(0.97f, 1.03f);
                    StackifyEffects.SpawnLocal(gui.m_moveItemEffects, ear, Quaternion.identity, null, pitch);
                    break;
                case SortSounds.Settle:
                    StackifyEffects.SpawnLocal(p.m_dropEffects, ear, Quaternion.identity, null, 1.05f);
                    break;
            }
        }

        private static IEnumerator Ripple(InventoryGrid grid, List<ItemDrop.ItemData> moved)
        {
            int n = moved.Count;
            if (n == 0)
            {
                yield return new WaitForSecondsRealtime(0.18f);
                Play(SortSounds.Settle, 1f);
                yield break;
            }
            // Gaps of about 30 ms, uneven; a big sort squeezes them so the
            // whole ripple stays under about half a second. It starts just
            // after the rummage.
            float gap = Mathf.Min(0.03f, 0.5f / n);
            float[] at = new float[n];
            at[0] = 0.08f;
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

            // The shuffle clicks, spread over the ripple (starting just after
            // the rummage), and the settle when the last item lands.
            int clicks = Mathf.Clamp(Mathf.CeilToInt(n / 4f), 1, 5);
            float lead = 0.08f, span = Mathf.Max(at[n - 1] - lead, 0.12f);
            float[] clickAt = new float[clicks];
            for (int c = 0; c < clicks; c++)
                clickAt[c] = lead + (clicks == 1 ? 0f : span * c / (clicks - 1)) + UnityEngine.Random.Range(-0.012f, 0.012f);
            float settleAt = at[n - 1] + 0.09f;
            int clicked = 0;
            bool settled = false;

            const float pop = 0.22f;
            float t = 0f;
            while (t < Mathf.Max(at[n - 1] + pop, settleAt))
            {
                while (clicked < clicks && t >= clickAt[clicked])
                {
                    Play(SortSounds.Shuffle, clicks == 1 ? 0.5f : (float)clicked / (clicks - 1));
                    clicked++;
                }
                if (!settled && t >= settleAt) { Play(SortSounds.Settle, 1f); settled = true; }
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
            if (!settled) Play(SortSounds.Settle, 1f);
            foreach (RectTransform r in icons) if (r != null) r.localScale = Vector3.one;
        }
    }
}
