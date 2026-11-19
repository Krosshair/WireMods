using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
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

    internal static class NativeCursorController
    {
        private enum HostCursorPlatform
        {
            Unsupported,
            Windows,
            MacOS,
            Linux
        }

        private const int IDC_ARROW = 32512;
        private const int IDC_IBEAM = 32513;
        private const int IDC_WAIT = 32514;
        private const int IDC_CROSS = 32515;
        private const int IDC_SIZEWE = 32644;
        private const int IDC_SIZENS = 32645;
        private const int IDC_SIZEALL = 32646;
        private const int IDC_HAND = 32649;
        private const int IDC_APPSTARTING = 32650;

        private const int GWL_WNDPROC = -4;
        private const int GCLP_HCURSOR = -12;
        private const uint WM_SETCURSOR = 0x0020;
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const uint WM_INPUT = 0x00FF;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_MBUTTONUP = 0x0208;
        private const uint WM_MOUSEWHEEL = 0x020A;
        private const uint WM_XBUTTONDOWN = 0x020B;
        private const uint WM_XBUTTONUP = 0x020C;
        private const uint WM_MOUSEHWHEEL = 0x020E;
        private const uint WM_CAPTURECHANGED = 0x0215;
        private const uint WM_MOUSEHOVER = 0x02A1;
        private const uint WM_MOUSELEAVE = 0x02A3;
        private const int HTCLIENT = 1;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProc(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        private static readonly WindowProc HookDelegate =
            WindowProcHook;

        private static readonly IntPtr HookDelegatePtr =
            Marshal.GetFunctionPointerForDelegate(HookDelegate);

        private static readonly FieldInfo CraftTimerField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_craftTimer");

        private static readonly FieldInfo DragItemField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_dragItem");

        private static readonly HashSet<NativeCursorKind>
            VerifiedKinds =
                new HashSet<NativeCursorKind>();

        private static bool _supported;
        private static bool _enabled;
        private static HostCursorPlatform _hostPlatform =
            HostCursorPlatform.Unsupported;

        private static IntPtr _arrow;
        private static IntPtr _hand;
        private static IntPtr _text;
        private static IntPtr _move;
        private static IntPtr _resizeHorizontal;
        private static IntPtr _resizeVertical;
        private static IntPtr _crosshair;
        private static IntPtr _working;
        private static IntPtr _wait;

        private static IntPtr _window;
        private static IntPtr _previousWindowProc;
        private static bool _hookInstalled;
        private static float _nextHookAttempt;

        private static IntPtr _capturedGameCursor;
        private static bool _capturedGameCursorValid;

        private static bool _unitySystemDefaultInstalled;
        private static Texture2D _transparentUnityCursor;
        private static IntPtr _originalClassCursor;
        private static bool _originalClassCursorCaptured;
        private static IntPtr _lastClassCursor;

        private static NativeCursorKind _desiredKind =
            NativeCursorKind.Arrow;

        private static bool _lastWait;
        private static bool _lastCrafting;
        private static bool _lastShouldControl;
        private static bool _loggedUnityOverwrite;
        private static int _overwriteCorrections;

        internal static void Initialize()
        {
            RuntimePlatform platform =
                Application.platform;

            if (platform ==
                    RuntimePlatform.WindowsPlayer ||
                platform ==
                    RuntimePlatform.WindowsEditor)
            {
                _hostPlatform =
                    HostCursorPlatform.Windows;

                _supported =
                    InitializeWindowsBackend();
            }
            else if (platform ==
                         RuntimePlatform.OSXPlayer ||
                     platform ==
                         RuntimePlatform.OSXEditor)
            {
                _hostPlatform =
                    HostCursorPlatform.MacOS;

                _supported =
                    MacNativeCursorBackend
                        .Initialize();

                if (_supported)
                    CreateTransparentUnityCursor();
            }
            else if (platform ==
                         RuntimePlatform.LinuxPlayer ||
                     platform ==
                         RuntimePlatform.LinuxEditor)
            {
                _hostPlatform =
                    HostCursorPlatform.Linux;

                _supported =
                    LinuxNativeCursorBackend
                        .Initialize();
            }
            else
            {
                _hostPlatform =
                    HostCursorPlatform.Unsupported;

                _supported = false;
            }

            if (!_supported)
            {
                Plugin.Log?.LogInfo(
                    "Native cursor support is inactive on " +
                    $"platform '{platform}'.");
                return;
            }

            Plugin.Log?.LogInfo(
                "Native cursor backend active: " +
                _hostPlatform + ".");
        }

        private static bool
            InitializeWindowsBackend()
        {
            _arrow = NativeMethods.LoadCursor(
                IntPtr.Zero,
                new IntPtr(IDC_ARROW));

            _hand = NativeMethods.LoadCursor(
                IntPtr.Zero,
                new IntPtr(IDC_HAND));

            _text = NativeMethods.LoadCursor(
                IntPtr.Zero,
                new IntPtr(IDC_IBEAM));

            _move = NativeMethods.LoadCursor(
                IntPtr.Zero,
                new IntPtr(IDC_SIZEALL));

            _resizeHorizontal =
                NativeMethods.LoadCursor(
                    IntPtr.Zero,
                    new IntPtr(IDC_SIZEWE));

            _resizeVertical =
                NativeMethods.LoadCursor(
                    IntPtr.Zero,
                    new IntPtr(IDC_SIZENS));

            _crosshair =
                NativeMethods.LoadCursor(
                    IntPtr.Zero,
                    new IntPtr(IDC_CROSS));

            _working =
                NativeMethods.LoadCursor(
                    IntPtr.Zero,
                    new IntPtr(IDC_APPSTARTING));

            _wait = NativeMethods.LoadCursor(
                IntPtr.Zero,
                new IntPtr(IDC_WAIT));

            Plugin.Log?.LogInfo(
                "Loaded Windows system cursors: " +
                $"arrow=0x{_arrow.ToInt64():X}, " +
                $"hand=0x{_hand.ToInt64():X}, " +
                $"text=0x{_text.ToInt64():X}, " +
                $"move=0x{_move.ToInt64():X}, " +
                $"resizeH=0x{_resizeHorizontal.ToInt64():X}, " +
                $"resizeV=0x{_resizeVertical.ToInt64():X}, " +
                $"crosshair=0x{_crosshair.ToInt64():X}, " +
                $"working=0x{_working.ToInt64():X}, " +
                $"wait=0x{_wait.ToInt64():X}.");

            if (_arrow == IntPtr.Zero)
            {
                Plugin.Log?.LogError(
                    "Windows did not return the standard arrow cursor handle.");
                return false;
            }

            CreateTransparentUnityCursor();
            return true;
        }

        private static void
            CreateTransparentUnityCursor()
        {
            if (_transparentUnityCursor != null)
                return;

            _transparentUnityCursor =
                new Texture2D(
                    32,
                    32,
                    TextureFormat.RGBA32,
                    false,
                    false);

            _transparentUnityCursor.name =
                "WireNativeCursor_TransparentUnityCursor";

            _transparentUnityCursor.hideFlags =
                HideFlags.HideAndDontSave;

            _transparentUnityCursor.SetPixels32(
                new Color32[32 * 32]);

            _transparentUnityCursor.Apply(
                false,
                false);
        }

        internal static void SetEnabled(bool enabled)
        {
            _enabled =
                enabled &&
                _supported;

            if (!_enabled)
            {
                RestorePlatformCursor();
                return;
            }

            RefreshControlState(
                forceApply: true);
        }

        internal static void Shutdown()
        {
            _enabled = false;

            RestorePlatformCursor();

            if (_hostPlatform ==
                HostCursorPlatform.MacOS)
            {
                MacNativeCursorBackend
                    .Shutdown();
            }
            else if (_hostPlatform ==
                     HostCursorPlatform.Linux)
            {
                LinuxNativeCursorBackend
                    .Shutdown();
            }

            HoverCursorTracker.Clear();

            VerifiedKinds.Clear();
            _capturedGameCursor = IntPtr.Zero;
            _capturedGameCursorValid = false;
            _unitySystemDefaultInstalled = false;

            if (_transparentUnityCursor != null)
            {
                UnityEngine.Object.Destroy(
                    _transparentUnityCursor);

                _transparentUnityCursor = null;
            }

            _originalClassCursor = IntPtr.Zero;
            _originalClassCursorCaptured = false;
            _lastClassCursor = IntPtr.Zero;
            _loggedUnityOverwrite = false;
            _overwriteCorrections = 0;
            _hostPlatform =
                HostCursorPlatform.Unsupported;
            _supported = false;
        }

        private static void
            RestorePlatformCursor()
        {
            switch (_hostPlatform)
            {
                case HostCursorPlatform.Windows:
                    RestoreVanillaCursorState();
                    RemoveWindowHook();
                    break;

                case HostCursorPlatform.MacOS:
                    Cursor.SetCursor(
                        null,
                        Vector2.zero,
                        CursorMode.Auto);

                    MacNativeCursorBackend
                        .RestoreDefault();

                    _unitySystemDefaultInstalled =
                        false;
                    break;

                case HostCursorPlatform.Linux:
                    LinuxNativeCursorBackend
                        .RestoreDefault();
                    break;
            }

            _desiredKind =
                NativeCursorKind.Arrow;
        }

        internal static void LateTick()
        {
            if (!_enabled ||
                !_supported)
                return;

            bool shouldControl =
                ShouldControlCursor();

            _lastShouldControl =
                shouldControl;

            _lastWait =
                IsWaitState();

            _lastCrafting =
                IsCrafting();

            if (!shouldControl)
                return;

            ReconcileDesiredCursor(
                "LateUpdate",
                false);
        }

        internal static void OnBeforeRender()
        {
            if (!_enabled ||
                !_supported ||
                !ShouldControlCursor())
                return;

            // This runs after normal script LateUpdate and just before
            // Unity renders/presents the frame. If Unity's native player
            // changed the hardware cursor after our LateUpdate, repair it
            // here so that transient one-frame flashes never reach the
            // presented frame.
            ReconcileDesiredCursor(
                "BeforeRender",
                false);
        }

        internal static void EndOfFrameTick()
        {
            if (!_enabled ||
                !_supported ||
                !ShouldControlCursor())
                return;

            // WaitForEndOfFrame is later still than onBeforeRender on the
            // normal desktop player loop. This is deliberately redundant:
            // SetCursor is only called if GetCursorInfo shows a mismatch.
            ReconcileDesiredCursor(
                "EndOfFrame",
                false);
        }

        internal static void OnUnityCursorVisibilityApplied()
        {
            if (!_enabled ||
                !_supported ||
                !ShouldControlCursor())
                return;

            // GameCamera.UpdateMouseCapture calls ZCursor.Show() every
            // LateUpdate while in-game UI such as InventoryGui is open.
            // ZCursor.Show() always reaches SetVisible(), even when the
            // visibility state did not change. Treating this callback as
            // a cursor-texture reset made 0.2.6 reinstall the transparent
            // Unity cursor and force SetCursor every single gameplay frame.
            //
            // Cursor.visible does not invalidate the custom cursor texture,
            // so leave the transparent Unity mask alone here. Only verify
            // the current native HCURSOR and repair it if Windows actually
            // reports a mismatch.
            ReconcileDesiredCursor(
                "ZCursor.SetVisible",
                false);
        }

        internal static void OnCursorContextChanged()
        {
            if (_enabled)
            {
                RefreshControlState(
                    forceApply: true);
            }
        }

        internal static void OnApplicationFocusChanged(
            bool focused)
        {
            if (!_enabled ||
                !focused)
                return;

            // Unity can recreate/reapply cursor state when the player
            // window regains focus. Mark only the active platform backend
            // dirty and re-assert once.
            if (_hostPlatform ==
                    HostCursorPlatform.Windows ||
                _hostPlatform ==
                    HostCursorPlatform.MacOS)
            {
                _unitySystemDefaultInstalled =
                    false;
            }
            else if (_hostPlatform ==
                     HostCursorPlatform.Linux)
            {
                LinuxNativeCursorBackend
                    .MarkDirty();
            }

            if (_hostPlatform ==
                HostCursorPlatform.Windows)
            {
                _nextHookAttempt = 0f;
            }

            RefreshControlState(
                forceApply: true);
        }

        internal static void OnHoverStateChanged()
        {
            if (_enabled)
            {
                RefreshControlState(
                    forceApply: true);
            }
        }

        private static void RefreshControlState(
            bool forceApply)
        {
            if (!_enabled ||
                !_supported ||
                !ShouldControlCursor())
                return;

            ReconcileDesiredCursor(
                "ContextChanged",
                forceApply);
        }

        private static void ReconcileDesiredCursor(
            string source,
            bool forceApply)
        {
            if (!_enabled ||
                !_supported ||
                !ShouldControlCursor())
                return;

            NativeCursorKind kind =
                DetermineCursorKind();

            bool changed =
                kind != _desiredKind;

            _desiredKind = kind;

            if (_hostPlatform ==
                HostCursorPlatform.Linux)
            {
                LinuxNativeCursorBackend.Apply(
                    kind,
                    changed || forceApply);

                return;
            }

            if (_hostPlatform ==
                HostCursorPlatform.MacOS)
            {
                // Hide Unity's own project-cursor layer and let AppKit
                // draw the selected native macOS cursor.
                EnsureUnityUsesSystemDefault();

                MacNativeCursorBackend.Apply(
                    kind,
                    changed || forceApply);

                return;
            }

            if (_hostPlatform !=
                HostCursorPlatform.Windows)
                return;

            // Windows needs both the Unity transparent-mask layer and the
            // Win32 HCURSOR layer, plus WM_SETCURSOR compensation.
            CaptureGameCursorIfPossible();

            if (!_hookInstalled &&
                Time.unscaledTime >=
                    _nextHookAttempt)
            {
                EnsureWindowHook();
            }

            EnsureUnityUsesSystemDefault();

            if (changed || forceApply)
                ApplyDesiredCursor();

            EnsureDesiredCursorPresent(source);
        }

        private static void EnsureUnityUsesSystemDefault()
        {
            if (_transparentUnityCursor == null ||
                _unitySystemDefaultInstalled)
                return;

            // Install the transparent Unity cursor only when needed.
            // Reassigning Cursor.SetCursor every reconciliation briefly
            // changes Unity's hardware cursor state and was the reason
            // the native Hand visibly flickered even though the desired
            // state itself was stable.
            Cursor.SetCursor(
                _transparentUnityCursor,
                Vector2.zero,
                CursorMode.Auto);

            _unitySystemDefaultInstalled = true;

            Plugin.Log?.LogInfo(
                "Masked Unity's own cursor layer with a transparent " +
                "CPU-readable RGBA32 cursor.");
        }

        private static void CaptureOriginalClassCursor()
        {
            if (_originalClassCursorCaptured ||
                _window == IntPtr.Zero ||
                !NativeMethods.IsWindow(_window))
                return;

            _originalClassCursor =
                NativeMethods.GetClassLongPtr(
                    _window,
                    GCLP_HCURSOR);

            _originalClassCursorCaptured = true;
        }

        private static void UpdateWindowClassCursor()
        {
            if (_window == IntPtr.Zero ||
                !NativeMethods.IsWindow(_window))
                return;

            CaptureOriginalClassCursor();

            IntPtr desired =
                GetHandle(_desiredKind);

            if (desired == IntPtr.Zero)
                desired = _arrow;

            if (desired == IntPtr.Zero ||
                desired == _lastClassCursor)
                return;

            NativeMethods.SetClassLongPtr(
                _window,
                GCLP_HCURSOR,
                desired);

            _lastClassCursor = desired;
        }

        private static void RestoreVanillaCursorState()
        {
            if (!_supported)
                return;

            // Cursor.SetCursor(null) intentionally changed Unity's own
            // state while this feature was enabled. For the immediate
            // disabled state, restore the captured Valheim HCURSOR as the
            // window-class cursor as well as the active cursor. This lets
            // the ordinary Windows WM_SETCURSOR path keep showing the
            // original Valheim pointer until the process restarts.
            // Remove our transparent Unity cursor first.
            Cursor.SetCursor(
                null,
                Vector2.zero,
                CursorMode.Auto);

            IntPtr restore =
                _capturedGameCursorValid
                    ? _capturedGameCursor
                    : _originalClassCursor;

            if (_window != IntPtr.Zero &&
                NativeMethods.IsWindow(_window) &&
                restore != IntPtr.Zero)
            {
                NativeMethods.SetClassLongPtr(
                    _window,
                    GCLP_HCURSOR,
                    restore);

                _lastClassCursor = restore;
            }

            if (restore != IntPtr.Zero)
                NativeMethods.SetCursor(restore);

            _unitySystemDefaultInstalled = false;
            _desiredKind = NativeCursorKind.Arrow;
        }

        private static NativeCursorKind
            DetermineCursorKind()
        {
            if (IsWaitState())
                return NativeCursorKind.Wait;

            if (IsCrafting())
                return NativeCursorKind.Working;

            if (IsDraggingInventoryItem())
                return NativeCursorKind.Move;

            return HoverCursorTracker.DesiredKind;
        }

        private static bool IsWaitState()
        {
            FejdStartup startup =
                FejdStartup.instance;

            if (startup == null)
                return false;

            return
                (startup.m_loading != null &&
                 startup.m_loading.activeInHierarchy) ||
                (startup.m_pleaseWait != null &&
                 startup.m_pleaseWait.activeInHierarchy);
        }

        private static bool IsCrafting()
        {
            InventoryGui gui =
                InventoryGui.instance;

            if (gui == null ||
                !InventoryGui.IsVisible() ||
                CraftTimerField == null)
                return false;

            object value =
                CraftTimerField.GetValue(gui);

            return value is float timer &&
                   timer >= 0f;
        }

        private static bool IsDraggingInventoryItem()
        {
            InventoryGui gui =
                InventoryGui.instance;

            if (gui == null ||
                !InventoryGui.IsVisible() ||
                DragItemField == null)
                return false;

            return
                DragItemField.GetValue(gui) != null;
        }

        private static bool ShouldControlCursor()
        {
            try
            {
                return
                    Application.isFocused &&
                    Cursor.lockState ==
                        CursorLockMode.None &&
                    ZCursor.IsRequested &&
                    ZCursor.IsVisible &&
                    ZInput.IsMouseActive() &&
                    !ZInput.IsTouchActive() &&
                    !ZInput.IsGamepadMouseActive() &&
                    (_hostPlatform !=
                         HostCursorPlatform.Windows ||
                     IsCursorInsideClientArea());
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCursorInsideClientArea()
        {
            // Before the window hook exists we cannot cheaply test the
            // client rect; allow startup to proceed so EnsureWindowHook
            // can discover it.
            if (_window == IntPtr.Zero ||
                !NativeMethods.IsWindow(_window))
                return true;

            NativeMethods.POINT point;

            if (!NativeMethods.GetCursorPos(out point))
                return true;

            if (!NativeMethods.ScreenToClient(
                    _window,
                    ref point))
                return true;

            NativeMethods.RECT rect;

            if (!NativeMethods.GetClientRect(
                    _window,
                    out rect))
                return true;

            return
                point.x >= rect.left &&
                point.y >= rect.top &&
                point.x < rect.right &&
                point.y < rect.bottom;
        }

        private static void ApplyDesiredCursor()
        {
            if (!ShouldControlCursor())
                return;

            IntPtr handle =
                GetHandle(_desiredKind);

            if (handle == IntPtr.Zero)
                handle = _arrow;

            if (handle == IntPtr.Zero)
                return;

            NativeMethods.SetCursor(handle);
            VerifyCursor(handle, _desiredKind);
        }

        private static void EnsureDesiredCursorPresent(
            string source)
        {
            if (!ShouldControlCursor())
                return;

            IntPtr expected =
                GetHandle(_desiredKind);

            if (expected == IntPtr.Zero)
                expected = _arrow;

            if (expected == IntPtr.Zero)
                return;

            NativeMethods.CURSORINFO info =
                CreateCursorInfo();

            bool visible =
                NativeMethods.GetCursorInfo(
                    ref info) &&
                (info.flags &
                 NativeMethods.CURSOR_SHOWING) != 0;

            if (visible &&
                info.hCursor == expected)
                return;

            NativeMethods.SetCursor(expected);
            _overwriteCorrections++;

            if (!_loggedUnityOverwrite)
            {
                _loggedUnityOverwrite = true;

                string actual =
                    visible &&
                    info.hCursor != IntPtr.Zero
                        ? $"0x{info.hCursor.ToInt64():X}"
                        : "hidden/unknown";

                Plugin.Log?.LogInfo(
                    "Detected Unity replacing the native cursor " +
                    $"(actual={actual}, expected=0x{expected.ToInt64():X}, " +
                    $"source={source}). Persistent cursor reassertion is now active.");
            }

            VerifyCursor(
                expected,
                _desiredKind);
        }

        private static bool IsMouseCursorMessage(
            uint msg)
        {
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                case WM_INPUT:
                case WM_MOUSEMOVE:
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                case WM_MBUTTONDOWN:
                case WM_MBUTTONUP:
                case WM_MOUSEWHEEL:
                case WM_XBUTTONDOWN:
                case WM_XBUTTONUP:
                case WM_MOUSEHWHEEL:
                case WM_CAPTURECHANGED:
                case WM_MOUSEHOVER:
                case WM_MOUSELEAVE:
                    return true;

                default:
                    return false;
            }
        }

        private static IntPtr GetHandle(
            NativeCursorKind kind)
        {
            switch (kind)
            {
                case NativeCursorKind.Hand:
                    return _hand;

                case NativeCursorKind.Text:
                    return _text;

                case NativeCursorKind.Move:
                    return _move != IntPtr.Zero
                        ? _move
                        : _hand;

                case NativeCursorKind.ResizeHorizontal:
                    return _resizeHorizontal != IntPtr.Zero
                        ? _resizeHorizontal
                        : _arrow;

                case NativeCursorKind.ResizeVertical:
                    return _resizeVertical != IntPtr.Zero
                        ? _resizeVertical
                        : _arrow;

                case NativeCursorKind.Crosshair:
                    return _crosshair != IntPtr.Zero
                        ? _crosshair
                        : _arrow;

                case NativeCursorKind.Working:
                    return _working;

                case NativeCursorKind.Wait:
                    return _wait;

                default:
                    return _arrow;
            }
        }

        private static void CaptureGameCursorIfPossible()
        {
            if (_capturedGameCursorValid)
                return;

            NativeMethods.CURSORINFO info =
                CreateCursorInfo();

            if (!NativeMethods.GetCursorInfo(
                    ref info))
                return;

            if ((info.flags &
                 NativeMethods.CURSOR_SHOWING) == 0 ||
                info.hCursor == IntPtr.Zero)
                return;

            _capturedGameCursor =
                info.hCursor;

            _capturedGameCursorValid = true;

            Plugin.Log?.LogInfo(
                "Captured Valheim/Unity cursor handle before override: " +
                $"0x{_capturedGameCursor.ToInt64():X}.");
        }

        private static void RestoreCapturedGameCursor()
        {
            RestoreVanillaCursorState();
        }

        private static void VerifyCursor(
            IntPtr expected,
            NativeCursorKind kind)
        {
            if (VerifiedKinds.Contains(kind))
                return;

            NativeMethods.CURSORINFO info =
                CreateCursorInfo();

            if (!NativeMethods.GetCursorInfo(
                    ref info))
                return;

            if ((info.flags &
                 NativeMethods.CURSOR_SHOWING) == 0)
                return;

            if (info.hCursor == expected)
            {
                VerifiedKinds.Add(kind);

                Plugin.Log?.LogInfo(
                    $"Runtime verified native cursor state '{kind}' " +
                    $"(Windows reports handle 0x{expected.ToInt64():X}).");
            }
        }

        private static NativeMethods.CURSORINFO
            CreateCursorInfo()
        {
            NativeMethods.CURSORINFO info =
                new NativeMethods.CURSORINFO();

            info.cbSize =
                Marshal.SizeOf(
                    typeof(
                        NativeMethods.CURSORINFO));

            return info;
        }

        private static void EnsureWindowHook()
        {
            if (_hookInstalled)
            {
                if (_window != IntPtr.Zero &&
                    NativeMethods.IsWindow(_window))
                    return;

                _hookInstalled = false;
                _window = IntPtr.Zero;
                _previousWindowProc = IntPtr.Zero;
            }

            if (Time.unscaledTime <
                _nextHookAttempt)
                return;

            _nextHookAttempt =
                Time.unscaledTime + 1f;

            IntPtr window =
                FindUnityWindow();

            if (window == IntPtr.Zero)
                return;

            IntPtr previous =
                NativeMethods.SetWindowLongPtr(
                    window,
                    GWL_WNDPROC,
                    HookDelegatePtr);

            if (previous == IntPtr.Zero)
            {
                int error =
                    Marshal.GetLastWin32Error();

                Plugin.Log?.LogWarning(
                    "Could not subclass the Valheim window for " +
                    $"WM_SETCURSOR (Win32 error {error}). " +
                    "The mod will retry; direct cursor updates remain active.");
                return;
            }

            _window = window;
            _previousWindowProc = previous;
            _hookInstalled = true;

            CaptureOriginalClassCursor();
            UpdateWindowClassCursor();

            Plugin.Log?.LogInfo(
                "Installed WM_SETCURSOR hook on " +
                DescribeWindow(window) + ".");
        }

        private static void RemoveWindowHook()
        {
            if (!_hookInstalled ||
                _window == IntPtr.Zero ||
                _previousWindowProc == IntPtr.Zero)
            {
                _hookInstalled = false;
                _window = IntPtr.Zero;
                _previousWindowProc = IntPtr.Zero;
                return;
            }

            if (NativeMethods.IsWindow(_window))
            {
                IntPtr current =
                    NativeMethods.GetWindowLongPtr(
                        _window,
                        GWL_WNDPROC);

                // Only restore if we are still the top-most subclass.
                // If another mod subclassed the window after us, its
                // chain still contains our proc; our proc simply passes
                // through while disabled.
                if (current == HookDelegatePtr)
                {
                    NativeMethods.SetWindowLongPtr(
                        _window,
                        GWL_WNDPROC,
                        _previousWindowProc);
                }
            }

            _hookInstalled = false;
            _window = IntPtr.Zero;
            _previousWindowProc = IntPtr.Zero;
        }

        private static IntPtr FindUnityWindow()
        {
            Process process =
                Process.GetCurrentProcess();

            process.Refresh();

            IntPtr main =
                process.MainWindowHandle;

            if (main != IntPtr.Zero &&
                NativeMethods.IsWindow(main))
                return main;

            uint currentPid =
                (uint)process.Id;

            IntPtr best =
                IntPtr.Zero;

            long bestScore =
                long.MinValue;

            NativeMethods.EnumWindows(
                delegate(
                    IntPtr hwnd,
                    IntPtr lParam)
                {
                    uint pid;

                    NativeMethods.GetWindowThreadProcessId(
                        hwnd,
                        out pid);

                    if (pid != currentPid ||
                        !NativeMethods.IsWindowVisible(hwnd))
                        return true;

                    NativeMethods.RECT rect;

                    long area = 0;

                    if (NativeMethods.GetClientRect(
                            hwnd,
                            out rect))
                    {
                        area =
                            Math.Max(
                                0,
                                rect.right - rect.left) *
                            (long)Math.Max(
                                0,
                                rect.bottom - rect.top);
                    }

                    string className =
                        GetWindowClass(hwnd);

                    long score = area;

                    if (className.IndexOf(
                            "Unity",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score +=
                            1L << 60;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = hwnd;
                    }

                    return true;
                },
                IntPtr.Zero);

            return best;
        }

        private static string DescribeWindow(
            IntPtr hwnd)
        {
            return
                $"HWND=0x{hwnd.ToInt64():X}, " +
                $"class='{GetWindowClass(hwnd)}', " +
                $"title='{GetWindowTitle(hwnd)}'";
        }

        private static string GetWindowClass(
            IntPtr hwnd)
        {
            StringBuilder builder =
                new StringBuilder(256);

            NativeMethods.GetClassName(
                hwnd,
                builder,
                builder.Capacity);

            return builder.ToString();
        }

        private static string GetWindowTitle(
            IntPtr hwnd)
        {
            StringBuilder builder =
                new StringBuilder(512);

            NativeMethods.GetWindowText(
                hwnd,
                builder,
                builder.Capacity);

            return builder.ToString();
        }

        private static IntPtr WindowProcHook(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (msg == WM_SETCURSOR &&
                _enabled &&
                ShouldControlCursor() &&
                LowWord(lParam) == HTCLIENT)
            {
                IntPtr handle =
                    GetHandle(_desiredKind);

                if (handle == IntPtr.Zero)
                    handle = _arrow;

                if (handle != IntPtr.Zero)
                {
                    NativeMethods.SetCursor(handle);
                    VerifyCursor(
                        handle,
                        _desiredKind);

                    // Microsoft documents returning TRUE here as the
                    // correct way to keep a custom/system cursor from
                    // being reset on the next mouse movement.
                    return new IntPtr(1);
                }
            }

            IntPtr result;

            if (_previousWindowProc !=
                IntPtr.Zero)
            {
                result =
                    NativeMethods.CallWindowProc(
                        _previousWindowProc,
                        hWnd,
                        msg,
                        wParam,
                        lParam);
            }
            else
            {
                result =
                    NativeMethods.DefWindowProc(
                        hWnd,
                        msg,
                        wParam,
                        lParam);
            }

            // Let Unity process the mouse message first. If its native
            // Windows player code calls SetCursor while doing so, our
            // handler runs afterwards and restores the selected Windows
            // system cursor in the very same message dispatch.
            if (_enabled &&
                IsMouseCursorMessage(msg) &&
                ShouldControlCursor())
            {
                EnsureDesiredCursorPresent(
                    $"WM 0x{msg:X4}");
            }

            return result;
        }

        private static int LowWord(
            IntPtr value)
        {
            return unchecked(
                (ushort)(
                    value.ToInt64() &
                    0xFFFF));
        }

        private static class NativeMethods
        {
            internal const int CURSOR_SHOWING =
                0x00000001;

            [StructLayout(LayoutKind.Sequential)]
            internal struct POINT
            {
                internal int x;
                internal int y;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct CURSORINFO
            {
                internal int cbSize;
                internal int flags;
                internal IntPtr hCursor;
                internal POINT ptScreenPos;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct RECT
            {
                internal int left;
                internal int top;
                internal int right;
                internal int bottom;
            }

            internal delegate bool EnumWindowsProc(
                IntPtr hWnd,
                IntPtr lParam);

            [DllImport(
                "user32.dll",
                CharSet = CharSet.Auto)]
            internal static extern IntPtr LoadCursor(
                IntPtr hInstance,
                IntPtr lpCursorName);

            [DllImport("user32.dll")]
            internal static extern IntPtr SetCursor(
                IntPtr hCursor);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetCursorInfo(
                ref CURSORINFO pci);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetCursorPos(
                out POINT lpPoint);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ScreenToClient(
                IntPtr hWnd,
                ref POINT lpPoint);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindow(
                IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindowVisible(
                IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnumWindows(
                EnumWindowsProc lpEnumFunc,
                IntPtr lParam);

            [DllImport("user32.dll")]
            internal static extern uint
                GetWindowThreadProcessId(
                    IntPtr hWnd,
                    out uint lpdwProcessId);

            [DllImport(
                "user32.dll",
                CharSet = CharSet.Unicode)]
            internal static extern int GetClassName(
                IntPtr hWnd,
                StringBuilder lpClassName,
                int nMaxCount);

            [DllImport(
                "user32.dll",
                CharSet = CharSet.Unicode)]
            internal static extern int GetWindowText(
                IntPtr hWnd,
                StringBuilder lpString,
                int nMaxCount);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetClientRect(
                IntPtr hWnd,
                out RECT lpRect);

            [DllImport(
                "user32.dll",
                EntryPoint = "SetWindowLongPtrW",
                SetLastError = true)]
            private static extern IntPtr
                SetWindowLongPtr64(
                    IntPtr hWnd,
                    int nIndex,
                    IntPtr dwNewLong);

            [DllImport(
                "user32.dll",
                EntryPoint = "SetWindowLongW",
                SetLastError = true)]
            private static extern int
                SetWindowLong32(
                    IntPtr hWnd,
                    int nIndex,
                    int dwNewLong);

            [DllImport(
                "user32.dll",
                EntryPoint = "GetWindowLongPtrW",
                SetLastError = true)]
            private static extern IntPtr
                GetWindowLongPtr64(
                    IntPtr hWnd,
                    int nIndex);

            [DllImport(
                "user32.dll",
                EntryPoint = "GetWindowLongW",
                SetLastError = true)]
            private static extern int
                GetWindowLong32(
                    IntPtr hWnd,
                    int nIndex);

            [DllImport(
                "user32.dll",
                EntryPoint = "CallWindowProcW")]
            internal static extern IntPtr
                CallWindowProc(
                    IntPtr lpPrevWndFunc,
                    IntPtr hWnd,
                    uint msg,
                    IntPtr wParam,
                    IntPtr lParam);

            [DllImport(
                "user32.dll",
                EntryPoint = "DefWindowProcW")]
            internal static extern IntPtr
                DefWindowProc(
                    IntPtr hWnd,
                    uint msg,
                    IntPtr wParam,
                    IntPtr lParam);

            // Reserved no-op compatibility shims. The final
            // implementation deliberately does not mutate the Unity
            // window class; Unity's own cursor state is neutralized via
            // Cursor.SetCursor(null, ...) instead.
            internal static IntPtr SetClassLongPtr(
                IntPtr hWnd,
                int nIndex,
                IntPtr value)
            {
                return IntPtr.Zero;
            }

            internal static IntPtr GetClassLongPtr(
                IntPtr hWnd,
                int nIndex)
            {
                return IntPtr.Zero;
            }

            internal static IntPtr SetWindowLongPtr(
                IntPtr hWnd,
                int nIndex,
                IntPtr value)
            {
                if (IntPtr.Size == 8)
                {
                    return SetWindowLongPtr64(
                        hWnd,
                        nIndex,
                        value);
                }

                return new IntPtr(
                    SetWindowLong32(
                        hWnd,
                        nIndex,
                        value.ToInt32()));
            }

            internal static IntPtr GetWindowLongPtr(
                IntPtr hWnd,
                int nIndex)
            {
                if (IntPtr.Size == 8)
                {
                    return GetWindowLongPtr64(
                        hWnd,
                        nIndex);
                }

                return new IntPtr(
                    GetWindowLong32(
                        hWnd,
                        nIndex));
            }
        }
    }

    [HarmonyPatch(typeof(ZCursor), "SetVisible")]
    internal static class ZCursorSetVisiblePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            NativeCursorController
                .OnUnityCursorVisibilityApplied();
        }
    }
}
