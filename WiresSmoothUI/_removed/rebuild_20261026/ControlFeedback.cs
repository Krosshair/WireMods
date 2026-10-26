using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Settings-style controls: checkmarks pop when you tick them, slider
    // handles bounce when grabbed and let go, dropdown lists scale open.
    // All of it fires from the click itself.
    internal static class ControlFeedback
    {
        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.ButtonAnimations.Value;

        internal static void Toggled(Toggle toggle)
        {
            if (!On || toggle == null || toggle.graphic == null)
                return;
            if (toggle.isOn)
                MicroMotionSystem.Pop(toggle.graphic.transform, 0.4f, 1.25f, 0.24f);
            else
                MicroMotionSystem.Punch(toggle.graphic.transform, 0.85f, 1.05f, 0.16f);
        }

        internal static void SliderGrab(Slider slider, bool down)
        {
            if (!On || slider == null || slider.handleRect == null)
                return;
            if (down)
                MicroMotionSystem.Pulse(slider.handleRect, 1.3f, 0.22f);
            else
                MicroMotionSystem.Punch(slider.handleRect, 0.85f, 1.08f, 0.2f);
        }
    }

    [HarmonyPatch(typeof(Toggle), nameof(Toggle.OnPointerClick))]
    internal static class SmoothUIToggleClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Toggle __instance) => ControlFeedback.Toggled(__instance);
    }

    [HarmonyPatch(typeof(Toggle), nameof(Toggle.OnSubmit))]
    internal static class SmoothUIToggleSubmitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Toggle __instance) => ControlFeedback.Toggled(__instance);
    }

    [HarmonyPatch(typeof(Slider), nameof(Slider.OnPointerDown))]
    internal static class SmoothUISliderDownPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Slider __instance) => ControlFeedback.SliderGrab(__instance, true);
    }

    [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerUp))]
    internal static class SmoothUISliderUpPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Selectable __instance)
        {
            if (__instance is Slider slider)
                ControlFeedback.SliderGrab(slider, false);
        }
    }

    [HarmonyPatch(typeof(TMP_Dropdown), nameof(TMP_Dropdown.Show))]
    internal static class SmoothUIDropdownPatch
    {
        private static readonly FieldInfo ListField = AccessTools.Field(typeof(TMP_Dropdown), "m_Dropdown");

        [HarmonyPostfix]
        private static void Postfix(TMP_Dropdown __instance)
        {
            if (!ControlFeedback.On)
                return;
            // The dropdown already fades itself; this adds the scale.
            GameObject list = ListField?.GetValue(__instance) as GameObject;
            if (list != null)
                MicroMotionSystem.Pop(list.transform, 0.9f, 1.01f, 0.2f);
        }
    }
}
