using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Both effects trace the visible dark border of the vanilla Health
    // prefab (its "darken" image, around the bar), one ring per bar.
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

        internal static void Fit(RectTransform frame, Image top,
            Image bottom, Image left, Image right)
        {
            if (frame == null || top == null || bottom == null ||
                left == null || right == null) return;

            Rect outer = frame.rect;
            if (outer.width <= 0f || outer.height <= 0f) return;

            // The visible dark border is vanilla's "darken" image, which
            // extends past the bar on every side: flash exactly that margin
            // around the bar, never an inset over the bar itself.
            RectTransform shadow = frame.Find("darken") as RectTransform;
            if (shadow != null && shadow.gameObject.activeSelf)
            {
                Bounds around = RectTransformUtility.CalculateRelativeRectTransformBounds(frame, shadow);
                float ml = outer.xMin - around.min.x, mr = around.max.x - outer.xMax;
                float mb = outer.yMin - around.min.y, mt = around.max.y - outer.yMax;
                if (ml > .1f && mr > .1f && mb > .1f && mt > .1f)
                {
                    // Half the border's thickness, hugging the bar.
                    FitSliced(frame, top, ml * .5f, mb * .5f, mr * .5f, mt * .5f, true);
                    Hide(bottom); Hide(left); Hide(right);
                    return;
                }
            }

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

            FitSliced(frame, top, l, b, r, t, false);
            Hide(bottom); Hide(left); Hide(right);
        }

        // ---- One frame image instead of four strips ----
        // Four separate strips are each rounded to screen pixels on their
        // own, so the corners can double up and look brighter. A single
        // 9-sliced image (center not drawn) has one shared mesh: every
        // corner is drawn exactly once and the thickness per side matches
        // the dark frame (to a quarter unit).
        private const int TextureSize = 64;
        private const float TexelsPerUnit = 4f;
        private static Texture2D white;
        private static readonly Dictionary<long, Sprite> sprites = new Dictionary<long, Sprite>();

        private static int Quantize(float units) =>
            Mathf.Clamp(Mathf.RoundToInt(units * TexelsPerUnit), 1, TextureSize / 2 - 1);

        private static Sprite FrameSprite(float l, float b, float r, float t, float canvasReference)
        {
            int L = Quantize(l), B = Quantize(b), R = Quantize(r), T = Quantize(t);
            int reference = Mathf.Max(1, Mathf.RoundToInt(canvasReference));
            long key = (long)L | (long)B << 8 | (long)R << 16 | (long)T << 24 | (long)reference << 32;
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite) && sprite != null) return sprite;
            if (white == null)
            {
                white = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
                var pixels = new Color32[TextureSize * TextureSize];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                white.SetPixels32(pixels);
                white.filterMode = FilterMode.Point;
                white.wrapMode = TextureWrapMode.Clamp;
                white.hideFlags = HideFlags.HideAndDontSave;
                white.Apply(false, true);
            }
            if (sprites.Count > 64) sprites.Clear();
            // Slice thickness in UI units = border / (sprite PPU / canvas reference PPU),
            // so border = units * TexelsPerUnit with PPU = TexelsPerUnit * reference.
            sprite = Sprite.Create(white, new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(.5f, .5f), TexelsPerUnit * reference, 0,
                SpriteMeshType.FullRect, new Vector4(L, B, R, T));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            sprites[key] = sprite;
            return sprite;
        }

        // outside = the ring sits around the frame (margin); otherwise it is
        // drawn inside the frame's edge (fallback for unusual HUD prefabs).
        private static void FitSliced(RectTransform frame, Image image,
            float l, float b, float r, float t, bool outside)
        {
            RectTransform rect = image.rectTransform;
            if (rect.parent != frame) rect.SetParent(frame, false);
            if (rect.anchorMin != Vector2.zero) rect.anchorMin = Vector2.zero;
            if (rect.anchorMax != Vector2.one) rect.anchorMax = Vector2.one;
            Vector2 min = outside ? new Vector2(-l, -b) : Vector2.zero;
            Vector2 max = outside ? new Vector2(r, t) : Vector2.zero;
            if (rect.offsetMin != min) rect.offsetMin = min;
            if (rect.offsetMax != max) rect.offsetMax = max;
            float reference = image.canvas != null ? image.canvas.referencePixelsPerUnit : 100f;
            Sprite sprite = FrameSprite(l, b, r, t, reference);
            if (image.sprite != sprite) image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.pixelsPerUnitMultiplier = 1f;
        }

        private static void Hide(Image image)
        {
            if (image != null && image.gameObject.activeSelf)
                image.gameObject.SetActive(false);
        }
    }
}
