using UnityEngine;

namespace WiresNativeCursorSupport
{
    internal enum NativeCursorKind
    {
        Arrow, Hand, Text, Move, ResizeHorizontal, ResizeVertical,
        Crosshair, Working, Wait
    }

    // Native OS cursor handles, not copied Texture2D images.
    // The game provides state-change events; the operating system renders the pointer.
    internal static class SystemCursor
    {
        private static bool s_enabled;
        private static bool s_active;
        private static NativeCursorKind? s_current;

        internal static void Apply(bool enabled)
        {
            s_enabled = enabled;
            if (!enabled)
            {
                ShutdownBackend();
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            Refresh();
        }

        internal static void Refresh()
        {
            if (!s_enabled) return;
            if (!s_active)
            {
                if (!InitializeBackend()) return;
                s_active = true;
                s_current = null;
                // Clear any Unity-supplied texture cursor before changing the native handle.
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
            NativeCursorKind kind = CursorEvents.Kind;
            if (s_current == kind) return;
            if (Application.platform == RuntimePlatform.WindowsPlayer)
                WindowsNativeCursor.Apply(kind);
            else if (Application.platform == RuntimePlatform.OSXPlayer)
                MacCursors.ApplyNative(kind);
            else if (Application.platform == RuntimePlatform.LinuxPlayer)
                LinuxSystemCursor.Apply(kind);
            s_current = kind;
        }

        private static bool InitializeBackend()
        {
            if (Application.platform == RuntimePlatform.WindowsPlayer)
                return WindowsNativeCursor.Initialize();
            if (Application.platform == RuntimePlatform.OSXPlayer)
                return MacCursors.InitializeNative();
            if (Application.platform == RuntimePlatform.LinuxPlayer)
                return LinuxSystemCursor.Initialize();
            Plugin.Log?.LogWarning("No native cursor API for " + Application.platform);
            return false;
        }

        private static void ShutdownBackend()
        {
            if (!s_active) return;
            if (Application.platform == RuntimePlatform.WindowsPlayer)
                WindowsNativeCursor.Shutdown();
            else if (Application.platform == RuntimePlatform.OSXPlayer)
                MacCursors.ShutdownNative();
            else if (Application.platform == RuntimePlatform.LinuxPlayer)
                LinuxSystemCursor.Shutdown();
            s_active = false;
            s_current = null;
        }

        internal static void Shutdown()
        {
            s_enabled = false;
            ShutdownBackend();
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
