using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    internal static class TooltipAnimationSystem
    {
        private static readonly FieldInfo TooltipField =
            AccessTools.Field(typeof(UITooltip), "m_tooltip");

        private static GameObject _tooltip;
        private static Transform _visual;
        private static CanvasGroup _group;
        private static Vector3 _baseScale = Vector3.one;
        private static float _baseAlpha = 1f;
        private static float _timer;
        private static float _appliedScale = 1f;
        private static float _appliedAlpha = 1f;
        private static Vector3 _basePosition;
        private static Vector3 _startOffset;
        private static Vector3 _appliedOffset;
        private const float SlidePixels = 10f;
        private static bool _applied;
        private static bool _started;
        private static bool _finished;

        internal static void NotifyHoverStart()
        {
            RestoreApplied();

            _tooltip = TooltipField?.GetValue(null) as GameObject;
            _visual = null;
            _group = null;
            _timer = 0f;
            _started = false;
            _finished = false;
            SmoothUIDriver.Wake();
        }

        internal static bool Busy => _tooltip != null && !_finished;

        internal static void Tick()
        {
            if (_tooltip == null || _finished)
                return;

            if (!WiresSmoothUIPlugin.TooltipAnimations.Value)
            {
                RestoreApplied();
                _finished = true;
                return;
            }

            if (!_tooltip.activeInHierarchy)
                return;

            if (!_started)
                BeginAnimation();

            if (_visual == null || _group == null)
            {
                _finished = true;
                return;
            }

            RestoreApplied();

            float duration = Mathf.Max(0.01f, WiresSmoothUIPlugin.TooltipDuration.Value);
            _timer += WiresSmoothUIPlugin.Dt;
            if (_timer >= duration)
            {
                _visual.localScale = _baseScale;
                _group.alpha = _baseAlpha;
                _finished = true;
                return;
            }

            float t = WiresSmoothUIPlugin.EaseOutCubic(_timer / duration);
            float startScale = Mathf.Clamp(
                WiresSmoothUIPlugin.TooltipStartScale.Value,
                0.75f,
                1f);

            _appliedScale = Mathf.Lerp(startScale, 1f, t);
            _appliedAlpha = Mathf.Lerp(0f, _baseAlpha, t);
            _appliedOffset = _startOffset * (1f - t);
            _visual.localScale = _baseScale * _appliedScale;
            _visual.localPosition = _basePosition + _appliedOffset;
            _group.alpha = _appliedAlpha;
            _applied = true;
        }

        private static void BeginAnimation()
        {
            _visual = _tooltip.transform.childCount > 0
                ? _tooltip.transform.GetChild(0)
                : _tooltip.transform;

            _group = _tooltip.GetComponent<CanvasGroup>();
            if (_group == null)
                _group = _tooltip.AddComponent<CanvasGroup>();

            _baseScale = _visual.localScale;
            _baseAlpha = _group.alpha;
            _basePosition = _visual.localPosition;

            // Start a little further from the cursor and slide toward it, so
            // the tooltip feels attached to what you're pointing at.
            _startOffset = Vector3.zero;
            if (_visual.parent != null && ZInput.instance != null && ZInput.IsMouseActive())
            {
                Vector3 away = _visual.position - (Vector3)ZInput.pointerPosition;
                away.z = 0f;
                if (away.sqrMagnitude > 1f)
                    _startOffset = _visual.parent.InverseTransformVector(away.normalized * SlidePixels);
            }
            _timer = 0f;
            _started = true;
        }

        private static void RestoreApplied()
        {
            if (!_applied)
                return;

            if (_visual != null)
            {
                Vector3 expected = _baseScale * _appliedScale;
                if ((_visual.localScale - expected).sqrMagnitude < 0.0009f)
                    _visual.localScale = _baseScale;
                else
                    _baseScale = _visual.localScale;

                Vector3 expectedPosition = _basePosition + _appliedOffset;
                if ((_visual.localPosition - expectedPosition).sqrMagnitude < 0.0009f)
                    _visual.localPosition = _basePosition;
                else
                    _basePosition = _visual.localPosition;
            }

            if (_group != null)
            {
                if (Mathf.Abs(_group.alpha - _appliedAlpha) < 0.002f)
                    _group.alpha = _baseAlpha;
                else
                    _baseAlpha = _group.alpha;
            }

            _applied = false;
            _appliedOffset = Vector3.zero;
            _appliedScale = 1f;
            _appliedAlpha = _baseAlpha;
        }

        internal static void Restore()
        {
            RestoreApplied();
            _tooltip = null;
            _visual = null;
            _group = null;
            _timer = 0f;
            _started = false;
            _finished = false;
            SmoothUIDriver.Wake();
        }
    }

    [HarmonyPatch(typeof(UITooltip), "OnHoverStart")]
    internal static class SmoothUITooltipHoverPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TooltipAnimationSystem.NotifyHoverStart();
        }
    }

    [HarmonyPatch(typeof(UITooltip), "HideTooltip")]
    internal static class SmoothUITooltipHidePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            TooltipAnimationSystem.Restore();
        }
    }
}
