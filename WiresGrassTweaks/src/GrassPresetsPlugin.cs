using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Clutter = ClutterSystem.Clutter;

namespace GrassPresets
{
    // Clean source for Wire's Grass Tweaks 0.2.3. The original source was
    // lost; this was rebuilt from the shipped DLL with identical behavior
    // (same GUID, settings, defaults and presets). Unused decompiled
    // leftovers were removed.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class GrassPresetsPlugin : BaseUnityPlugin
    {
        public const string Guid = "kajetan.valheim.grasspresets";
        public const string Name = "Wire's Grass Tweaks";
        public const string Version = "0.2.3";

        private const string Default = "Default", Enhanced = "Enhanced", Ultra = "Ultra", Custom = "Custom";

        internal static GrassPresetsPlugin Instance;

        private struct RendererDefaults
        {
            public bool UseLod;
            public bool UseXZ;
            public float LodMin;
            public float LodMax;
            public ShadowCastingMode Shadows;
        }

        private sealed class ConfigurationManagerAttributes
        {
            public Action<ConfigEntryBase> CustomDrawer;
        }

        private Harmony _harmony;
        private bool _suppressEvents;

        private ConfigEntry<string> _preset;
        private ConfigEntry<bool> _useLod, _useXZ;
        private ConfigEntry<float> _lodMin, _lodMax, _scaleMin, _scaleMax, _amount, _distance, _patch, _pushFade;
        private ConfigEntry<ShadowCastingMode> _shadows;

        // Vanilla values captured from the live ClutterSystem.
        private bool _haveClutterDefaults;
        private float _defaultAmount = 1f, _defaultDistance = 40f, _defaultPatch = 8f, _defaultPushFade = 0.05f;
        private readonly Dictionary<Clutter, Vector2> _clutterScales = new Dictionary<Clutter, Vector2>();
        private readonly Dictionary<int, RendererDefaults> _rendererDefaults = new Dictionary<int, RendererDefaults>();

        private void Awake()
        {
            Instance = this;
            _preset = Config.Bind("General", "Preset", Enhanced, new ConfigDescription(
                "Choose Default, Enhanced, or Ultra. Editing any setting below automatically changes the state to Custom.",
                new AcceptableValueList<string>(Default, Enhanced, Ultra, Custom),
                new ConfigurationManagerAttributes { CustomDrawer = DrawPresetSelector }));
            _useLod = Config.Bind("Grass Settings", "UseLod", true, "Use LOD.");
            _useXZ = Config.Bind("Grass Settings", "UseXZLodDistance", true, "Use XZ LOD distance.");
            _lodMin = Config.Bind("Grass Settings", "LodMinDistanceMult", 1f, "Minimum LOD distance multiplier.");
            _lodMax = Config.Bind("Grass Settings", "LodMaxDistanceMult", 1f, "Maximum LOD distance multiplier.");
            _scaleMin = Config.Bind("Grass Settings", "ScaleMinMult", 1f, "Clutter minimum scale multiplier.");
            _scaleMax = Config.Bind("Grass Settings", "ScaleMaxMult", 1f, "Clutter maximum scale multiplier.");
            _amount = Config.Bind("Grass Settings", "AmountMult", 0.5f, "Clutter amount multiplier.");
            _distance = Config.Bind("Grass Settings", "ClutterDistance", 200f, "Clutter draw distance.");
            _patch = Config.Bind("Grass Settings", "GrassPatchSize", 12f, "Grass patch size.");
            _pushFade = Config.Bind("Grass Settings", "PlayerPushFade", 0.05f, "Player push fade.");
            _shadows = Config.Bind("Grass Settings", "ShadowCastingMode", ShadowCastingMode.On, "Grass/clutter shadow casting.");

            _preset.SettingChanged += OnPresetChanged;
            foreach (ConfigEntryBase entry in new ConfigEntryBase[] {
                _useLod, _useXZ, _lodMin, _lodMax, _scaleMin, _scaleMax,
                _amount, _distance, _patch, _pushFade, _shadows })
                Subscribe(entry);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
        }

        private void Subscribe(ConfigEntryBase entry)
        {
            // Every ConfigEntry<T> exposes SettingChanged; bind it generically.
            var evt = entry.GetType().GetEvent("SettingChanged");
            evt?.AddEventHandler(entry, new EventHandler(OnCustomSettingChanged));
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Instance = null;
        }

        private void DrawPresetSelector(ConfigEntryBase entry)
        {
            GUILayout.BeginHorizontal();
            foreach (string preset in new[] { Default, Enhanced, Ultra })
            {
                GUI.enabled = _preset.Value != preset;
                if (GUILayout.Button(preset)) _preset.Value = preset;
            }
            GUI.enabled = true;
            if (_preset.Value == Custom) GUILayout.Label(Custom);
            GUILayout.EndHorizontal();
        }

        private void OnPresetChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;
            if (_preset.Value != Custom) WritePresetSettings(_preset.Value);
            ApplyAll(rebuild: true);
        }

