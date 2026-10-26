using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // The hotbar keeps its slots in a private list of a private type.
    internal static class HotbarAccess
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(HotkeyBar), "m_elements");
        private static FieldInfo _go, _queued, _equiped;

        // Looked up when something happens, never per frame.
        internal static HotkeyBar[] Bars() => Object.FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None);

        internal static IList Elements(HotkeyBar bar) => bar != null ? ElementsField?.GetValue(bar) as IList : null;

        internal static GameObject Slot(object element) => Get(ref _go, element, "m_go");
        internal static GameObject Queued(object element) => Get(ref _queued, element, "m_queued");
        internal static GameObject Equiped(object element) => Get(ref _equiped, element, "m_equiped");

        internal static IEnumerable<GameObject> SlotsAt(int slot)
        {
            foreach (HotkeyBar bar in Bars())
            {
                IList list = Elements(bar);
                if (list == null || slot < 0 || slot >= list.Count)
                    continue;
                GameObject go = Slot(list[slot]);
                if (go != null && go.activeInHierarchy)
                    yield return go;
            }
        }

        private static GameObject Get(ref FieldInfo field, object element, string name)
        {
            if (element == null)
                return null;
            if (field == null)
                field = AccessTools.Field(element.GetType(), name);
            return field?.GetValue(element) as GameObject;
        }
    }

    // Small reactions to things you do. Things that happen constantly on their
    // own (weight changing, getting hit, regen, status effects) aren't animated.
    internal static class FeedbackPops
    {
        // Equipping on spawn/load isn't something you did.
        private const float SpawnQuiet = 2f;
        internal static float SpawnTime = -100f;

        private static bool Pops =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.FeedbackPops.Value;

        private static bool Controls =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.ButtonAnimations.Value;

        private static bool JustSpawned => Time.unscaledTime - SpawnTime < SpawnQuiet;

        // ---- Hotbar, armor, food ----

        internal static void HotbarSlot(int slot)
        {
            if (!Pops)
                return;
            foreach (GameObject go in HotbarAccess.SlotsAt(slot))
            {
                Transform icon = go.transform.Find("icon");
                if (icon != null)
                    UiMotion.Pop(icon, 0.88f, 1.12f, 0.22f);
            }
        }

        internal static void EquipChanged(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (!Pops || item == null || player == null || JustSpawned)
                return;

            InventoryGui gui = InventoryGui.instance;
            if (item.m_shared.m_armor > 0f && gui != null && gui.m_armor != null)
                UiMotion.Pop(gui.m_armor.transform, 1f, 1.15f, 0.2f);

            if (item.m_gridPos.y == 0 && player.GetInventory().ContainsItem(item))
                HotbarSlot(item.m_gridPos.x);
        }

        internal static void Ate(Player player, ItemDrop.ItemData item)
        {
            if (!Pops || Hud.instance == null || Hud.instance.m_foodIcons == null)
                return;

            List<Player.Food> foods = player.GetFoods();
            for (int i = 0; i < foods.Count && i < Hud.instance.m_foodIcons.Length; i++)
            {
                if (foods[i].m_item != null && foods[i].m_item.m_shared.m_name == item.m_shared.m_name)
                {
                    if (Hud.instance.m_foodIcons[i] != null)
                        UiMotion.Pop(Hud.instance.m_foodIcons[i].transform, 0.7f, 1.2f, 0.3f);
                    return;
                }
            }
        }

        // ---- "Can't" shakes ----

        internal static void DisabledClick(Button button)
        {
            if (!Controls || button == null || IsTab(button) || button.GetComponentInParent<InventoryElement>() != null)
                return;
            UiMotion.Shake(button.transform);
        }

        internal static void EmptyHotbarKey(int slot)
        {
            if (!Pops)
                return;
            foreach (GameObject go in HotbarAccess.SlotsAt(slot))
                UiMotion.Shake(go.transform);
        }

        // The selected tab is disabled; clicking it isn't a mistake.
        private static bool IsTab(Button button)
        {
            foreach (TabHandler handler in button.GetComponentsInParent<TabHandler>(true))
                foreach (TabHandler.Tab tab in handler.m_tabs)
                    if (tab.m_button == button)
                        return true;

            // Craft/Upgrade, and tabs other mods add next to them (Recycler).
            InventoryGui gui = InventoryGui.instance;
            return gui != null && gui.m_tabCraft != null && button.transform.parent == gui.m_tabCraft.transform.parent;
        }

        // ---- Menus ----

        internal static void Tab(Button tab)
        {
            if (!PanelMotionSystem.On || tab == null)
                return;
            TMP_Text label = tab.GetComponentInChildren<TMP_Text>();
            if (label != null)
                UiMotion.Pop(label.transform, 0.85f, 1.12f, 0.22f);
        }

        internal static void SplitAmount(TMP_Text amount)
        {
            if (Controls && amount != null)
                UiMotion.Pop(amount.transform, 1f, 1.14f, 0.14f);
        }

        internal static void Toggled(Toggle toggle)
        {
            if (!Controls || toggle == null || toggle.graphic == null)
                return;
            if (toggle.isOn)
                UiMotion.Pop(toggle.graphic.transform, 0.4f, 1.25f, 0.24f);
            else
                UiMotion.Pop(toggle.graphic.transform, 0.85f, 1.05f, 0.16f);
        }

        internal static void SliderGrab(Slider slider, bool down)
        {
            if (!Controls || slider == null || slider.handleRect == null)
                return;
            if (down)
                UiMotion.Pop(slider.handleRect, 1f, 1.3f, 0.22f);
            else
                UiMotion.Pop(slider.handleRect, 0.85f, 1.08f, 0.2f);
        }

        // The dropdown already fades itself; this adds the scale.
        internal static void DropdownOpened(GameObject list)
        {
            if (Controls && list != null)
                UiMotion.Pop(list.transform, 0.9f, 1.01f, 0.2f);
        }
    }

    // ---- Game hooks ----

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class SmoothUISpawnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                FeedbackPops.SpawnTime = Time.unscaledTime;
        }
    }

    [HarmonyPatch(typeof(Player), "UseHotbarItem")]
    internal static class SmoothUIHotbarUsePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, int index)
        {
            if (__instance != Player.m_localPlayer || index < 1)
                return;
            if (__instance.GetInventory().GetItemAt(index - 1, 0) != null)
                FeedbackPops.HotbarSlot(index - 1);
            else
                FeedbackPops.EmptyHotbarKey(index - 1);
        }
    }

    [HarmonyPatch(typeof(Player), "EatFood")]
    internal static class SmoothUIEatPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result && __instance == Player.m_localPlayer && item != null)
                FeedbackPops.Ate(__instance, item);
        }
    }

    [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerDown))]
    internal static class SmoothUIDisabledClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Selectable __instance, PointerEventData eventData)
        {
            if (__instance is Button button && !button.interactable
                && eventData != null && eventData.button == PointerEventData.InputButton.Left)
                FeedbackPops.DisabledClick(button);
        }
    }

    [HarmonyPatch(typeof(SplitDialog), "SliderChanged")]
    internal static class SmoothUISplitAmountPatch
    {
        private static readonly FieldInfo AmountField = AccessTools.Field(typeof(SplitDialog), "m_splitAmount");

        [HarmonyPostfix]
        private static void Postfix(SplitDialog __instance) =>
            FeedbackPops.SplitAmount(AmountField?.GetValue(__instance) as TMP_Text);
    }

    [HarmonyPatch(typeof(Toggle), nameof(Toggle.OnPointerClick))]
    internal static class SmoothUIToggleClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Toggle __instance) => FeedbackPops.Toggled(__instance);
    }

    [HarmonyPatch(typeof(Toggle), nameof(Toggle.OnSubmit))]
    internal static class SmoothUIToggleSubmitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Toggle __instance) => FeedbackPops.Toggled(__instance);
    }

    [HarmonyPatch(typeof(Slider), nameof(Slider.OnPointerDown))]
    internal static class SmoothUISliderDownPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Slider __instance) => FeedbackPops.SliderGrab(__instance, true);
    }

    [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerUp))]
    internal static class SmoothUISliderUpPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Selectable __instance)
        {
            if (__instance is Slider slider)
                FeedbackPops.SliderGrab(slider, false);
        }
    }

    [HarmonyPatch(typeof(TMP_Dropdown), nameof(TMP_Dropdown.Show))]
    internal static class SmoothUIDropdownPatch
    {
        private static readonly FieldInfo ListField = AccessTools.Field(typeof(TMP_Dropdown), "m_Dropdown");

        [HarmonyPostfix]
        private static void Postfix(TMP_Dropdown __instance) =>
            FeedbackPops.DropdownOpened(ListField?.GetValue(__instance) as GameObject);
    }

    // ---- Map pins you remove shrink away ----
    // Only removing a pin; the map also destroys markers when it hides them
    // (switching map modes, filters), and those should just go.

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Minimap.PinData) })]
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
            UiMotion.ShrinkAway(go);
        }
    }
}
