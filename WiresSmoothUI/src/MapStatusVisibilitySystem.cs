using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal static class MapStatusVisibilitySystem
    {
        internal const float NewEffectRevealSeconds = 1f;
        internal const float EndingEffectRevealSeconds = 5f;
        internal const float MapSlideSeconds = 0.10f;
        internal const float EffectPanelFadeSeconds = 0.10f;
        internal const float ContextFadeSeconds = 0.20f;

        internal static SmoothUIMapStatusDriver Driver;

        internal static void Register(
            SmoothUIMapStatusDriver driver)
        {
            Driver = driver;
        }

        internal static void Unregister(
            SmoothUIMapStatusDriver driver)
        {
            if (Driver == driver)
                Driver = null;
        }

        internal static void Shutdown()
        {
            if (Driver != null)
                Driver.RestoreEverything();

            Driver = null;
        }
        internal static bool ActivatorHeld
        {
            get
            {
                return
                    (WiresSmoothUIPlugin.Enabled.Value || WiresSmoothUIPlugin.HudEnabled.Value) &&
                    Driver != null &&
                    Driver.IsRevealHeld();
            }
        }

        internal static bool IsShortcutHeld(
            KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None ||
                !Input.GetKey(shortcut.MainKey))
                return false;

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier))
                    return false;
            }

            return true;
        }

        internal sealed class MinimapSlideGate
        {
            private readonly RectTransform _root;
            private readonly Vector2 _basePosition;
            private readonly float _slideDistance;

            private float _progress;
            private float _startProgress;
            private float _targetProgress;
            private float _elapsed;
            private bool _wanted;

            internal bool IsAlive =>
                _root != null;

            internal bool RevealComplete =>
                _wanted &&
                _progress >= 0.999f;

            internal MinimapSlideGate(
                Minimap minimap,
                bool startHidden)
            {
                if (minimap == null ||
                    minimap.m_smallRoot == null)
                    return;

                _root =
                    minimap.m_smallRoot
                        .GetComponent<RectTransform>();

                if (_root == null)
                    return;

                _basePosition = _root.anchoredPosition;
                float width =
                    Mathf.Abs(_root.rect.width);

                if (width < 1f &&
                    minimap.m_mapImageSmall != null)
                {
                    width =
                        Mathf.Abs(
                            minimap.m_mapImageSmall
                                .rectTransform.rect.width);
                }

                _slideDistance =
                    Mathf.Max(
                        320f,
                        width + 80f);

                _progress = startHidden ? 0f : 1f;
                _startProgress = _progress;
                _targetProgress = _progress;
                _elapsed = 0f;
                _wanted = !startHidden;

                Apply();
            }

            internal void Update(
                bool wanted,
                float dt)
            {
                if (!IsAlive)
                    return;

                float target = wanted ? 1f : 0f;

                if (_wanted != wanted ||
                    !Mathf.Approximately(
                        _targetProgress,
                        target))
                {
                    _wanted = wanted;
                    _startProgress = _progress;
                    _targetProgress = target;
                    _elapsed = 0f;
                }
                if (!Mathf.Approximately(
                        _progress,
                        _targetProgress))
                {
                    _elapsed += dt;

                    float t =
                        Mathf.Clamp01(
                            _elapsed /
                            Mathf.Max(0.0001f, WiresSmoothUIPlugin.MinimapMoveSeconds.Value));

                    float eased =
                        wanted
                            ? Mathf.Sin(
                                t * Mathf.PI * 0.5f)
                            : 1f - Mathf.Cos(
                                t * Mathf.PI * 0.5f);

                    _progress =
                        Mathf.Lerp(
                            _startProgress,
                            _targetProgress,
                            eased);

                    if (t >= 1f)
                        _progress = _targetProgress;

                    Apply();
                }
            }

            internal void Restore()
            {
                if (!IsAlive)
                    return;

                _progress = 1f;
                _startProgress = 1f;
                _targetProgress = 1f;
                _elapsed = 0f;
                _wanted = true;
                Apply();
            }

            private void Apply()
            {
                if (!IsAlive)
                    return;

                float offset =
                    _slideDistance *
                    (1f - Mathf.Clamp01(_progress));

                _root.anchoredPosition =
                    _basePosition +
                    Vector2.right * offset;
            }
        }

        internal sealed class ActivationFadeGate
        {
            private readonly GameObject _root;
            private readonly CanvasGroup _group;
            private readonly float _baseAlpha;
            private readonly bool _baseInteractable;
            private readonly bool _baseBlocksRaycasts;

            private float _alpha;

            internal bool IsAlive =>
                _root != null &&
                _group != null;

            internal ActivationFadeGate(
                GameObject root)
            {
                _root = root;
                if (_root == null)
                    return;

                _group = _root.GetComponent<CanvasGroup>();
                if (_group == null)
                    _group = _root.AddComponent<CanvasGroup>();

                _baseAlpha = _group.alpha;
                _baseInteractable = _group.interactable;
                _baseBlocksRaycasts = _group.blocksRaycasts;
                _alpha =
                    _root.activeInHierarchy
                        ? _baseAlpha
                        : 0f;
                Apply();
            }

            internal void Update(
                bool wanted,
                float dt)
            {
                if (!IsAlive)
                    return;

                if (wanted &&
                    !_root.activeSelf)
                {
                    _root.SetActive(true);
                }

                if (!wanted &&
                    !_root.activeSelf &&
                    _alpha <= 0.001f)
                {
                    return;
                }

                float target =
                    wanted
                        ? _baseAlpha
                        : 0f;

                float speed =
                    Mathf.Max(0.01f, _baseAlpha) /
                    MapStatusVisibilitySystem
                        .ContextFadeSeconds;

                _alpha =
                    Mathf.MoveTowards(
                        _alpha,
                        target,
                        speed * dt);

                Apply();

                if (!wanted &&
                    _alpha <= 0.001f &&
                    _root.activeSelf)
                {
                    _root.SetActive(false);
                }
            }

            internal void Restore()
            {
                if (!IsAlive)
                    return;

                _alpha = _baseAlpha;
                Apply();
            }

            private void Apply()
            {
                if (!IsAlive)
                    return;

                _group.alpha = _alpha;
                bool visible = _alpha > 0.02f;
                _group.interactable =
                    visible && _baseInteractable;
                _group.blocksRaycasts =
                    visible && _baseBlocksRaycasts;
            }
        }

    }

    internal sealed class SmoothUIMapStatusDriver :
        MonoBehaviour
    {
        private sealed class EffectVisual
        {
            internal int Hash;
            internal RectTransform Root;
            internal CanvasGroup Group;
            internal float BaseAlpha;
            internal bool BaseInteractable;
            internal bool BaseBlocksRaycasts;
            internal float RevealUntil;
            internal bool Ending;
        }

        private static readonly AccessTools.FieldRef<
            Hud,
            List<RectTransform>> StatusEffectRects =
            AccessTools.FieldRefAccess<
                Hud,
                List<RectTransform>>(
                "m_statusEffects");

        private readonly Dictionary<int, EffectVisual>
            _effects =
                new Dictionary<int, EffectVisual>();
        private readonly HashSet<int> _seenEffects =
            new HashSet<int>();

        private readonly HashSet<int> _currentEffects =
            new HashSet<int>();

        private Hud _hud;
        private MapStatusVisibilitySystem.MinimapSlideGate
            _minimapGate;

        private HudVisibilityReworkSystem.CanvasFadeGate
            _clockGate;
        private HudVisibilityReworkSystem.CanvasFadeGate
            _forecastGate;
        private HudVisibilityReworkSystem.CanvasFadeGate
            _windsGate;

        private MapStatusVisibilitySystem.ActivationFadeGate
            _shipWindGate;

        private float _nextBind;
        private bool _controllerInputArmed;
        private bool _revealStateInitialized;
        private bool _toggleActive;
        private bool _lastToggleKeyboard;
        private bool _lastToggleController;
        private int _toggleFrame = -1;
        private bool _previousRevealHeld;
        private float _mapRetreatFadeRemaining;

        internal void Initialize(Hud hud)
        {
            _hud = hud;
            MapStatusVisibilitySystem.Register(this);

            BindMinimap();
            BindMlui();
            BindShipWind();
        }

        private void BindMinimap()
        {
            if (_minimapGate != null &&
                _minimapGate.IsAlive)
                return;

            Minimap minimap = Minimap.instance;

            if (minimap == null && _hud != null)
            {
                Transform root =
                    _hud.m_rootObject != null
                        ? _hud.m_rootObject.transform
                        : null;

                Transform minimapTransform =
                    root != null
                        ? root.Find("MiniMap")
                        : null;

                if (minimapTransform != null)
                    minimap =
                        minimapTransform.GetComponent<Minimap>();
            }
            if (minimap != null)
            {
                _minimapGate =
                    new MapStatusVisibilitySystem.MinimapSlideGate(
                        minimap,
                        startHidden:
                            WiresSmoothUIPlugin
                                .DynamicMinimap.Value);

            }
        }

        private void BindShipWind()
        {
            if (_shipWindGate != null &&
                _shipWindGate.IsAlive)
                return;

            if (_hud == null ||
                _hud.m_shipHudRoot == null)
                return;

            _shipWindGate =
                new MapStatusVisibilitySystem.ActivationFadeGate(
                    _hud.m_shipHudRoot);
        }

        private void BindMlui()
        {
            if (_hud == null ||
                _hud.m_rootObject == null)
                return;

            Transform root =
                _hud.m_rootObject.transform;

            Transform parent =
                root.Find("MyLittleUI_Parent");

            if (parent == null)
                return;

            BindMluiGate(
                parent.Find("Clock"),
                ref _clockGate);

            BindMluiGate(
                parent.Find("Forecast"),
                ref _forecastGate);

            BindMluiGate(
                parent.Find("Winds"),
                ref _windsGate);
        }

        private static void BindMluiGate(
            Transform target,
            ref HudVisibilityReworkSystem.CanvasFadeGate gate)
        {
            if (gate != null &&
                gate.IsAlive)
                return;

            if (target == null)
                return;

            gate =
                new HudVisibilityReworkSystem.CanvasFadeGate(
                    target.gameObject,
                    startHidden:
                        WiresSmoothUIPlugin.DynamicMapInfo.Value);
        }
        private void LateUpdate()
        {
            if (_hud == null)
                return;

            if (Time.unscaledTime >= _nextBind)
            {
                _nextBind =
                    Time.unscaledTime + 1f;

                BindMinimap();
                BindMlui();
                BindShipWind();
            }

            float dt =
                Mathf.Min(
                    Time.unscaledDeltaTime,
                    0.05f);

            Player player =
                Player.m_localPlayer;

            bool controllingShip =
                player != null &&
                player.GetControlledShip() != null;

            if (!WiresSmoothUIPlugin.HudEnabled.Value)
            {
                RestoreMapCluster();
                RestoreEffects();
                _shipWindGate?.Restore();
                return;
            }

            _shipWindGate?.Update(
                controllingShip,
                dt);

            if (player == null)
            {
                RestoreMapCluster();
                RestoreEffects();
                return;
            }

            bool revealHeld =
                IsRevealHeld();

            if (!_revealStateInitialized)
            {
                _revealStateInitialized = true;
                _previousRevealHeld = revealHeld;
            }
            else if (revealHeld != _previousRevealHeld)
            {
                if (revealHeld)
                {
                    _mapRetreatFadeRemaining = 0f;
                }
                else
                {
                    bool extrasHadAppeared =
                        WiresSmoothUIPlugin.DynamicMinimap.Value &&
                        WiresSmoothUIPlugin.MinimapActivator.Value &&
                        _minimapGate != null &&
                        _minimapGate.RevealComplete;

                    _mapRetreatFadeRemaining =
                        extrasHadAppeared
                            ? WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value
                            : 0f;
                }

                _previousRevealHeld = revealHeld;
            }

            // Disabling the minimap's activator must never call in the map
            // merely because another activator-aware HUD element is fading out.
            if (!WiresSmoothUIPlugin.MinimapActivator.Value)
                _mapRetreatFadeRemaining = 0f;

            bool retreatFadeActive =
                WiresSmoothUIPlugin.MinimapActivator.Value &&
                !revealHeld &&
                _mapRetreatFadeRemaining > 0f;

            bool mapSlideWanted =
                (WiresSmoothUIPlugin
                    .MinimapActivator.Value &&
                 revealHeld) ||
                (WiresSmoothUIPlugin
                    .MinimapShip.Value &&
                 controllingShip) ||
                retreatFadeActive;

            if (WiresSmoothUIPlugin
                    .DynamicMinimap.Value)
            {
                _minimapGate?.Update(
                    mapSlideWanted,
                    dt);
            }
            else
            {
                _minimapGate?.Restore();
            }

            bool mapRevealComplete =
                !WiresSmoothUIPlugin.DynamicMinimap.Value ||
                !WiresSmoothUIPlugin.MinimapActivator.Value ||
                _minimapGate == null ||
                _minimapGate.RevealComplete;

            bool callInExtrasWanted =
                WiresSmoothUIPlugin
                    .MapInfoActivator.Value &&
                revealHeld &&
                mapRevealComplete;

            if (WiresSmoothUIPlugin
                    .DynamicMapInfo.Value)
            {
                _clockGate?.Update(
                    callInExtrasWanted,
                    dt,
                    0f,
                    WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value,
                    WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value);

                _forecastGate?.Update(
                    callInExtrasWanted,
                    dt,
                    0f,
                    WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value,
                    WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value);

                _windsGate?.Update(
                    callInExtrasWanted,
                    dt,
                    0f,
                    WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value,
                    WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value);
            }
            else
            {
                _clockGate?.Restore();
                _forecastGate?.Restore();
                _windsGate?.Restore();
            }

            bool quickMapPanel =
                WiresSmoothUIPlugin
                    .StatusEffectsActivator.Value &&
                revealHeld &&
                mapRevealComplete;

            if (WiresSmoothUIPlugin
                    .DynamicStatusEffects.Value)
            {
                UpdateEffectVisuals(
                    quickMapPanel,
                    retreatFadeActive,
                    dt);
            }
            else
            {
                RestoreEffects();
            }

            if (retreatFadeActive)
            {
                _mapRetreatFadeRemaining =
                    Mathf.Max(
                        0f,
                        _mapRetreatFadeRemaining - dt);
            }
        }

        internal bool IsRevealHeld()
        {
            bool keyboard =
                MapStatusVisibilitySystem
                    .IsShortcutHeld(
                        WiresSmoothUIPlugin
                            .MapInfoHotkey.Value);

            KeyCode controllerKey =
                WiresSmoothUIPlugin
                    .MapInfoControllerHotkey.Value;

            bool controllerRaw = false;
            bool controllerAccepted = false;

            if (controllerKey != KeyCode.None)
            {
                controllerRaw =
                    ZInput.GetKey(
                        controllerKey,
                        false);

                if (!controllerRaw)
                    _controllerInputArmed = true;

                controllerAccepted =
                    _controllerInputArmed &&
                    ZInput.IsGamepadActive() &&
                    controllerRaw;
            }

            if (_toggleFrame != Time.frameCount)
            {
                _toggleFrame = Time.frameCount;
                if (WiresSmoothUIPlugin.ActivatorBehavior.Value == ActivatorBehaviorMode.Toggle &&
                    ((keyboard && !_lastToggleKeyboard) ||
                     (controllerAccepted && !_lastToggleController)))
                    _toggleActive = !_toggleActive;
                _lastToggleKeyboard = keyboard;
                _lastToggleController = controllerAccepted;
            }

            if (WiresSmoothUIPlugin.ActivatorBehavior.Value == ActivatorBehaviorMode.Toggle)
                return _toggleActive;

            _toggleActive = false;
            return keyboard || controllerAccepted;
        }

        internal bool CanManageShipHud(
            Hud hud)
        {
            return
                hud != null &&
                _hud == hud &&
                _shipWindGate != null &&
                _shipWindGate.IsAlive;
        }

        internal void SyncStatusEffects(
            List<StatusEffect> statusEffects)
        {
            if (_hud == null ||
                statusEffects == null)
                return;

            List<RectTransform> rects;

            try
            {
                rects =
                    StatusEffectRects(_hud);
            }
            catch
            {
                return;
            }

            if (rects == null)
                return;

            _currentEffects.Clear();

            int visualIndex = 0;

            for (int i = 0;
                 i < statusEffects.Count;
                 i++)
            {
                StatusEffect effect =
                    statusEffects[i];

                if (effect == null ||
                    effect.m_hidden)
                    continue;

                if (visualIndex >= rects.Count)
                    break;

                RectTransform rect =
                    rects[visualIndex++];

                if (rect == null)
                    continue;

                int hash =
                    effect.NameHash();

                _currentEffects.Add(hash);
                EffectVisual visual;

                if (!_effects.TryGetValue(
                        hash,
                        out visual) ||
                    visual.Root != rect ||
                    visual.Group == null)
                {
                    CanvasGroup group =
                        rect.GetComponent<CanvasGroup>();

                    if (group == null)
                    {
                        group =
                            rect.gameObject
                                .AddComponent<CanvasGroup>();
                    }

                    visual =
                        new EffectVisual
                        {
                            Hash = hash,
                            Root = rect,
                            Group = group,
                            BaseAlpha = group.alpha,
                            BaseInteractable = group.interactable,
                            BaseBlocksRaycasts = group.blocksRaycasts,
                            RevealUntil = 0f,
                            Ending = false
                        };

                    _effects[hash] = visual;

                    if (WiresSmoothUIPlugin.HudEnabled.Value &&
                        WiresSmoothUIPlugin.DynamicStatusEffects.Value)
                    {
                        group.alpha = 0f;
                        group.interactable = false;
                        group.blocksRaycasts = false;
                    }
                }

                visual.Ending =
                    WiresSmoothUIPlugin
                        .StatusEffectsEnding.Value &&
                    effect.m_ttl > 0f &&
                    effect.GetRemaningTime() > 0f &&
                    effect.GetRemaningTime() <=
                        MapStatusVisibilitySystem
                            .EndingEffectRevealSeconds;

                if (!_seenEffects.Contains(hash) &&
                    WiresSmoothUIPlugin
                        .StatusEffectsNew.Value)
                {
                    visual.RevealUntil =
                        Time.unscaledTime +
                        MapStatusVisibilitySystem
                            .NewEffectRevealSeconds;
                }
            }

            _seenEffects.RemoveWhere(
                hash => !_currentEffects.Contains(hash));

            foreach (int hash in _currentEffects)
                _seenEffects.Add(hash);

            RemoveDeadEffectVisuals();
        }
        private void UpdateEffectVisuals(
            bool quickMapPanel,
            bool retreatFadeActive,
            float dt)
        {
            if (_effects.Count == 0)
                return;

            foreach (KeyValuePair<int, EffectVisual> pair
                     in _effects)
            {
                EffectVisual visual =
                    pair.Value;

                if (visual == null ||
                    visual.Root == null ||
                    visual.Group == null)
                    continue;

                bool newReveal =
                    WiresSmoothUIPlugin
                        .StatusEffectsNew.Value &&
                    Time.unscaledTime <
                    visual.RevealUntil;

                bool independentReveal =
                    newReveal ||
                    (WiresSmoothUIPlugin
                        .StatusEffectsEnding.Value &&
                     visual.Ending);

                bool wanted =
                    quickMapPanel ||
                    independentReveal;

                float target =
                    wanted
                        ? visual.BaseAlpha
                        : 0f;

                float fadeSeconds =
                    WiresSmoothUIPlugin.StatusFadeSeconds.Value;

                if (wanted && quickMapPanel)
                {
                    fadeSeconds =
                        WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value;
                }
                else if (!wanted && retreatFadeActive)
                {
                    fadeSeconds =
                        WiresSmoothUIPlugin.StatusActivatorFadeSeconds.Value;
                }

                visual.Group.alpha =
                    Mathf.MoveTowards(
                        visual.Group.alpha,
                        target,
                        Mathf.Max(0.01f, visual.BaseAlpha) *
                        (fadeSeconds <= 0f ? 1f : dt / fadeSeconds));

                bool visible =
                    visual.Group.alpha > 0.02f;

                visual.Group.interactable =
                    visible &&
                    visual.BaseInteractable;

                visual.Group.blocksRaycasts =
                    visible &&
                    visual.BaseBlocksRaycasts;
            }
        }

        private void RemoveDeadEffectVisuals()
        {
            List<int> remove = null;

            foreach (KeyValuePair<int, EffectVisual> pair
                     in _effects)
            {
                EffectVisual visual =
                    pair.Value;

                if (visual == null ||
                    visual.Root == null ||
                    !_currentEffects.Contains(pair.Key))
                {
                    if (remove == null)
                        remove = new List<int>();

                    remove.Add(pair.Key);
                }
            }

            if (remove == null)
                return;

            for (int i = 0; i < remove.Count; i++)
                _effects.Remove(remove[i]);
        }

        private void RestoreMapCluster()
        {
            _minimapGate?.Restore();
            _clockGate?.Restore();
            _forecastGate?.Restore();
            _windsGate?.Restore();
        }

        private void RestoreEffects()
        {
            foreach (EffectVisual visual
                     in _effects.Values)
            {
                if (visual == null ||
                    visual.Group == null)
                    continue;

                visual.Group.alpha = visual.BaseAlpha;
                visual.Group.interactable =
                    visual.BaseInteractable;
                visual.Group.blocksRaycasts =
                    visual.BaseBlocksRaycasts;
            }
        }

        internal void RestoreEverything()
        {
            RestoreMapCluster();
            RestoreEffects();
            _shipWindGate?.Restore();
        }

        private void OnDestroy()
        {
            RestoreEverything();
            MapStatusVisibilitySystem.Unregister(this);
        }
    }
    [HarmonyPatch(typeof(Hud), "Awake")]
    [HarmonyAfter("shudnal.MyLittleUI")]
    internal static class SmoothUIMapStatusHudAwakePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Hud __instance)
        {
            if (__instance == null)
                return;

            SmoothUIMapStatusDriver driver =
                __instance.GetComponent<
                    SmoothUIMapStatusDriver>();

            if (driver == null)
            {
                driver =
                    __instance.gameObject
                        .AddComponent<
                            SmoothUIMapStatusDriver>();
            }

            driver.Initialize(__instance);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateShipHud")]
    internal static class SmoothUIShipHudFadePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Hud __instance,
            Player player)
        {
            if (!WiresSmoothUIPlugin.HudEnabled.Value ||
                player == null ||
                player.GetControlledShip() != null)
            {
                return true;
            }

            SmoothUIMapStatusDriver driver =
                MapStatusVisibilitySystem.Driver;

            // Vanilla normally disables the entire ship HUD immediately
            // when control ends. Keep it alive briefly so its CanvasGroup
            // can actually fade to zero; the gate disables it afterwards.
            return
                driver == null ||
                !driver.CanManageShipHud(__instance);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    [HarmonyAfter("shudnal.MyLittleUI")]
    internal static class SmoothUIMapStatusEffectsPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Hud __instance,
            List<StatusEffect> statusEffects)
        {
            SmoothUIMapStatusDriver driver =
                MapStatusVisibilitySystem.Driver;

            if (driver == null ||
                __instance == null)
                return;

            driver.SyncStatusEffects(
                statusEffects);
        }
    }
}
