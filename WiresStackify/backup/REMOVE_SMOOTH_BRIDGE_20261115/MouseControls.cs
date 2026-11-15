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
        internal static bool HasHeldStack => InventoryGui.instance != null && DragItem(InventoryGui.instance) != null;
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
        private static readonly AccessTools.FieldRef<InventoryGrid, System.Collections.Generic.List<InventoryElement>> GridElements =
            AccessTools.FieldRefAccess<InventoryGrid, System.Collections.Generic.List<InventoryElement>>("m_elements");

        private static void PlayMoveSound(InventoryGui gui)
        {
            if (gui != null)
                gui.m_moveItemEffects.Create(gui.transform.position, Quaternion.identity);
        }

        // Only the actual WASD movement keys suppress idle half-pickup.
        // Shift and Control are modifiers, never movement input by themselves.
        private static bool MovingKey => ZInput.GetKey(KeyCode.W) || ZInput.GetKey(KeyCode.A) ||
                                         ZInput.GetKey(KeyCode.S) || ZInput.GetKey(KeyCode.D);

        private static bool Shift => ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
        private static bool Ctrl => ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);

        private static bool Fav(ItemDrop.ItemData item, Inventory inv = null)
        {
            Player p = Player.m_localPlayer;
            // Slot favorites belong only to the player's inventory; a chest
            // slot with the same coordinates is NOT a protected hotbar slot.
            if (p == null || item == null) return false;
            Favorites fav = Favorites.For(p);
            return fav.IsItemFavorited(item.m_shared) ||
                   (inv == p.GetInventory() && fav.IsSlotFavorited(item.m_gridPos));
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

        private static Vector3 IconWorld(InventoryGui gui, Inventory inv, ItemDrop.ItemData item, Vector3 fallback)
        {
            if (gui == null || inv == null || item == null) return fallback;
            InventoryGrid grid = inv == Inv(PlayerGrid(gui)) ? PlayerGrid(gui) : ContainerGrid(gui);
            if (grid == null || !grid.gameObject.activeInHierarchy || Inv(grid) != inv) return fallback;
            System.Collections.Generic.List<InventoryElement> elements = GridElements(grid);
            int i = item.m_gridPos.y * inv.GetWidth() + item.m_gridPos.x;
            if (elements == null || i < 0 || i >= elements.Count || elements[i] == null) return fallback;
            InventoryElement e = elements[i];
            return e.m_icon != null ? e.m_icon.transform.position : e.transform.position;
        }

        // Match Valheim Inventory.FindFreeStackItem: the first same-name,
        // quality and world-level stack with room. Used ONLY on mouse scroll.
        private static ItemDrop.ItemData MergeTarget(Inventory inv, ItemDrop.ItemData source)
        {
            if (inv == null || source == null) return null;
            foreach (ItemDrop.ItemData candidate in inv.GetAllItems())
                if (candidate.m_shared.m_name == source.m_shared.m_name &&
                    candidate.m_quality == source.m_quality &&
                    candidate.m_worldLevel == source.m_worldLevel &&
                    candidate.m_stack < candidate.m_shared.m_maxStackSize)
                    return candidate;
            return null;
        }

        // Vanilla performs the actual Ctrl+left transfer and native sounds.
        // Only when the target was an existing stack do we show a separate
        // source-to-target item flight; empty-slot moves are already tracked
        // by Smooth UI via the original ItemData reference.
        private static void MoveViaVanilla(InventoryGui gui, InventoryGrid grid,
            ItemDrop.ItemData item, Vector2i pos)
        {
            if (gui == null || grid == null || item == null) return;
            Inventory from = Inv(grid), to = Other(gui, from);
            ItemDrop.ItemData merge = MergeTarget(to, item);
            int oldCount = merge != null ? merge.m_stack : 0;
            Vector3 fromWorld = IconWorld(gui, from, item, ZInput.pointerPosition);
            Vector3 toWorld = IconWorld(gui, to, merge, fromWorld);
            Selected.Invoke(gui, new object[] { grid, item, pos, InventoryGrid.Modifier.Move });
            if (merge != null && merge.m_stack > oldCount)
                SmoothUIBridge.StackTransfer(item, fromWorld, toWorld);
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
            if (at != null)
                n = Mathf.Min(n, at.m_shared.m_maxStackSize - at.m_stack);
            // Do not submit a quantity the target cannot fit: native
            // AddItem can partially change both stacks yet report false.
            // Use the actual Smooth UI drag sprite position, not the raw
            // pointer: its physics/follow can lag behind the cursor.
            Vector3 fromWorld = SmoothUIBridge.DragVisualWorld(gui);
            if (n <= 0 || !to.MoveItemToThis(from, d, n, pos.x, pos.y)) return;
            // A newly created destination clone otherwise looks like a new
            // appearance to Smooth UI. Its origin is the cursor release.
            if (at == null)
                SmoothUIBridge.NewItem(to.GetItemAt(pos.x, pos.y), fromWorld);
            else
                SmoothUIBridge.StackTransfer(d, fromWorld, IconWorld(gui, to, at, fromWorld));
            PlayMoveSound(gui);
            int left = held - n;
            if (left <= 0 || d.m_stack <= 0) ClearDrag(gui);
            else DragAmount(gui) = Mathf.Min(left, d.m_stack);
        }

        private static Vector2i s_lastDrag = new Vector2i(-1, -1), s_lastShift = new Vector2i(-1, -1), s_lastClickPos = new Vector2i(-1, -1);
        private static InventoryGrid s_lastClickGrid;
        private static InventoryGrid s_lastDragGrid, s_lastShiftGrid;
        private static float s_lastClick = -1f;
        // A Shift+RMB pickup must finish before a SECOND RMB press starts
        // distributing items across slots. Releasing RMB arms placement.
        private static bool s_initialPickupRightPress;

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
                // First left-click normally starts a vanilla drag. The
                // second click MUST recognize that same held item, otherwise
                // double-click fill is unreachable under normal mouse input.
                if (twice && item != null && !Ctrl && !Shift &&
                    (DragItem(gui) == null || DragItem(gui) == item))
                {
                    if (DragItem(gui) == item) ClearDrag(gui);
                    Fill(Inv(__instance), item);
                    return false;
                }
                if (DragItem(gui) != null) return true; // holding: the game's drop
                if (Ctrl)
                {
                    if (item != null && !Fav(item, Inv(__instance))) MoveViaVanilla(gui, __instance, item, pos);
                    s_lastShift = pos;
                    s_lastShiftGrid = __instance;
                    return false;
                }
                return true;
            }
        }

        // Handle the actual vanilla item-use callback, not the earlier grid
        // mouse-down. This is where native right-click would drink/use items.
        // Returning false prevents that use ONLY for an idle Shift+RMB split.
        [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
        private static class HalfPickup
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item)
            {
                if (!Shift || MovingKey || item == null || item.m_stack <= 1 ||
                    DragItem(__instance) != null || Player.m_localPlayer == null ||
                    Player.m_localPlayer.IsTeleporting()) return true;
                SetupDrag.Invoke(__instance, new object[] {
                    item, Inv(grid), (item.m_stack + 1) / 2
                });
                if (DragItem(__instance) == item) s_initialPickupRightPress = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), "OnRightDown")]
        private static class RightDown
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler element)
            {
                InventoryGui gui = InventoryGui.instance;
                if (gui == null) return true;
                if (DragItem(gui) == null) return true; // native item-use hook above handles Shift split
                Vector2i pos = (Vector2i)AccessTools.Method(typeof(InventoryGrid), "GetButtonPos").Invoke(__instance, new object[] { element.gameObject });
                Place(gui, __instance, pos, Shift);
                s_lastDrag = pos;
                s_lastDragGrid = __instance;
                return false;
            }
        }

        // The same item elsewhere in the inventory onto this stack, till it's full.
        private static void Fill(Inventory inv, ItemDrop.ItemData item)
        {
            int moved = 0;
            InventoryGui gui = InventoryGui.instance;
            Vector3 targetWorld = IconWorld(gui, inv, item, ZInput.pointerPosition);
            foreach (ItemDrop.ItemData o in inv.GetAllItems().ToArray())
            {
                int room = item.m_shared.m_maxStackSize - item.m_stack;
                if (room <= 0) break;
                if (o == item || !item.IsSameType(o)) continue;
                int n = Mathf.Min(room, o.m_stack);
                Vector3 sourceWorld = IconWorld(gui, inv, o, targetWorld);
                if (!inv.RemoveItem(o, n)) continue;
                item.m_stack += n;
                moved += n;
                SmoothUIBridge.StackTransfer(o, sourceWorld, targetWorld);
            }
            if (moved <= 0) return;
            Changed.Invoke(inv, new object[] { false, false });
            PlayMoveSound(InventoryGui.instance);
        }

        // Held mouse buttons dragged over slots, hotbar numbers and the wheel, while the inventory is open.
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static class Each
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!InventoryGui.IsVisible() || Player.m_localPlayer == null) return;
                bool rmb = ZInput.GetMouseButton(1), lmb = ZInput.GetMouseButton(0);
                if (!rmb)
                {
                    s_lastDrag = new Vector2i(-1, -1);
                    s_lastDragGrid = null;
                    s_initialPickupRightPress = false;
                }
                if (!lmb || !Ctrl)
                {
                    s_lastShift = new Vector2i(-1, -1);
                    s_lastShiftGrid = null;
                }
                float wheel = ZInput.GetMouseScrollWheel();
                bool number = false;
                for (int k = 0; k < 8; k++) if (ZInput.GetKeyDown(KeyCode.Alpha1 + k)) { number = true; break; }
                if (!rmb && !(lmb && Ctrl) && wheel == 0f && !number) return;
                if (!Hover(__instance, out InventoryGrid grid, out Vector2i pos)) return;
                Inventory inv = Inv(grid);
                ItemDrop.ItemData item = inv.GetItemAt(pos.x, pos.y);
                ItemDrop.ItemData held = DragItem(__instance);

                if (rmb && held != null && !s_initialPickupRightPress &&
                    (pos != s_lastDrag || grid != s_lastDragGrid))
                {
                    s_lastDrag = pos;
                    s_lastDragGrid = grid;
                    Place(__instance, grid, pos, false);
                }
                if (lmb && Ctrl && held == null &&
                    (pos != s_lastShift || grid != s_lastShiftGrid))
                {
                    s_lastShift = pos;
                    s_lastShiftGrid = grid;
                    if (item != null && !Fav(item, inv)) MoveViaVanilla(__instance, grid, item, pos);
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
                bool carried = DragItem(gui) == item;
                if (carried && DragAmount(gui) <= 0) return;
                Vector3 origin = DragItem(gui) != null ?
                    SmoothUIBridge.DragVisualWorld(gui) : (Vector3)ZInput.pointerPosition;
                if (DragItem(gui) == null && sourceGrid != null)
                {
                    // Read the already-rendered item slot; no reflective
                    // GetHoveredElement Invoke (which caused repeated
                    // TargetParameterCountException in the actual game log).
                    System.Collections.Generic.List<InventoryElement> elements = GridElements(sourceGrid);
                    int index = item.m_gridPos.y * inv.GetWidth() + item.m_gridPos.x;
                    if (elements != null && index >= 0 && index < elements.Count)
                    {
                        InventoryElement element = elements[index];
                        if (element != null)
                            origin = element.m_icon != null ? element.m_icon.transform.position : element.transform.position;
                    }
                }
                ItemDrop.ItemData destinationStack = MergeTarget(other, item);
                Vector3 destinationWorld = IconWorld(gui, other, destinationStack, origin);
                int previousCount = destinationStack != null ? destinationStack.m_stack : 0;
                ItemDrop.ItemData one = item.Clone();
                one.m_stack = 1;
                if (!other.CanAddItem(one) || !other.AddItem(one)) return;
                // Every scroll has a visible originating item. First transfer
                // into empty space uses Smooth UI's quick NEW-item glide;
                // subsequent transfers into a stack use a separate flying
                // icon and the regular native quantity pulse at destination.
                if (other.ContainsItem(one)) SmoothUIBridge.NewItem(one, origin);
                else if (destinationStack != null && destinationStack.m_stack > previousCount)
                    SmoothUIBridge.StackTransfer(item, origin, destinationWorld);
                if (!inv.RemoveItem(item, 1)) return;
                PlayMoveSound(gui);
                if (carried)
                {
                    int remainingHeld = DragAmount(gui) - 1;
                    if (remainingHeld <= 0 || item.m_stack <= 0 || !inv.ContainsItem(item)) ClearDrag(gui);
                    else DragAmount(gui) = Mathf.Min(remainingHeld, item.m_stack);
                }
            }
            else
            {
                if (item.m_stack >= item.m_shared.m_maxStackSize) return;
                foreach (ItemDrop.ItemData o in other.GetAllItems())
                    if (item.IsSameType(o))
                    {
                        Vector3 fromWorld = IconWorld(gui, other, o, ZInput.pointerPosition);
                        Vector3 toWorld = IconWorld(gui, inv, item, ZInput.pointerPosition);
                        if (!other.RemoveItem(o, 1)) return;
                        item.m_stack++;
                        // While carrying a reserved half-stack, wheel UP
                        // refills the actually HELD amount too, not just the
                        // underlying source stack's inventory count.
                        if (DragItem(gui) == item)
                            DragAmount(gui) = Mathf.Min(item.m_stack, DragAmount(gui) + 1);
                        Changed.Invoke(inv, new object[] { false, false });
                        SmoothUIBridge.StackTransfer(o, fromWorld, toWorld);
                        PlayMoveSound(gui);
                        return;
                    }
            }
        }
    }
}
