using System.Collections.Generic;
using UnityEngine;

namespace WiresSmoothUI
{
    /// <summary>
    /// Sparse, transient scale animation helper used by HUD/map systems.
    /// It never scans the scene: callers explicitly kick transforms when an event occurs.
    /// </summary>
    internal static class MicroMotionSystem
    {
        private sealed class State
        {
            internal Transform Root;
            internal Transform Parent;
            internal Vector3 BaseScale;
            internal float AppliedScale = 1f;
            internal bool Applied;
            internal float Time;
            internal float Duration;
            internal float StartScale;
            internal float PeakScale;
            internal int ActiveIndex = -1;
        }

        private static readonly Dictionary<Transform, State> States =
            new Dictionary<Transform, State>();
        private static readonly List<State> Active = new List<State>();

        internal static void Pop(Transform root, float startScale, float peakScale, float duration)
        {
            if (root == null || duration <= 0f)
                return;

            if (!States.TryGetValue(root, out State state))
            {
                state = new State
                {
                    Root = root
                };
                States.Add(root, state);
            }

            RestoreState(state);
            CaptureBase(state);

            state.Time = 0f;
            state.Duration = Mathf.Max(0.03f, duration);
            state.StartScale = Mathf.Max(0.05f, startScale);
            state.PeakScale = Mathf.Max(0.05f, peakScale);

            if (state.ActiveIndex < 0)
            {
                state.ActiveIndex = Active.Count;
                Active.Add(state);
            }
        }

        internal static void Pulse(Transform root, float peakScale, float duration)
        {
            Pop(root, 1f, peakScale, duration);
        }

        internal static void Punch(Transform root, float compression, float peakScale, float duration)
        {
            Pop(root, compression, peakScale, duration);
        }

        internal static void Tick()
        {
            if (!WiresSmoothUIPlugin.Enabled.Value)
            {
                RestoreAll();
                return;
            }

            float dt = WiresSmoothUIPlugin.Dt;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                if (state.Root == null)
                {
                    RemoveAt(i);
                    continue;
                }

                RestoreState(state);

                if (!state.Root.gameObject.activeInHierarchy)
                {
                    FinishAt(i);
                    continue;
                }

                state.Time += dt;
                float t = Mathf.Clamp01(state.Time / state.Duration);
                float scale;

                const float peakTime = 0.44f;
                if (t < peakTime)
                {
                    float p = WiresSmoothUIPlugin.EaseOutCubic(t / peakTime);
                    scale = Mathf.Lerp(state.StartScale, state.PeakScale, p);
                }
                else
                {
                    float p = Mathf.SmoothStep(0f, 1f, (t - peakTime) / (1f - peakTime));
                    scale = Mathf.Lerp(state.PeakScale, 1f, p);
                }

                state.Root.localScale = state.BaseScale * scale;
                state.AppliedScale = scale;
                state.Applied = true;

                if (t >= 1f)
                    FinishAt(i);
            }
        }

        private static void CaptureBase(State state)
        {
            if (state?.Root == null)
                return;

            state.Parent = state.Root.parent;
            state.BaseScale = state.Root.localScale;
            state.Applied = false;
            state.AppliedScale = 1f;
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

            Vector3 expected = state.BaseScale * state.AppliedScale;
            if ((root.localScale - expected).sqrMagnitude < 0.0009f)
                root.localScale = state.BaseScale;
            else
                state.BaseScale = root.localScale;

            state.Applied = false;
            state.AppliedScale = 1f;
        }

        private static void FinishAt(int index)
        {
            State state = Active[index];
            RestoreState(state);
            RemoveAt(index);
        }

        private static void RemoveAt(int index)
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

            if (!ReferenceEquals(removed.Root, null))
                States.Remove(removed.Root);
        }

        internal static void RestoreAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                State state = Active[i];
                RestoreState(state);
                if (!ReferenceEquals(state.Root, null))
                    States.Remove(state.Root);
            }

            Active.Clear();
        }

        internal static void Shutdown()
        {
            RestoreAll();
            States.Clear();
        }
    }
}
