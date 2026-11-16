
using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresDLSS
{
    internal enum Mode
    {
        Off=0, DLAA=1, Quality=2, Balanced=3, Performance=4
    }

    [BepInPlugin("wire.valheim.dlss", "Wire's DLSS", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        private Harmony patches;
        internal static ConfigEntry<int> ModeConfig;
        internal static Mode Current => (Mode)Mathf.Clamp(ModeConfig == null ? 0 : ModeConfig.Value, 0, 4);

        private void Awake()
        {
            Instance = this;
            ModeConfig = Config.Bind("Graphics", "DLSS Mode", 1,
                "0 Off, 1 DLAA, 2 DLSS Quality, 3 Balanced, 4 Performance. No sharpening. FG is not enabled.");
            NativeBootstrap.Ensure(Info.Location, Logger);
            patches = new Harmony("wire.valheim.dlss");
            patches.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("DLAA / DLSS module loaded. No DXGI proxy, sharpening or frame generation. Requires NVIDIA NGX runtime. Mode " + Current);
        }

        private void OnDestroy()
        {
            patches?.UnpatchSelf();
            Instance = null;
        }

        internal static void Report(string message) => Instance?.Logger.LogInfo(message);
        internal static void Warn(string message) => Instance?.Logger.LogWarning(message);
        internal static void Error(string message) => Instance?.Logger.LogError(message);

        internal static void AttachCamera(Camera cam, string reason)
        {
            if (cam == null || !cam.isActiveAndEnabled || cam.cullingMask == 0 ||
                cam.GetComponent<DLSSRenderer>() != null) return;
            cam.gameObject.AddComponent<DLSSRenderer>();
            Report("Attached DLSS render effect to " + cam.name + " (" + reason + ")");
        }

        [HarmonyPatch(typeof(GameCamera), "Awake")]
        private static class GameCam
        {
            private static void Postfix(GameCamera __instance)
            {
                if (__instance != null)
                    AttachCamera(__instance.GetComponent<Camera>(), "game camera");
            }
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static class MenuCam
        {
            private static void Postfix()
            {
                var cam = Camera.main;
                if (cam != null) AttachCamera(cam, "main menu");
                // Menu background can have a separate camera without the
                // MainCamera tag; inspect these cameras once per menu start.
                if (cam == null)
                {
                    foreach (var candidate in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                        if (candidate != null && candidate.isActiveAndEnabled &&
                            candidate.targetTexture == null && candidate.cullingMask != 0)
                        { AttachCamera(candidate, "menu fallback"); break; }
                }
            }
        }
    }
}
