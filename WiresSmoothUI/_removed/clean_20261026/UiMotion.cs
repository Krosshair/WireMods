using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WiresSmoothUI
{
    // The one place that moves, scales and fades UI for Smooth UI's event
    // animations (panels, list rows, pops, shakes, things shrinking away).
    //
    // Each animated transform has a single entry, so two animations on the same
    // object combine instead of fighting over it. Every frame the entry first
    // takes back what it applied last frame, keeping anything the game changed
    // meanwhile, then applies the new frame. Only the properties an animation
    // actually uses are touched: a pop never writes position, a slide never
    // writes scale.
    //
    // Nothing here runs unless something is animating; the driver sleeps
    // otherwise.
    internal static class UiMotion
    {
        private enum ScaleKind { None, Ease, Pop, Exit }

        private sealed class Entry
        {
            internal Transform Root;
            internal CanvasGroup Group;

            internal Vector3 BasePosition;
            internal Vector3 BaseScale;
            internal float BaseAlpha = 1f;

            internal Vector3 AppliedOffset;
            internal float AppliedScale = 1f;
            internal float AppliedAlpha = 1f;
            internal bool PositionApplied;
            internal bool ScaleApplied;
            internal bool AlphaApplied;

            // Slide: from an offset back to rest.
            internal bool Slide;
            internal Vector3 SlideFrom;
            internal float SlideDelay, SlideDuration, SlideTime;

            // Fade in from transparent.
            internal bool Fade;
            internal float FadeDelay, FadeDuration, FadeTime;

            // Scale: ease from a start, pop through a peak, or shrink away.
            internal ScaleKind Scale;
            internal float ScaleStart, ScalePeak, ScaleDelay, ScaleDuration, ScaleTime;

            // Horizontal "no" shake.
            internal bool Shake;
            internal float ShakeTime;
        }

        private const float ShakeDuration = 0.32f;
        private const float ShakePixels = 6f;
        private const float ExitDuration = 0.16f;

        private static readonly Dictionary<Transform, Entry> Entries = new Dictionary<Transform, Entry>();
        private static readonly List<Entry> Active = new List<Entry>();
        private static readonly List<Action> Deferred = new List<Action>();
        private static readonly List<Action> Running = new List<Action>();

        internal static bool Busy => Active.Count > 0 || Deferred.Count > 0;

        // ---- Starting animations ----

        // Comes into view: slides from `from`, eases from `startScale`, and
        // fades in if `fade`.
        internal static void Enter(Transform t, float delay, float duration, Vector3 from, float startScale, bool fade)
        {
            Entry e = Get(t, fade);
            if (e == null)
                return;

            if (from != Vector3.zero)
            {
                e.Slide = true;
                e.SlideFrom = from;
                e.SlideDelay = delay;
                e.SlideDuration = Mathf.Max(0.01f, duration);
                e.SlideTime = 0f;
            }

            if (!Mathf.Approximately(startScale, 1f))
            {
                e.Scale = ScaleKind.Ease;
                e.ScaleStart = startScale;
                e.ScaleDelay = delay;
                e.ScaleDuration = Mathf.Max(0.01f, duration);
                e.ScaleTime = 0f;
            }

            if (fade && e.Group != null)
            {
                e.Fade = true;
                e.FadeDelay = delay;
                e.FadeDuration = Mathf.Max(0.01f, duration);
                e.FadeTime = 0f;
            }

            Start(e);
        }

        // Scales from `start` up through `peak` and settles at its normal size.
        internal static void Pop(Transform t, float start, float peak, float duration)
        {
            Entry e = Get(t, false);
            if (e == null || e.Scale == ScaleKind.Exit)
                return;

            e.Scale = ScaleKind.Pop;
            e.ScaleStart = Mathf.Max(0.05f, start);
            e.ScalePeak = Mathf.Max(0.05f, peak);
            e.ScaleDelay = 0f;
            e.ScaleDuration = Mathf.Max(0.03f, duration);
            e.ScaleTime = 0f;
            Start(e);
        }

        internal static void Shake(Transform t)
        {
            Entry e = Get(t, false);
            if (e == null)
                return;

            e.Shake = true;
            e.ShakeTime = 0f;
            Start(e);
        }

        // Takes over a UI object the game is about to destroy: it swells a
        // touch, shrinks away, then is destroyed.
        internal static void ShrinkAway(GameObject go)
        {
            if (go == null)
                return;

            if (!go.activeInHierarchy)
            {
                Object.Destroy(go);
                return;
            }

            foreach (UnityEngine.UI.Graphic graphic in go.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                graphic.raycastTarget = false;

            Entry e = Get(go.transform, false);
            e.Scale = ScaleKind.Exit;
            e.ScaleTime = 0f;
            e.ScaleDelay = 0f;
            e.ScaleDuration = ExitDuration;
            Start(e);
        }

        // Runs once on the next driver frame, after the game has filled in what
        // it's about to show.
        internal static void NextFrame(Action action)
        {
            Deferred.Add(action);
            SmoothUIDriver.Wake();
        }

        internal static void Cancel(Transform t)
        {
            if (t == null || !Entries.TryGetValue(t, out Entry e))
                return;
            Restore(e);
            Finish(e);
        }

        // ---- Frame ----

        internal static void Tick()
        {
            if (Deferred.Count > 0)
            {
                Running.AddRange(Deferred);
                Deferred.Clear();
                foreach (Action action in Running)
                {
                    try { action(); }
                    catch (Exception ex) { Debug.LogWarning("[Wire's Smooth UI] " + ex.Message); }
                }
                Running.Clear();
            }

            float dt = WiresSmoothUIPlugin.Dt;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Entry e = Active[i];
                if (e.Root == null)
                {
                    Active.RemoveAt(i);
                    Entries.Remove(e.Root);
                    continue;
                }

                Restore(e);

                if (!e.Root.gameObject.activeInHierarchy)
                {
                    if (e.Scale == ScaleKind.Exit)
                        Object.Destroy(e.Root.gameObject);
                    Finish(e);
                    continue;
                }

                if (Advance(e, dt))
                {
                    if (e.Scale == ScaleKind.Exit)
                        Object.Destroy(e.Root.gameObject);
                    Finish(e);
                    continue;
                }

                Apply(e);
            }
        }

        // Moves every channel forward; true when all are done.
        private static bool Advance(Entry e, float dt)
        {
            bool done = true;
            if (e.Slide)
            {
                e.SlideTime += dt;
                if (e.SlideTime >= e.SlideDelay + e.SlideDuration) e.Slide = false; else done = false;
            }
            if (e.Fade)
            {
                e.FadeTime += dt;
                if (e.FadeTime >= e.FadeDelay + e.FadeDuration) e.Fade = false; else done = false;
            }
            if (e.Scale != ScaleKind.None)
            {
                e.ScaleTime += dt;
                if (e.ScaleTime >= e.ScaleDelay + e.ScaleDuration)
                {
                    if (e.Scale == ScaleKind.Exit)
                        return true;
                    e.Scale = ScaleKind.None;
                }
                else done = false;
            }
            if (e.Shake)
            {
                e.ShakeTime += dt;
                if (e.ShakeTime >= ShakeDuration) e.Shake = false; else done = false;
            }
            return done;
        }

        private static float Progress(float time, float delay, float duration) =>
            Mathf.Clamp01((time - delay) / duration);

        private static void Apply(Entry e)
        {
            Transform root = e.Root;

            if (e.Slide || e.Shake)
            {
                Vector3 offset = Vector3.zero;
                if (e.Slide)
                    offset += e.SlideFrom * (1f - WiresSmoothUIPlugin.EaseOutCubic(Progress(e.SlideTime, e.SlideDelay, e.SlideDuration)));
                if (e.Shake)
                {
                    float t = e.ShakeTime / ShakeDuration;
                    offset.x += Mathf.Sin(t * Mathf.PI * 6f) * ShakePixels * (1f - t) * (1f - t);
                }
                root.localPosition = e.BasePosition + offset;
                e.AppliedOffset = offset;
                e.PositionApplied = true;
            }

            if (e.Scale != ScaleKind.None)
            {
                float t = Progress(e.ScaleTime, e.ScaleDelay, e.ScaleDuration);
                float scale;
                switch (e.Scale)
                {
                    case ScaleKind.Ease:
                        scale = Mathf.Lerp(e.ScaleStart, 1f, WiresSmoothUIPlugin.EaseOutCubic(t));
                        break;
                    case ScaleKind.Pop:
                        const float peakTime = 0.44f;
                        scale = t < peakTime
                            ? Mathf.Lerp(e.ScaleStart, e.ScalePeak, WiresSmoothUIPlugin.EaseOutCubic(t / peakTime))
                            : Mathf.Lerp(e.ScalePeak, 1f, Mathf.SmoothStep(0f, 1f, (t - peakTime) / (1f - peakTime)));
                        break;
                    default: // Exit
                        scale = t < 0.25f
                            ? Mathf.Lerp(1f, 1.12f, t / 0.25f)
                            : Mathf.Lerp(1.12f, 0f, WiresSmoothUIPlugin.EaseOutCubic((t - 0.25f) / 0.75f));
                        break;
                }
                root.localScale = e.BaseScale * scale;
                e.AppliedScale = scale;
                e.ScaleApplied = true;
            }

            if (e.Fade && e.Group != null)
            {
                float alpha = e.BaseAlpha * WiresSmoothUIPlugin.EaseOutCubic(Progress(e.FadeTime, e.FadeDelay, e.FadeDuration));
                e.Group.alpha = alpha;
                e.AppliedAlpha = alpha;
                e.AlphaApplied = true;
            }
        }

        // Takes back last frame's values. If the game changed a value since,
        // that becomes the new rest value instead.
        private static void Restore(Entry e)
        {
            Transform root = e.Root;
            if (root == null)
                return;

            if (e.PositionApplied)
            {
                if ((root.localPosition - (e.BasePosition + e.AppliedOffset)).sqrMagnitude < 0.0009f)
                    root.localPosition = e.BasePosition;
                else
                    e.BasePosition = root.localPosition - e.AppliedOffset;
                e.PositionApplied = false;
            }

            if (e.ScaleApplied)
            {
                if ((root.localScale - e.BaseScale * e.AppliedScale).sqrMagnitude < 0.0009f)
                    root.localScale = e.BaseScale;
                else if (e.AppliedScale > 0.001f)
                    e.BaseScale = root.localScale / e.AppliedScale;
                e.ScaleApplied = false;
            }

            if (e.AlphaApplied && e.Group != null)
            {
                if (Mathf.Abs(e.Group.alpha - e.AppliedAlpha) < 0.002f)
                    e.Group.alpha = e.BaseAlpha;
                else
                    e.BaseAlpha = e.Group.alpha;
                e.AlphaApplied = false;
            }
        }

        // ---- Bookkeeping ----

        private static Entry Get(Transform t, bool needsGroup)
        {
            if (t == null || !t.gameObject.activeInHierarchy)
                return null;

            if (!Entries.TryGetValue(t, out Entry e))
            {
                e = new Entry
                {
                    Root = t,
                    BasePosition = t.localPosition,
                    BaseScale = t.localScale
                };
                Entries.Add(t, e);
            }
            else
            {
                // Settle to rest before layering a new animation on top.
                Restore(e);
            }

            if (needsGroup && e.Group == null)
            {
                e.Group = t.GetComponent<CanvasGroup>();
                if (e.Group == null)
                    e.Group = t.gameObject.AddComponent<CanvasGroup>();
                e.BaseAlpha = e.Group.alpha;
            }

            return e;
        }

        private static void Start(Entry e)
        {
            if (!Active.Contains(e))
                Active.Add(e);
            Apply(e);
            SmoothUIDriver.Wake();
        }

        private static void Finish(Entry e)
        {
            Active.Remove(e);
            if (!ReferenceEquals(e.Root, null))
                Entries.Remove(e.Root);
        }

        internal static void RestoreAll()
        {
            foreach (Entry e in Active)
            {
                Restore(e);
                if (e.Scale == ScaleKind.Exit && e.Root != null)
                    Object.Destroy(e.Root.gameObject);
            }
            Active.Clear();
            Entries.Clear();
            Deferred.Clear();
        }
    }
}
