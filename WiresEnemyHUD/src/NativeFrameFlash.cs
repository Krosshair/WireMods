using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Both effects trace the visible dark frame of the vanilla Health
    // prefab. The inner rectangle is the full GuiBar TRACK, rather
    // than the current HP fill, so lost HP never becomes part of the flash.
    internal static class NativeFrameFlash
    {
        private static float SpriteInset(RectTransform frame,
            bool horizontal, bool farSide)
        {
            if (frame == null) return 0f;
            Image image = frame.GetComponent<Image>();
            if (image == null || image.sprite == null) return 0f;
            Sprite sprite = image.sprite;
            Vector4 border = sprite.border;
            Rect source = sprite.rect;
            float reference = horizontal ? source.width : source.height;
            if (reference <= 0f) return 0f;
            float pixels = horizontal
                ? (farSide ? border.z : border.x)
                : (farSide ? border.w : border.y);
            return Mathf.Max(0f, pixels / reference *
                (horizontal ? frame.rect.width : frame.rect.height));
        }

        private static RectTransform FindTrack(RectTransform frame)
        {
            if (frame == null) return null;
            Transform child = frame.Find("health_fast");
            if (child == null)
                child = frame.Find("health_fast_friendly");
            // The GuiBar transform covers the entire HP/stagger capacity.
            // GuiBar.m_bar shrinks with HP and MUST NOT be measured.
            return child as RectTransform;
        }

        private static void SetEdge(Image image, float left,
            float bottom, float width, float height)
        {
            if (image == null) return;
            RectTransform r = image.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            r.anchoredPosition = new Vector2(left, bottom);
            r.sizeDelta = new Vector2(Mathf.Max(.1f, width),
                Mathf.Max(.1f, height));
        }

        internal static void Fit(RectTransform frame, Image top,
            Image bottom, Image left, Image right)
        {
            if (frame == null || top == null || bottom == null ||
                left == null || right == null) return;

            Rect outer = frame.rect;
            if (outer.width <= 0f || outer.height <= 0f) return;

            float l = 0f, r = 0f, b = 0f, t = 0f;
            RectTransform track = FindTrack(frame);
            if (track != null)
            {
                Bounds inner =
                    RectTransformUtility.CalculateRelativeRectTransformBounds(
                        frame, track);
                l = Mathf.Clamp(inner.min.x - outer.xMin, 0f,
                    outer.width * .4f);
                r = Mathf.Clamp(outer.xMax - inner.max.x, 0f,
                    outer.width * .4f);
                b = Mathf.Clamp(inner.min.y - outer.yMin, 0f,
                    outer.height * .4f);
                t = Mathf.Clamp(outer.yMax - inner.max.y, 0f,
                    outer.height * .4f);
            }

            // For prefabs with a sliced background sprite the border
            // thickness is encoded in its original nine-slice margins.
            // Otherwise fall back only where the layout cannot provide
            // a measurable dark-frame margin.
            const float minimumVisibleMargin = .45f;
            float fallback = Mathf.Clamp(outer.height * .28f, 2f, 5f);
            if (l < minimumVisibleMargin)
                l = Mathf.Max(SpriteInset(frame, true, false), fallback);
            if (r < minimumVisibleMargin)
                r = Mathf.Max(SpriteInset(frame, true, true), fallback);
            if (b < minimumVisibleMargin)
                b = Mathf.Max(SpriteInset(frame, false, false), fallback);
            if (t < minimumVisibleMargin)
                t = Mathf.Max(SpriteInset(frame, false, true), fallback);

            // Keep a visible inner bar even for unusually small modded
            // enemy HUD prefabs.
            l = Mathf.Min(l, outer.width * .32f);
            r = Mathf.Min(r, outer.width * .32f);
            b = Mathf.Min(b, outer.height * .32f);
            t = Mathf.Min(t, outer.height * .32f);

            // Full-width upper/lower frame, with full-height sides
            // between them. Together these completely cover the frame.
            SetEdge(top, 0f, outer.height - t, outer.width, t);
            SetEdge(bottom, 0f, 0f, outer.width, b);
            SetEdge(left, 0f, b, l, outer.height - t - b);
            SetEdge(right, outer.width - r, b, r,
                outer.height - t - b);
        }
    }
}
