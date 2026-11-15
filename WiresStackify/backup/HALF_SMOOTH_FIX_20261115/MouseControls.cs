using HarmonyLib;
using UnityEngine;

namespace WiresStackify
{
    // Mouse and key controls in the inventory (after Azumatt's MouseTweaks, and Minecraft's):
    //  - Ctrl + left click: the game's move to the other inventory, and held down, every stack the
    //    mouse passes over (not favorited ones). Shift + left click stays the game's split;
    //  - holding a stack, right click: put 1 in the slot (Shift: half the held stack); held down and
    //    dragged, 1 in each slot passed over. Without a held stack right click is the game's (use
    //    it: a potion still drinks with Shift held while running);
    //  - double left click: fill that stack from the same item elsewhere in the same inventory;
    //  - a hotbar number over an item: swap it with that hotbar slot (the game's own drag and drop);
    //  - scroll down over an item (or holding one): one into the other inventory; scroll up: one back
    //    from the other inventory onto it. Never within the same inventory.
    internal static class MouseControls
    {
        private static readonly AccessTools.FieldRef<InventoryGrid, Inventory> Inv = AccessTools.FieldRefAccess<InventoryGrid, Inventory>("m_inventory");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragInv = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");
        private static readonly AccessTools.FieldRef<InventoryGui, int> DragAmount = AccessTools.FieldRefAccess<InventoryGui, int>("m_dragAmount");
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> PlayerGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_playerGrid");
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        private static readonly System.Reflection.MethodInfo SetupDrag = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");
        private static readonly System.Reflection.MethodInfo Selected = AccessTools.Method(typeof(InventoryGui), "OnSelectedItem");
        private static readonly System.Reflection.MethodInfo Hovered = AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");
        private static readonly System.Reflection.MethodInfo Changed = AccessTools.Method(typeof(Inventory), "Changed"); // the game saves and redraws on it
        private static readonly System.Reflection.MethodInfo ElementPos = AccessTools.Method(typeof(InventoryGrid), "GetElementPos");

        private static bool Shift => ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
        private static bool Ctrl => ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);

        private static bool Fav(ItemDrop.ItemData item)
        {
            Player p = Player.m_localPlayer;
            return p != null && item != null && Favorites.For(p).IsFavorited(item);
        }

        private static bool HoverOf(InventoryGrid g, out Vector2i pos)
        {
            pos = default;
            if (g == null || !g.gameObject.activeInHierarchy) return false;
            object e = Hovered.Invoke(g, null);
            if (e == null) return false;
            pos = (Vector2i)ElementPos.Invoke(g, new[] { e });
            return true;
        }

        private static bool Hover(InventoryGui gui, out InventoryGrid grid, out Vector2i pos)
        {
            grid = PlayerGrid(gui);
            if (HoverOf(grid, out pos)) return true;
            grid = ContainerGrid(gui);
            return HoverOf(grid, out pos);
        }

        private static Inventory Other(InventoryGui gui, Inventory of)
        {
            Container c = CurrentContainer(gui);
            Player p = Player.m_localPlayer;
            if (c == null || p == null) return null;
            Inventory player = p.GetInventory(), cont = c.GetInventory();
            return of == player ? cont : of == cont ? player : null;
        }

        private static void ClearDrag(InventoryGui gui) => SetupDrag.Invoke(gui, new object[] { null, null, 1 });

        // Holding a stack: n of it into the slot (empty, or the same item with room).
        private static void Place(InventoryGui gui, InventoryGrid grid, Vector2i pos, bool half)
        {
            ItemDrop.ItemData d = DragItem(gui);
            Inventory from = DragInv(gui), to = Inv(grid);
            if (d == null || from == null || to == null) return;
            if (to == from && d.m_gridPos == pos) return;
            ItemDrop.ItemData at = to.GetItemAt(pos.x, pos.y);
            if (at != null && (at.m_shared.m_name != d.m_shared.m_name || at.m_quality != d.m_quality || at.m_stack >= at.m_shared.m_maxStackSize)) return;
            int held = Mathf.Min(DragAmount(gui), d.m_stack);
            int n = half ? Mathf.Max(1, held / 2) : 1;
            if (n <= 0 || !to.MoveItemToThis(from, d, n, pos.x, pos.y)) return;
            // A newly created destination clone otherwise looks like a new
            // appearance to Smooth UI. Its origin is the cursor release.
            if (at == null)
                SmoothUIBridge.NewItem(to.GetItemAt(pos.x, pos.y), ZInput.pointerPosition);
            int left = held - n;
            if (left <= 0 || d.m_stack <= 0) ClearDrag(gui);
            else DragAmount(gui) = Mathf.Min(left, d.m_stack);
        }

