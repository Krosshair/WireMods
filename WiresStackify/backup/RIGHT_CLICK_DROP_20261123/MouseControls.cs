using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

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

        private static void PlayMoveSound(InventoryGui gui)
        {
            if (gui != null)
                gui.m_moveItemEffects.Create(gui.transform.position, Quaternion.identity);
        }

        private static bool Shift => ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
        private static bool Ctrl => ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);
        // Only real WASD input blocks idle Shift+RMB; Shift and Ctrl alone
        // do not count as movement.
        private static bool MovingWASD => ZInput.GetKey(KeyCode.W) || ZInput.GetKey(KeyCode.A) ||
                                          ZInput.GetKey(KeyCode.S) || ZInput.GetKey(KeyCode.D);

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

        private static Inventory Other(InventoryGui gui, Inventory of)
        {
            Container c = CurrentContainer(gui);
            Player p = Player.m_localPlayer;
            if (c == null || p == null) return null;
            Inventory player = p.GetInventory(), cont = c.GetInventory();
            return of == player ? cont : of == cont ? player : null;
        }

        // Follow MouseTweaks' native InventoryGrid selection callback rather
        // than modifying Inventory data just for animation compatibility.
        private static void MoveViaVanilla(InventoryGui gui, InventoryGrid grid,
            ItemDrop.ItemData item, Vector2i pos)
        {
            if (gui == null || grid == null || item == null) return;
            Selected.Invoke(gui, new object[] { grid, item, pos, InventoryGrid.Modifier.Move });
        }
        private static void ClearDrag(InventoryGui gui) => SetupDrag.Invoke(gui, new object[] { null, null, 1 });

        // MouseTweaks-style placement: ask VANILLA InventoryGui to drop an
        // amount, then keep the remaining reserved drag amount on the cursor.
        // Shift changes only the quantity of a stack ALREADY held. With no
        // held item, right-click is never intercepted (potions stay usable).
        private static void Place(InventoryGui gui, InventoryGrid grid, Vector2i pos, bool half)
        {
            ItemDrop.ItemData heldItem = DragItem(gui);
            Inventory source = DragInv(gui), target = Inv(grid);
            if (heldItem == null || source == null || target == null) return;
            if (target == source && heldItem.m_gridPos == pos) return;
            ItemDrop.ItemData at = target.GetItemAt(pos.x, pos.y);
            if (at != null && (!heldItem.IsSameType(at) ||
                at.m_stack >= at.m_shared.m_maxStackSize)) return;
            int heldAmount = Mathf.Min(DragAmount(gui), heldItem.m_stack);
            int quantity = half ? Mathf.Max(1, heldAmount / 2) : 1;
            if (at != null) quantity = Mathf.Min(quantity,
                at.m_shared.m_maxStackSize - at.m_stack);
            if (heldAmount <= 0 || quantity <= 0) return;
            int original = heldItem.m_stack;
            // The native callback performs drop validation, equipping rules,
            // inventory notifications and the same sound used by MouseTweaks.
            DragAmount(gui) = quantity;
            Selected.Invoke(gui, new object[] { grid, at, pos, InventoryGrid.Modifier.Select });
            int transferred = Mathf.Max(0, original - heldItem.m_stack);
            int left = Mathf.Min(heldItem.m_stack, heldAmount - transferred);
            if (left > 0 && source.ContainsItem(heldItem))
            {
                if (DragItem(gui) == heldItem) DragAmount(gui) = left;
                else SetupDrag.Invoke(gui, new object[] { heldItem, source, left });
            }
            else if (DragItem(gui) != null)
                ClearDrag(gui);
        }
        private static Vector2i s_lastDrag = new Vector2i(-1, -1), s_lastShift = new Vector2i(-1, -1), s_lastClickPos = new Vector2i(-1, -1);
        private static InventoryGrid s_lastClickGrid;
        private static InventoryGrid s_lastDragGrid, s_lastShiftGrid;
        private static float s_lastClick = -1f;
        // Suppress drag-place during the SAME right-button press that
        // initiated a half pickup. A later, separate press can place it.
        private static bool s_halfPickupPress;

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
                    // Same model as MouseTweaks: the result stays ON the
                    // cursor, with the maximum compatible amount reserved.
                    if (DragItem(gui) == null)
                        SetupDrag.Invoke(gui, new object[] { item, Inv(__instance), item.m_stack });
                    FillHeld(gui);
                    return false;
                }
                if (DragItem(gui) != null) return true; // holding: the game's drop
                if (Ctrl)
                {
                    // one move per slot per press: the held-Ctrl sweep may have moved this slot
                    // already this press, and whatever lands in it after (a pickup, the rest of
                    // a stack) stays put until the button is let go
                    bool done = s_lastShift == pos && s_lastShiftGrid == __instance;
                    if (!done && item != null && !Fav(item, Inv(__instance))) MoveViaVanilla(gui, __instance, item, pos);
                    s_lastShift = pos;
                    s_lastShiftGrid = __instance;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
        private static class IdleHalfPickup
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(InventoryGui __instance, InventoryGrid grid,
                ItemDrop.ItemData item)
            {
                if (!Shift || MovingWASD || item == null || item.m_stack <= 1 ||
                    DragItem(__instance) != null || Player.m_localPlayer == null)
                    return true;
                SetupDrag.Invoke(__instance, new object[] {
                    item, Inv(grid), (item.m_stack + 1) / 2
                });
                // Vanilla UpdateItemDrag would otherwise cancel this same
                // press; existing MouseDragGuard suppresses RMB cancellation
                // while a stack is held, as in MouseTweaks.
                if (DragItem(__instance) == item) s_halfPickupPress = true;
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
                if (DragItem(gui) == null) return true; // native use or idle half pickup
                if (s_halfPickupPress) return false; // same RMB that picked up half
                Vector2i pos = (Vector2i)AccessTools.Method(typeof(InventoryGrid), "GetButtonPos").Invoke(__instance, new object[] { element.gameObject });
                Place(gui, __instance, pos, Shift);
                s_lastDrag = pos;
                s_lastDragGrid = __instance;
                return false;
            }
        }

        // The same item elsewhere in the inventory onto this stack, till it's full.
        // Port MouseTweaks' double-click behavior: gather compatible items
        // into the dragged stack through native Inventory.MoveItemToThis,
        // then make the whole filled stack the amount held on the cursor.
        private static void FillHeld(InventoryGui gui)
        {
            ItemDrop.ItemData held = DragItem(gui);
            Inventory target = DragInv(gui);
            Player player = Player.m_localPlayer;
            if (held == null || target == null || player == null) return;
            int before = held.m_stack;
            GatherIntoHeld(target, target, held);
            Inventory other = Other(gui, target);
            if (other != null) GatherIntoHeld(target, other, held);
            if (held.m_stack != before)
            {
                SetupDrag.Invoke(gui, new object[] { held, target, held.m_stack });
            }
            else
                DragAmount(gui) = held.m_stack;
        }

        private static void GatherIntoHeld(Inventory target, Inventory from,
            ItemDrop.ItemData held)
        {
            if (target == null || from == null || held == null) return;
            // Only a user action; no inventory snapshot/allocation each frame.
            foreach (ItemDrop.ItemData candidate in from.GetAllItems().ToArray())
            {
                int room = held.m_shared.m_maxStackSize - held.m_stack;
                if (room <= 0) break;
                if (candidate == held || !held.IsSameType(candidate)) continue;
                int amount = Mathf.Min(room, candidate.m_stack);
                target.MoveItemToThis(from, candidate, amount,
                    held.m_gridPos.x, held.m_gridPos.y);
            }
        }
        // ---- Over a slot: told by the slot itself (SlotInput), never asked every frame ----

        // The pointer came onto a slot: with the right button held and a stack in hand, one goes in
        // (dragging across slots); with Ctrl and the left button held, the stack moves across.
        internal static void Entered(InventoryElement element)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !InventoryGui.IsVisible() || Player.m_localPlayer == null || !At(element, out InventoryGrid grid, out Vector2i pos)) return;
            bool rmb = ZInput.GetMouseButton(1), lmb = ZInput.GetMouseButton(0);
            if (!rmb) Released(1);
            if (!lmb || !Ctrl) Released(0);
            Inventory inv = Inv(grid);
            ItemDrop.ItemData item = inv.GetItemAt(pos.x, pos.y);
            ItemDrop.ItemData held = DragItem(gui);
            if (rmb && held != null && !s_halfPickupPress && (pos != s_lastDrag || grid != s_lastDragGrid))
            {
                s_lastDrag = pos;
                s_lastDragGrid = grid;
                Place(gui, grid, pos, false);
            }
            if (lmb && Ctrl && held == null && (pos != s_lastShift || grid != s_lastShiftGrid))
            {
                s_lastShift = pos;
                s_lastShiftGrid = grid;
                if (item != null && !Fav(item, inv)) MoveViaVanilla(gui, grid, item, pos);
            }
        }

        // A mouse button let go over a slot ends that press's sweep.
        internal static void Released(int button)
        {
            if (button == 1)
            {
                s_halfPickupPress = false;
                s_lastDrag = new Vector2i(-1, -1);
                s_lastDragGrid = null;
            }
            else if (button == 0)
            {
                s_lastShift = new Vector2i(-1, -1);
                s_lastShiftGrid = null;
            }
        }

        // The wheel over a slot: one item across. False if there was nothing to do, so the wheel can
        // still scroll the grid.
        internal static bool Wheel(InventoryElement element, float delta)
        {
            InventoryGui gui = InventoryGui.instance;
            if (delta == 0f || gui == null || !InventoryGui.IsVisible() || !At(element, out InventoryGrid grid, out Vector2i pos)) return false;
            Inventory inv = Inv(grid);
            ItemDrop.ItemData held = DragItem(gui);
            ItemDrop.ItemData item = held ?? inv.GetItemAt(pos.x, pos.y);
            if (item == null) return false;
            Inventory from = held != null ? DragInv(gui) : inv;
            // Ctrl and down: one dropped on the ground instead, from your inventory or an open chest
            if (Ctrl && delta < 0f) { DropOne(gui, item, from, held != null); return true; }
            Scroll(gui, item, from, delta < 0f);
            return true;
        }

        // One of the item onto the ground, the game's own drop (in front of you; favorites stay, FavoriteDropProtection).
        private static void DropOne(InventoryGui gui, ItemDrop.ItemData item, Inventory inv, bool carried)
        {
            Player p = Player.m_localPlayer;
            if (p == null || inv == null || !p.DropItem(inv, item, 1)) return;
            if (!carried) return;
            int remaining = DragAmount(gui) - 1;
            if (remaining <= 0 || !inv.ContainsItem(item)) ClearDrag(gui);
            else DragAmount(gui) = Mathf.Min(remaining, item.m_stack);
        }

        // A hotbar number while the pointer is over an item: it swaps with that hotbar slot, by the
        // game's own drag and drop. The game reads no keys with the inventory open, so these are
        // looked at by KeyWatch, which runs only while the pointer is over a slot.
        internal static void Number(InventoryElement element, int k)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || DragItem(gui) != null || !At(element, out InventoryGrid grid, out Vector2i pos)) return;
            Inventory inv = Inv(grid);
            ItemDrop.ItemData item = inv.GetItemAt(pos.x, pos.y);
            if (item == null) return;
            InventoryGrid pg = PlayerGrid(gui);
            Vector2i hot = new Vector2i(k, 0);
            if (inv == Inv(pg) && pos == hot) return;
            SetupDrag.Invoke(gui, new object[] { item, inv, item.m_stack });
            Selected.Invoke(gui, new object[] { pg, Inv(pg).GetItemAt(k, 0), hot, InventoryGrid.Modifier.Select });
            if (DragItem(gui) != null) ClearDrag(gui);
        }

        private static bool At(InventoryElement element, out InventoryGrid grid, out Vector2i pos)
        {
            grid = element != null ? element.GetComponentInParent<InventoryGrid>() : null;
            pos = element != null ? element.Position : default;
            return grid != null && Inv(grid) != null;
        }

        [HarmonyPatch(typeof(InventoryElement), nameof(InventoryElement.Initialize))]
        private static class SlotMade
        {
            private static void Postfix(InventoryElement __instance)
            {
                if (__instance != null && __instance.GetComponent<SlotInput>() == null)
                    __instance.gameObject.AddComponent<SlotInput>();
            }
        }

        // The press ends wherever the button is let go; the slot it started on hears it.
        [HarmonyPatch(typeof(UIInputHandler), nameof(UIInputHandler.OnPointerUp))]
        private static class ButtonUp
        {
            private static void Postfix(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Right) Released(1);
                else if (eventData.button == PointerEventData.InputButton.Left) Released(0);
            }
        }

        // Wheel transfers one item per gesture, only across two distinct
        // inventories. No extra animation bridge or Smooth UI hooks.
        private static void Scroll(InventoryGui gui, ItemDrop.ItemData item, Inventory inv,
            bool down)
        {
            if (item == null || inv == null) return;
            Inventory other = Other(gui, inv);
            if (other == null || other == inv) return;
            if (down)
            {
                bool carried = DragItem(gui) == item;
                if (carried && DragAmount(gui) <= 0) return;
                ItemDrop.ItemData one = item.Clone();
                one.m_stack = 1;
                if (!other.CanAddItem(one) || !other.AddItem(one)) return;
                if (!inv.RemoveItem(item, 1)) return;
                PlayMoveSound(gui);
                if (carried)
                {
                    int remaining = DragAmount(gui) - 1;
                    if (remaining <= 0 || !inv.ContainsItem(item)) ClearDrag(gui);
                    else DragAmount(gui) = Mathf.Min(remaining, item.m_stack);
                }
            }
            else
            {
                if (item.m_stack >= item.m_shared.m_maxStackSize) return;
                foreach (ItemDrop.ItemData source in other.GetAllItems())
                {
                    if (!item.IsSameType(source)) continue;
                    if (!other.RemoveItem(source, 1)) return;
                    item.m_stack++;
                    if (DragItem(gui) == item)
                        DragAmount(gui) = Mathf.Min(item.m_stack, DragAmount(gui) + 1);
                    StackifyPlugin.Changed(inv);
                    PlayMoveSound(gui);
                    return;
                }
            }
        }
    }
}
