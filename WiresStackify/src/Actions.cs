using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WiresStackify
{
    internal enum StackAction { None, Sort, SortByWeight, QuickStack, Restock, QuickStackAndRestock, Everything }

    // Every action from the pouches and the panels goes through here, so
    // they all sound and move the same way:
    //  the rustle  the empty-bag shake plays the moment you click, always
    //  the result  a moment later its own sound follows if anything
    //              happened (pick up for quick stack, put down for
    //              restock, the item move sound for a sort), then each
    //              chest it used, in turn
    //  nothing     just the rustle, and the pouch gives a quick squeeze
    internal static class Actions
    {
        internal const float ResultDelay = 0.1f; // rustle first, then the result

        internal static void Do(StackAction action, bool chest)
        {
            Player p = Player.m_localPlayer;
            if (action == StackAction.None || p == null || p.IsTeleporting()) return;
            Rustle();
            bool any = false;
            try
            {
                switch (action)
                {
                    case StackAction.Sort:
                    case StackAction.SortByWeight:
                        bool weight = action == StackAction.SortByWeight;
                        any = Sorting.Run(chest, weight ? Sorting.Mode.Weight : Sorting.Mode.Type);
                        if (any)
                        {
                            ButtonFeel.Wiggle(chest, weight ? -1f : 1f);
                            if (weight) WeightShake.Shake(chest);
                        }
                        break;
                    case StackAction.QuickStack:
                        any = QuickStack.Run(p, chest) > 0;
                        if (any) Later(p.m_pickupEffects);
                        break;
                    case StackAction.Restock:
                        any = Restock.Run(p, chest) > 0;
                        if (any) Later(p.m_dropEffects);
                        break;
                    case StackAction.QuickStackAndRestock:
                        int stacked = QuickStack.Run(p, chest);
                        int restocked = Restock.Run(p, chest);
                        any = stacked > 0 || restocked > 0;
                        if (any) Later(stacked > 0 ? p.m_pickupEffects : p.m_dropEffects);
                        break;
                    case StackAction.Everything:
                        // Quick stack, restock, then sort your inventory and
                        // every chest either of them used.
                        StackifyEffects.Used.Clear();
                        int s1 = QuickStack.Run(p, chest);
                        int r1 = Restock.Run(p, chest);
                        List<Container> used = new List<Container>(StackifyEffects.Used);
                        StackifyEffects.Used.Clear();
                        int sorted = Sorting.RunAll(used, Sorting.Mode.Type);
                        any = s1 > 0 || r1 > 0 || sorted > 0;
                        if (s1 > 0 || r1 > 0) Later(s1 > 0 ? p.m_pickupEffects : p.m_dropEffects);
                        if (sorted > 0) ButtonFeel.Wiggle(chest, 1f);
                        break;
                }
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Stackify action failed: " + e); }
            if (!any) ButtonFeel.Squeeze(chest);
            StackifyButtons.RefreshHover();
        }

        // Holding an item and clicking a pouch. The inventory pouch: every
        // stack of that kind goes into chests (the open one and nearby
        // ones). The chest pouch: every stack of that kind you carry is
        // topped up from that chest. Can't: it stays in your hand.
        internal static void DropOn(bool chest, ItemDrop.ItemData item, Inventory from)
        {
            Player p = Player.m_localPlayer;
            if (p == null || item == null) return;
            Rustle();
            int moved = 0;
            try
            {
                if (chest)
                {
                    Container c = InventoryGui.instance != null ? StackifyPlugin.CurrentContainer(InventoryGui.instance) : null;
                    moved = Restock.RunOnly(p, c, item);
                }
                else if (from == p.GetInventory()) moved = QuickStack.Run(p, false, null, item);
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Stackify drop failed: " + e); }
            if (moved > 0)
            {
                StackifyPlugin.DropDragItem();
                Later(chest ? p.m_dropEffects : p.m_pickupEffects);
            }
            else ButtonFeel.Squeeze(chest);
            StackifyButtons.RefreshHover();
        }

        // The empty-bag shake: the game's inventory closing rustle, light
        // and quick.
        private static GameObject _rustle;
        internal static void Rustle()
        {
            InventoryGui gui = InventoryGui.instance;
            if (_rustle == null && gui != null && gui.m_closeInventoryEffects != null)
                foreach (EffectList.EffectData d in gui.m_closeInventoryEffects.m_effectPrefabs)
                    if (d != null && d.m_enabled && d.m_prefab != null && d.m_prefab.GetComponentInChildren<ZSFX>(true) != null)
                    { _rustle = d.m_prefab; break; }
            if (_rustle != null) StackifyEffects.PlayUi(_rustle, 1.25f);
        }

        // A game sound a moment after the rustle.
        internal static void Later(EffectList sound)
        {
            StackifyEffects.Runner.Get().StartCoroutine(After(ResultDelay, () =>
                StackifyEffects.SpawnLocal(sound, StackifyEffects.Ear(), Quaternion.identity)));
        }

        internal static IEnumerator After(float seconds, Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
        }

        // ---- Which click does what (from the config) ----

        internal static StackAction ForButton(int button, bool shift)
        {
            switch (button)
            {
                case 0: return (shift ? StackifyPlugin.ShiftLeftClick : StackifyPlugin.LeftClick).Value;
                case 1: return (shift ? StackifyPlugin.ShiftRightClick : StackifyPlugin.RightClick).Value;
                case 2: return (shift ? StackifyPlugin.ShiftMiddleClick : StackifyPlugin.MiddleClick).Value;
            }
            return StackAction.None;
        }

        internal static bool Shift()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        // Short: the container's pouch says once that it only works with
        // that container.
        internal static string Describe(StackAction a)
        {
            switch (a)
            {
                case StackAction.Sort: return "sort by type";
                case StackAction.SortByWeight: return "sort by weight";
                case StackAction.QuickStack: return "quick stack";
                case StackAction.Restock: return "restock";
                case StackAction.QuickStackAndRestock: return "quick stack, restock";
                case StackAction.Everything: return "quick stack, restock, sort all used";
            }
            return null;
        }

        // ---- Middle click on a panel ----
        // Almost anywhere on screen counts. With a chest open the screen is
        // split halfway between your inventory and the chest. Nothing runs
        // per frame: it listens to the clicks themselves, on the game's
        // full-screen backdrop behind the panels, on the two panels, and on
        // anything inside them that takes clicks (slots).
        internal sealed class PanelMiddleClick : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Middle) Clicked(gameObject, e.position);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static class PanelClickSetup
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (__instance.m_dropButton != null) Add(__instance.m_dropButton.gameObject);
                if (__instance.m_player != null) Add(__instance.m_player.gameObject);
                if (__instance.m_container != null) Add(__instance.m_container.gameObject);
            }

            private static void Add(GameObject go)
            {
                if (go.GetComponent<PanelMiddleClick>() == null) go.AddComponent<PanelMiddleClick>();
            }
        }

        // Slots and other clickable things inside the panels take the click
        // themselves; the game's own click handler tells us about it.
        [HarmonyPatch(typeof(UIInputHandler), "OnPointerDown")]
        private static class SlotMiddleClick
        {
            private static void Postfix(UIInputHandler __instance, PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Middle && __instance.GetComponent<PanelMiddleClick>() == null)
                    Clicked(__instance.gameObject, eventData.position);
            }
        }

        private static void Clicked(GameObject source, Vector2 mouse)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !InventoryGui.IsVisible() || source == null) return;
            StackAction a = (Shift() ? StackifyPlugin.PanelShiftMiddleClick : StackifyPlugin.PanelMiddleClick).Value;
            if (a == StackAction.None || StackifyPlugin.DragGo(gui) != null) return;
            RectTransform inv = gui.m_player, chest = gui.m_container;
            if (inv == null) return;
            bool chestOpen = StackifyPlugin.CurrentContainer(gui) != null && chest != null && chest.gameObject.activeInHierarchy;
            Transform t = source.transform;
            bool backdrop = gui.m_dropButton != null && t == gui.m_dropButton.transform;
            int target;
            if (chestOpen && t.IsChildOf(chest)) target = 2;          // the chest's panel sits inside the inventory's
            else if (t.IsChildOf(inv)) target = 1;
            else if (backdrop) target = !chestOpen ? 1 : mouse.y >= (Edge(inv, false) + Edge(chest, true)) * 0.5f ? 1 : 2;
            else return; // another window
            Do(a, target == 2);
        }

        // A panel's top or bottom edge in screen pixels.
        private static float Edge(RectTransform r, bool top)
        {
            Vector3[] c = new Vector3[4];
            r.GetWorldCorners(c);
            Camera cam = Cam(r);
            float v = top ? float.MinValue : float.MaxValue;
            foreach (Vector3 p in c)
            {
                float y = RectTransformUtility.WorldToScreenPoint(cam, p).y;
                v = top ? Mathf.Max(v, y) : Mathf.Min(v, y);
            }
            return v;
        }

        private static bool Over(RectTransform r, Vector2 screen)
        {
            return r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, screen, Cam(r));
        }

        private static Camera Cam(RectTransform r)
        {
            Canvas canvas = r.GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        }
    }
}
