using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Health segments: thin gaps across creature health bars every 100 HP,
    // in the bar's own background color, so each gap reads as the bar
    // showing through. Tough creatures get many, small ones none. If 100 HP
    // steps would crowd the bar (thousands of HP on a small bar) the step
    // grows to 1000, then 10000. Laid out only when the creature's max
    // health, the bar's size or a setting changes.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _healthSegments;
        private const float SegmentHp = 100f;
        private const float SegmentMinSpacing = 3f; // bar units between gaps, at least

        private sealed class SegmentUi
        {
            public RectTransform Root;
            public readonly List<Image> Gaps = new List<Image>();
            public float Max = -1f, Width = -1f; public int Layout = -1;
        }

        private void UpdateHealthSegments(EnemyUiState state)
        {
            if (state.HealthRect == null || state.Character == null ||
                (state.Kind != HudKind.Enemy && state.Kind != HudKind.Boss))
                return;
            SegmentUi seg = state.Segments;
            if (!_healthSegments.Value)
            {
                if (seg != null && seg.Root != null && seg.Root.gameObject.activeSelf)
                    seg.Root.gameObject.SetActive(false);
                return;
            }
            float max = state.Character.GetMaxHealth();
            float width = state.HealthRect.rect.width;
            if (seg != null && seg.Max == max && seg.Width == width && seg.Layout == _layoutVersion &&
                seg.Root != null && seg.Root.gameObject.activeSelf)
                return;

            if (seg == null)
            {
                seg = state.Segments = new SegmentUi();
                GameObject go = new GameObject("WireHealthSegments", typeof(RectTransform));
                seg.Root = (RectTransform)go.transform;
                seg.Root.SetParent(state.HealthRect, false);
                seg.Root.anchorMin = Vector2.zero;
                seg.Root.anchorMax = Vector2.one;
                seg.Root.sizeDelta = Vector2.zero;
                seg.Root.anchoredPosition = Vector2.zero;
            }
            seg.Root.SetAsLastSibling();
            if (!seg.Root.gameObject.activeSelf) seg.Root.gameObject.SetActive(true);
            seg.Max = max; seg.Width = width; seg.Layout = _layoutVersion;

            float step = SegmentHp;
            while (max > 0f && width * step / max < SegmentMinSpacing) step *= 10f;
            int count = max > 0f ? Mathf.CeilToInt(max / step) - 1 : 0;
            Color color = state.HealthBkg != null ? state.HealthBkg.color : new Color(0f, 0f, 0f, 0.48f);
            color.a = 1f; // a gap, not a tint: the background's color, solid
            float gapWidth = Mathf.Max(1f, state.HealthRect.rect.height * 0.15f);

            for (int k = 0; k < Mathf.Max(count, seg.Gaps.Count); k++)
            {
                if (k >= count) { if (seg.Gaps[k].gameObject.activeSelf) seg.Gaps[k].gameObject.SetActive(false); continue; }
                if (k >= seg.Gaps.Count)
                {
                    GameObject g = new GameObject("Gap", typeof(RectTransform), typeof(Image));
                    RectTransform gr = (RectTransform)g.transform;
                    gr.SetParent(seg.Root, false);
                    gr.anchorMin = new Vector2(0f, 0f);
                    gr.anchorMax = new Vector2(0f, 1f);
                    gr.pivot = new Vector2(0.5f, 0.5f);
                    Image img = g.GetComponent<Image>();
                    img.raycastTarget = false;
                    seg.Gaps.Add(img);
                }
                Image gap = seg.Gaps[k];
                if (!gap.gameObject.activeSelf) gap.gameObject.SetActive(true);
                gap.color = color;
                RectTransform r = gap.rectTransform;
                r.sizeDelta = new Vector2(gapWidth, 0f);
                r.anchoredPosition = new Vector2(width * ((k + 1) * step / max), 0f);
            }
        }
    }
}
