using System;
using System.IO;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    // Read once when the cursor backend initializes. Proton presents itself
    // to Unity as Windows; the Linux Xcursor backend is not reached there.
    internal static class DesktopCursorSize
    {
        internal static int Preferred()
        {
            int size = Parse(Environment.GetEnvironmentVariable("XCURSOR_SIZE"));
            if (size > 0) return size;

            // Under Proton, desktop cursor configuration is often available
            // through Wine's Z: drive even though libXcursor cannot be P/Invoked.
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) return 0;
            if (home[0] == '/') home = "Z:" + home.Replace('/', '\\');
            if (!Directory.Exists(home)) return 0;
            int kde = ReadSetting(Path.Combine(home, ".config", "kcminputrc"), "cursorSize");
            if (kde > 0) return kde;
            return ReadSetting(Path.Combine(home, ".config", "gtk-3.0", "settings.ini"),
                "gtk-cursor-theme-size");
        }

        private static int Parse(string value)
        {
            int size;
            return int.TryParse(value, out size) && size >= 8 && size <= 256 ? size : 0;
        }

        private static int ReadSetting(string path, string key)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(),
                        key, StringComparison.OrdinalIgnoreCase))
                        return Parse(line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug("Cursor size configuration unavailable: " + e.Message);
            }
            return 0;
        }

        internal static Texture2D Resize(Texture2D input, int target, ref Vector2 hotspot, int nominal = 0)
        {
            if (input == null || target <= 0) return input;
            int natural = nominal > 0 ? nominal : Math.Max(input.width, input.height);
            if (natural == target) return input;
            float scale = (float)target / natural;
            int width = Mathf.Clamp(Mathf.RoundToInt(input.width * scale), 1, 256);
            int height = Mathf.Clamp(Mathf.RoundToInt(input.height * scale), 1, 256);
            Color32[] original = input.GetPixels32();
            Color32[] scaled = new Color32[width * height];
            // Nearest-neighbour keeps crisp outlines and avoids blurring.
            for (int y = 0; y < height; y++)
            {
                int sy = Mathf.Min(input.height - 1, y * input.height / height);
                for (int x = 0; x < width; x++)
                {
                    int sx = Mathf.Min(input.width - 1, x * input.width / width);
                    scaled[y * width + x] = original[sy * input.width + sx];
                }
            }
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = input.name + "_desktop_size",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            output.SetPixels32(scaled);
            output.Apply(false, false);
            hotspot = new Vector2(hotspot.x * width / input.width,
                hotspot.y * height / input.height);
            UnityEngine.Object.Destroy(input);
            return output;
        }
    }
}
