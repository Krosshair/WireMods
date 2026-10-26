using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // "Can't" shakes and things shrinking away instead of vanishing.
    internal static class MotionExtras
    {
        private sealed class Shake
        {
            internal Transform Root;
            internal Vector3 Base;
            internal Vector3 Applied;
            internal float Time;
        }

        private sealed class Exit
        {
            internal Transform Root;
            internal Vector3 BaseScale;
            internal float Time;
        }

        private const float ShakeDuration = 0.32f;
        private const float ShakePixels = 6f;
        private const float ExitDuration = 0.16f;

        private static readonly List<Shake> Shakes = new List<Shake>();
        private static readonly List<Exit> Exits = new List<Exit>();

        internal static bool Busy => Shakes.Count > 0 || Exits.Count > 0;

        internal static bool ShakeOn =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.ButtonAnimations.Value;

        internal static void ShakeNo(Transform root)
        {
            if (!ShakeOn || root == null || !root.gameObject.activeInHierarchy)
                return;

            foreach (Shake existing in Shakes)
            {
                if (existing.Root == root)
                {
                    existing.Time = 0f;
                    return;
                }
            }

            Shakes.Add(new Shake { Root = root, Base = root.localPosition });
            SmoothUIDriver.Wake();
        }

        // Takes over a UI element the game is about to destroy and shrinks it out first.
        internal static void ShrinkAway(GameObject go)
        {
            if (go == null || !go.activeInHierarchy)
            {
                if (go != null)
                    Object.Destroy(go);
                return;
            }

            foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>())
                graphic.raycastTarget = false;

            Exits.Add(new Exit { Root = go.transform, BaseScale = go.transform.localScale });
            SmoothUIDriver.Wake();
        }

        internal static void Tick()
        {
            float dt = WiresSmoothUIPlugin.Dt;

            for (int i = Shakes.Count - 1; i >= 0; i--)
            {
                Shake s = Shakes[i];
                if (s.Root == null)
                {
                    Shakes.RemoveAt(i);
                    continue;
                }

                // Keep anything the game moved meanwhile.
                if ((s.Root.localPosition - (s.Base + s.Applied)).sqrMagnitude > 0.0009f)
                    s.Base = s.Root.localPosition - s.Applied;

                s.Time += dt;
                float t = s.Time / ShakeDuration;
                if (t >= 1f || !s.Root.gameObject.activeInHierarchy)
                {
                    s.Root.localPosition = s.Base;
                    Shakes.RemoveAt(i);
                    continue;
                }

                float x = Mathf.Sin(t * Mathf.PI * 6f) * ShakePixels * (1f - t) * (1f - t);
                s.Applied = new Vector3(x, 0f, 0f);
                s.Root.localPosition = s.Base + s.Applied;
            }

            for (int i = Exits.Count - 1; i >= 0; i--)
            {
                Exit e = Exits[i];
                if (e.Root == null)
                {
                    Exits.RemoveAt(i);
                    continue;
                }

                e.Time += dt;
                float t = Mathf.Clamp01(e.Time / ExitDuration);
                if (t >= 1f)
                {
                    Object.Destroy(e.Root.gameObject);
                    Exits.RemoveAt(i);
                    continue;
                }

                // A tiny swell, then gone.
                float scale = t < 0.25f
                    ? Mathf.Lerp(1f, 1.12f, t / 0.25f)
                    : Mathf.Lerp(1.12f, 0f, WiresSmoothUIPlugin.EaseOutCubic((t - 0.25f) / 0.75f));
                e.Root.localScale = e.BaseScale * scale;
            }
        }

        internal static void RestoreAll()
        {
            foreach (Shake s in Shakes)
                if (s.Root != null)
                    s.Root.localPosition = s.Base;
            Shakes.Clear();

            foreach (Exit e in Exits)
                if (e.Root != null)
                    Object.Destroy(e.Root.gameObject);
            Exits.Clear();
        }
    }

    // ---- Shakes ----

    // Clicking a button that can't be used right now (craft without materials,
    // repair with nothing to repair, ...) shakes it. Tab buttons are left out:
    // the selected tab is disabled, and clicking it isn't a mistake.
    [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerDown))]
    internal static class SmoothUIDisabledClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Selectable __instance, PointerEventData eventData)
        {
            if (!(__instance is Button button) || button.interactable
                || eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            if (button.GetComponentInParent<TabHandler>() != null)
                return;

            InventoryGui gui = InventoryGui.instance;
            if (gui != null && gui.m_tabCraft != null && button.transform.parent == gui.m_tabCraft.transform.parent)
                return;

            if (button.GetComponentInParent<InventoryElement>() != null)
                return;

            MotionExtras.ShakeNo(button.transform);
        }
    }

    // Pressing a hotbar key with nothing on it shakes that slot.
    [HarmonyPatch(typeof(Player), "UseHotbarItem")]
    internal static class SmoothUIEmptyHotbarPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, int index)
        {
            if (__instance != Player.m_localPlayer || index < 1 || __instance.GetInventory().GetItemAt(index - 1, 0) != null)
                return;

            foreach (HotkeyBar bar in HotbarAccess.Bars())
            {
                var list = HotbarAccess.Elements(bar);
                if (list != null && index - 1 < list.Count)
                {
                    GameObject slot = HotbarAccess.Slot(list[index - 1]);
                    if (slot != null)
                        MotionExtras.ShakeNo(slot.transform);
                }
            }
        }
    }

    // ---- Map pins shrink away when removed ----

    [HarmonyPatch(typeof(Minimap), "DestroyPinMarker")]
    internal static class SmoothUIPinRemovePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Minimap.PinData pin)
        {
            if (pin == null || pin.m_uiElement == null
                || !WiresSmoothUIPlugin.Enabled.Value || !WiresSmoothUIPlugin.MapPinAnimations.Value)
                return;

            GameObject go = pin.m_uiElement.gameObject;
            pin.m_uiElement = null;
            MotionExtras.ShrinkAway(go);
        }
    }

    // ---- Small extras ----

    // The split amount bounces as you drag the slider.
    [HarmonyPatch(typeof(SplitDialog), "SliderChanged")]
    internal static class SmoothUISplitAmountPatch
    {
        private static readonly FieldInfo AmountField = AccessTools.Field(typeof(SplitDialog), "m_splitAmount");

        [HarmonyPostfix]
        private static void Postfix(SplitDialog __instance)
        {
            if (!ControlFeedback.On)
                return;
            TMP_Text amount = AmountField?.GetValue(__instance) as TMP_Text;
            if (amount != null)
                MicroMotionSystem.Pop(amount.transform, 1f, 1.14f, 0.14f);
        }
    }

    // The label of the tab you switch to pops.
    internal static class TabPop
    {
        internal static void Pop(Button tab)
        {
            if (!CascadeSystem.Panels || tab == null)
                return;
            TMP_Text label = tab.GetComponentInChildren<TMP_Text>();
            if (label != null)
                MicroMotionSystem.Pop(label.transform, 0.85f, 1.12f, 0.22f);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnTabCraftPressed")]
    internal static class SmoothUITabCraftPopPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance) => TabPop.Pop(__instance.m_tabCraft);
    }

    [HarmonyPatch(typeof(InventoryGui), "OnTabUpgradePressed")]
    internal static class SmoothUITabUpgradePopPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance) => TabPop.Pop(__instance.m_tabUpgrade);
    }
}
