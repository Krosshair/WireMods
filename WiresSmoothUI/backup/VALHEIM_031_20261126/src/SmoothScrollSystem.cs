using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Browser-style smooth scrolling. Every wheel notch moves a target by the
    // list's own scroll step, and the list follows it on a critically damped
    // spring: it speeds up and slows down smoothly, never jumps, and keeps
    // its speed when more notches arrive mid-glide. Gamepad/keyboard
    // recentering (ScrollRectEnsureVisible) follows the same spring.
    // Only ticks while something is moving.
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
            internal Vector2 Velocity;
            internal bool Active;
        }

        private const float MaxStep = 1f / 120f;
        private const float SnapDistance = 0.1f;
        private const float SnapSpeed = 2f;

        private static readonly Dictionary<int, State> States = new Dictionary<int, State>();
        private static readonly List<int> Remove = new List<int>();

        internal static bool On =>
            WiresSmoothUIPlugin.Enabled != null && WiresSmoothUIPlugin.Enabled.Value &&
            WiresSmoothUIPlugin.SmoothScrolling != null && WiresSmoothUIPlugin.SmoothScrolling.Value;

        // Spring rate from the Smoothness setting: settles in about that time.
        private static float Omega => 5f / Mathf.Max(0.05f, WiresSmoothUIPlugin.ScrollGlide.Value);

        private static State Get(ScrollRect scroll)
        {
            int id = scroll.GetInstanceID();
            if (!States.TryGetValue(id, out State state))
            {
                state = new State { Scroll = scroll };
                States[id] = state;
            }
            if (!state.Active)
            {
                state.Target = scroll.content.anchoredPosition;
                state.Velocity = Vector2.zero;
            }
            return state;
        }

        // ---- Wheel ----

        // Replaces ScrollRect.OnScroll: same direction rules and step size,
        // applied to the target.
        internal static bool Wheel(ScrollRect scroll, PointerEventData data)
        {
            if (!On || scroll == null || scroll.content == null || !scroll.IsActive())
                return true;

            Vector2 delta = data.scrollDelta;
            delta.y *= -1f;
            if (scroll.vertical && !scroll.horizontal)
            {
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                    delta.y = delta.x;
                delta.x = 0f;
            }
            if (scroll.horizontal && !scroll.vertical)
            {
                if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                    delta.x = delta.y;
                delta.y = 0f;
            }

            State state = Get(scroll);
            state.Target = Clamp(scroll, state.Target + delta * scroll.scrollSensitivity);
            state.Active = true;
            scroll.StopMovement();
            SmoothUIDriver.Wake();
            return false;
        }

        // ---- Recentering ----

        internal static Capture Begin(ScrollRect scroll)
        {
            Capture capture = new Capture();
            if (!On || scroll == null || scroll.content == null)
                return capture;
            State state = Get(scroll);
            capture.Scroll = scroll;
            capture.VisualPosition = scroll.content.anchoredPosition;
            capture.Valid = true;
            // Build from where it's heading, not from mid-glide.
            scroll.content.anchoredPosition = state.Target;
            return capture;
        }

        internal static void End(Capture capture)
        {
            if (!capture.Valid || capture.Scroll == null || capture.Scroll.content == null)
                return;
            State state = Get(capture.Scroll);
            state.Target = capture.Scroll.content.anchoredPosition;
            capture.Scroll.content.anchoredPosition = capture.VisualPosition;
            capture.Scroll.StopMovement();
            state.Active = true;
            SmoothUIDriver.Wake();
        }

        // ---- Frame ----

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

            float dt = WiresSmoothUIPlugin.Dt;
            Remove.Clear();
            foreach (KeyValuePair<int, State> pair in States)
            {
                State state = pair.Value;
                ScrollRect scroll = state.Scroll;
                if (scroll == null || scroll.content == null)
                {
                    Remove.Add(pair.Key);
                    continue;
                }
                if (!state.Active)
                    continue;

                // Grabbing the list or its scrollbar always wins.
                if (Input.GetMouseButton(0) || ZInput.IsTouchActive() || !scroll.gameObject.activeInHierarchy)
                {
                    state.Active = false;
                    state.Velocity = Vector2.zero;
                    continue;
                }

                Step(state, scroll, dt);
            }
            foreach (int id in Remove)
                States.Remove(id);
        }

        // Critically damped spring toward the target, in small fixed steps so
        // it feels the same at any frame rate.
        private static void Step(State state, ScrollRect scroll, float dt)
        {
            float w = Omega;
            Vector2 x = scroll.content.anchoredPosition;
            Vector2 v = state.Velocity;
            while (dt > 0f)
            {
                float h = Mathf.Min(dt, MaxStep);
                Vector2 accel = (state.Target - x) * (w * w) - v * (2f * w);
                v += accel * h;
                x += v * h;
                dt -= h;
            }
            state.Velocity = v;

            if ((state.Target - x).sqrMagnitude <= SnapDistance * SnapDistance &&
                v.sqrMagnitude <= SnapSpeed * SnapSpeed)
            {
                x = state.Target;
                state.Velocity = Vector2.zero;
                state.Active = false;
            }
            scroll.content.anchoredPosition = x;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        // Keeps the content inside the viewport the way a clamped ScrollRect
        // would, whatever the list's pivots and anchors.
        private static Vector2 Clamp(ScrollRect scroll, Vector2 position)
        {
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
            RectTransform content = scroll.content;
            if (viewport == null)
                return position;

            Vector2 offset = position - content.anchoredPosition;
            Bounds view = new Bounds(viewport.rect.center, viewport.rect.size);
            Bounds bounds = ContentBounds(viewport, content);
            bounds.center += (Vector3)offset;

            Vector2 fix = Vector2.zero;
            Vector2 min = bounds.min, max = bounds.max;
            Vector2 vmin = view.min, vmax = view.max;
            // A list that fits doesn't move on that axis at all.
            if (bounds.size.x <= view.size.x) fix.x = -offset.x;
            else if (scroll.horizontal)
            {
                if (min.x > vmin.x) fix.x = vmin.x - min.x;
                else if (max.x < vmax.x) fix.x = vmax.x - max.x;
            }
            if (bounds.size.y <= view.size.y) fix.y = -offset.y;
            else if (scroll.vertical)
            {
                if (max.y < vmax.y) fix.y = vmax.y - max.y;
                else if (min.y > vmin.y) fix.y = vmin.y - min.y;
            }
            return position + fix;
        }

        private static Bounds ContentBounds(RectTransform viewport, RectTransform content)
        {
            content.GetWorldCorners(Corners);
            Matrix4x4 toView = viewport.worldToLocalMatrix;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, 0f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = toView.MultiplyPoint3x4(Corners[i]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Bounds b = new Bounds(min, Vector3.zero);
            b.Encapsulate(max);
            return b;
        }

        internal static void Stop()
        {
            foreach (KeyValuePair<int, State> pair in States)
            {
                pair.Value.Active = false;
                pair.Value.Velocity = Vector2.zero;
            }
        }

        internal static void Shutdown() => States.Clear();
    }

    [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnScroll))]
    internal static class SmoothScrollPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ScrollRect __instance, PointerEventData data) =>
            SmoothScrollSystem.Wheel(__instance, data);
    }
}
