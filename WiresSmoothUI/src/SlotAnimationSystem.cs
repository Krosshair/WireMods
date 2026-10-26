using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    internal static class SlotAnimationSystem
    {
        private sealed class RefComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly RefComparer<T> Instance = new RefComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class State
        {
            internal InventoryElement Element;
            internal InventoryGrid Grid;
            internal Transform Root;
            internal Transform Parent;
            internal UIInputHandler Input;
            internal Action<UIInputHandler> EnterHandler;
            internal Action<UIInputHandler> ExitHandler;
            internal Action<UIInputHandler> DownHandler;
            internal Action<UIInputHandler> UpHandler;
            internal Vector3 BasePosition;
            internal Vector3 BaseScale;
            internal Quaternion BaseRotation;
            internal Vector3 AppliedPosition;
            internal float AppliedScale = 1f;
            internal float AppliedRotation;
            internal bool Applied;
            internal bool Hovered;
            internal bool Pressed;
            internal bool GamepadSelected;
            internal float Scale = 1f;
            internal Vector2 Sway;
            internal float Rotation;
            internal int ActiveIndex = -1;
        }

        private static readonly Dictionary<InventoryElement, State> States =
            new Dictionary<InventoryElement, State>(RefComparer<InventoryElement>.Instance);

        private static readonly Dictionary<InventoryGrid, State> GamepadSelections =
            new Dictionary<InventoryGrid, State>(RefComparer<InventoryGrid>.Instance);

        private static readonly Dictionary<Transform, State> RootStates =
            new Dictionary<Transform, State>(RefComparer<Transform>.Instance);

        private static readonly List<State> Active = new List<State>();

        internal static void RegisterElement(InventoryElement element, InventoryGrid grid)
        {
            if (element == null || States.ContainsKey(element))
                return;

            State state = new State
            {
                Element = element,
                Grid = grid,
                Root = element.transform
            };
            CaptureBase(state);
            States.Add(element, state);
            RootStates[element.transform] = state;

            UIInputHandler input = element.GetComponentInChildren<UIInputHandler>(true);
            if (input != null)
            {
                state.Input = input;
                state.EnterHandler = _ => SetHovered(state, true);
                state.ExitHandler = _ => SetHovered(state, false);
                state.DownHandler = _ => SetPressed(state, true);
                state.UpHandler = _ => SetPressed(state, false);
                input.m_onPointerEnter += state.EnterHandler;
                input.m_onPointerExit += state.ExitHandler;
                input.m_onLeftDown += state.DownHandler;
                input.m_onLeftUp += state.UpHandler;
            }
        }

        internal static void UnregisterElement(InventoryElement element)
        {
            if (ReferenceEquals(element, null) || !States.TryGetValue(element, out State state))
                return;

            RestoreState(state);
            Deactivate(state);
            DetachInput(state);

            if (state.Grid != null
                && GamepadSelections.TryGetValue(state.Grid, out State selected)
                && ReferenceEquals(selected, state))
                GamepadSelections.Remove(state.Grid);

            if (state.Root != null)
                RootStates.Remove(state.Root);

            States.Remove(element);
        }

        private static void DetachInput(State state)
        {
            if (state.Input == null)
                return;

            if (state.EnterHandler != null)
                state.Input.m_onPointerEnter -= state.EnterHandler;
            if (state.ExitHandler != null)
                state.Input.m_onPointerExit -= state.ExitHandler;
            if (state.DownHandler != null)
                state.Input.m_onLeftDown -= state.DownHandler;
            if (state.UpHandler != null)
                state.Input.m_onLeftUp -= state.UpHandler;
        }

        private static void SetHovered(State state, bool value)
        {
            if (state == null)
                return;

            state.Hovered = value && ZInput.instance != null && ZInput.IsMouseActive();
            if (!state.Hovered)
                state.Pressed = false;

            ActivateWithFreshBase(state);
        }

        private static void SetPressed(State state, bool value)
        {
            if (state == null)
                return;

            // Only push in when the click picks up or places an item. Modifier
            // clicks like Stackify's Alt favoriting, or clicking an empty slot
            // with nothing in hand, leave the slot alone.
            state.Pressed = value && state.Hovered && ClickActs(state);
            ActivateWithFreshBase(state);
        }

        private static bool ClickActs(State state)
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr))
                return false;
            if (DragAnimationSystem.IsDragging)
                return true;
            return state.Element != null && state.Element.m_used;
        }

        private static void ActivateWithFreshBase(State state)
        {
            if (state.ActiveIndex < 0)
            {
                CaptureBase(state);
                state.Scale = 1f;
                state.Sway = Vector2.zero;
                state.Rotation = 0f;
                state.ActiveIndex = Active.Count;
                Active.Add(state);
                SmoothUIDriver.Wake();
            }
        }

        internal static void UpdateGamepadSelection(InventoryGrid grid)
        {
            if (grid == null)
                return;

            State previous = null;
            GamepadSelections.TryGetValue(grid, out previous);

            State next = null;
            if (ZInput.instance != null && ZInput.IsExclusiveGamepadActive())
            {
                RectTransform selected = grid.GetGamepadSelectedElement();
                if (selected != null)
                    RootStates.TryGetValue(selected, out next);
            }

            if (!ReferenceEquals(previous, next))
            {
                if (previous != null)
                {
                    previous.GamepadSelected = false;
                    ActivateWithFreshBase(previous);
                }

                if (next != null)
                {
                    next.GamepadSelected = true;
                    ActivateWithFreshBase(next);
                    GamepadSelections[grid] = next;
                }
                else
                {
                    GamepadSelections.Remove(grid);
                }
            }
        }

        internal static bool Busy => Active.Count > 0;

        internal static void Tick()
        {
            if (!WiresSmoothUIPlugin.SlotAnimations.Value)
            {
                RestoreAll();
                return;
            }

            if (Active.Count == 0)
                return;

            bool dragging = DragAnimationSystem.IsDragging;
            float response = Mathf.Max(1f, WiresSmoothUIPlugin.HoverResponse.Value);

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                InventoryElement element = state.Element;
                Transform root = state.Root;

                if (element == null || root == null || !element.gameObject.activeInHierarchy)
                {
                    RestoreState(state);
                    DeactivateAt(i);
                    continue;
                }

                RestoreState(state);

                bool mouse = state.Hovered && ZInput.instance != null && ZInput.IsMouseActive();
                bool gamepad = state.GamepadSelected && ZInput.IsExclusiveGamepadActive();
                bool validDrop = dragging && mouse && (!element.m_used || element.m_canBeDroppedOn);

                float targetScale = 1f;
                if (state.Pressed)
                    targetScale = 0.965f;
                else if (validDrop)
                    targetScale = Mathf.Max(
                        WiresSmoothUIPlugin.HoverScale.Value,
                        WiresSmoothUIPlugin.DropTargetScale.Value);
                else if (mouse)
                    targetScale = Mathf.Clamp(WiresSmoothUIPlugin.HoverScale.Value, 1f, 1.25f);
                else if (gamepad)
                    targetScale = Mathf.Clamp(
                        WiresSmoothUIPlugin.GamepadSelectionScale.Value,
                        1f,
                        1.2f);

                state.Scale = WiresSmoothUIPlugin.Damp(state.Scale, targetScale, response);

                Vector2 swayTarget = Vector2.zero;
                float rotationTarget = 0f;
                if (mouse && WiresSmoothUIPlugin.SlotSway.Value)
                {
                    Vector2 delta = Vector2.ClampMagnitude(PointerMotion.Delta, 18f) / 18f;
                    swayTarget = new Vector2(-delta.x, -delta.y)
                        * Mathf.Max(0f, WiresSmoothUIPlugin.SwayPixels.Value);
                    rotationTarget = -delta.x
                        * Mathf.Max(0f, WiresSmoothUIPlugin.SwayDegrees.Value);
                }

                state.Sway = WiresSmoothUIPlugin.Damp(
                    state.Sway,
                    swayTarget,
                    response * 0.75f);
                state.Rotation = WiresSmoothUIPlugin.Damp(
                    state.Rotation,
                    rotationTarget,
                    response * 0.7f);

                ApplyState(state);

                bool engaged = mouse || (state.Pressed && mouse) || gamepad;
                bool settled = Mathf.Abs(state.Scale - 1f) < 0.001f
                    && state.Sway.sqrMagnitude < 0.0005f
                    && Mathf.Abs(state.Rotation) < 0.01f;

                if (!engaged && settled)
                {
                    RestoreState(state);
                    DeactivateAt(i);
                }
            }
        }
        internal static void RestoreGrid(InventoryGrid grid)
        {
            if (grid == null || Active.Count == 0)
                return;

            for (int i = 0; i < Active.Count; i++)
            {
                State state = Active[i];
                if (state.Grid == grid)
                    RestoreState(state);
            }
        }

        internal static void CaptureGrid(InventoryGrid grid)
        {
            if (grid == null || Active.Count == 0)
                return;

            for (int i = 0; i < Active.Count; i++)
            {
                State state = Active[i];
                if (state.Grid == grid)
                    CaptureBase(state);
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
                state.GamepadSelected = false;
                state.Scale = 1f;
                state.Sway = Vector2.zero;
                state.Rotation = 0f;
                DeactivateAt(i);
            }

            GamepadSelections.Clear();
        }

        internal static void Shutdown()
        {
            RestoreAll();

            foreach (KeyValuePair<InventoryElement, State> pair in States)
                DetachInput(pair.Value);

            RootStates.Clear();
            States.Clear();
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "UpdateGamepad")]
    internal static class SmoothUISlotGamepadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGrid __instance)
        {
            SlotAnimationSystem.UpdateGamepadSelection(__instance);
        }
    }
}
