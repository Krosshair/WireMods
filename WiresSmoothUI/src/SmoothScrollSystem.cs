using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal static class SmoothScrollSystem
    {
        internal struct Capture
        {
            internal ScrollRect Scroll;
            internal Vector2 VisualPosition;
            internal bool Valid;
        }

        private sealed class State
        {
            internal ScrollRect Scroll;
            internal Vector2 Target;
            internal bool Active;
        }

        private const float Response = 18f;
        private const float SnapDistance = 0.05f;

        private static readonly Dictionary<int, State> States =
            new Dictionary<int, State>();

        internal static Capture Begin(ScrollRect scroll)
        {
            Capture capture = new Capture();

            if (scroll == null ||
                scroll.content == null ||
                WiresSmoothUIPlugin.Enabled == null ||
                !WiresSmoothUIPlugin.Enabled.Value)
            {
                return capture;
            }

            int id = scroll.GetInstanceID();

            if (!States.TryGetValue(id, out State state))
            {
                state = new State
                {
                    Scroll = scroll,
                    Target = scroll.content.anchoredPosition
                };

                States[id] = state;
            }

            capture.Scroll = scroll;
            capture.VisualPosition =
                scroll.content.anchoredPosition;
            capture.Valid = true;

            // Each wheel notch should build from the intended destination,
            // not from the partially interpolated visual position. This keeps
            // rapid scrolling responsive instead of making it feel weak.
            if (state.Active)
                scroll.content.anchoredPosition =
                    state.Target;

            return capture;
        }

        internal static void End(Capture capture)
        {
            if (!capture.Valid ||
                capture.Scroll == null ||
                capture.Scroll.content == null)
                return;

            ScrollRect scroll = capture.Scroll;
            int id = scroll.GetInstanceID();

            if (!States.TryGetValue(id, out State state))
                return;

            state.Target =
                scroll.content.anchoredPosition;

            scroll.content.anchoredPosition =
                capture.VisualPosition;

            scroll.StopMovement();
            state.Active = true;
            SmoothUIDriver.Wake();
        }

        internal static bool Busy
        {
            get
            {
                foreach (KeyValuePair<int, State> pair in States)
                    if (pair.Value.Active)
                        return true;
                return false;
            }
        }

        internal static void Tick()
        {
            if (States.Count == 0)
                return;

            List<int> remove = null;

            foreach (KeyValuePair<int, State> pair in States)
            {
                State state = pair.Value;
                ScrollRect scroll = state.Scroll;

                if (scroll == null ||
                    scroll.content == null)
                {
                    if (remove == null)
                        remove = new List<int>();

                    remove.Add(pair.Key);
                    continue;
                }

                if (!state.Active)
                    continue;

                // Direct dragging of a scrollbar/content must always win over
                // wheel interpolation.
                if (Input.GetMouseButton(0) ||
                    ZInput.IsTouchActive())
                {
                    state.Target =
                        scroll.content.anchoredPosition;
                    state.Active = false;
                    continue;
                }

                Vector2 current =
                    scroll.content.anchoredPosition;

                float t =
                    1f -
                    Mathf.Exp(
                        -Response *
                        WiresSmoothUIPlugin.Dt);

                Vector2 next =
                    Vector2.Lerp(
                        current,
                        state.Target,
                        t);

                if ((state.Target - next).sqrMagnitude <=
                    SnapDistance * SnapDistance)
                {
                    next = state.Target;
                    state.Active = false;
                }

                scroll.content.anchoredPosition =
                    next;
            }

            if (remove != null)
            {
                for (int i = 0; i < remove.Count; ++i)
                    States.Remove(remove[i]);
            }
        }

        internal static void Shutdown()
        {
            States.Clear();
        }
    }

    [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnScroll))]
    internal static class SmoothScrollPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            ScrollRect __instance,
            out SmoothScrollSystem.Capture __state)
        {
            __state =
                SmoothScrollSystem.Begin(
                    __instance);
        }

        [HarmonyPostfix]
        private static void Postfix(
            SmoothScrollSystem.Capture __state)
        {
            SmoothScrollSystem.End(
                __state);
        }
    }
}
