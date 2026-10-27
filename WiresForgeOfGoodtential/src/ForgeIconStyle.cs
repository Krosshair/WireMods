using System;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    // Map icons are a darker grey with a dark shadow around them; the round
    // icon buttons above the inventory are a bright silvery white with dark
    // linework and no shadow. This turns a map icon into the latter, made
    // once at runtime from the game's own sprite.
    internal static class ForgeIconStyle
    {
        // The map icon's own grey range is stretched onto the buttons' one.
        private const float Low = 0.30f;
        private const float High = 0.78f;
        private const float Gamma = 0.75f;
        private const float Floor = 0.30f;
        private const float Span = 0.66f;
        // Dark, semi-transparent edge pixels are the shadow.
        private const float Shadow = 0.42f;

        internal static Sprite Restyle(Sprite source)
        {
            if (source == null)
                return null;

            try
            {
                Rect rect = source.textureRect;
                Texture2D texture = source.texture;
                int width = Mathf.RoundToInt(rect.width);
                int height = Mathf.RoundToInt(rect.height);

                // The texture isn't readable; copy it through the GPU.
                RenderTexture rt = RenderTexture.GetTemporary(
                    texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;

                Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                Color[] pixels = copy.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c = pixels[i];
                    float l = (c.r + c.g + c.b) / 3f;
                    float a = c.a;

                    if (a < 0.97f && l < Shadow)
                    {
                        float keep = Mathf.Clamp01(l / Shadow);
                        a *= keep * keep;
                    }

                    float t = Mathf.Pow(Mathf.Clamp01((l - Low) / (High - Low)), Gamma);
                    float v = Floor + Span * t;
                    pixels[i] = new Color(v, v, v, a);
                }

                copy.SetPixels(pixels);
                copy.Apply(false, true);
                copy.name = "Goodtential forge icon";

                return Sprite.Create(copy, new Rect(0, 0, width, height),
                    new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            }
            catch (Exception)
            {
                // Tightly packed atlas sprites can't be cut out; use it as is.
                return source;
            }
        }
    }
}
