using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Valheim.SettingsGui;

namespace WiresNativeCursorSupport
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.nativecursorsupport";
        public const string Name = "Wire's Native Cursor";
        public const string Version = "0.4.2";

        internal const string PrefKey = "WireNativeCursorSupport";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static bool RuntimeEnabled { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Plain PlayerPrefs: PlatformPrefs can reach Steam before it's up during BepInEx's Awake.
            RuntimeEnabled = PlayerPrefs.GetInt(PrefKey, 1) == 1;

            _harmony = new Harmony(Guid);
            foreach (System.Type t in typeof(CursorEvents).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
            {
                try { _harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception ex) { Logger.LogWarning("Cursor change not hooked (" + t.Name + "): " + ex.Message); }
            }

            try
            {
                RequireMethod(typeof(AccessibilitySettings), nameof(AccessibilitySettings.Initialize));
                RequireMethod(typeof(AccessibilitySettings), nameof(AccessibilitySettings.OnOkAsync));
                RequireMethod(typeof(AccessibilitySettings), nameof(AccessibilitySettings.OnBack));
                RequireMethod(typeof(AccessibilitySettings), nameof(AccessibilitySettings.OnTabOpen));
                if (AccessTools.Field(typeof(AccessibilitySettings), "m_toggleBlockToggle") == null)
                    throw new MissingFieldException(typeof(AccessibilitySettings).FullName, "m_toggleBlockToggle");

                _harmony.CreateClassProcessor(typeof(AccessibilityInitializePatch)).Patch();
                _harmony.CreateClassProcessor(typeof(AccessibilityOkPatch)).Patch();
                _harmony.CreateClassProcessor(typeof(AccessibilityBackPatch)).Patch();
                _harmony.CreateClassProcessor(typeof(AccessibilityTabOpenPatch)).Patch();
            }
            catch (Exception ex)
            {
                // Only the setting's row is missing then; the cursor works without it.
                Logger.LogWarning("No row in the Accessibility settings: " + ex.Message);
            }

            SystemCursor.Apply(RuntimeEnabled);
        }

        private void OnDestroy()
        {
            SystemCursor.Shutdown();
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }

        internal static void SetRuntimeEnabled(bool enabled)
        {
            if (enabled == RuntimeEnabled) return;
            RuntimeEnabled = enabled;
            SystemCursor.Apply(enabled);
        }

        private static void RequireMethod(Type type, string name)
        {
            if (AccessTools.DeclaredMethod(type, name) == null)
                throw new MissingMethodException(type.FullName, name);
        }
    }
}
