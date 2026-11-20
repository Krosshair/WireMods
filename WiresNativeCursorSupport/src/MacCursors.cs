using System;
using System.Runtime.InteropServices;

namespace WiresNativeCursorSupport
{
    // AppKit NSCursor objects directly. No screenshots, PNG conversion or Unity textures.
    internal static class MacCursors
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        internal static bool InitializeNative() => Class("NSCursor") != IntPtr.Zero;

        internal static void ApplyNative(NativeCursorKind kind)
        {
            IntPtr cursor = Cursor(kind);
            if (cursor == IntPtr.Zero) cursor = Cursor(NativeCursorKind.Arrow);
            if (cursor != IntPtr.Zero)
                msgSend(cursor, Sel("set"));
        }

        internal static void ShutdownNative() => ApplyNative(NativeCursorKind.Arrow);

        private static IntPtr Cursor(NativeCursorKind kind)
        {
            switch (kind)
            {
                case NativeCursorKind.Arrow: return Named("arrowCursor");
                case NativeCursorKind.Hand: return Named("pointingHandCursor");
                case NativeCursorKind.Text: return Named("IBeamCursor");
                case NativeCursorKind.Move: return Named("closedHandCursor", "openHandCursor");
                case NativeCursorKind.ResizeHorizontal: return Named("resizeLeftRightCursor");
                case NativeCursorKind.ResizeVertical: return Named("resizeUpDownCursor");
                case NativeCursorKind.Crosshair: return Named("crosshairCursor");
                case NativeCursorKind.Working: return Named("busyButClickableCursor", "arrowCursor");
                case NativeCursorKind.Wait: return Named("busyButClickableCursor", "arrowCursor");
                default: return Named("arrowCursor");
            }
        }

        private static IntPtr Named(params string[] names)
        {
            IntPtr cls = Class("NSCursor");
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            foreach (string name in names)
            {
                IntPtr selector = Sel(name);
                if (selector != IntPtr.Zero &&
                    msgSend_bool(cls, Sel("respondsToSelector:"), selector))
                {
                    IntPtr cursor = msgSend(cls, selector);
                    if (cursor != IntPtr.Zero) return cursor;
                }
            }
            return IntPtr.Zero;
        }

        private static IntPtr Class(string name) => objc_getClass(name);
        private static IntPtr Sel(string name) => sel_registerName(name);

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr msgSend(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg);
    }
}
