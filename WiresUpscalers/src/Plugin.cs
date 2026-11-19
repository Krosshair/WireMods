using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresUpscalers
{
    internal enum Upscaler { None = 0, DLSS = 1, FSR3 = 2, FSR4 = 3, XeSS = 4 }

    [BepInPlugin("wire.valheim.upscalers", "Wire's Upscalers", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ConfigEntry<int> UpscalerConfig, DlssModel;
        internal static ConfigEntry<float> Sharpness;
        internal static ConfigEntry<bool> ShowAll;
        private Harmony _harmony;

        internal static Upscaler Current => (Upscaler)Mathf.Clamp(UpscalerConfig.Value, 0, 4);

        private void Awake()
        {
            Instance = this;
            UpscalerConfig = Config.Bind("Upscaling", "Upscaler", 0,
                "Set in the game's graphics settings (the Smooth/Pixelated row). 0 none, 1 NVIDIA DLSS, 2 AMD FSR 3, 3 AMD FSR 4, 4 Intel XeSS.");
            DlssModel = Config.Bind("Upscaling", "DLSS model", 13,
                "Set in the game's graphics settings. NVIDIA render preset: 13 Transformer 2 (DLSS 4.5), 11 Transformer (DLSS 4), 5 CNN (DLSS 3).");
            Sharpness = Config.Bind("Upscaling", "FSR sharpness", 0f,
                new ConfigDescription("Set in the game's graphics settings. AMD's own sharpening, 0 off.", new AcceptableValueRange<float>(0f, 1f)));
            ShowAll = Config.Bind("Advanced", "Show every upscaler", false,
                new ConfigDescription("Lists every upscaler and its settings even where this PC can't run them (for screenshots). One that can't run shows the game's own image.",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            InstallNative();
            _harmony = new Harmony("wire.valheim.upscalers");
            _harmony.PatchAll(typeof(Plugin).Assembly);
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        internal static void Log(string m) => Instance?.Logger.LogInfo(m);
        internal static void Warn(string m) => Instance?.Logger.LogWarning(m);

        // Unity only hands its graphics device to native plugins it loads from its own plugin folder,
        // so the one native file goes there (copied from this mod, updated when the mod's copy changes).
        // The vendors' DLLs stay in this mod's folder.
        private void InstallNative()
        {
            string modDir = Path.GetDirectoryName(Info.Location);
            string source = Path.Combine(modDir, Native.Dll + ".dll");
            string dir = Path.Combine(Application.dataPath, "Plugins", "x86_64");
            string target = Path.Combine(dir, Native.Dll + ".dll");
            try
            {
                if (File.Exists(source) && (!File.Exists(target) || Hash(source) != Hash(target)))
                {
                    Directory.CreateDirectory(dir);
                    string pending = target + ".new";
                    File.Copy(source, pending, true);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(pending, target);
                    Logger.LogInfo("Native part installed to " + target);
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("Native part not installed (" + e.Message + "); upscalers stay off.");
            }
            Native.Start(modDir);
        }

        private static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(s));
        }

        // The game's camera, in the world and in the main menu.
        internal static void Attach(Camera cam)
        {
            if (cam == null || cam.GetComponent<UpscaleRenderer>() != null) return;
            cam.gameObject.AddComponent<UpscaleRenderer>();
        }

        [HarmonyPatch(typeof(GameCamera), "Awake")]
        private static class GameCam
        {
            private static void Postfix(GameCamera __instance) => Attach(__instance != null ? __instance.GetComponent<Camera>() : null);
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static class MenuCam
        {
            private static void Postfix() => Attach(Camera.main);
        }
    }

    // BepInEx ConfigurationManager's "advanced" flag
    internal sealed class ConfigurationManagerAttributes { public bool? IsAdvanced; }
}
