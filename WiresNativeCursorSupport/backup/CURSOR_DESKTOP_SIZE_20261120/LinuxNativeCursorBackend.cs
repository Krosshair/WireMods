using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    internal static class LinuxNativeCursorBackend
    {
        private const string XcursorLib =
            "libXcursor.so.1";

        private const string X11Lib =
            "libX11.so.6";

        private sealed class CursorFrame
        {
            internal Texture2D Texture;
            internal Vector2 Hotspot;
            internal int DelayMs;
        }

        private sealed class CursorFrames
        {
            internal readonly List<CursorFrame>
                Frames =
                    new List<CursorFrame>();

            internal int TotalDelayMs;
        }

        private static readonly Dictionary<NativeCursorKind, CursorFrames>
            Cursors =
                new Dictionary<NativeCursorKind, CursorFrames>();

        private static string _theme;
        private static int _size;
        private static bool _initialized;
        private static bool _dirty = true;
        private static NativeCursorKind _appliedKind =
            NativeCursorKind.Arrow;
        private static int _appliedFrame = -1;

        internal static bool Initialize()
        {
            try
            {
                ResolveThemeAndSize();

                Cursors.Clear();

                LoadKind(
                    NativeCursorKind.Arrow,
                    "left_ptr",
                    "default",
                    "arrow");

                LoadKind(
                    NativeCursorKind.Hand,
                    "pointer",
                    "hand2",
                    "hand1");

                LoadKind(
                    NativeCursorKind.Text,
                    "text",
                    "xterm");

                LoadKind(
                    NativeCursorKind.Move,
                    "grabbing",
                    "closedhand",
                    "fleur",
                    "move");

                LoadKind(
                    NativeCursorKind.ResizeHorizontal,
                    "ew-resize",
                    "sb_h_double_arrow",
                    "size_hor");

                LoadKind(
                    NativeCursorKind.ResizeVertical,
                    "ns-resize",
                    "sb_v_double_arrow",
                    "size_ver");

                LoadKind(
                    NativeCursorKind.Crosshair,
                    "crosshair",
                    "cross");

                LoadKind(
                    NativeCursorKind.Working,
                    "progress",
                    "left_ptr_watch",
                    "half-busy",
                    "watch");

                LoadKind(
                    NativeCursorKind.Wait,
                    "wait",
                    "watch",
                    "progress");

                _initialized =
                    Cursors.ContainsKey(
                        NativeCursorKind.Arrow);

                if (!_initialized)
                {
                    Plugin.Log?.LogError(
                        "Linux system cursor theme could not supply an " +
                        "Arrow cursor. Native cursor support will stay inactive.");
                    return false;
                }

                Plugin.Log?.LogInfo(
                    "Initialized Linux system cursor-theme backend " +
                    $"(theme='{(_theme ?? "default")}', size={_size}px, " +
                    $"loaded states={Cursors.Count}/9). " +
                    "Cursor images come from the desktop Xcursor theme and " +
                    "are displayed through Unity, so the same backend works " +
                    "under X11, XWayland and native Wayland.");

                return true;
            }
            catch (DllNotFoundException ex)
            {
                Plugin.Log?.LogError(
                    "Linux libXcursor is unavailable; native cursor " +
                    "support cannot load the desktop cursor theme. " +
                    ex.Message);

                _initialized = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError(
                    "Could not initialize Linux system cursor backend: " +
                    ex.Message);

                _initialized = false;
                return false;
            }
        }

        internal static void Apply(
            NativeCursorKind kind,
            bool force)
        {
            if (!_initialized)
                return;

            CursorFrames frames =
                GetFrames(kind);

            if (frames == null ||
                frames.Frames.Count == 0)
                return;

            int frameIndex =
                GetFrameIndex(frames);

            if (!force &&
                !_dirty &&
                kind == _appliedKind &&
                frameIndex == _appliedFrame)
                return;

            CursorFrame frame =
                frames.Frames[frameIndex];

            Cursor.SetCursor(
                frame.Texture,
                frame.Hotspot,
                CursorMode.Auto);

            _appliedKind = kind;
            _appliedFrame = frameIndex;
            _dirty = false;
        }

        internal static void MarkDirty()
        {
            _dirty = true;
        }

        internal static void RestoreDefault()
        {
            Cursor.SetCursor(
                null,
                Vector2.zero,
                CursorMode.Auto);

            _dirty = true;
            _appliedFrame = -1;
            _appliedKind =
                NativeCursorKind.Arrow;
        }

        internal static void Shutdown()
        {
            foreach (CursorFrames frames
                     in Cursors.Values)
            {
                foreach (CursorFrame frame
                         in frames.Frames)
                {
                    if (frame.Texture != null)
                    {
                        UnityEngine.Object.Destroy(
                            frame.Texture);
                    }
                }
            }

            Cursors.Clear();
            _initialized = false;
            _dirty = true;
            _appliedFrame = -1;
        }

        private static CursorFrames GetFrames(
            NativeCursorKind kind)
        {
            if (Cursors.TryGetValue(
                    kind,
                    out CursorFrames frames))
                return frames;

            Cursors.TryGetValue(
                NativeCursorKind.Arrow,
                out frames);

            return frames;
        }

        private static int GetFrameIndex(
            CursorFrames frames)
        {
            if (frames.Frames.Count <= 1 ||
                frames.TotalDelayMs <= 0)
                return 0;

            int elapsed =
                (int)(
                    Time.unscaledTime *
                    1000f) %
                frames.TotalDelayMs;

            int sum = 0;

            for (int i = 0;
                 i < frames.Frames.Count;
                 i++)
            {
                int delay =
                    Math.Max(
                        1,
                        frames.Frames[i].DelayMs);

                sum += delay;

                if (elapsed < sum)
                    return i;
            }

            return frames.Frames.Count - 1;
        }

        private static void ResolveThemeAndSize()
        {
            _theme =
                Environment.GetEnvironmentVariable(
                    "XCURSOR_THEME");

            _size = 0;

            string sizeText =
                Environment.GetEnvironmentVariable(
                    "XCURSOR_SIZE");

            if (!string.IsNullOrEmpty(sizeText))
            {
                int.TryParse(
                    sizeText,
                    out _size);
            }

            // Under X11 or XWayland, ask libXcursor for the actual
            // display theme and default size. This is more accurate than
            // environment variables when the desktop configured them via
            // X resources.
            IntPtr display = IntPtr.Zero;

            try
            {
                display =
                    XOpenDisplay(
                        IntPtr.Zero);

                if (display != IntPtr.Zero)
                {
                    IntPtr themePtr =
                        XcursorGetTheme(
                            display);

                    if (themePtr != IntPtr.Zero)
                    {
                        string displayTheme =
                            Marshal.PtrToStringAnsi(
                                themePtr);

                        if (!string.IsNullOrEmpty(
                                displayTheme))
                        {
                            _theme =
                                displayTheme;
                        }
                    }

                    int displaySize =
                        XcursorGetDefaultSize(
                            display);

                    if (displaySize > 0)
                        _size = displaySize;
                }
            }
            catch
            {
                // Native Wayland sessions may not expose an X display.
                // The theme loader itself does not require one.
            }
            finally
            {
                if (display != IntPtr.Zero)
                {
                    try
                    {
                        XCloseDisplay(
                            display);
                    }
                    catch
                    {
                    }
                }
            }

            if (string.IsNullOrEmpty(_theme))
            {
                _theme =
                    ReadDefaultThemeInheritance();
            }

            if (_size <= 0)
                _size = 24;
        }

        private static string
            ReadDefaultThemeInheritance()
        {
            try
            {
                string home =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .UserProfile);

                string[] candidates =
                {
                    Path.Combine(
                        home,
                        ".icons",
                        "default",
                        "index.theme"),
                    Path.Combine(
                        home,
                        ".local",
                        "share",
                        "icons",
                        "default",
                        "index.theme")
                };

                foreach (string file
                         in candidates)
                {
                    if (!File.Exists(file))
                        continue;

                    foreach (string raw
                             in File.ReadAllLines(file))
                    {
                        string line =
                            raw.Trim();

                        if (!line.StartsWith(
                                "Inherits=",
                                StringComparison
                                    .OrdinalIgnoreCase))
                            continue;

                        string value =
                            line.Substring(
                                    "Inherits=".Length)
                                .Trim();

                        int split =
                            value.IndexOfAny(
                                new[]
                                {
                                    ',',
                                    ';',
                                    ':'
                                });

                        if (split >= 0)
                        {
                            value =
                                value.Substring(
                                    0,
                                    split)
                                .Trim();
                        }

                        if (!string.IsNullOrEmpty(
                                value))
                            return value;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static void LoadKind(
            NativeCursorKind kind,
            params string[] names)
        {
            foreach (string name in names)
            {
                CursorFrames frames =
                    LoadImages(name);

                if (frames == null ||
                    frames.Frames.Count == 0)
                    continue;

                Cursors[kind] = frames;

                Plugin.Log?.LogDebug(
                    $"Linux cursor '{kind}' -> Xcursor '{name}' " +
                    $"({frames.Frames.Count} frame(s)).");

                return;
            }

            Plugin.Log?.LogWarning(
                $"Linux cursor theme has no native image for '{kind}'. " +
                "Arrow will be used for that state.");
        }

        private static CursorFrames LoadImages(
            string name)
        {
            IntPtr imagesPtr =
                XcursorLibraryLoadImages(
                    name,
                    _theme,
                    _size);

            if (imagesPtr == IntPtr.Zero)
                return null;

            try
            {
                XcursorImagesNative images =
                    Marshal.PtrToStructure<
                        XcursorImagesNative>(
                        imagesPtr);

                if (images.nimage <= 0 ||
                    images.images ==
                        IntPtr.Zero)
                    return null;

                CursorFrames result =
                    new CursorFrames();

                for (int i = 0;
                     i < images.nimage;
                     i++)
                {
                    IntPtr imagePtr =
                        Marshal.ReadIntPtr(
                            images.images,
                            i * IntPtr.Size);

                    if (imagePtr == IntPtr.Zero)
                        continue;

                    XcursorImageNative image =
                        Marshal.PtrToStructure<
                            XcursorImageNative>(
                            imagePtr);

                    if (image.width == 0 ||
                        image.height == 0 ||
                        image.pixels ==
                            IntPtr.Zero ||
                        image.width > 512 ||
                        image.height > 512)
                        continue;

                    Texture2D texture =
                        CreateTexture(
                            name,
                            image);

                    if (texture == null)
                        continue;

                    CursorFrame frame =
                        new CursorFrame
                        {
                            Texture = texture,
                            Hotspot =
                                new Vector2(
                                    image.xhot,
                                    image.yhot),
                            DelayMs =
                                (int)image.delay
                        };

                    result.Frames.Add(
                        frame);

                    result.TotalDelayMs +=
                        Math.Max(
                            1,
                            frame.DelayMs);
                }

                if (result.Frames.Count == 0)
                    return null;

                return result;
            }
            finally
            {
                XcursorImagesDestroy(
                    imagesPtr);
            }
        }

        private static Texture2D CreateTexture(
            string cursorName,
            XcursorImageNative image)
        {
            int width =
                checked((int)image.width);

            int height =
                checked((int)image.height);

            int count =
                checked(width * height);

            int[] packed =
                new int[count];

            Marshal.Copy(
                image.pixels,
                packed,
                0,
                count);

            Color32[] colors =
                new Color32[count];

            // Xcursor image rows are top-to-bottom, while Unity texture
            // pixel arrays are bottom-to-top. Flip Y but keep the hotspot
            // in its original top-left coordinate system because Unity's
            // Cursor.SetCursor hotspot is also measured from top-left.
            for (int y = 0;
                 y < height;
                 y++)
            {
                int dstY =
                    height - 1 - y;

                for (int x = 0;
                     x < width;
                     x++)
                {
                    uint argb =
                        unchecked(
                            (uint)packed[
                                y * width + x]);

                    byte a =
                        (byte)(argb >> 24);

                    byte r =
                        (byte)(argb >> 16);

                    byte g =
                        (byte)(argb >> 8);

                    byte b =
                        (byte)argb;

                    // Xcursor stores premultiplied ARGB. Unity expects
                    // straight-alpha Color32 data.
                    if (a > 0 &&
                        a < 255)
                    {
                        r =
                            Unpremultiply(
                                r,
                                a);

                        g =
                            Unpremultiply(
                                g,
                                a);

                        b =
                            Unpremultiply(
                                b,
                                a);
                    }

                    colors[
                        dstY * width + x] =
                        new Color32(
                            r,
                            g,
                            b,
                            a);
                }
            }

            Texture2D texture =
                new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    false);

            texture.name =
                "WireNativeCursor_Linux_" +
                cursorName;

            texture.hideFlags =
                HideFlags.HideAndDontSave;

            texture.SetPixels32(colors);

            texture.Apply(
                false,
                false);

            return texture;
        }

        private static byte Unpremultiply(
            byte value,
            byte alpha)
        {
            int straight =
                (value * 255 +
                 alpha / 2) /
                alpha;

            return
                (byte)Math.Min(
                    255,
                    straight);
        }

        [StructLayout(
            LayoutKind.Sequential)]
        private struct XcursorImageNative
        {
            internal uint version;
            internal uint size;
            internal uint width;
            internal uint height;
            internal uint xhot;
            internal uint yhot;
            internal uint delay;
            internal IntPtr pixels;
        }

        [StructLayout(
            LayoutKind.Sequential)]
        private struct XcursorImagesNative
        {
            internal int nimage;
            internal IntPtr images;
            internal IntPtr name;
        }

        [DllImport(
            XcursorLib,
            CallingConvention =
                CallingConvention.Cdecl,
            CharSet = CharSet.Ansi)]
        private static extern IntPtr
            XcursorLibraryLoadImages(
                string name,
                string theme,
                int size);

        [DllImport(
            XcursorLib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern void
            XcursorImagesDestroy(
                IntPtr images);

        [DllImport(
            XcursorLib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern IntPtr
            XcursorGetTheme(
                IntPtr display);

        [DllImport(
            XcursorLib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern int
            XcursorGetDefaultSize(
                IntPtr display);

        [DllImport(
            X11Lib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern IntPtr
            XOpenDisplay(
                IntPtr displayName);

        [DllImport(
            X11Lib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern int
            XCloseDisplay(
                IntPtr display);
    }
}
