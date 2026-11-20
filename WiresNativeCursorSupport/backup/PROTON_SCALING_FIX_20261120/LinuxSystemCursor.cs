using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    // Native Linux/X11 cursor handles. No copied cursor images or resampling.
    // Wayland without an X11 window requires compositor integration and is not claimed here.
    internal static class LinuxSystemCursor
    {
        private const string X11 = "libX11.so.6";
        private const string Xcursor = "libXcursor.so.1";

        private static readonly string[][] Names = {
            new[] { "left_ptr", "default", "arrow" },
            new[] { "pointer", "hand2", "hand1" },
            new[] { "text", "xterm" },
            new[] { "grabbing", "closedhand", "fleur" },
            new[] { "ew-resize", "sb_h_double_arrow" },
            new[] { "ns-resize", "sb_v_double_arrow" },
            new[] { "crosshair", "cross" },
            new[] { "progress", "left_ptr_watch", "watch" },
            new[] { "wait", "watch" }
        };

        private static IntPtr s_display;
        private static IntPtr[] s_cursors;
        private static IntPtr s_window;

        internal static bool Initialize()
        {
            if (s_display != IntPtr.Zero) return true;
            try
            {
                IntPtr display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero)
                {
                    Plugin.Log?.LogWarning("Native Linux cursor requires an X11/XWayland window. Wayland-native cursor support is unavailable.");
                    return false;
                }
                var cursors = new IntPtr[Names.Length];
                for (int k = 0; k < Names.Length; k++)
                    foreach (string name in Names[k])
                    {
                        cursors[k] = XcursorLibraryLoadCursor(display, name);
                        if (cursors[k] != IntPtr.Zero) break;
                    }
                if (cursors[0] == IntPtr.Zero)
                {
                    XCloseDisplay(display);
                    return false;
                }
                for (int k = 1; k < cursors.Length; k++)
                    if (cursors[k] == IntPtr.Zero) cursors[k] = cursors[0];
                s_display = display;
                s_cursors = cursors;
                Plugin.Log?.LogInfo("Native X11 system cursor handles enabled.");
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Plugin.Log?.LogWarning("Native Linux/X11 cursor APIs unavailable: " + e.Message);
                return false;
            }
        }

        internal static void Apply(NativeCursorKind kind)
        {
            if (s_display == IntPtr.Zero) return;
            try
            {
                // Only modify Valheim's own focused X11 window. Never redefine
                // the cursor for an unrelated focused desktop application.
                if (s_window == IntPtr.Zero)
                {
                    XGetInputFocus(s_display, out IntPtr focus, out int revert);
                    if (focus == IntPtr.Zero || focus == new IntPtr(1)) return;
                    IntPtr current = focus;
                    for (int depth = 0; depth < 10 && current != IntPtr.Zero; depth++)
                    {
                        if (XFetchName(s_display, current, out IntPtr name) != 0)
                        {
                            try
                            {
                                string title = Marshal.PtrToStringAnsi(name);
                                if (!string.IsNullOrEmpty(title) &&
                                    title.IndexOf("Valheim", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    s_window = current;
                                    break;
                                }
                            }
                            finally { if (name != IntPtr.Zero) XFree(name); }
                        }
                        if (XQueryTree(s_display, current, out IntPtr root,
                            out IntPtr parent, out IntPtr children, out uint count) == 0) break;
                        if (children != IntPtr.Zero) XFree(children);
                        if (parent == current || parent == root) break;
                        current = parent;
                    }
                }
                if (s_window == IntPtr.Zero) return;
                XDefineCursor(s_display, s_window, s_cursors[(int)kind]);
                XFlush(s_display);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("Couldn't apply native X11 cursor: " + e.Message);
            }
        }

        internal static void Shutdown()
        {
            if (s_display == IntPtr.Zero) return;
            if (s_window != IntPtr.Zero)
            {
                XUndefineCursor(s_display, s_window);
                XFlush(s_display);
            }
            if (s_cursors != null)
                for (int i = 0; i < s_cursors.Length; i++)
                {
                    IntPtr cursor = s_cursors[i];
                    if (cursor == IntPtr.Zero) continue;
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                        if (s_cursors[j] == cursor) { seen = true; break; }
                    if (!seen) XFreeCursor(s_display, cursor);
                }
            XCloseDisplay(s_display);
            s_display = s_window = IntPtr.Zero;
            s_cursors = null;
        }

        [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(X11)] private static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revert);
        [DllImport(X11)] private static extern int XFetchName(IntPtr display, IntPtr window, out IntPtr name);
        [DllImport(X11)] private static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
        [DllImport(X11)] private static extern int XDefineCursor(IntPtr display, IntPtr window, IntPtr cursor);
        [DllImport(X11)] private static extern int XUndefineCursor(IntPtr display, IntPtr window);
        [DllImport(X11)] private static extern int XFlush(IntPtr display);
        [DllImport(X11)] private static extern int XFree(IntPtr ptr);
        [DllImport(X11)] private static extern int XFreeCursor(IntPtr display, IntPtr cursor);
        [DllImport(Xcursor)] private static extern IntPtr XcursorLibraryLoadCursor(IntPtr display, string name);
    }
}
