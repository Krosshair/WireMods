using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal static class BuildMenuAnimationSystem
    {
        private sealed class State
        {
            internal Transform Root;
            internal Transform Parent;
            internal Button Button;
            internal Vector3 BasePosition;
            internal Vector3 BaseScale;
            internal Quaternion BaseRotation;
            internal Vector3 AppliedPosition;
            internal float AppliedScale = 1f;
            internal float AppliedRotation;
            internal bool Applied;
            internal bool Hovered;
            internal bool Pressed;
            internal bool Selected;
            internal float Scale = 1f;
            internal Vector2 Sway;
            internal float Rotation;
            internal int ActiveIndex = -1;
        }

        private static readonly Dictionary<Transform, State> States =
            new Dictionary<Transform, State>();
        private static readonly List<State> Active = new List<State>();

        internal static bool OwnsButton(Button button)
        {
            if (button == null)
                return false;

            return button.GetComponentInParent<BuildUiPieceButton>() != null ||
                   button.GetComponentInParent<BuildUiTagButton>() != null ||
                   button.GetComponentInParent<BuildUiFavoriteCategoryCheckButton>() != null;
        }

        internal static void Register(Component owner)
        {
            if (owner == null)
                return;

            Transform root = owner.transform;
            if (root == null || States.ContainsKey(root))
                return;

            Button button = owner.GetComponent<Button>();
            if (button == null)
                return;

            // This custom owner moves the complete tile. Prevent the generic button
            // system from also scaling the same transform.
            ButtonAnimationSystem.Unregister(button);

            State state = new State
            {
                Root = root,
                Button = button
            };
            CaptureBase(state);
            States.Add(root, state);

            BuildMenuMotionHook hook = owner.GetComponent<BuildMenuMotionHook>();
            if (hook == null)
                hook = owner.gameObject.AddComponent<BuildMenuMotionHook>();
            hook.Bind(root);
        }

        internal static void Unregister(Transform root)
        {
            if (root == null || !States.TryGetValue(root, out State state))
                return;

            RestoreState(state);
            Deactivate(state);
            States.Remove(root);
        }

        internal static void SetHovered(Transform root, bool value)
        {
            if (root == null || !States.TryGetValue(root, out State state))
                return;

            state.Hovered = value && ZInput.instance != null && ZInput.IsMouseActive();
            if (!state.Hovered)
                state.Pressed = false;
            ActivateFresh(state);
        }

        internal static void SetPressed(Transform root, bool value)
        {
            if (root == null || !States.TryGetValue(root, out State state))
                return;

            state.Pressed = value && state.Hovered;
            ActivateFresh(state);
        }

        internal static void SetSelected(Transform root, bool value)
        {
            if (root == null || !States.TryGetValue(root, out State state))
                return;

            state.Selected = value;
            ActivateFresh(state);
        }

        private static void ActivateFresh(State state)
        {
            if (state.ActiveIndex >= 0)
                return;

            CaptureBase(state);
            state.Scale = 1f;
            state.Sway = Vector2.zero;
            state.Rotation = 0f;
            state.ActiveIndex = Active.Count;
            Active.Add(state);
            SmoothUIDriver.Wake();
        }

        internal static bool Busy => Active.Count > 0;

        internal static void Tick()
        {
            if (!WiresSmoothUIPlugin.BuildMenuAnimations.Value)
            {
                RestoreAll();
                return;
            }

            if (Active.Count == 0)
                return;

            float response = Mathf.Max(1f, WiresSmoothUIPlugin.BuildMenuResponse.Value);
            float hoverScale = Mathf.Clamp(WiresSmoothUIPlugin.BuildMenuHoverScale.Value, 1f, 1.20f);
            float gamepadScale = 1f + (hoverScale - 1f) * 0.75f;

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                Transform root = state.Root;
                Button button = state.Button;

                if (root == null || button == null || !root.gameObject.activeInHierarchy)
                {
                    RestoreState(state);
                    DeactivateAt(i);
                    continue;
                }

                RestoreState(state);

                bool mouse = state.Hovered && ZInput.instance != null && ZInput.IsMouseActive();
                bool gamepad = state.Selected && ZInput.instance != null && ZInput.IsExclusiveGamepadActive();

                float targetScale = 1f;
                if (state.Pressed && mouse)
                    targetScale = 0.965f;
                else if (mouse)
                    targetScale = hoverScale;
                else if (gamepad)
                    targetScale = gamepadScale;

                state.Scale = WiresSmoothUIPlugin.Damp(state.Scale, targetScale, response);

                Vector2 swayTarget = Vector2.zero;
                float rotationTarget = 0f;
                if (mouse && WiresSmoothUIPlugin.BuildMenuInertia.Value)
                {
                    Vector2 delta = Vector2.ClampMagnitude(PointerMotion.Delta, 18f) / 18f;
                    swayTarget = new Vector2(-delta.x, -delta.y) * 1.65f;
                    rotationTarget = -delta.x * 1.15f;
                }

                state.Sway = WiresSmoothUIPlugin.Damp(
                    state.Sway, swayTarget, response * 0.75f);
                state.Rotation = WiresSmoothUIPlugin.Damp(
                    state.Rotation, rotationTarget, response * 0.70f);

                ApplyState(state);

                bool engaged = mouse || gamepad || (state.Pressed && mouse);
                bool settled = Mathf.Abs(state.Scale - 1f) < 0.001f &&
                               state.Sway.sqrMagnitude < 0.0005f &&
                               Mathf.Abs(state.Rotation) < 0.01f;

                if (!engaged && settled)
                {
                    RestoreState(state);
                    DeactivateAt(i);
                }
            }
        }

        private static void ApplyState(State state)
        {
            Transform root = state.Root;
            if (root == null)
                return;

            Vector3 offset = new Vector3(state.Sway.x, state.Sway.y, 0f);
            root.localPosition = state.BasePosition + offset;
            root.localScale = state.BaseScale * state.Scale;
            root.localRotation =
                state.BaseRotation * Quaternion.Euler(0f, 0f, state.Rotation);

            state.AppliedPosition = offset;
            state.AppliedScale = state.Scale;
            state.AppliedRotation = state.Rotation;
            state.Applied = true;
        }

        private static void CaptureBase(State state)
        {
            if (state?.Root == null)
                return;

            state.Parent = state.Root.parent;
            state.BasePosition = state.Root.localPosition;
            state.BaseScale = state.Root.localScale;
            state.BaseRotation = state.Root.localRotation;
            state.Applied = false;
            state.AppliedPosition = Vector3.zero;
            state.AppliedScale = 1f;
            state.AppliedRotation = 0f;
        }

        private static void RestoreState(State state)
        {
            if (state?.Root == null || !state.Applied)
                return;

            Transform root = state.Root;
            if (root.parent != state.Parent)
            {
                CaptureBase(state);
                return;
            }

            Vector3 expectedPosition = state.BasePosition + state.AppliedPosition;
            Vector3 expectedScale = state.BaseScale * state.AppliedScale;
            Quaternion expectedRotation =
                state.BaseRotation * Quaternion.Euler(0f, 0f, state.AppliedRotation);

            if ((root.localPosition - expectedPosition).sqrMagnitude < 0.0009f)
                root.localPosition = state.BasePosition;
            else
                state.BasePosition = root.localPosition;

            if ((root.localScale - expectedScale).sqrMagnitude < 0.0009f)
                root.localScale = state.BaseScale;
            else
                state.BaseScale = root.localScale;

            if (Quaternion.Angle(root.localRotation, expectedRotation) < 0.05f)
                root.localRotation = state.BaseRotation;
            else
                state.BaseRotation = root.localRotation;

            state.Applied = false;
            state.AppliedPosition = Vector3.zero;
            state.AppliedScale = 1f;
            state.AppliedRotation = 0f;
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

        internal static void RestoreAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                RestoreState(state);
                state.Hovered = false;
                state.Pressed = false;
                state.Selected = false;
                state.Scale = 1f;
                state.Sway = Vector2.zero;
                state.Rotation = 0f;
                DeactivateAt(i);
            }
        }

        internal static void Shutdown()
        {
            RestoreAll();
            States.Clear();
        }
    }

    internal sealed class BuildMenuMotionHook : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        private Transform _root;

        internal void Bind(Transform root)
        {
            _root = root;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            BuildMenuAnimationSystem.SetHovered(_root, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            BuildMenuAnimationSystem.SetHovered(_root, false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                BuildMenuAnimationSystem.SetPressed(_root, true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                BuildMenuAnimationSystem.SetPressed(_root, false);
        }

        public void OnSelect(BaseEventData eventData)
        {
            BuildMenuAnimationSystem.SetSelected(_root, true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            BuildMenuAnimationSystem.SetSelected(_root, false);
        }

        private void OnDisable()
        {
            BuildMenuAnimationSystem.SetHovered(_root, false);
            BuildMenuAnimationSystem.SetPressed(_root, false);
            BuildMenuAnimationSystem.SetSelected(_root, false);
        }

        private void OnDestroy()
        {
            BuildMenuAnimationSystem.Unregister(_root);
        }
    }

    [HarmonyPatch(typeof(BuildUiPieceButton), "Awake")]
    internal static class SmoothUIBuildPieceButtonPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUiPieceButton __instance)
        {
            BuildMenuAnimationSystem.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(BuildUiTagButton), "Awake")]
    internal static class SmoothUIBuildTagButtonPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUiTagButton __instance)
        {
            BuildMenuAnimationSystem.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(BuildUiFavoriteCategoryCheckButton), "Awake")]
    internal static class SmoothUIBuildFavoriteCheckPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUiFavoriteCategoryCheckButton __instance)
        {
            BuildMenuAnimationSystem.Register(__instance);
        }
    }
}
