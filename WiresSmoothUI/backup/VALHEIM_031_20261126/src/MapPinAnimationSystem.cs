using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal static class MapPinAnimationSystem
    {
        // Event-driven only: this contains pins that were just created and are waiting
        // for Valheim's next UpdatePins pass to instantiate their visual object.
        private static readonly List<Minimap.PinData> Pending =
            new List<Minimap.PinData>(8);

        private static readonly MethodInfo ClosestPinToCursor =
            AccessTools.Method(typeof(Minimap), "GetClosestPinToCursor");

        internal struct ToggleState
        {
            internal Minimap.PinData Pin;
            internal bool Checked;
            internal long OwnerId;
        }

        private static bool Enabled =>
            WiresSmoothUIPlugin.Enabled.Value &&
            WiresSmoothUIPlugin.MapPinAnimations.Value;

        private const float Strength = 1f;

        internal static void OnPinAdded(
            Minimap minimap,
            Minimap.PinData pin,
            bool save,
            long ownerId)
        {
            if (!Enabled || minimap == null || pin == null)
                return;

            // Random-event, ping and shout pins already have Valheim's own continuous
            // animated size pulse. Do not layer a second animation over those.
            if (pin.m_animate)
                return;

            // Saved pins are also created while loading a world/map. Only treat one as
            // a placement event when the map is actually open.
            if (save && !Minimap.IsOpen())
                return;

            // The same applies to old shared pins loaded from another player.
            if (ownerId != 0L && !Minimap.IsOpen())
                return;

            if (!Pending.Contains(pin))
                Pending.Add(pin);
        }

        internal static void OnPinRemoved(Minimap.PinData pin)
        {
            if (pin != null)
                Pending.Remove(pin);
        }

        internal static void ProcessPending()
        {
            if (!Enabled || Pending.Count == 0)
                return;

            float peak = Mathf.Max(1f, WiresSmoothUIPlugin.MapPinPopScale.Value);
            float duration = Mathf.Max(0.05f, WiresSmoothUIPlugin.MapPinPopDuration.Value);

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                Minimap.PinData pin = Pending[i];
                if (pin == null)
                {
                    Pending.RemoveAt(i);
                    continue;
                }

                if (pin.m_animate)
                {
                    Pending.RemoveAt(i);
                    continue;
                }

                if (pin.m_uiElement == null || !pin.m_uiElement.gameObject.activeInHierarchy)
                    continue;

                MicroMotionSystem.Pop(pin.m_uiElement, 0.72f, peak, duration);
                Pending.RemoveAt(i);
            }
        }

        internal static ToggleState CaptureToggle(Minimap minimap)
        {
            ToggleState state = default;
            if (!Enabled || minimap == null || ClosestPinToCursor == null)
                return state;

            try
            {
                state.Pin = ClosestPinToCursor.Invoke(minimap, null) as Minimap.PinData;
                if (state.Pin != null)
                {
                    state.Checked = state.Pin.m_checked;
                    state.OwnerId = state.Pin.m_ownerID;
                }
            }
            catch
            {
                state.Pin = null;
            }

            return state;
        }

        internal static void FinishToggle(ToggleState state)
        {
            if (!Enabled || state.Pin == null || state.Pin.m_uiElement == null)
                return;

            bool changed = state.Checked != state.Pin.m_checked ||
                           state.OwnerId != state.Pin.m_ownerID;
            if (!changed)
                return;

            MicroMotionSystem.Punch(
                state.Pin.m_uiElement,
                0.92f,
                1f + 0.09f * Strength,
                0.18f);
        }

        internal static void AnimateIconFilter(Minimap minimap, Minimap.PinType type)
        {
            if (!Enabled || minimap == null || !Minimap.IsOpen())
                return;

            Image image = GetSelectionImage(minimap, type);
            Transform target = image != null && image.transform.parent != null
                ? image.transform.parent
                : image?.transform;

            if (target != null)
                MicroMotionSystem.Punch(
                    target,
                    0.95f,
                    1f + 0.055f * Strength,
                    0.16f);
        }

        internal static void AnimateNameInput(Minimap minimap)
        {
            if (!Enabled || minimap?.m_nameInput == null ||
                !minimap.m_nameInput.gameObject.activeInHierarchy)
                return;

            MicroMotionSystem.Pop(
                minimap.m_nameInput.transform,
                0.92f,
                1f + 0.05f * Strength,
                0.18f);
        }

        private static Image GetSelectionImage(Minimap minimap, Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Icon0: return minimap.m_selectedIcon0;
                case Minimap.PinType.Icon1: return minimap.m_selectedIcon1;
                case Minimap.PinType.Icon2: return minimap.m_selectedIcon2;
                case Minimap.PinType.Icon3: return minimap.m_selectedIcon3;
                case Minimap.PinType.Icon4: return minimap.m_selectedIcon4;
                case Minimap.PinType.Death: return minimap.m_selectedIconDeath;
                case Minimap.PinType.Boss: return minimap.m_selectedIconBoss;
                case Minimap.PinType.Ping: return minimap.m_selectedIconPing;
                default: return null;
            }
        }

        internal static void Shutdown()
        {
            Pending.Clear();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.AddPin))]
    internal static class SmoothUIMapPinAddPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Minimap __instance,
            bool save,
            long ownerID,
            Minimap.PinData __result)
        {
            MapPinAnimationSystem.OnPinAdded(__instance, __result, save, ownerID);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), new[] { typeof(Minimap.PinData) })]
    internal static class SmoothUIMapPinRemovePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Minimap.PinData pin)
        {
            MapPinAnimationSystem.OnPinRemoved(pin);
        }
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class SmoothUIMapPinUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MapPinAnimationSystem.ProcessPending();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class SmoothUIMapPinTogglePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Minimap __instance,
            out MapPinAnimationSystem.ToggleState __state)
        {
            __state = MapPinAnimationSystem.CaptureToggle(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(MapPinAnimationSystem.ToggleState __state)
        {
            MapPinAnimationSystem.FinishToggle(__state);
        }
    }

    [HarmonyPatch(typeof(Minimap), "ToggleIconFilter")]
    internal static class SmoothUIMapFilterIconPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance, Minimap.PinType type)
        {
            MapPinAnimationSystem.AnimateIconFilter(__instance, type);
        }
    }

    [HarmonyPatch(typeof(Minimap), "ShowPinNameInput")]
    internal static class SmoothUIMapPinNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance)
        {
            MapPinAnimationSystem.AnimateNameInput(__instance);
        }
    }
}