        private void OnCustomSettingChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) return;
            _suppressEvents = true;
            _preset.Value = Custom;
            _suppressEvents = false;
            ApplyAll(rebuild: true);
        }

        private void WritePresetSettings(string preset)
        {
            _suppressEvents = true;
            _useLod.Value = true;
            _useXZ.Value = true;
            _lodMin.Value = 1f;
            _lodMax.Value = 1f;
            _scaleMin.Value = 1f;
            _scaleMax.Value = 1f;
            _pushFade.Value = _haveClutterDefaults ? _defaultPushFade : 0.05f;
            _shadows.Value = ShadowCastingMode.On;
            if (preset == Default)
            {
                _amount.Value = _haveClutterDefaults ? _defaultAmount : 1f;
                _distance.Value = _haveClutterDefaults ? _defaultDistance : 40f;
                _patch.Value = _haveClutterDefaults ? _defaultPatch : 8f;
            }
            else if (preset == Ultra)
            {
                _amount.Value = 1f;
                _distance.Value = 200f;
                _patch.Value = 12f;
            }
            else // Enhanced
            {
                _amount.Value = 0.5f;
                _distance.Value = 200f;
                _patch.Value = 12f;
            }
            _suppressEvents = false;
            Config.Save();
        }

        internal void OnClutterAwake(ClutterSystem clutter)
        {
            if (clutter == null) return;
            _defaultAmount = clutter.m_amountScale;
            _defaultDistance = clutter.m_distance;
            _defaultPatch = clutter.m_grassPatchSize;
            _defaultPushFade = clutter.m_playerPushFade;
            _haveClutterDefaults = true;
            _clutterScales.Clear();
            foreach (Clutter item in clutter.m_clutter)
                if (item != null) _clutterScales[item] = new Vector2(item.m_scaleMin, item.m_scaleMax);
            if (_preset.Value != Custom) WritePresetSettings(_preset.Value);
            ApplyClutter(clutter, rebuild: false);
        }

        internal void OnRendererEnable(InstanceRenderer renderer)
        {
            if (renderer == null) return;
            int id = renderer.GetInstanceID();
            if (!_rendererDefaults.ContainsKey(id))
                _rendererDefaults[id] = new RendererDefaults
                {
                    UseLod = renderer.m_useLod,
                    UseXZ = renderer.m_useXZLodDistance,
                    LodMin = renderer.m_lodMinDistance,
                    LodMax = renderer.m_lodMaxDistance,
                    Shadows = renderer.m_shadowCasting
                };
            ApplyRenderer(renderer);
        }

        // Only on setting changes, never per frame.
        private void ApplyAll(bool rebuild)
        {
            if (ClutterSystem.instance != null && _haveClutterDefaults)
                ApplyClutter(ClutterSystem.instance, rebuild);
            foreach (InstanceRenderer renderer in Resources.FindObjectsOfTypeAll<InstanceRenderer>())
                OnRendererEnable(renderer);
        }

        private void ApplyClutter(ClutterSystem clutter, bool rebuild)
        {
            if (clutter == null) return;
            clutter.m_amountScale = _amount.Value;
            clutter.m_distance = _distance.Value;
            clutter.m_grassPatchSize = _patch.Value;
            clutter.m_playerPushFade = _pushFade.Value;
            foreach (KeyValuePair<Clutter, Vector2> scale in _clutterScales)
            {
                if (scale.Key == null) continue;
                scale.Key.m_scaleMin = scale.Value.x * _scaleMin.Value;
                scale.Key.m_scaleMax = scale.Value.y * _scaleMax.Value;
            }
            if (rebuild) clutter.ClearAll();
            Logger.LogInfo("Grass state " + _preset.Value + ": amount=" + clutter.m_amountScale +
                ", distance=" + clutter.m_distance + ", patch=" + clutter.m_grassPatchSize);
        }

        private void ApplyRenderer(InstanceRenderer renderer)
        {
            if (!_rendererDefaults.TryGetValue(renderer.GetInstanceID(), out RendererDefaults defaults)) return;
            renderer.m_useLod = _useLod.Value;
            renderer.m_useXZLodDistance = _useXZ.Value;
            renderer.m_lodMinDistance = defaults.LodMin * _lodMin.Value;
            renderer.m_lodMaxDistance = defaults.LodMax * _lodMax.Value;
            renderer.m_shadowCasting = _shadows.Value;
        }

        [HarmonyPatch(typeof(ClutterSystem), "Awake")]
        private static class ClutterAwakePatch
        {
            private static void Postfix(ClutterSystem __instance) => Instance?.OnClutterAwake(__instance);
        }

        [HarmonyPatch(typeof(InstanceRenderer), "OnEnable")]
        private static class RendererEnablePatch
        {
            private static void Postfix(InstanceRenderer __instance) => Instance?.OnRendererEnable(__instance);
        }
    }
}
