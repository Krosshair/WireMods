using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WiresNativeCursorSupport
{
    internal static class MacNativeCursorBackend
    {
        private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

        private static readonly Dictionary<NativeCursorKind, IntPtr>
            Cursors =
                new Dictionary<NativeCursorKind, IntPtr>();

        private static IntPtr _nsCursorClass;
        private static IntPtr _setSelector;
        private static IntPtr _currentCursorSelector;
        private static IntPtr _respondsSelector;
        private static bool _initialized;

        internal static bool Initialize()
        {
            try
            {
                _nsCursorClass =
                    objc_getClass("NSCursor");

                if (_nsCursorClass == IntPtr.Zero)
                    return false;

                _setSelector =
                    sel_registerName("set");

                _currentCursorSelector =
                    sel_registerName("currentCursor");

                _respondsSelector =
                    sel_registerName("respondsToSelector:");

                Cursors.Clear();

                Cursors[NativeCursorKind.Arrow] =
                    GetClassCursor(
                        "arrowCursor");

                Cursors[NativeCursorKind.Hand] =
                    GetClassCursor(
                        "pointingHandCursor");

                Cursors[NativeCursorKind.Text] =
                    GetClassCursor(
                        "IBeamCursor");

                Cursors[NativeCursorKind.Move] =
                    GetClassCursor(
                        "closedHandCursor",
                        "openHandCursor");

                Cursors[NativeCursorKind.ResizeHorizontal] =
                    GetClassCursor(
                        "columnResizeCursor",
                        "resizeLeftRightCursor");

                Cursors[NativeCursorKind.ResizeVertical] =
                    GetClassCursor(
                        "rowResizeCursor",
                        "resizeUpDownCursor");

                Cursors[NativeCursorKind.Crosshair] =
                    GetClassCursor(
                        "crosshairCursor");

                // AppKit does not expose a public wait/progress cursor,
                // but macOS itself has the native busy-but-clickable
                // cursor used by Safari/Chromium/SDL. Resolve it only if
                // this OS exposes the selector; otherwise Arrow is the
                // safe public-API fallback.
                IntPtr busy =
                    GetClassCursor(
                        "busyButClickableCursor");

                if (busy == IntPtr.Zero)
                {
                    busy =
                        Cursors[
                            NativeCursorKind.Arrow];
                }

                Cursors[NativeCursorKind.Working] =
                    busy;

                Cursors[NativeCursorKind.Wait] =
                    busy;

                _initialized =
                    Cursors[
                        NativeCursorKind.Arrow] !=
                    IntPtr.Zero;

                if (_initialized)
                {
                    Plugin.Log?.LogInfo(
                        "Initialized macOS AppKit system cursor backend " +
                        "(Arrow, Pointing Hand, I-beam, Closed Hand, " +
                        "Resize, Crosshair, Busy/Progress where available).");
                }

                return _initialized;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError(
                    "Could not initialize macOS AppKit cursor backend: " +
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

            IntPtr expected =
                GetCursor(kind);

            if (expected == IntPtr.Zero)
                return;

            if (!force)
            {
                IntPtr current =
                    objc_msgSend(
                        _nsCursorClass,
                        _currentCursorSelector);

                if (current == expected)
                    return;
            }

            objc_msgSend_void(
                expected,
                _setSelector);
        }

        internal static void RestoreDefault()
        {
            if (!_initialized)
                return;

            Apply(
                NativeCursorKind.Arrow,
                true);
        }

        internal static void Shutdown()
        {
            Cursors.Clear();
            _initialized = false;
            _nsCursorClass = IntPtr.Zero;
        }

        private static IntPtr GetCursor(
            NativeCursorKind kind)
        {
            if (Cursors.TryGetValue(
                    kind,
                    out IntPtr cursor) &&
                cursor != IntPtr.Zero)
            {
                return cursor;
            }

            if (Cursors.TryGetValue(
                    NativeCursorKind.Arrow,
                    out cursor))
            {
                return cursor;
            }

            return IntPtr.Zero;
        }

        private static IntPtr GetClassCursor(
            params string[] selectors)
        {
            foreach (string name in selectors)
            {
                IntPtr selector =
                    sel_registerName(name);

                if (selector == IntPtr.Zero)
                    continue;

                bool responds =
                    objc_msgSend_bool(
                        _nsCursorClass,
                        _respondsSelector,
                        selector);

                if (!responds)
                    continue;

                IntPtr cursor =
                    objc_msgSend(
                        _nsCursorClass,
                        selector);

                if (cursor != IntPtr.Zero)
                    return cursor;
            }

            return IntPtr.Zero;
        }

        [DllImport(
            ObjCLib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern IntPtr
            objc_getClass(
                string name);

        [DllImport(
            ObjCLib,
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern IntPtr
            sel_registerName(
                string name);

        [DllImport(
            ObjCLib,
            EntryPoint = "objc_msgSend",
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern IntPtr
            objc_msgSend(
                IntPtr receiver,
                IntPtr selector);

        [DllImport(
            ObjCLib,
            EntryPoint = "objc_msgSend",
            CallingConvention =
                CallingConvention.Cdecl)]
        private static extern void
            objc_msgSend_void(
                IntPtr receiver,
                IntPtr selector);

        [DllImport(
            ObjCLib,
            EntryPoint = "objc_msgSend",
            CallingConvention =
                CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool
            objc_msgSend_bool(
                IntPtr receiver,
                IntPtr selector,
                IntPtr argument);
    }
}