        private static Vector2i s_lastDrag = new Vector2i(-1, -1), s_lastShift = new Vector2i(-1, -1), s_lastClickPos = new Vector2i(-1, -1);
        private static InventoryGrid s_lastClickGrid;
        private static float s_lastClick = -1f;

        [HarmonyPatch(typeof(InventoryGrid), "OnLeftDown")]
        private static class LeftDown
        {
            [HarmonyPriority(Priority.Low)]
            private static bool Prefix(InventoryGrid __instance, UIInputHandler clickHandler, bool __runOriginal)
            {
                if (!__runOriginal || InventoryGui.instance == null) return __runOriginal;
                InventoryGui gui = InventoryGui.instance;
                Vector2i pos = (Vector2i)AccessTools.Method(typeof(InventoryGrid), "GetButtonPos").Invoke(__instance, new object[] { clickHandler.gameObject });
                ItemDrop.ItemData item = Inv(__instance).GetItemAt(pos.x, pos.y);
                // double click: fill this stack from the rest of this inventory
                float now = Time.unscaledTime;
                bool twice = now - s_lastClick < .3f && s_lastClickGrid == __instance && s_lastClickPos == pos;
                s_lastClick = twice ? -1f : now; s_lastClickGrid = __instance; s_lastClickPos = pos;
                if (twice && item != null && DragItem(gui) == null) { Fill(Inv(__instance), item); return false; }
                if (DragItem(gui) != null) return true; // holding: the game's drop
                if (Ctrl)
                {
                    if (item != null && !Fav(item)) Selected.Invoke(gui, new object[] { __instance, item, pos, InventoryGrid.Modifier.Move });
                    s_lastShift = pos;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), "OnRightDown")]
        private static class RightDown
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler element)
            {
                InventoryGui gui = InventoryGui.instance;
                if (gui == null) return true;
                if (DragItem(gui) == null)
                {
                    // Shift + right click on a stack: the game's split, only while no movement key is held
                    // (running with Shift held, right click still drinks the potion)
                    if (!Shift || ZInput.GetButton("Forward") || ZInput.GetButton("Backward") || ZInput.GetButton("Left") || ZInput.GetButton("Right")) return true;
                    Vector2i at = (Vector2i)AccessTools.Method(typeof(InventoryGrid), "GetButtonPos").Invoke(__instance, new object[] { element.gameObject });
                    ItemDrop.ItemData it = Inv(__instance).GetItemAt(at.x, at.y);
                    if (it == null) return true;
                    Selected.Invoke(gui, new object[] { __instance, it, at, InventoryGrid.Modifier.Split });
                    return false;
                }
                Vector2i pos = (Vector2i)AccessTools.Method(typeof(InventoryGrid), "GetButtonPos").Invoke(__instance, new object[] { element.gameObject });
                Place(gui, __instance, pos, Shift);
                s_lastDrag = pos;
                return false;
            }
        }

        // The same item elsewhere in the inventory onto this stack, till it's full.
        private static void Fill(Inventory inv, ItemDrop.ItemData item)
        {
            foreach (ItemDrop.ItemData o in inv.GetAllItems().ToArray())
            {
                int room = item.m_shared.m_maxStackSize - item.m_stack;
                if (room <= 0) break;
                if (o == item || o.m_shared.m_name != item.m_shared.m_name || o.m_quality != item.m_quality) continue;
                int n = Mathf.Min(room, o.m_stack);
                item.m_stack += n;
                inv.RemoveItem(o, n);
            }
            Changed.Invoke(inv, null);
        }

        // Held mouse buttons dragged over slots, hotbar numbers and the wheel, while the inventory is open.
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static class Each
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!InventoryGui.IsVisible() || Player.m_localPlayer == null) return;
                bool rmb = ZInput.GetMouseButton(1), lmb = ZInput.GetMouseButton(0);
                if (!rmb) s_lastDrag = new Vector2i(-1, -1);
                if (!lmb || !Ctrl) s_lastShift = new Vector2i(-1, -1);
                float wheel = ZInput.GetMouseScrollWheel();
                bool number = false;
                for (int k = 0; k < 8; k++) if (ZInput.GetKeyDown(KeyCode.Alpha1 + k)) { number = true; break; }
                if (!rmb && !(lmb && Ctrl) && wheel == 0f && !number) return;
                if (!Hover(__instance, out InventoryGrid grid, out Vector2i pos)) return;
                Inventory inv = Inv(grid);
                ItemDrop.ItemData item = inv.GetItemAt(pos.x, pos.y);
                ItemDrop.ItemData held = DragItem(__instance);

                if (rmb && held != null && pos != s_lastDrag) { s_lastDrag = pos; Place(__instance, grid, pos, false); }
                if (lmb && Ctrl && held == null && pos != s_lastShift)
                {
                    s_lastShift = pos;
                    if (item != null && !Fav(item)) Selected.Invoke(__instance, new object[] { grid, item, pos, InventoryGrid.Modifier.Move });
                }
                if (number && item != null && held == null)
                    for (int k = 0; k < 8; k++)
                        if (ZInput.GetKeyDown(KeyCode.Alpha1 + k))
                        {
                            // the game's own drag and drop onto that hotbar slot (it swaps)
                            InventoryGrid pg = PlayerGrid(__instance);
                            Vector2i hot = new Vector2i(k, 0);
                            if (inv == Inv(pg) && pos == hot) break;
                            SetupDrag.Invoke(__instance, new object[] { item, inv, item.m_stack });
                            Selected.Invoke(__instance, new object[] { pg, Inv(pg).GetItemAt(k, 0), hot, InventoryGrid.Modifier.Select });
                            if (DragItem(__instance) != null) ClearDrag(__instance);
                            break;
                        }
                if (wheel != 0f)
                {
                    InventoryGrid sourceGrid = held != null ?
                        (DragInv(__instance) == Inv(PlayerGrid(__instance)) ? PlayerGrid(__instance) : ContainerGrid(__instance)) : grid;
                    Scroll(__instance, held ?? item, held != null ? DragInv(__instance) : inv,
                        wheel < 0f, sourceGrid);
                }
            }
        }

        private static void Scroll(InventoryGui gui, ItemDrop.ItemData item, Inventory inv, bool down,
            InventoryGrid sourceGrid)
        {
            if (item == null || inv == null) return;
            Inventory other = Other(gui, inv);
            if (other == null) return;
            if (down)
            {
                Vector3 origin = ZInput.pointerPosition;
                if (DragItem(gui) == null && sourceGrid != null)
                {
                    InventoryElement element = Hovered.Invoke(sourceGrid, null) as InventoryElement;
                    if (element != null)
                        origin = element.m_icon != null ? element.m_icon.transform.position : element.transform.position;
                }
                ItemDrop.ItemData one = item.Clone();
                one.m_stack = 1;
                if (!other.CanAddItem(one) || !other.AddItem(one)) return;
                // Native AddItem merges into an existing stack OR inserts
                // this exact clone. Smooth UI already pulses merges; only
                // inserted clones need an explicit originating position.
                if (other.ContainsItem(one)) SmoothUIBridge.NewItem(one, origin);
                inv.RemoveItem(item, 1);
                if (DragItem(gui) == item && (item.m_stack <= 0 || !inv.ContainsItem(item))) ClearDrag(gui);
            }
            else
            {
                if (item.m_stack >= item.m_shared.m_maxStackSize) return;
                foreach (ItemDrop.ItemData o in other.GetAllItems())
                    if (o.m_shared.m_name == item.m_shared.m_name && o.m_quality == item.m_quality)
                    {
                        other.RemoveItem(o, 1);
                        item.m_stack++;
                        Changed.Invoke(inv, null);
                        return;
                    }
            }
        }
    }
}
