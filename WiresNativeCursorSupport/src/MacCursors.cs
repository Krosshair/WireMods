using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    // macOS's own cursors (AppKit's NSCursor), read once as pictures for Unity like the Windows ones:
    // the cursor's image as PNG, and its hotspot moved from points to the picture's pixels.
    internal static class MacCursors
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        // ImageConversion.LoadImage, found by name: this project builds against .NET 4.7.2 and the module
        // against netstandard 2.1.
        private static readonly System.Reflection.MethodInfo LoadImage =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        [StructLayout(LayoutKind.Sequential)]
        private struct NSPoint { public double X, Y; }

        internal static Texture2D Read(NativeCursorKind kind, out Vector2 hotspot)
        {
            hotspot = Vector2.zero;
            try
            {
                IntPtr cursor = Cursor(kind);
                if (cursor == IntPtr.Zero) return null;
                IntPtr image = Send(cursor, "image");
                if (image == IntPtr.Zero) return null;
                NSPoint size = SendPoint(image, "size");
                NSPoint hot = SendPoint(cursor, "hotSpot");

                IntPtr tiff = Send(image, "TIFFRepresentation");
                IntPtr rep = SendArg(Class("NSBitmapImageRep"), "imageRepWithData:", tiff);
                if (rep == IntPtr.Zero) return null;
                IntPtr png = SendPng(rep, Sel("representationUsingType:properties:"), new IntPtr(4), IntPtr.Zero); // NSBitmapImageFileTypePNG
                if (png == IntPtr.Zero) return null;
                int length = (int)(long)Send(png, "length");
                IntPtr bytes = Send(png, "bytes");
                if (length <= 0 || bytes == IntPtr.Zero) return null;
                var data = new byte[length];
                Marshal.Copy(bytes, data, 0, length);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Wire's Native Cursor " + kind,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
                if (LoadImage == null || !(bool)LoadImage.Invoke(null, new object[] { tex, data, false })) { UnityEngine.Object.Destroy(tex); return null; }
                float scale = size.X > 0 ? tex.width / (float)size.X : 1f;
                hotspot = new Vector2((float)hot.X * scale, (float)hot.Y * scale); // both from the top left
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("Couldn't read the macOS " + kind + " cursor: " + e.Message);
                return null;
            }
        }

        // The AppKit cursor for each kind; macOS has no public busy cursor, so working and wait use
        // the one Safari and others use where it's there.
        private static IntPtr Cursor(NativeCursorKind kind)
        {
            switch (kind)
            {
                case NativeCursorKind.Arrow: return ClassCursor("arrowCursor");
                case NativeCursorKind.Hand: return ClassCursor("pointingHandCursor");
                case NativeCursorKind.Text: return ClassCursor("IBeamCursor");
                case NativeCursorKind.Move: return ClassCursor("closedHandCursor", "openHandCursor");
                case NativeCursorKind.Working:
                case NativeCursorKind.Wait: return ClassCursor("busyButClickableCursor");
                default: return IntPtr.Zero;
            }
        }

        private static IntPtr ClassCursor(params string[] names)
        {
            IntPtr cls = Class("NSCursor");
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            foreach (string name in names)
            {
                IntPtr sel = Sel(name);
                if (sel != IntPtr.Zero && msgSend_bool(cls, Sel("respondsToSelector:"), sel))
                {
                    IntPtr c = msgSend(cls, sel);
                    if (c != IntPtr.Zero) return c;
                }
            }
            return IntPtr.Zero;
        }

        private static IntPtr Class(string name) => objc_getClass(name);
        private static IntPtr Sel(string name) => sel_registerName(name);
        private static IntPtr Send(IntPtr o, string sel) => msgSend(o, Sel(sel));
        private static IntPtr SendArg(IntPtr o, string sel, IntPtr a) => msgSend_arg(o, Sel(sel), a);
        private static NSPoint SendPoint(IntPtr o, string sel) => msgSend_point(o, Sel(sel));
        private static IntPtr SendPng(IntPtr o, IntPtr sel, IntPtr type, IntPtr props) => msgSend_args(o, sel, type, props);

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr msgSend(IntPtr o, IntPtr sel);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr msgSend_arg(IntPtr o, IntPtr sel, IntPtr a);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr msgSend_args(IntPtr o, IntPtr sel, IntPtr a, IntPtr b);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern NSPoint msgSend_point(IntPtr o, IntPtr sel);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool msgSend_bool(IntPtr o, IntPtr sel, IntPtr a);
    }
}
