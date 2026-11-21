using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    // Windows (and Wine/Proton): use OS cursor handles directly.
    // WM_SETCURSOR is processed by the window, not Unity's texture cursor API.
    internal static class WindowsNativeCursor
    {
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_NCDESTROY = 0x0082;
        private const int HTCLIENT = 1;
        private const int GWLP_WNDPROC = -4;
        private const uint IMAGE_CURSOR = 2;
        private const uint LR_COPYFROMRESOURCE = 0x4000;

        // Wine/Proton does not necessarily scale Windows stock cursors to the
        // host desktop's Xcursor size. Read the host hint only at startup.
        // Native Windows with no XCURSOR_SIZE retains unmodified OS handles.
        private static int ProtonCursorSize()
        {
            return DesktopCursorSize.Preferred();
        }

        private static readonly int[] Ids = {
            32512, // arrow
            32649, // hand
            32513, // text beam
            32646, // move
            32644, // resize horizontal
            32645, // resize vertical
            32515, // crosshair
            32650, // app starting
            32514  // wait
        };

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

        private static IntPtr s_hwnd, s_previous;
        private static IntPtr[] s_handles;
        private static bool[] s_owned;
        private static WindowProc s_hook;
        private static IntPtr s_hookPointer;
        private static bool s_hooked;
        private static bool s_enabled;
        private static NativeCursorKind s_kind;

        internal static bool Initialize()
        {
            if (s_hooked) { s_enabled = true; return true; }
            IntPtr hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero)
            {
                Plugin.Log?.LogWarning("Windows cursor: game window not ready; will retry on a cursor event.");
                return false;
            }
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                return false;

            var handles = new IntPtr[Ids.Length];
            var owned = new bool[Ids.Length];
            int desktopSize = ProtonCursorSize();
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = LoadCursor(IntPtr.Zero, new IntPtr(Ids[i]));
                if (handles[i] == IntPtr.Zero) handles[i] = LoadCursor(IntPtr.Zero, new IntPtr(Ids[0]));
                if (desktopSize > 0 && handles[i] != IntPtr.Zero)
                {
                    // CopyImage allocates a real HCURSOR at the requested size.
                    // Never resize Windows' normal accessibility-selected cursors.
                    IntPtr resized = CopyImage(handles[i], IMAGE_CURSOR,
                        desktopSize, desktopSize, LR_COPYFROMRESOURCE);
                    if (resized != IntPtr.Zero)
                    {
                        handles[i] = resized;
                        owned[i] = true;
                    }
                }
            }
            if (handles[0] == IntPtr.Zero)
            {
                Plugin.Log?.LogWarning("Windows native cursor: could not load system arrow.");
                return false;
            }

            // Keep the callback rooted until the original window procedure is restored.
            s_hook = WndProc;
            s_hookPointer = Marshal.GetFunctionPointerForDelegate(s_hook);
            SetLastError(0);
            IntPtr old = SetWindowLongPtr(hwnd, GWLP_WNDPROC, s_hookPointer);
            int error = Marshal.GetLastWin32Error();
            if (old == IntPtr.Zero && error != 0)
            {
                Plugin.Log?.LogWarning("Couldn't install native window cursor handler: " + error);
                s_hook = null;
                s_hookPointer = IntPtr.Zero;
                return false;
            }
            s_hwnd = hwnd;
            s_previous = old;
            s_handles = handles;
            s_owned = owned;
            if (desktopSize > 0)
                Plugin.Log?.LogInfo("Wine/Proton cursor requested size: " + desktopSize + "px (one-time native HCURSOR loading).");
            s_hooked = true;
            s_enabled = true;
            s_kind = NativeCursorKind.Arrow;
            Apply(s_kind);
            Plugin.Log?.LogInfo("Native Windows system cursor handles enabled (also used by Wine/Proton).");
            return true;
        }

        internal static void Apply(NativeCursorKind kind)
        {
            if (!s_hooked || s_handles == null) return;
            s_kind = kind;
            IntPtr handle = s_handles[(int)kind];
            if (handle == IntPtr.Zero) handle = s_handles[0];
            // Set now as well as on future WM_SETCURSOR events.
            if (Cursor.visible && Cursor.lockState != CursorLockMode.Locked)
                SetCursor(handle);
        }

        private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp)
        {
            if (msg == WM_SETCURSOR && s_hooked && s_enabled && hwnd == s_hwnd &&
                ((long)lp & 0xffff) == HTCLIENT &&
                Cursor.visible && Cursor.lockState != CursorLockMode.Locked)
            {
                IntPtr handle = s_handles[(int)s_kind];
                if (handle == IntPtr.Zero) handle = s_handles[0];
                SetCursor(handle);
                return new IntPtr(1);
            }
            IntPtr result = s_previous != IntPtr.Zero
                ? CallWindowProc(s_previous, hwnd, msg, wp, lp)
                : DefWindowProc(hwnd, msg, wp, lp);

            // Unity/Proton can replace the native cursor while processing mouse
            // input without issuing WM_SETCURSOR. Restore ours after the game's
            // window procedure, but only when the handle actually changed.
            // This runs on mouse input events, not every rendered frame.
            if (s_hooked && s_enabled && hwnd == s_hwnd &&
                (msg == WM_MOUSEMOVE || msg == WM_LBUTTONUP ||
                 msg == WM_RBUTTONUP || msg == WM_MBUTTONUP || msg == WM_XBUTTONUP) &&
                Cursor.visible && Cursor.lockState != CursorLockMode.Locked &&
                GetForegroundWindow() == hwnd)
            {
                IntPtr handle = s_handles[(int)s_kind];
                if (handle == IntPtr.Zero) handle = s_handles[0];
                if (GetCursor() != handle) SetCursor(handle);
            }

            if (msg == WM_NCDESTROY && hwnd == s_hwnd)
            {
                s_hwnd = IntPtr.Zero;
                s_hooked = false;
                // Do not release the callback here; native code may still unwind through it.
            }
            return result;
        }

        internal static void Shutdown()
        {
            s_enabled = false;
            if (s_hooked && s_hwnd != IntPtr.Zero && s_previous != IntPtr.Zero)
            {
                IntPtr current = GetWindowLongPtr(s_hwnd, GWLP_WNDPROC);
                if (current == s_hookPointer)
                {
                    SetWindowLongPtr(s_hwnd, GWLP_WNDPROC, s_previous);
                    s_hwnd = s_previous = IntPtr.Zero;
                    if (s_handles != null && s_owned != null)
                        for (int i = 0; i < s_handles.Length; ++i)
                            if (s_owned[i] && s_handles[i] != IntPtr.Zero)
                                DestroyCursor(s_handles[i]);
                    s_handles = null;
                    s_owned = null;
                    s_hooked = false;
                    s_hookPointer = IntPtr.Zero;
                    s_hook = null;
                }
                else
                {
                    // Another window hook is ahead of ours. It may still call
                    // our callback, so keep its delegate and previous proc rooted.
                    Plugin.Log?.LogInfo("Native cursor disabled; window hook chain retained safely.");
                }
            }
        }
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr resource);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CopyImage(IntPtr handle, uint type, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
        [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] private static extern IntPtr GetCursor();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CallWindowProc(IntPtr old, IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("kernel32.dll")] private static extern void SetLastError(int error);
    }
}
