using UnityEngine;

namespace WiresStackify
{
    // A wooden chest in the style of the game's armor and weight icons:
    // mottled gray, a bright rim with a thin dark line inside it, light
    // from the top left. Drawn once from shapes, 4x4 samples per pixel.
    internal static class ChestIcon
    {
        private const int N = 64, SS = 4;
        private const float X0 = 0.04f, X1 = 0.96f, Y0 = 0.08f, Y1 = 0.58f, Arch = 0.32f, Rim = 0.042f;
        private static Sprite _sprite;

        internal static Sprite Get()
        {
            if (_sprite != null) return _sprite;
            Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[N * N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float sum = 0f; int hits = 0;
                    for (int sj = 0; sj < SS; sj++)
                        for (int si = 0; si < SS; si++)
                        {
                            float x = (i + (si + 0.5f) / SS) / N;
                            float y = (j + (sj + 0.5f) / SS) / N; // texture rows go up
                            float g;
                            if (Shade(x, y, out g)) { sum += g; hits++; }
                        }
                    byte v = hits > 0 ? (byte)Mathf.Clamp(Mathf.RoundToInt(sum / hits), 0, 255) : (byte)0;
                    px[j * N + i] = new Color32(v, v, v, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            return _sprite;
        }

        private static float Box(float px, float py, float cx, float cy, float hx, float hy, float r)
        {
            float dx = Mathf.Abs(px - cx) - hx + r, dy = Mathf.Abs(py - cy) - hy + r;
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
        }

        private static float Hash(float x, float y)
        {
            float v = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static float ValueNoise(float x, float y)
        {
            float ix = Mathf.Floor(x), iy = Mathf.Floor(y), fx = x - ix, fy = y - iy;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
        }

        // The mottled metal look of the shield, two octaves.
        private static float Noise(float x, float y)
        {
            return ValueNoise(x * 9f, y * 9f) * 0.65f + ValueNoise(x * 23f, y * 23f) * 0.35f;
        }

        private static float ChestDistance(float x, float y)
        {
            float body = Box(x, y, (X0 + X1) / 2, (Y0 + Y1) / 2, (X1 - X0) / 2, (Y1 - Y0) / 2, 0.05f);
            // Lid: the same width, strongly rounded top, square bottom.
            float lid = Box(x, y, 0.5f, Y1 + Arch / 2 - 0.03f, (X1 - X0) / 2, Arch / 2 + 0.03f, 0.16f);
            lid = Mathf.Min(lid, Box(x, y, 0.5f, Y1 + 0.06f, (X1 - X0) / 2, 0.09f, 0f));
            return Mathf.Min(body, lid);
        }

        private static bool Shade(float x, float y, out float g)
        {
            g = 0f;
            float d = ChestDistance(x, y);
            if (d > 0f) return false;
            g = 150f + (0.5f - x) * 30f + (y - 0.5f) * 40f + (Noise(x, y) - 0.5f) * 34f;
            // plank seams
            if (y < Y1 && (Mathf.Abs(y - (Y0 + 0.19f)) < 0.013f || Mathf.Abs(y - (Y0 + 0.38f)) < 0.013f)) g -= 38f;
            if (y > Y1 + 0.02f && Mathf.Abs(y - (Y1 + 0.14f)) < 0.013f) g -= 38f;
            // iron bands with rivets
            for (int k = 0; k < 2; k++)
            {
                float bx = k == 0 ? 0.25f : 0.75f;
                if (Mathf.Abs(x - bx) >= 0.045f) continue;
                g = (Mathf.Abs(x - bx) > 0.03f ? 170f : 205f) + (Noise(x + 3f, y) - 0.5f) * 20f;
                if (Rivet(x, y, bx, Y0 + 0.10f) || Rivet(x, y, bx, Y0 + 0.30f) || Rivet(x, y, bx, Y1 + 0.12f)) g = 242f;
            }
            // where the lid closes
            if (Mathf.Abs(y - Y1) < 0.02f) g = 228f + (Noise(x, y + 5f) - 0.5f) * 16f;
            // lock plate and keyhole
            if (Mathf.Abs(x - 0.5f) < 0.07f && y > Y1 - 0.19f && y < Y1 - 0.02f) g = 226f;
            if (new Vector2(x - 0.5f, y - (Y1 - 0.08f)).magnitude < 0.022f ||
                (Mathf.Abs(x - 0.5f) < 0.009f && y > Y1 - 0.15f && y < Y1 - 0.08f)) g = 88f;
            // the frame: bright rim, thin dark line inside it
            if (d > -Rim) g = 236f + (Noise(x + 7f, y) - 0.5f) * 14f;
            else if (d > -Rim - 0.025f) g = Mathf.Min(g, 108f);
            return true;
        }

        private static bool Rivet(float x, float y, float cx, float cy)
        {
            return new Vector2(x - cx, y - cy).magnitude < 0.018f;
        }
    }
}
