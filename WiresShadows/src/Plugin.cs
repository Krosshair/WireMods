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
        public const string Version = "0.5.2";

        private const string BundleResource = "wiresshadows.bundle"; // the shaders, built into the DLL

        internal static ManualLogSource Log;
        // Benchmark temporarily turns the cached path off; normal play always uses it.
        internal static bool CachedShadows = true;
        internal const int Slots = 24;
        internal const int Resolution = 1024;
        internal const int FarSlots = 40;
        internal const int UltraSlots = 24;
        internal const float UltraDistance = 150f;
        internal const int FarMovingLights = 8;

        internal const float Distance = 60f;
        internal static ConfigEntry<bool> Benchmark;
        internal static ConfigEntry<bool> QuickBenchmark;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> TiledLights;
        internal static ConfigEntry<bool> LitParticles;
        internal static ConfigEntry<bool> ModAO;
        internal static ConfigEntry<bool> OcclusionCulling;
        internal static ConfigEntry<bool> BurleyDiffuse;
        internal static ConfigEntry<bool> SmoothShading;
        internal static ConfigEntry<bool> WeatherFacing;
        internal static ConfigEntry<bool> ContactShadowsOn;
        internal enum ShadowMode { All, Performance, Off }
        internal static ConfigEntry<ShadowMode> Shadows;
        private static readonly int BurleyId = Shader.PropertyToID("_WireBurley");
        
        internal static ConfigEntry<bool> FlickerCulling;
        internal static ConfigEntry<float> FlickerDistance;
        internal static ConfigEntry<bool> TimingLog;
        internal static bool BenchRunning; // the log is on while the benchmark runs
        internal static bool Debug => BenchRunning || (DebugLog != null && DebugLog.Value);
        internal static void Note(string text) { if (Debug) Log.LogInfo(text); }
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

        private const string General = "1 - General", Look = "2 - Look", Speed = "3 - Performance", Testing = "4 - Testing";

        // In-game menu: listed in the order bound here, the sections by their number.
        private ConfigEntry<T> Setting<T>(string section, string name, T value, ConfigDescription description, string was = null)
        {
            var a = new ConfigurationManagerAttributes { Order = _order-- };
            ConfigEntry<T> entry = Config.Bind(section, name, value,
                new ConfigDescription(description.Description, description.AcceptableValues, a));
            KeepOldValue(entry, "General", name);
            if (was != null) { KeepOldValue(entry, "General", was); KeepOldValue(entry, section, was); }
            return entry;
        }

        // Up to 0.3.6 every setting was under [General], and some had other names. A value someone
        // set there is carried over to the setting's new place once, then the old line is dropped.
        // Only at startup.
        private static readonly System.Reflection.PropertyInfo Orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries");
        private void KeepOldValue(ConfigEntryBase entry, string section, string name)
        {
            if (!(Orphans?.GetValue(Config) is Dictionary<ConfigDefinition, string> orphans)) return;
            var old = new ConfigDefinition(section, name);
            if (!orphans.TryGetValue(old, out string value)) return;
            entry.SetSerializedValue(value);
            orphans.Remove(old);
            Config.Save();
        }

        private static void ApplySmooth(bool smooth)
        {
            if (smooth) Shader.EnableKeyword("WIRE_SMOOTH_ALL"); else Shader.DisableKeyword("WIRE_SMOOTH_ALL");
        }
        private void Awake()
        {
            Log = Logger;
            UseShader = Setting(General, "Enabled", true,
                "Master switch for Wire's Shadows. Off restores Valheim's lighting and shadows.");
            Shadows = Setting(General, "Shadows", ShadowMode.All,
                "Which placed lights cast shadows. All: every light. Performance: only the lights the game itself gives shadows (fires, braziers, hearths, lanterns); sconces, standing torches and the like get none. Off: no shadows from placed lights. Passing effects (spells, a boss's fire) get none from the mod in any mode. Valheim's Point light shadows setting isn't used for placed lights.");
            Shadows.SettingChanged += (_, __) => ShadowCache.ShadowsSettingChanged();
            // All rendering quality and capacity choices now use the working
            // built-in defaults. They are not user-facing configuration toggles.
            // Existing shadow fallbacks handle unsupported GPU formats.

            BurleyDiffuse = Setting(Look, "Enhanced shading", true,
                new ConfigDescription("More natural shading from every light (Burley's diffuse, the one in Unity's Standard shader): rough surfaces such as ground, rock, wood and foliage brighten a little when the light is behind you, smooth ones darken a little at their edges. Looks softer and more believable. Off: the game's look. Needs Enabled on."),
                "Diffuse lighting");
            Shader.SetGlobalFloat(BurleyId, BurleyDiffuse.Value ? 1f : 0f);
            BurleyDiffuse.SettingChanged += (_, __) => Shader.SetGlobalFloat(BurleyId, BurleyDiffuse.Value ? 1f : 0f);
            SmoothShading = Setting(Look, "Smooth shading", false,
                "Light fades smoothly from lit to unlit instead of in the game's toon steps, for the sun, moon and every light. Just as bright overall. Off: the game's steps. Needs Enabled on.");
            ApplySmooth(SmoothShading.Value);
            SmoothShading.SettingChanged += (_, __) => ApplySmooth(SmoothShading.Value);
            ModAO = Setting(Look, "Improved AO", true,
                new ConfigDescription("Improves the game's ambient occlusion (the shade in corners and creases): light bouncing between surfaces is taken into account, so bright surfaces darken less and coloured ones keep their colour instead of going grey. Looks warmer and less dirty. Steady from frame to frame. Off: Valheim's. Needs Enabled on."),
                "Ambient occlusion");
            ModAO.SettingChanged += (_, __) => { AmbientOcclusion.Apply(); };
            WeatherFacing = Setting(Look, "Weather catches light", true,
                "Rain and snow are lit by which way each flake faces the light, so flakes turned toward a fire or the moon catch it and those turned away stay dark, instead of flat white specks. Off: the game's look. Needs Enabled on.");
            WeatherFacing.SettingChanged += (_, __) => ShadowCache.FacingSettingChanged();
            ContactShadowsOn = Setting(Look, "Contact shadows", true,
                "Short sun and moon shadows the game's shadow map is too coarse for: stones on the ground, plank edges, the foot of a post, traced on screen toward the sun (Bend Studio's screen-space shadows). Only direct sunlight and moonlight is darkened. Off: the game's sun and moon shadows alone. Needs Enabled on.");
            ContactShadowsOn.SettingChanged += (_, __) => ContactShadows.Apply();

            LitParticles = Setting(Speed, "Weather lit in one pass", true,
                "Rain, snow, mist and wind dust lit by every nearby point light in one draw, instead of the game drawing them again for each light (one more draw call per light per weather effect). Same look. Off: the game's way.");
            LitParticles.SettingChanged += (_, __) => ShadowCache.LitSettingChanged();
            FlickerCulling = Setting(Speed, "Flicker culling", true,
                "Lights far away or fully off screen stop flickering, where nobody sees it. Lights that fade out still do.");
            FlickerDistance = Setting(Speed, "Flicker distance", 15f,
                new ConfigDescription("How far away (metres) a light still flickers. It also has to light something on screen.",
                    new AcceptableValueRange<float>(0f, 250f)));
            TiledLights = Setting(Speed, "Lights in one pass", true,
                "Every point light in one pass instead of one pass each. Off: the game's way, one at a time.");
            OcclusionCulling = Setting(Speed, "Skip hidden pieces", true,
                "Build pieces fully hidden behind others (the rooms upstairs from the basement, the inside of a house from outside) aren't drawn; their shadows stay. Off: the game's way.");
            OcclusionCulling.SettingChanged += (_, __) => { if (!OcclusionCulling.Value) Occlusion.Disable(); };

            Benchmark = Setting(Testing, "Benchmark", false,
                "Measures the mod against vanilla where you stand (about a minute) and shows the frame times. Resets itself.");
            QuickBenchmark = Setting(Testing, "Quick benchmark", false,
                "The same against vanilla in about 10 seconds, the mod and vanilla only. For small changes. Resets itself.");
            TimingLog = Setting(Testing, "Timing log", false,
                "Every 5 seconds, what the mod's work costs per frame (average and worst), in the log (BepInEx/LogOutput.log).");
            DebugLog = Setting(Testing, "Debug log", false,
                "Writes what the mod is doing to the log (BepInEx/LogOutput.log). On by itself while a benchmark runs.");
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
                    else if (s.name == "Hidden/WireTiledLights") ShadowCache.TiledShader = s;
                    else if (s.name == "Wire/LitParticles") ShadowCache.LitParticlesShader = s;
                    else if (s.name == "Hidden/WireOcclusionMask") Occlusion.MaskShader = s;
                    else if (s.name == "Hidden/WireContactApply") ContactShadows.ApplyShader = s;
                }
                foreach (ComputeShader cs in bundle.LoadAllAssets<ComputeShader>())
                    if (cs.name == "WireTiledLights") ShadowCache.TiledCompute = cs;
                    else if (cs.name == "WireOcclusion") Occlusion.Compute = cs;
                    else if (cs.name == "WireContactShadows") ContactShadows.Compute = cs;
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
            UseShader.SettingChanged += (_, __) =>
            {
                Apply(); AmbientOcclusion.Apply(); ContactShadows.Apply(); if (!UseShader.Value) Occlusion.Disable();
                // the weather's materials back to the game's shader and values, or to ours (the benchmark's
                // vanilla phase turns Enabled off too)
                ShadowCache.LitSettingChanged(); ShadowCache.FacingSettingChanged();
            };
            Apply();
            ContactShadows.Apply();
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

