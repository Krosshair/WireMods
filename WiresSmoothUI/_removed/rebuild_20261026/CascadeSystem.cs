using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    // One motion for every "comes into view" animation: a panel opening, a list
    // row sliding in, a recipe detail appearing. Each
    // entry eases from a start offset, scale and alpha back to where the game
    // has it. Callers check their own setting before adding.
    internal static class CascadeSystem
    {
        private sealed class State
        {
            internal Transform Root;
            internal CanvasGroup Group;
            internal Vector3 BasePosition;
            internal Vector3 BaseScale;
            internal float BaseAlpha;
            internal Vector3 AppliedOffset;
            internal float AppliedScale = 1f;
            internal float AppliedAlpha = 1f;
            internal Vector3 StartOffset;
            internal float StartScale;
            internal bool Fade;
            internal float Delay;
            internal float Duration;
            internal float Time;
            internal bool Applied;
        }

        private const float RowDuration = 0.24f;
        private const float RowStep = 0.03f;
        private const float RowMaxDelay = 0.3f;

        private static readonly List<State> Active = new List<State>();

        internal static bool Busy => Active.Count > 0 || Deferred.Count > 0;

        // Work that has to wait one frame, until the game has filled in what
        // it's about to show.
        private static readonly List<System.Action> Deferred = new List<System.Action>();
        private static readonly List<System.Action> Running = new List<System.Action>();

        internal static void NextFrame(System.Action action)
        {
            Deferred.Add(action);
            SmoothUIDriver.Wake();
        }

        internal static bool Panels =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.PanelAnimations.Value;

        internal static Transform ToTransform(object o)
        {
            if (o is Transform tr) return tr;
            if (o is GameObject go) return go != null ? go.transform : null;
            if (o is Component c) return c != null ? c.transform : null;
            return null;
        }

        internal static void Add(Transform t, float delay, Vector3 startOffset, float startScale,
            bool fade, float duration)
        {
            if (t == null || !t.gameObject.activeInHierarchy)
                return;

            Cancel(t);
            CanvasGroup group = null;
            if (fade)
            {
                group = t.GetComponent<CanvasGroup>();
                if (group == null)
                    group = t.gameObject.AddComponent<CanvasGroup>();
            }

            State state = new State
            {
                Root = t,
                Group = group,
                BasePosition = t.localPosition,
                BaseScale = t.localScale,
                BaseAlpha = group != null ? group.alpha : 1f,
                StartOffset = startOffset,
                StartScale = startScale,
                Fade = fade,
                Delay = delay,
                Duration = Mathf.Max(0.01f, duration)
            };
            Apply(state, 0f);
            Active.Add(state);
            SmoothUIDriver.Wake();
        }

        // List rows, one after another, rising a little as they fade in.
        internal static void Rows(IEnumerable rows)
        {
            if (!Panels || rows == null)
                return;

            int index = 0;
            foreach (object row in rows)
            {
                Transform t = ToTransform(row);
                if (t == null || !t.gameObject.activeInHierarchy)
                    continue;
                Add(t, Mathf.Min(RowMaxDelay, index * RowStep), new Vector3(0f, -12f, 0f), 1f, true, RowDuration);
                index++;
            }
        }

        internal static void Cancel(Transform t)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (Active[i].Root == t)
                {
                    Restore(Active[i]);
                    Active.RemoveAt(i);
                }
            }
        }

        internal static void Tick()
        {
            if (Deferred.Count > 0)
            {
                Running.AddRange(Deferred);
                Deferred.Clear();
                foreach (System.Action action in Running)
                    action();
                Running.Clear();
            }

            if (Active.Count == 0)
                return;

            float dt = WiresSmoothUIPlugin.Dt;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                Restore(state);

                if (state.Root == null || !state.Root.gameObject.activeInHierarchy
                    || (state.Fade && state.Group == null))
                {
                    Active.RemoveAt(i);
                    continue;
                }

                state.Time += dt;
                float t = Mathf.Clamp01((state.Time - state.Delay) / state.Duration);
                if (t >= 1f)
                {
                    Active.RemoveAt(i);
                    continue;
                }

                Apply(state, WiresSmoothUIPlugin.EaseOutCubic(t));
            }
        }

        private static void Apply(State state, float t)
        {
            state.AppliedOffset = state.StartOffset * (1f - t);
            state.AppliedScale = Mathf.Lerp(state.StartScale, 1f, t);
            state.Root.localPosition = state.BasePosition + state.AppliedOffset;
            if (state.StartScale != 1f)
                state.Root.localScale = state.BaseScale * state.AppliedScale;
            if (state.Fade)
            {
                state.AppliedAlpha = state.BaseAlpha * t;
                state.Group.alpha = state.AppliedAlpha;
            }
            state.Applied = true;
        }

        // Puts back what we changed, but keeps anything the game changed meanwhile.
        private static void Restore(State state)
        {
            if (!state.Applied)
                return;

            Transform root = state.Root;
            if (root != null)
            {
                Vector3 expectedPosition = state.BasePosition + state.AppliedOffset;
                if ((root.localPosition - expectedPosition).sqrMagnitude < 0.0009f)
                    root.localPosition = state.BasePosition;
                else
                    state.BasePosition = root.localPosition;

                if (state.StartScale != 1f)
                {
                    Vector3 expectedScale = state.BaseScale * state.AppliedScale;
                    if ((root.localScale - expectedScale).sqrMagnitude < 0.0009f)
                        root.localScale = state.BaseScale;
                    else
                        state.BaseScale = root.localScale;
                }
            }

            if (state.Fade && state.Group != null)
            {
                if (Mathf.Abs(state.Group.alpha - state.AppliedAlpha) < 0.002f)
                    state.Group.alpha = state.BaseAlpha;
                else
                    state.BaseAlpha = state.Group.alpha;
            }

            state.Applied = false;
        }

        internal static void RestoreAll()
        {
            foreach (State state in Active)
                Restore(state);
            Active.Clear();
            Deferred.Clear();
        }
    }
}
