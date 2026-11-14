using System.Collections.Generic;
using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Swaps Valheim's deferred lighting shader for a rebuilt copy that reads
    // cached shadow maps (ShadowCache) for the nearest point lights.
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(VpoCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.shadows";
        public const string Name = "Wire's Shadows";
        public const string Version = "0.1.7";

        private const string BundleResource = "wiresshadows.bundle"; // the shaders, built into the DLL

        internal static ManualLogSource Log;
        // Benchmark temporarily turns the cached path off; normal play always uses it.
        internal static bool CachedShadows = true;
        // Moving shadows redrawn less often the fainter the light is where the moving things are
        // (ShadowCache.FaintEvery). The benchmark compares it with every frame.
        internal static bool FaintCut = true;
        internal const int Slots = 24;
        internal const int Resolution = 1024;
        internal const int FarSlots = 40;
        internal const int UltraSlots = 24;
        internal const float UltraDistance = 150f;
        internal const int FarMovingLights = 8;
        internal const bool GhostShadows = true;
        internal const bool FollowGameShadows = true;

        internal const bool MovingLightShadows = true;

        internal const int FarResolution = 256;
        internal const float Distance = 60f;
        internal const bool MovingCasters = true;
        internal static ConfigEntry<bool> Benchmark;
        internal static ConfigEntry<bool> DebugLog;
        internal static bool BenchRunning; // the log is on while the benchmark runs
        internal static bool Debug => BenchRunning || (DebugLog != null && DebugLog.Value);
        internal static void Note(string text) { if (Debug) Log.LogInfo(text); }
        internal const bool DirectMovingDraw = true;
        internal const bool NativeDepthPcf = true;
        internal const bool NativeMovingDepthPcf = true;
        internal static ConfigEntry<bool> UseShader;

        private Shader _original;
        private BuiltinShaderMode _originalMode;
        private Shader _shader;
        private Harmony _harmony;

        // Shudnal/BepInEx ConfigurationManager identifies these public
        // attributes by NAME via reflection. No dependency on its DLL.
        public sealed class ConfigurationManagerAttributes
        {
            public int? Order { get; set; }
        }

        private int _order = 1000;

        private ConfigEntry<T> Setting<T>(string section, string name, T value, string help) =>
            Setting(section, name, value, new ConfigDescription(help));

        // In-game menu: listed in the order bound here, Benchmark first.
        private ConfigEntry<T> Setting<T>(string section, string name, T value, ConfigDescription description)
        {
            var a = new ConfigurationManagerAttributes
            {
                Order = section == "General" && name == "Benchmark" ? 10000 : section == "General" && name == "Enabled" ? 9000 : _order--
            };
            return Config.Bind(section, name, value,
                new ConfigDescription(description.Description, description.AcceptableValues, a));
        }
        private void Awake()
        {
            Log = Logger;
            UseShader = Setting("General", "Enabled", true,
                "Master switch for Wire's Shadows. Off restores the game's own lighting and shadows.");
            // All rendering quality and capacity choices now use the working
            // built-in defaults. They are not user-facing configuration toggles.
            // Existing shadow fallbacks handle unsupported GPU formats.

            Benchmark = Setting("General", "Benchmark", false,
                "Measures the mod against vanilla where you stand (about a minute) and shows the frame times. Resets itself.");
            DebugLog = Setting("General", "Debug log", false,
                "Writes what the mod is doing to the log (BepInEx/LogOutput.log). On by itself while a benchmark runs.");
            Benchmark.SettingChanged += (_, __) => { if (Benchmark.Value && GetComponent<Bench>() == null) gameObject.AddComponent<Bench>(); };
            AssetBundle bundle = null;
            using (Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream(BundleResource))
                if (stream != null)
                {
                    var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                }
            if (bundle != null)
            {
                foreach (Shader s in bundle.LoadAllAssets<Shader>())
                {
                    if (s.name == "Hidden/WireToonDeferredShading") _shader = s;
                    else if (s.name == "Hidden/WireShadowDistance") ShadowCache.DistanceShader = s;
                    else if (s.name == "Hidden/WireShadowFrontBack") ShadowCache.FrontBackShader = s;
                    else if (s.name == "Hidden/WireShadowCombine") ShadowCache.CombineShader = s;
                    else if (s.name == "Hidden/WireShadowNativeDepthCopy") ShadowCache.NativeCopyShader = s;
                    else if (s.name == "Hidden/WireAOApply") AmbientOcclusion.BounceShader = s;
                }
            }
            if (_shader == null || !_shader.isSupported || ShadowCache.DistanceShader == null ||
                ShadowCache.CombineShader == null)
            {
                Logger.LogError("Shaders not found or not supported. The game's own lighting stays.");
                return;
            }

            ShadowCache.LightingShader = _shader;
            _original = GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredShading);
            _originalMode = GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredShading);

            _harmony = new Harmony(Guid);
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception e) { Logger.LogError("Couldn't patch " + type.Name + ": " + e.Message); }
            }

            // One-time transpiler; check that the existing vanilla flicker
            // method is actually patched. No per-frame monitoring.
            var flickerInfo = Harmony.GetPatchInfo(
                AccessTools.Method(typeof(LightFlicker), nameof(LightFlicker.CustomUpdate)));
            bool positionalJitterDisabled = false;
            if (flickerInfo != null)
                foreach (var patch in flickerInfo.Transpilers)
                    if (patch.owner == Guid)
                        positionalJitterDisabled = true;
            if (positionalJitterDisabled)
                Note("[firelight] Position jitter OFF for ALL LightFlicker sources; vanilla brightness flicker unmodified.");
            else
                Logger.LogError("[firelight] Could not verify LightFlicker positional-jitter patch; some fire shadows may still wobble.");

            // VPO's flicker culling freezes shadow movement for lights outside the view.
            VpoCompat.Remove(_harmony, Logger, AccessTools.Method(typeof(LightFlicker), "Awake"), "ValheimPerformanceOptimizations.Patches.VPOLightFlicker");

            gameObject.AddComponent<ShadowCache>();
            UseShader.SettingChanged += (_, __) => { Apply(); AmbientOcclusion.Apply(); };
            Apply();
        }

        private void Apply()
        {
            if (UseShader.Value)
            {
                GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseCustom);
                GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, _shader);
            }
            else
            {
                GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, _original);
                GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, _originalMode);
            }
            Note("Lighting shader: " + (UseShader.Value ? "rebuilt" : "game's own"));
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            if (_shader == null)
                return;
            GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, _original);
            GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, _originalMode);
        }
    }
}
