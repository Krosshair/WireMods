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
        }

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
            _visual.localScale = _baseScale * _appliedScale;
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
            }

            if (_group != null)
            {
                if (Mathf.Abs(_group.alpha - _appliedAlpha) < 0.002f)
                    _group.alpha = _baseAlpha;
                else
                    _baseAlpha = _group.alpha;
            }

            _applied = false;
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
