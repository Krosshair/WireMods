using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresNativeCursorSupport
{
    // Which cursor to show, told by the game as things happen instead of asked every frame: the UI's
    // own pointer enter/exit calls, an item picked up or put down, crafting started or done, the map
    // pressed or let go, a loading screen shown or hidden. Each just sets its flag and lets
    // SystemCursor pick; nothing here runs unless one of those happens.
    internal static class CursorEvents
    {
        internal static int Waiting;      // loading screens showing
        internal static bool Crafting, Dragging, MapDrag;
        private static Object s_hover;   // what the pointer is over, if it changes the cursor
        private static NativeCursorKind s_hoverKind;

        internal static NativeCursorKind Kind
        {
            get
            {
                if (Waiting > 0) return NativeCursorKind.Wait;
                if (Crafting) return NativeCursorKind.Working;
                if (Dragging || MapDrag) return NativeCursorKind.Move;
                return s_hover != null ? s_hoverKind : NativeCursorKind.Arrow;
            }
        }

        internal static void Hover(Object over, NativeCursorKind kind)
        {
            s_hover = over;
            s_hoverKind = kind;
            SystemCursor.Refresh();
        }

        internal static void Leave(Object over)
        {
            if (s_hover != over) return;
            s_hover = null;
            SystemCursor.Refresh();
        }

        internal static void Reset()
        {
            Waiting = 0;
            Crafting = Dragging = MapDrag = false;
            s_hover = null;
        }

        // An item in an inventory slot: the hand, as it can be picked up. Empty slots and the rest of
        // the UI keep the arrow.
        private static NativeCursorKind ForHandler(UIInputHandler h)
        {
            InventoryElement element = h.GetComponentInParent<InventoryElement>();
            if (element == null) return NativeCursorKind.Arrow;
            InventoryGrid grid = element.GetComponentInParent<InventoryGrid>();
            Inventory inventory = grid != null ? grid.GetInventory() : null;
            return inventory != null && inventory.GetItemAt(element.Position.x, element.Position.y) != null
                ? NativeCursorKind.Hand : NativeCursorKind.Arrow;
        }

        [HarmonyPatch(typeof(UIInputHandler), nameof(UIInputHandler.OnPointerEnter))]
        private static class HandlerEnter
        {
            private static void Postfix(UIInputHandler __instance) => Hover(__instance, ForHandler(__instance));
        }

        [HarmonyPatch(typeof(UIInputHandler), nameof(UIInputHandler.OnPointerExit))]
        private static class HandlerExit
        {
            private static void Postfix(UIInputHandler __instance) => Leave(__instance);
        }

        // Text boxes get the text cursor.
        private static bool IsTextBox(Selectable s) =>
            (s is TMP_InputField || s is InputField) && s.IsInteractable();

        [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerEnter))]
        private static class SelectableEnter
        {
            private static void Postfix(Selectable __instance)
            {
                if (IsTextBox(__instance)) Hover(__instance, NativeCursorKind.Text);
            }
        }

        [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerExit))]
        private static class SelectableExit
        {
            private static void Postfix(Selectable __instance) => Leave(__instance);
        }

        // A hidden UI object gets no pointer exit.
        [HarmonyPatch(typeof(Selectable), "OnDisable")]
        private static class SelectableGone
        {
            private static void Postfix(Selectable __instance) => Leave(__instance);
        }

        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem =
            AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, float> CraftTimer =
            AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");

        [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
        private static class Drag
        {
            private static void Postfix(InventoryGui __instance)
            {
                Dragging = DragItem(__instance) != null;
                // the slot under the pointer may have just filled or emptied
                if (s_hover is UIInputHandler h && h != null) s_hoverKind = ForHandler(h);
                SystemCursor.Refresh();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
        private static class CraftStart
        {
            private static void Postfix(InventoryGui __instance)
            {
                Crafting = CraftTimer(__instance) >= 0f;
                SystemCursor.Refresh();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class CraftDone
        {
            private static void Postfix() { Crafting = false; SystemCursor.Refresh(); }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnCraftCancelPressed")]
        private static class CraftCancel
        {
            private static void Postfix() { Crafting = false; SystemCursor.Refresh(); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class InventoryClosed
        {
            private static void Postfix()
            {
                Crafting = false;
                if (s_hover is UIInputHandler) s_hover = null;
                SystemCursor.Refresh();
            }
        }

        [HarmonyPatch(typeof(Minimap), "OnMapLeftDown")]
        private static class MapDown
        {
            private static void Postfix() { MapDrag = true; SystemCursor.Refresh(); }
        }

        [HarmonyPatch(typeof(Minimap), "OnMapLeftUp")]
        private static class MapUp
        {
            private static void Postfix() { MapDrag = false; SystemCursor.Refresh(); }
        }

        // The menu's loading and please-wait screens: Unity tells a component on them when they show.
        [HarmonyPatch(typeof(FejdStartup), "Awake")]
        private static class Loading
        {
            private static void Postfix(FejdStartup __instance)
            {
                SystemCursor.Refresh();
                Mark(__instance.m_loading);
                Mark(__instance.m_pleaseWait);
            }

            private static void Mark(GameObject go)
            {
                if (go != null && go.GetComponent<WaitMarker>() == null) go.AddComponent<WaitMarker>();
            }
        }
    }

    internal sealed class WaitMarker : MonoBehaviour
    {
        private void OnEnable() { CursorEvents.Waiting++; SystemCursor.Refresh(); }

        private void OnDisable()
        {
            if (CursorEvents.Waiting > 0) CursorEvents.Waiting--;
            SystemCursor.Refresh();
        }
    }

    // Put on what another mod marked as clickable (NativeCursorApi.SetHand).
    internal sealed class PointerHand : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public void OnPointerEnter(PointerEventData e) => CursorEvents.Hover(this, NativeCursorKind.Hand);
        public void OnPointerExit(PointerEventData e) => CursorEvents.Leave(this);
        private void OnDisable() => CursorEvents.Leave(this);
    }
}
