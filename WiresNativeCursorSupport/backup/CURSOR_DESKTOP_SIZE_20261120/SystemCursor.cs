using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    internal enum NativeCursorKind
    {
        Arrow,
        Hand,
        Text,
        Move,
        ResizeHorizontal,
        ResizeVertical,
        Crosshair,
        Working,
        Wait
    }

    // Valheim's pointer isn't set by its code: it's the project's default cursor (PlayerSettings,
    // defaultCursor), which Unity shows as a hardware cursor. So the system's own cursors are read
    // once (arrow, hand, text, move, working, wait; Windows and macOS) and handed to Unity whenever CursorEvents says the
    // wanted one changed; Unity shows them the way it showed Valheim's. Nothing runs per frame.
    // Cursor.SetCursor(null) gives Valheim's back.
    internal static class SystemCursor
    {
        private struct Picture { public Texture2D Texture; public Vector2 Hotspot; }

        private static readonly Picture[] s_pictures = new Picture[(int)NativeCursorKind.Wait + 1];
        private static bool s_on, s_read, s_linux;
        private static NativeCursorKind? s_shown;

        // Windows' own cursor for each kind (IDC_ values); the ones not used stay empty.
        private static readonly int[] WindowsIds = { 32512, 32649, 32513, 32646, 32644, 32645, 32515, 32650, 32514 };

        internal static void Apply(bool on)
        {
            s_on = on;
            s_shown = null;
            if (!on)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            if (!s_read)
            {
                s_read = true;
                if (Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    foreach (NativeCursorKind k in new[] { NativeCursorKind.Arrow, NativeCursorKind.Hand, NativeCursorKind.Text,
                                 NativeCursorKind.Move, NativeCursorKind.Working, NativeCursorKind.Wait })
                        s_pictures[(int)k].Texture = WindowsCursor(WindowsIds[(int)k], out s_pictures[(int)k].Hotspot);
                }
                else if (Application.platform == RuntimePlatform.OSXPlayer)
                {
                    foreach (NativeCursorKind k in new[] { NativeCursorKind.Arrow, NativeCursorKind.Hand, NativeCursorKind.Text,
                                 NativeCursorKind.Move, NativeCursorKind.Working, NativeCursorKind.Wait })
                        s_pictures[(int)k].Texture = MacCursors.Read(k, out s_pictures[(int)k].Hotspot);
                }
                else if (Application.platform == RuntimePlatform.LinuxPlayer)
                    s_linux = LinuxNativeCursorBackend.Initialize();
            }
            Refresh();
        }

        // Shows the cursor CursorEvents wants, if it isn't showing already.
        internal static void Refresh()
        {
            if (!s_on) return;
            NativeCursorKind kind = CursorEvents.Kind;
            if (s_shown == kind) return;
            if (s_linux)
            {
                LinuxNativeCursorBackend.Apply(kind, true);
                s_shown = kind;
                return;
            }
            Picture p = s_pictures[(int)kind];
            if (p.Texture == null) p = s_pictures[(int)NativeCursorKind.Arrow];
            if (p.Texture == null) return;
            Cursor.SetCursor(p.Texture, p.Hotspot, CursorMode.Auto);
            s_shown = kind;
        }

        internal static void Shutdown()
        {
            s_on = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (s_linux) LinuxNativeCursorBackend.Shutdown();
            s_linux = false;
            for (int i = 0; i < s_pictures.Length; i++)
            {
                if (s_pictures[i].Texture != null) UnityEngine.Object.Destroy(s_pictures[i].Texture);
                s_pictures[i] = default;
            }
            s_read = false;
        }

        // A Windows cursor as it's set up on this PC (scheme, size), drawn once onto black and once
        // onto white: where the two differ it's see-through, which gives its alpha without caring
        // whether it's a colour cursor or an old black-and-white one. An animated one gives its
        // first frame.
        private static Texture2D WindowsCursor(int id, out Vector2 hotspot)
        {
            hotspot = Vector2.zero;
            IntPtr cursor = LoadCursor(IntPtr.Zero, new IntPtr(id));
            if (cursor == IntPtr.Zero || !GetIconInfo(cursor, out IconInfo info)) return null;
            try
            {
                IntPtr sized = info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask;
                if (GetObject(sized, Marshal.SizeOf<Bitmap>(), out Bitmap bm) == 0) return null;
                int w = bm.bmWidth, h = info.hbmColor != IntPtr.Zero ? bm.bmHeight : bm.bmHeight / 2;
                if (w <= 0 || h <= 0) return null;

                uint[] black = Draw(cursor, w, h, 0xFF000000u);
                uint[] white = Draw(cursor, w, h, 0xFFFFFFFFu);
                if (black == null || white == null) return null;

                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint b = black[y * w + x], wh = white[y * w + x];
                    // per channel: on white = colour*a + (1-a), on black = colour*a
                    int a = 255 - ((int)((wh >> 8) & 0xFF) - (int)((b >> 8) & 0xFF));
                    a = Mathf.Clamp(a, 0, 255);
                    byte r = 0, g = 0, bl = 0;
                    if (a > 0)
                    {
                        r = (byte)Mathf.Min(255, ((b >> 16) & 0xFF) * 255 / a);
                        g = (byte)Mathf.Min(255, ((b >> 8) & 0xFF) * 255 / a);
                        bl = (byte)Mathf.Min(255, (b & 0xFF) * 255 / a);
                    }
                    pixels[(h - 1 - y) * w + x] = new Color32(r, g, bl, (byte)a); // Unity's rows go bottom up
                }

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "Wire's Native Cursor " + id,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                hotspot = new Vector2(info.xHotspot, info.yHotspot);
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("Couldn't read Windows cursor " + id + ": " + e.Message);
                return null;
            }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            }
        }

        // The cursor drawn on a w x h 32-bit picture filled with one colour (top row first).
        private static uint[] Draw(IntPtr cursor, int w, int h, uint fill)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            var bi = new BitmapInfoHeader
            {
                biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32
            };
            IntPtr bmp = CreateDIBSection(dc, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (bmp == IntPtr.Zero) { DeleteDC(dc); return null; }
            IntPtr old = SelectObject(dc, bmp);
            try
            {
                var px = new int[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = unchecked((int)fill);
                Marshal.Copy(px, 0, bits, px.Length);
                if (!DrawIconEx(dc, 0, 0, cursor, w, h, 0, IntPtr.Zero, DiNormal)) return null;
                GdiFlush();
                Marshal.Copy(bits, px, 0, px.Length);
                var result = new uint[px.Length];
                for (int i = 0; i < px.Length; i++) result[i] = unchecked((uint)px[i]);
                return result;
            }
            finally
            {
                SelectObject(dc, old);
                DeleteObject(bmp);
                DeleteDC(dc);
            }
        }

        private const int DiNormal = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public bool fIcon;
            public int xHotspot, yHotspot;
            public IntPtr hbmMask, hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Bitmap
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out Bitmap bitmap);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    }
}
