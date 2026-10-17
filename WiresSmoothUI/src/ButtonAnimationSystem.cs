using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal static class ButtonAnimationSystem
    {
        internal sealed class State
        {
            internal Button Button;
            internal Vector3 BaseScale;
            internal float Scale = 1f;
            internal float AppliedScale = 1f;
            internal bool Applied;
            internal bool Hovered;
            internal bool Pressed;
            internal int ActiveIndex = -1;
        }

        private static readonly Dictionary<Button, State> States = new Dictionary<Button, State>();
        private static readonly List<State> Active = new List<State>();

        internal static void Register(Button button)
        {
            if (button == null || States.ContainsKey(button))
                return;

            // Inventory slots have their own single-owner motion system.
            // This is especially important for AzuEPI equipment/quick slots:
            // Azu reparents real InventoryElements outside InventoryGrid, so checking
            // InventoryGrid alone would make those slots receive two scale animations.
            if (button.GetComponentInParent<InventoryElement>() != null
                || button.GetComponentInParent<InventoryGrid>() != null
                || button.GetComponentInParent<HotkeyBar>() != null
                || BuildMenuAnimationSystem.OwnsButton(button))
                return;

            State state = new State
            {
                Button = button,
                BaseScale = button.transform.localScale
            };

            States.Add(button, state);
            ButtonMotionHook hook = button.GetComponent<ButtonMotionHook>();
            if (hook == null)
                hook = button.gameObject.AddComponent<ButtonMotionHook>();
            hook.Bind(button);
        }

        internal static void Unregister(Button button)
        {
            if (ReferenceEquals(button, null) || !States.TryGetValue(button, out State state))
                return;

            RestoreState(state);
            Deactivate(state);
            States.Remove(button);
        }

        internal static void SetHovered(Button button, bool hovered)
        {
            if (button == null || !States.TryGetValue(button, out State state))
                return;

            state.Hovered = hovered;
            if (!hovered)
                state.Pressed = false;

            if (WiresSmoothUIPlugin.ButtonAnimations.Value)
                Activate(state);
        }

        internal static void SetPressed(Button button, bool pressed)
        {
            if (button == null || !States.TryGetValue(button, out State state))
                return;

            state.Pressed = pressed;
            if (WiresSmoothUIPlugin.ButtonAnimations.Value)
                Activate(state);
        }

        internal static void Tick()
        {
            if (!WiresSmoothUIPlugin.ButtonAnimations.Value)
            {
                RestoreActive();
                return;
            }

            if (Active.Count == 0)
                return;

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                Button button = state.Button;
                if (button == null)
                {
                    DeactivateAt(i);
                    continue;
                }

                RestoreState(state);

                if (!button.gameObject.activeInHierarchy || !button.interactable)
                {
                    state.Hovered = false;
                    state.Pressed = false;
                }

                float target = state.Pressed
                    ? 0.975f
                    : state.Hovered
                        ? Mathf.Clamp(WiresSmoothUIPlugin.ButtonHoverScale.Value, 1f, 1.15f)
                        : 1f;

                state.Scale = WiresSmoothUIPlugin.Damp(
                    state.Scale,
                    target,
                    WiresSmoothUIPlugin.ButtonResponse.Value);

                if (!state.Hovered && !state.Pressed && Mathf.Abs(state.Scale - 1f) < 0.001f)
                {
                    state.Scale = 1f;
                    button.transform.localScale = state.BaseScale;
                    DeactivateAt(i);
                    continue;
                }

                state.AppliedScale = state.Scale;
                button.transform.localScale = state.BaseScale * state.AppliedScale;
                state.Applied = true;
            }
        }

        private static void RestoreState(State state)
        {
            if (state == null || state.Button == null || !state.Applied)
                return;

            Transform transform = state.Button.transform;
            Vector3 expected = state.BaseScale * state.AppliedScale;
            if ((transform.localScale - expected).sqrMagnitude < 0.0009f)
                transform.localScale = state.BaseScale;
            else
                state.BaseScale = transform.localScale;

            state.Applied = false;
            state.AppliedScale = 1f;
        }

        private static void Activate(State state)
        {
            if (state.ActiveIndex >= 0)
                return;

            state.ActiveIndex = Active.Count;
            Active.Add(state);
        }

        private static void Deactivate(State state)
        {
            if (state.ActiveIndex >= 0)
                DeactivateAt(state.ActiveIndex);
        }

        private static void DeactivateAt(int index)
        {
            int last = Active.Count - 1;
            State removed = Active[index];
            if (index != last)
            {
                State moved = Active[last];
                Active[index] = moved;
                moved.ActiveIndex = index;
            }

            Active.RemoveAt(last);
            removed.ActiveIndex = -1;
        }

        private static void RestoreActive()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                RestoreState(state);
                state.Scale = 1f;
                state.Hovered = false;
                state.Pressed = false;
                DeactivateAt(i);
            }
        }

        internal static void RestoreAll()
        {
            RestoreActive();
        }

        internal static void Shutdown()
        {
            RestoreActive();
            States.Clear();
        }
    }

    internal sealed class ButtonMotionHook : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Button _button;

        internal void Bind(Button button)
        {
            _button = button;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            ButtonAnimationSystem.SetHovered(_button, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ButtonAnimationSystem.SetHovered(_button, false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                ButtonAnimationSystem.SetPressed(_button, true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                ButtonAnimationSystem.SetPressed(_button, false);
        }

        private void OnDisable()
        {
            ButtonAnimationSystem.SetHovered(_button, false);
            ButtonAnimationSystem.SetPressed(_button, false);
        }

        private void OnDestroy()
        {
            ButtonAnimationSystem.Unregister(_button);
        }
    }

    [HarmonyPatch(typeof(Selectable), "OnEnable")]
    internal static class SmoothUIButtonEnablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Selectable __instance)
        {
            if (__instance is Button button)
                ButtonAnimationSystem.Register(button);
        }
    }
}
