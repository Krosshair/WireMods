using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace WiresNativeCursorSupport
{
    [BepInPlugin(Guid, Name, Version)]
    [DefaultExecutionOrder(32000)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.nativecursorsupport";
        public const string Name = "Wire's Native Cursor";
        public const string Version = "0.3.2";

        internal const string PrefKey =
            "WireNativeCursorSupport";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static bool RuntimeEnabled { get; private set; }

        private Harmony _harmony;
        private Coroutine _endOfFrameLoop;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Do not use PlatformPrefs during BepInEx Awake.
            // On current Valheim it may call SteamUtils before Steamworks
            // has been initialized, aborting the entire plugin before any
            // cursor hooks are installed. This setting is mod-local, so
            // plain Unity PlayerPrefs is the correct startup-safe store.
            RuntimeEnabled =
                PlayerPrefs.GetInt(PrefKey, 1) == 1;

            try
            {
                ValidateCurrentGameApi();

                NativeCursorController.Initialize();

                _harmony = new Harmony(Guid);

                InstallCorePatches();
                InstallAccessibilityPatches();

                ZCursor.IsRequestedChanged +=
                    NativeCursorController.OnCursorContextChanged;

                ZCursor.IsVisibleChanged +=
                    NativeCursorController.OnCursorContextChanged;

                Application.focusChanged +=
                    NativeCursorController.OnApplicationFocusChanged;

                Application.onBeforeRender +=
                    NativeCursorController.OnBeforeRender;

                NativeCursorController.SetEnabled(RuntimeEnabled);

                _endOfFrameLoop =
                    StartCoroutine(
                        EndOfFrameCursorLoop());

                Logger.LogInfo(
                    $"{Name} {Version} loaded. " +
                    $"Enabled={RuntimeEnabled}. " +
                    "Valheim remains the sole owner of cursor " +
                    "visibility and lock state; this mod selects the " +
                    "native/system cursor for the active desktop platform.");
            }
            catch (Exception ex)
            {
                _harmony?.UnpatchSelf();
                _harmony = null;

                NativeCursorController.Shutdown();

                Logger.LogError(
                    $"{Name} {Version} could not initialize safely. " +
                    "No cursor override will be left active. " +
                    ex);
            }
        }

        private void LateUpdate()
        {
            NativeCursorController.LateTick();
        }

        private IEnumerator EndOfFrameCursorLoop()
        {
            WaitForEndOfFrame wait =
                new WaitForEndOfFrame();

            while (true)
            {
                yield return wait;
                NativeCursorController.EndOfFrameTick();
            }
        }

        private void OnDestroy()
        {
            if (_endOfFrameLoop != null)
            {
                StopCoroutine(_endOfFrameLoop);
                _endOfFrameLoop = null;
            }

            ZCursor.IsRequestedChanged -=
                NativeCursorController.OnCursorContextChanged;

            ZCursor.IsVisibleChanged -=
                NativeCursorController.OnCursorContextChanged;

            Application.focusChanged -=
                NativeCursorController.OnApplicationFocusChanged;

            Application.onBeforeRender -=
                NativeCursorController.OnBeforeRender;

            NativeCursorController.Shutdown();

            _harmony?.UnpatchSelf();
            _harmony = null;

            Instance = null;
        }

        internal static void SetRuntimeEnabled(bool enabled)
        {
            RuntimeEnabled = enabled;
            NativeCursorController.SetEnabled(enabled);
        }

        private static void ValidateCurrentGameApi()
        {
            RequireMethod(
                typeof(ZCursor),
                "SetVisible",
                typeof(bool));

            RequireMethod(
                typeof(AccessibilitySettings),
                nameof(AccessibilitySettings.Initialize));

            RequireMethod(
                typeof(AccessibilitySettings),
                nameof(AccessibilitySettings.OnOkAsync));

            RequireMethod(
                typeof(AccessibilitySettings),
                nameof(AccessibilitySettings.OnBack));

            RequireMethod(
                typeof(AccessibilitySettings),
                nameof(AccessibilitySettings.OnTabOpen));

            RequireField(
                typeof(AccessibilitySettings),
                "m_toggleBlockToggle");

            RequireField(
                typeof(InventoryGui),
                "m_craftTimer");

            RequireField(
                typeof(FejdStartup),
                "m_loading");

            RequireField(
                typeof(FejdStartup),
                "m_pleaseWait");
        }

        private static void InstallCorePatches()
        {
            try
            {
                Instance._harmony
                    .CreateClassProcessor(
                        typeof(ZCursorSetVisiblePatch))
                    .Patch();

                Log?.LogInfo(
                    "Installed ZCursor visibility-reset compensation.");
            }
            catch (Exception ex)
            {
                // This is a supplementary same-call correction only.
                // The native window hook + LateUpdate + before-render +
                // end-of-frame reconciliation remain fully functional
                // if a future game build makes this private method
                // unpatchable.
                Log?.LogWarning(
                    "Could not patch ZCursor.SetVisible; continuing with " +
                    "the native/window/frame reconciliation paths. " +
                    ex.Message);
            }
        }

        private static void InstallAccessibilityPatches()
        {
            Instance._harmony
                .CreateClassProcessor(
                    typeof(AccessibilityInitializePatch))
                .Patch();

            Instance._harmony
                .CreateClassProcessor(
                    typeof(AccessibilityOkPatch))
                .Patch();

            Instance._harmony
                .CreateClassProcessor(
                    typeof(AccessibilityBackPatch))
                .Patch();

            Instance._harmony
                .CreateClassProcessor(
                    typeof(AccessibilityTabOpenPatch))
                .Patch();

            Log?.LogInfo(
                "Validated and installed Accessibility settings integration.");
        }

        private static void RequireMethod(
            Type type,
            string name,
            params Type[] args)
        {
            var method =
                args != null &&
                args.Length > 0
                    ? AccessTools.DeclaredMethod(
                        type,
                        name,
                        args)
                    : AccessTools.DeclaredMethod(
                        type,
                        name);

            if (method == null)
            {
                throw new MissingMethodException(
                    type.FullName,
                    name);
            }
        }

        private static void RequireField(
            Type type,
            string name)
        {
            if (AccessTools.Field(type, name) == null)
            {
                throw new MissingFieldException(
                    type.FullName,
                    name);
            }
        }
    }
}
