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
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.shadows";
        public const string Name = "Wire's Shadows";
        public const string Version = "0.2.0";

        private const string BundleResource = "wiresshadows.bundle"; // the shaders, built into the DLL

        internal static ManualLogSource Log;
        // Benchmark temporarily turns the cached path off; normal play always uses it.
        internal static bool CachedShadows = true;
        // Moving shadows redrawn less often the fainter the light is where the moving things are
        // (ShadowCache.FaintEvery).
        internal const bool FaintCut = true;
        internal const int Slots = 24;
        internal const int Resolution = 1024;
        internal const int FarSlots = 40;
        internal const int UltraSlots = 24;
        internal const float UltraDistance = 150f;
        internal const int FarMovingLights = 8;

        internal const bool MovingLightShadows = true;

        internal const float Distance = 60f;
        internal const bool MovingCasters = true;
        internal const bool ParticleShadows = true; // effects' particles cast moving shadows
        internal static ConfigEntry<bool> Benchmark;
        internal static ConfigEntry<bool> QuickBenchmark;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> TiledLights;
        internal static ConfigEntry<bool> FlickerCulling;
        internal static ConfigEntry<float> FlickerDistance;
        internal static ConfigEntry<bool> TimingLog;
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
                Order = section == "General" && name == "Benchmark" ? 10000 : section == "General" && name == "Quick benchmark" ? 9999 : section == "General" && name == "Enabled" ? 9000 : _order--
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
            TiledLights = Setting("General", "Tiled lights", false,
                "Experimental. Every point light in one pass instead of one pass each. Off: the game's way, one at a time.");
            FlickerCulling = Setting("General", "Flicker culling", true,
                "Skip brightness flicker for distant and completely off-screen lights; preserve TTL/fade/accessibility.");
            FlickerDistance = Setting("General", "Flicker distance", 15f,
                new ConfigDescription("Maximum source-to-camera distance (metres) for animated brightness flicker. Complete light influence must also intersect camera view.",
                    new AcceptableValueRange<float>(0f, 250f)));
            TimingLog = Setting("General", "Timing log", false,
                "Every 5 seconds, what the mod's own work costs per frame (average and worst), in the log (BepInEx/LogOutput.log).");
            DebugLog = Setting("General", "Debug log", false,
                "Writes what the mod is doing to the log (BepInEx/LogOutput.log). On by itself while a benchmark runs.");
            QuickBenchmark = Setting("General", "Quick benchmark", false,
                "The same against vanilla in about 10 seconds, the mod and vanilla only. For small changes. Resets itself.");
            Benchmark.SettingChanged += (_, __) => { if (Benchmark.Value && GetComponent<Bench>() == null) gameObject.AddComponent<Bench>(); };
            QuickBenchmark.SettingChanged += (_, __) => { if (QuickBenchmark.Value && GetComponent<Bench>() == null) gameObject.AddComponent<Bench>(); };
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
                    else if (s.name == "Hidden/WireTiledLights") ShadowCache.TiledShader = s;                }
                foreach (ComputeShader cs in bundle.LoadAllAssets<ComputeShader>())
                    if (cs.name == "WireTiledLights") ShadowCache.TiledCompute = cs;
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


            gameObject.AddComponent<ShadowCache>();
            UseShader.SettingChanged += (_, __) => { Apply(); AmbientOcclusion.Apply(); };
            Apply();
        }

        // The benchmark's phase with the game's own lighting shader and the rest of the mod running.
        internal static bool BenchGameLighting;
        private static Plugin s_instance;
        internal static void ReApply() => s_instance?.Apply();

        private void Apply()
        {
            s_instance = this;
            if (UseShader.Value && !BenchGameLighting)
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

