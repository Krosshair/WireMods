using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed partial class EnemyHudReworkPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "kajetan.valheim.enemyhudrework";
        public const string PluginName = "Wire's Enemy HUD";
        public const string PluginVersion = "0.7.1";

        private const string StaggerZdoKey = "EnemyHudRework_Stagger";
        private const float HealthHeightMultiplier = 2f;
        private const float PlayerLikeHideDelay = 1f;

        internal static EnemyHudReworkPlugin Instance;

        public enum NameColorMode { Pastel, Normal, Icons, None }
        public enum HealthDisplayMode { CurrentHP, CurrentHPMaxHP, Percentage, None }
        public enum BossHealthTextPosition { Tracked, Centered }
        public enum BossNameColorMode { Pastel, Vanilla, White }
        private enum HudKind { Enemy, Boss, Player }

        // ConfigurationManager discovers this by type name via reflection.
        // ReadOnly is evaluated dynamically every frame by shudnal's manager.
        private sealed class ConfigurationManagerAttributes
        {
            public int Order { get; set; }
            public string Category { get; set; }
            public Func<bool> ReadOnlyProvider { get; set; }
            public bool ReadOnly =>
                ReadOnlyProvider != null && ReadOnlyProvider();
        }

        private Harmony _harmony;

        private ConfigEntry<NameColorMode> _nameColorMode;
        private ConfigEntry<bool> _uppercaseNames;
        private ConfigEntry<bool> _doubleEnemyHealthBar;
        private ConfigEntry<string> _enemyHealthDisplayMode;
        private ConfigEntry<string> _bossHealthDisplayMode;
        private ConfigEntry<BossHealthTextPosition> _bossHealthTextPosition;
        private ConfigEntry<BossNameColorMode> _bossNameColorMode;
        private ConfigEntry<bool> _doublePlayerHealthBar;
        private ConfigEntry<string> _playerHealthDisplayMode;
        private ConfigEntry<bool> _debugBossHealthBar;
        private ConfigEntry<bool> _debugSeeOwnHealthBar;

        private ConfigEntry<bool> _staggerBarEnabled;
        private ConfigEntry<float> _staggerVerticalOffset;
        private ConfigEntry<float> _starRowHeight;
        private ConfigEntry<bool> _hideForNonStaggerable;

        private ConfigEntry<bool> _syncStaggerForMultiplayer;
        private ConfigEntry<float> _syncInterval;
        private ConfigEntry<float> _syncMinimumDelta;

        private readonly Dictionary<Character, EnemyUiState> _states =
            new Dictionary<Character, EnemyUiState>();

        private readonly Dictionary<int, StaggerSyncState> _syncStates =
            new Dictionary<int, StaggerSyncState>();

        private GameObject _debugBossGui;
        private EnemyUiState _debugBossState;

        private static readonly FieldInfo EnemyHudHudsField =
            AccessTools.Field(typeof(EnemyHud), "m_huds");

        private static readonly Type HudDataType =
            AccessTools.Inner(typeof(EnemyHud), "HudData");

        private static readonly FieldInfo HudDataGuiField =
            HudDataType != null
                ? AccessTools.Field(HudDataType, "m_gui")
                : null;

        private static readonly MethodInfo EnemyHudShowHudMethod =
            AccessTools.Method(typeof(EnemyHud), "ShowHud");

        private sealed class EnemyUiState
        {
            public Character Character;
            public BaseAI Ai;
            public GameObject Gui;
            public HudKind Kind;
            public bool IsDebug;
            public CanvasGroup PlayerOpacityGroup;
            public bool PlayerFadeGroupAdded;
            public float PlayerOriginalAlpha = 1f;

            public Graphic NameGraphic;
            public TMP_Text NameText;
            public RectTransform NameRect;
            public Vector2 NameOriginalPosition;
            public string OriginalNameText;
            public Color NormalNameColor;

            public GameObject HealthValueObject;
            public TMP_Text HealthValueText;
            public RectTransform HealthValueRect;
            public float HealthValueBaseFontSize;
            public GameObject AlertedIcon;
            public GameObject AwareIcon;

            public RectTransform HealthRect;
            public Vector2 HealthOriginalPosition;
            public Vector3 HealthOriginalScale;
            public float HealthOriginalHeight;
            public RectTransform HealthFastFill;
            public RectTransform HealthSlowFill;
            public RectTransform HealthFriendlyFill;
            public float HealthFastOriginalHeight;
            public float HealthSlowOriginalHeight;
            public float HealthFriendlyOriginalHeight;

            public GameObject StaggerRoot;
            public RectTransform StaggerRect;
            public GuiBar StaggerFast;
            public GuiBar StaggerBackground;

            public Image FlashTop;
            public Image FlashBottom;
            public Image FlashLeft;
            public Image FlashRight;

            public RectTransform Level2;
            public RectTransform Level3;
            public Vector2 Level2Original;
            public Vector2 Level3Original;
            public bool HasLevel2;
            public bool HasLevel3;
            public Image RecoveryOverlay;
            public Animator StaggerAnimator;
            public float StaggerBegan = -1f;
            public float StaggerEnd;
            public float StaggerChecked;
            public float StaggerSpeed = 1f;
            public Image HealthBkg, StaggerBkg;
            public Color HealthBkgOriginal, StaggerBkgOriginal;
            public int BkgApplied = -1;

            public float HideTimer = PlayerLikeHideDelay;
            public float LastStaggerPercentage;
            public bool WasStaggering;
            public float FlashStarted = -999f;
            public float FlashUntil = -999f;
        }

        private sealed class StaggerSyncState
        {
            public float LastSent = -1f;
            public float NextSendTime;
        }

        private void Awake()
        {
            Instance = this;
            BindConfig();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            DestroyDebugBossHud();
            OnPresetDestroy();
            DestroyAllBossDps();
            DestroyVisibilityOptions();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Instance = null;
        }

        internal void SetupHud(
            EnemyHud enemyHud,
            Character character,
            bool isMount)
        {
            if (enemyHud == null ||
                character == null)
                return;

            if (isMount)
                return;

            GameObject gui = GetHudGui(enemyHud, character);
            if (gui == null)
                return;

            EnemyUiState existing;
            if (_states.TryGetValue(character, out existing))
            {
                if (existing != null && existing.Gui == gui)
                    return;

                DestroyState(existing);
                _states.Remove(character);
            }

            EnemyUiState state = new EnemyUiState
            {
                Character = character,
                Ai = character.GetComponent<BaseAI>(),
                Gui = gui,
                Kind = character.IsBoss()
                    ? HudKind.Boss
                    : (character.IsPlayer() ? HudKind.Player : HudKind.Enemy)
            };

            Transform nameTransform = gui.transform.Find("Name");
            if (nameTransform != null)
            {
                state.NameGraphic =
                    nameTransform.GetComponent<Graphic>();
                state.NameText =
                    nameTransform.GetComponent<TMP_Text>();
                state.NameRect =
                    nameTransform as RectTransform;

                if (state.NameRect != null)
                    state.NameOriginalPosition =
                        state.NameRect.anchoredPosition;

                if (state.NameText != null)
                    state.OriginalNameText =
                        state.NameText.text;

                if (state.NameGraphic != null)
                    state.NormalNameColor =
                        state.NameGraphic.color;
            }

            Transform alerted = gui.transform.Find("Alerted");
            Transform aware = gui.transform.Find("Aware");

            state.AlertedIcon =
                alerted != null ? alerted.gameObject : null;

            state.AwareIcon =
                aware != null ? aware.gameObject : null;

            Transform health = gui.transform.Find("Health");
            if (health != null)
            {
                state.HealthRect = health as RectTransform;

                if (state.HealthRect != null)
                {
                    state.HealthOriginalPosition =
                        state.HealthRect.anchoredPosition;
                        state.HealthOriginalScale =
                            state.HealthRect.localScale;

                    state.HealthOriginalHeight =
                        state.HealthRect.rect.height;

                    CaptureHealthFill(health, "health_fast",
                        out state.HealthFastFill, out state.HealthFastOriginalHeight);
                    CaptureHealthFill(health, "health_slow",
                        out state.HealthSlowFill, out state.HealthSlowOriginalHeight);
                    CaptureHealthFill(health, "health_fast_friendly",
                        out state.HealthFriendlyFill, out state.HealthFriendlyOriginalHeight);
                }
            }

            state.Level2 =
                gui.transform.Find("level_2") as RectTransform;

            state.Level3 =
                gui.transform.Find("level_3") as RectTransform;

            if (state.Level2 != null)
            {
                state.HasLevel2 = true;
                state.Level2Original =
                    state.Level2.anchoredPosition;
            }

            if (state.Level3 != null)
            {
                state.HasLevel3 = true;
                state.Level3Original =
                    state.Level3.anchoredPosition;
            }

            if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
                CreateStaggerBar(state);

            CreateHealthValue(state);
            SetupCriticalOutline(state);
            SetupWeaknessIcons(state);
            SetupEnhancedEffects(state);
            CaptureNativePalette(state);

            ApplyHealthBarScale(state);
            if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
                ApplyStaggerPosition(state);
            if (state.Kind == HudKind.Boss)
                ApplyBossNameColor(state);
            UpdateHealthValue(state);
            SetupBossDps(state);

            _states[character] = state;
        }

        internal void UpdateEnemyHuds(
            Player localPlayer,
            float dt)
        {
            List<Character> remove = null;
            Player directlyTargeted = _playerNameplateAimFade != null &&
                _playerNameplateAimFade.Value
                ? GetCrosshairPlayer(localPlayer) : null;

            foreach (KeyValuePair<Character, EnemyUiState> pair in _states)
            {
                Character character = pair.Key;
                EnemyUiState state = pair.Value;

                if (character == null ||
                    state == null ||
                    state.Gui == null)
                {
                    if (remove == null)
                        remove = new List<Character>();

                    remove.Add(character);
                    continue;
                }

                bool isEnemy =
                    localPlayer == null ||
                    BaseAI.IsEnemy(localPlayer, character);

                ApplyHealthBarScale(state);

                if (state.Kind == HudKind.Enemy)
                    UpdateStatusName(state, isEnemy);
                else if (state.Kind == HudKind.Boss)
                    ApplyBossNameColor(state);

                if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
                {
                    ApplyStaggerPosition(state);
                    UpdateStaggerBar(state, isEnemy, dt);
                }

                UpdateHealthValue(state);
                UpdateCriticalOutline(state);
                UpdateWeaknessIcons(state, isEnemy);
                UpdateBossDps(state);
                UpdateEnhancedEffects(state, isEnemy);
                UpdateNativePalette(state, isEnemy);
                UpdateViewportVisibility(state);
                UpdatePlayerNameplateOpacity(
                    state, localPlayer, directlyTargeted, dt);
            }

            if (remove == null)
                return;

            foreach (Character character in remove)
            {
                EnemyUiState state;
                if (_states.TryGetValue(character, out state))
                    DestroyState(state);

                _states.Remove(character);
            }
        }

        private void UpdateStatusName(
            EnemyUiState state,
            bool isEnemy)
        {
            if (state == null || state.Kind != HudKind.Enemy)
                return;

            if (state.NameText != null)
            {
                string source = state.OriginalNameText ?? state.NameText.text ?? "";
                state.NameText.text =
                    _uppercaseNames.Value
                        ? source.ToUpperInvariant()
                        : source;
            }

            if (state.NameGraphic == null)
                return;

            // Icons: vanilla shows and hides them itself.
            if (state.Ai == null ||
                _nameColorMode.Value == NameColorMode.Icons)
            {
                state.NameGraphic.color = _cEnemyName.Or(state.NormalNameColor);
                return;
            }

            if (state.AlertedIcon != null)
                state.AlertedIcon.SetActive(false);

            if (state.AwareIcon != null)
                state.AwareIcon.SetActive(false);

            if (_nameColorMode.Value == NameColorMode.None)
            {
                state.NameGraphic.color = _cEnemyName.Or(state.NormalNameColor);
                return;
            }

            bool pastel = _nameColorMode.Value == NameColorMode.Pastel;
            Color alertedColor;
            Color awareColor;

            if (isEnemy)
            {
                alertedColor = _cEnemyAlerted.Or(pastel
                    ? new Color32(0xFF, 0x7A, 0x76, 255)
                    : new Color32(0xF2, 0x10, 0x0C, 255));
                awareColor = _cEnemyAware.Or(pastel
                    ? new Color32(0xFF, 0xE5, 0x80, 255)
                    : new Color32(0xFE, 0xDB, 0x00, 255));
            }
            else
            {
                alertedColor = _cFriendlyAlerted.Or(pastel
                    ? new Color32(0x83, 0xE5, 0xA7, 255)
                    : new Color32(0x18, 0xC9, 0x62, 255));
                awareColor = _cFriendlyAware.Or(pastel
                    ? new Color32(0x86, 0xBE, 0xFF, 255)
                    : new Color32(0x1E, 0x86, 0xFF, 255));
            }

            if (state.Ai.IsAlerted())
                state.NameGraphic.color = alertedColor;
            else if (state.Ai.HaveTarget())
                state.NameGraphic.color = awareColor;
            else
                state.NameGraphic.color = _cEnemyName.Or(state.NormalNameColor);
        }

        // A setting from an older config layout, read once and removed.
        private void ApplyBossNameColor(EnemyUiState state)
        {
            if (state == null ||
                state.Kind != HudKind.Boss ||
                state.NameGraphic == null)
                return;

            if (_cBossName.Has)
            {
                if (state.NameGraphic.color != _cBossName.Value)
                    state.NameGraphic.color = _cBossName.Value;
                return;
            }

            switch (_bossNameColorMode.Value)
            {
                case BossNameColorMode.White:
                    state.NameGraphic.color = Color.white;
                    return;

                case BossNameColorMode.Vanilla:
                    state.NameGraphic.color = state.NormalNameColor;
                    return;
            }

            Color color = new Color32(0xFF, 0x5A, 0xA7, 255);

            if (state.HealthRect != null)
            {
                Transform fastTransform =
                    state.HealthRect.Find("health_fast");
                GuiBar fastBar =
                    fastTransform != null
                        ? fastTransform.GetComponent<GuiBar>()
                        : null;

                if (fastBar != null)
                {
                    Color sampled = fastBar.GetColor();

                    bool nearlyWhite =
                        sampled.r > 0.95f &&
                        sampled.g > 0.95f &&
                        sampled.b > 0.95f;

                    if (!nearlyWhite)
                        color = sampled;
                }
            }

            color.a = state.NormalNameColor.a;
            state.NameGraphic.color = color;
        }

        private void CreateHealthValue(EnemyUiState state)
        {
            if (state == null ||
                state.NameText == null ||
                state.NameText.transform.parent == null)
                return;

            // Use the normal enemy HUD text object as the canonical HP-text
            // template for enemy, player and boss HUDs. This keeps the exact
            // same font sizing/material/scale behavior everywhere.
            TMP_Text template = state.NameText;
            EnemyHud enemyHud = EnemyHud.instance;
            if (enemyHud != null && enemyHud.m_baseHud != null)
            {
                Transform enemyName =
                    enemyHud.m_baseHud.transform.Find("Name");
                TMP_Text enemyTemplate =
                    enemyName != null
                        ? enemyName.GetComponent<TMP_Text>()
                        : null;

                if (enemyTemplate != null)
                    template = enemyTemplate;
            }

            GameObject obj = Instantiate(
                template.gameObject,
                state.NameText.transform.parent,
                false);

            obj.name = "HealthValue";
            state.HealthValueObject = obj;
            state.HealthValueText = obj.GetComponent<TMP_Text>();
            state.HealthValueRect = obj.transform as RectTransform;

            if (state.HealthValueText != null)
            {
                state.HealthValueText.text = "";
                state.HealthValueText.color = HpTextColor();
                state.HealthValueText.alignment =
                    TextAlignmentOptions.Center;
                // The Name prefab uses TMP automatic font sizing.
                // Unless we disable it, TMP silently shrinks text
                // back to its original size after our font setting.
                state.HealthValueText.enableAutoSizing = false;
                state.HealthValueText.textWrappingMode = TextWrappingModes.NoWrap;
                state.HealthValueText.overflowMode =
                    TextOverflowModes.Overflow;
                state.HealthValueText.raycastTarget = false;
                state.HealthValueBaseFontSize =
                    state.HealthValueText.fontSize;
            }

            obj.SetActive(false);
        }

        private string GetHealthDisplayMode(EnemyUiState state)
        {
            if (state == null)
                return "Current HP/Max HP";

            switch (state.Kind)
            {
                case HudKind.Boss:
                    return _bossHealthDisplayMode.Value;
                case HudKind.Player:
                    return _playerHealthDisplayMode.Value;
                default:
                    return _enemyHealthDisplayMode.Value;
            }
        }

        private bool IsDoubleHealthBar(EnemyUiState state)
        {
            if (state == null)
                return false;

            switch (state.Kind)
            {
                case HudKind.Boss:
                    return false;
                case HudKind.Player:
                    return _doublePlayerHealthBar.Value;
                default:
                    return _doubleEnemyHealthBar.Value;
            }
        }

        private void UpdateHealthValue(EnemyUiState state)
        {
            if (state == null ||
                state.HealthValueObject == null ||
                state.HealthValueText == null ||
                state.HealthValueRect == null)
                return;

            if (!state.IsDebug && state.Character == null)
                return;

            string mode = GetHealthDisplayMode(state);

            // Player HP text is only shown in the doubled player health bar.
            // With vanilla-height player bars, leave the nameplate completely
            // untouched regardless of the selected HP display mode.
            bool playerCounterDisabled =
                state.Kind == HudKind.Player &&
                !_doublePlayerHealthBar.Value;

            if (mode == "None" || playerCounterDisabled)
            {
                state.HealthValueObject.SetActive(false);
                if (state.NameRect != null)
                    state.NameRect.anchoredPosition = state.NameOriginalPosition;
                return;
            }

            float hp = state.Character != null
                ? Mathf.Max(0f, state.Character.GetHealth())
                : 0f;
            float max = state.Character != null
                ? Mathf.Max(1f, state.Character.GetMaxHealth())
                : 1f;

            if (mode == "Percentage")
            {
                state.HealthValueText.text =
                    Mathf.Clamp(Mathf.RoundToInt(hp / max * 100f), 0, 100) + "%";
            }
            else
            {
                int current = Mathf.CeilToInt(hp);
                int maximum = Mathf.CeilToInt(max);
                state.HealthValueText.text =
                    mode == "Current HP/Max HP"
                        ? current + "/" + maximum
                        : current.ToString();
            }

            state.HealthValueObject.SetActive(true);
            state.HealthValueText.color = HpTextColor();

            if (state.Kind == HudKind.Boss && state.HealthRect != null)
            {
                UpdateBossHealthValueLayout(state, Mathf.Clamp01(hp / max));
                return;
            }

            if (IsDoubleHealthBar(state) && state.HealthRect != null)
            {
                if (state.NameRect != null)
                    state.NameRect.anchoredPosition = state.NameOriginalPosition;

                state.HealthValueRect.anchorMin = state.HealthRect.anchorMin;
                state.HealthValueRect.anchorMax = state.HealthRect.anchorMax;
                state.HealthValueRect.pivot = state.HealthRect.pivot;
                state.HealthValueRect.anchoredPosition = state.HealthRect.anchoredPosition;
                state.HealthValueRect.sizeDelta = state.HealthRect.sizeDelta;
                state.HealthValueRect.localScale = Vector3.one;
                state.HealthValueText.fontSize = state.HealthValueBaseFontSize * 0.648f * _hpFontScale.Value;
                ReportFontIfAdjusted(state.HealthValueText);
                state.HealthValueText.alignment = TextAlignmentOptions.Center;
            }
            else if (state.NameRect != null)
            {
                state.HealthValueRect.anchorMin = state.NameRect.anchorMin;
                state.HealthValueRect.anchorMax = state.NameRect.anchorMax;
                state.HealthValueRect.pivot = state.NameRect.pivot;
                state.HealthValueRect.sizeDelta = state.NameRect.sizeDelta;
                state.HealthValueRect.localScale = Vector3.one;
                state.HealthValueText.fontSize = state.HealthValueBaseFontSize * 0.738f * _hpFontScale.Value;
                ReportFontIfAdjusted(state.HealthValueText);
                state.HealthValueText.alignment = TextAlignmentOptions.Center;

                float gap = 5f;
                float nameWidth = state.NameText != null ? state.NameText.preferredWidth : 0f;
                float valueWidth = state.HealthValueText.preferredWidth;

                state.NameRect.anchoredPosition =
                    state.NameOriginalPosition +
                    Vector2.left * ((valueWidth + gap) * 0.5f);

                state.HealthValueRect.anchoredPosition =
                    state.NameOriginalPosition +
                    Vector2.right * ((nameWidth + gap) * 0.5f);
            }
        }

        private void UpdateBossHealthValueLayout(
            EnemyUiState state,
            float healthPercentage)
        {
            if (state == null ||
                state.HealthRect == null ||
                state.HealthValueRect == null ||
                state.HealthValueText == null)
                return;

            if (state.NameRect != null)
                state.NameRect.anchoredPosition = state.NameOriginalPosition;

            state.HealthValueRect.localScale = Vector3.one;
            state.HealthValueText.fontSize = state.HealthValueBaseFontSize * 0.738f * _hpFontScale.Value;
                ReportFontIfAdjusted(state.HealthValueText);

            float textWidth = Mathf.Max(
                80f,
                state.HealthValueText.preferredWidth + 12f);
            float textHeight = Mathf.Max(
                state.HealthValueText.preferredHeight,
                state.HealthValueText.fontSize * 1.2f);

            state.HealthValueRect.sizeDelta =
                new Vector2(textWidth, textHeight);

            Rect barRect = state.HealthRect.rect;
            bool tracked =
                _bossHealthTextPosition.Value ==
                BossHealthTextPosition.Tracked;

            float localX = barRect.center.x;
            if (tracked)
            {
                if (state.HealthFastFill != null)
                {
                    Rect fillRect = state.HealthFastFill.rect;
                    Vector3 fillRightWorld =
                        state.HealthFastFill.TransformPoint(
                            new Vector3(
                                fillRect.xMax,
                                fillRect.center.y,
                                0f));

                    localX =
                        state.HealthRect.InverseTransformPoint(
                            fillRightWorld).x;
                }
                else
                {
                    localX = Mathf.Lerp(
                        barRect.xMin,
                        barRect.xMax,
                        Mathf.Clamp01(healthPercentage));
                }

                // Never let the number run past the bar's left end.
                localX = Mathf.Max(localX, barRect.xMin + TextWidthIn(
                    state.HealthValueText, state.HealthValueRect, state.HealthRect));
            }

            float localY =
                barRect.yMin -
                textHeight * 0.55f -
                2f;

            state.HealthValueRect.pivot =
                tracked
                    ? new Vector2(1f, 0.5f)
                    : new Vector2(0.5f, 0.5f);

            state.HealthValueText.alignment =
                tracked
                    ? TextAlignmentOptions.Right
                    : TextAlignmentOptions.Center;

            Vector3 worldPoint =
                state.HealthRect.TransformPoint(
                    new Vector3(localX, localY, 0f));

            state.HealthValueRect.position = worldPoint;
        }

        // A text's visible width, in another rect's local units.
        private static float TextWidthIn(TMP_Text text, RectTransform textRect, RectTransform space)
        {
            return text.preferredWidth * Mathf.Abs(textRect.lossyScale.x) /
                Mathf.Max(0.0001f, Mathf.Abs(space.lossyScale.x));
        }

        private static void CaptureHealthFill(
            Transform healthRoot,
            string childName,
            out RectTransform fill,
            out float originalHeight)
        {
            fill = null;
            originalHeight = 0f;
            if (healthRoot == null)
                return;

            Transform child = healthRoot.Find(childName);
            GuiBar bar = child != null ? child.GetComponent<GuiBar>() : null;
            if (bar == null || bar.m_bar == null)
                return;

            fill = bar.m_bar;
            originalHeight = fill.rect.height;
        }

        private float GetHealthFillBaseHeight(EnemyUiState state)
        {
            if (state == null)
                return 0f;
            if (state.HealthFastOriginalHeight > 0f)
                return state.HealthFastOriginalHeight;
            if (state.HealthSlowOriginalHeight > 0f)
                return state.HealthSlowOriginalHeight;
            if (state.HealthFriendlyOriginalHeight > 0f)
                return state.HealthFriendlyOriginalHeight;
            return Mathf.Max(0f, state.HealthOriginalHeight * 0.5f);
        }

        private static void SetFillHeight(RectTransform fill, float originalHeight, float multiplier)
        {
            if (fill == null || originalHeight <= 0f)
                return;
            fill.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                originalHeight * multiplier);
        }

        private void ApplyHealthBarScale(EnemyUiState state)
        {
            if (state == null || state.HealthRect == null)
                return;

            float multiplier = IsDoubleHealthBar(state) ? HealthHeightMultiplier : 1f;
            float extraLocalHeight = IsDoubleHealthBar(state)
                ? GetHealthFillBaseHeight(state) * (HealthHeightMultiplier - 1f)
                : 0f;

            state.HealthRect.localScale = state.HealthOriginalScale;
            state.HealthRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                state.HealthOriginalHeight + extraLocalHeight);

            SetFillHeight(state.HealthFastFill, state.HealthFastOriginalHeight, multiplier);
            SetFillHeight(state.HealthSlowFill, state.HealthSlowOriginalHeight, multiplier);
            SetFillHeight(state.HealthFriendlyFill, state.HealthFriendlyOriginalHeight, multiplier);

            float extraHeight = GetExtraHealthHeight(state);
            state.HealthRect.anchoredPosition =
                state.HealthOriginalPosition + Vector2.down * (extraHeight * 0.5f);
        }

        private float GetExtraHealthHeight(EnemyUiState state)
        {
            if (state == null || state.HealthRect == null || !IsDoubleHealthBar(state))
                return 0f;

            float extraLocalHeight =
                GetHealthFillBaseHeight(state) * (HealthHeightMultiplier - 1f);
            return Mathf.Abs(extraLocalHeight * state.HealthOriginalScale.y);
        }

        private void CreateStaggerBar(
            EnemyUiState state)
        {
            if (state == null ||
                state.Gui == null ||
                state.HealthRect == null)
                return;

            GameObject staggerRoot =
                Instantiate(
                    state.HealthRect.gameObject,
                    state.HealthRect.parent,
                    false);

            staggerRoot.name = "Stagger";
            state.StaggerRoot = staggerRoot;
            state.StaggerRect =
                staggerRoot.transform as RectTransform;

            if (state.StaggerRect != null)
                state.StaggerRect.localScale =
                    state.HealthOriginalScale;

            Transform fastTransform =
                staggerRoot.transform.Find("health_fast");

            Transform slowTransform =
                staggerRoot.transform.Find("health_slow");

            Transform friendlyTransform =
                staggerRoot.transform.Find("health_fast_friendly");

            if (friendlyTransform != null)
                friendlyTransform.gameObject.SetActive(false);

            if (fastTransform != null)
            {
                state.StaggerFast =
                    fastTransform.GetComponent<GuiBar>();

                if (state.StaggerFast != null)
                {
                    state.StaggerFast.SetColor(
                        new Color32(0xE7, 0x5D, 0x16, 255));

                    CopyPlayerStaggerBarBehavior(
                        state.StaggerFast);

                    state.StaggerFast.SetValue(0f);
                }
            }

            if (slowTransform != null)
            {
                state.StaggerBackground =
                    slowTransform.GetComponent<GuiBar>();

                if (state.StaggerBackground != null)
                {
                    state.StaggerBackground.gameObject.SetActive(false);
                }
            }

            foreach (Graphic graphic in
                staggerRoot.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                if (state.StaggerFast != null &&
                    graphic.transform.IsChildOf(
                        state.StaggerFast.transform))
                    continue;

                if (state.StaggerBackground != null &&
                    graphic.transform.IsChildOf(
                        state.StaggerBackground.transform))
                    continue;

                if (!(graphic is Image))
                    graphic.gameObject.SetActive(false);
            }

            CreateFlashBorder(state);
            staggerRoot.SetActive(false);
        }

        private static void CopyPlayerStaggerBarBehavior(
            GuiBar target)
        {
            if (target == null)
                return;

            Hud hud = Hud.instance;
            GuiBar playerBar =
                hud != null ? hud.m_staggerProgress : null;

            if (playerBar == null)
            {
                target.m_smoothDrain = false;
                target.m_smoothFill = false;
                target.m_changeDelay = 0f;
                return;
            }

            target.m_smoothDrain =
                playerBar.m_smoothDrain;

            target.m_smoothFill =
                playerBar.m_smoothFill;

            target.m_smoothSpeed =
                playerBar.m_smoothSpeed;

            target.m_changeDelay =
                playerBar.m_changeDelay;
        }

        private void ApplyStaggerPosition(
            EnemyUiState state)
        {
            if (state == null ||
                state.StaggerRect == null)
                return;

            state.StaggerRect.anchoredPosition =
                state.HealthOriginalPosition +
                Vector2.down *
                (_staggerVerticalOffset.Value +
                 GetExtraHealthHeight(state));
        }

        private void UpdateStaggerBar(
            EnemyUiState state,
            bool isEnemy,
            float dt)
        {
            if (state == null ||
                state.Character == null)
                return;

            bool eligible =
                state.Kind == HudKind.Player ||
                (state.Kind == HudKind.Enemy && isEnemy);

            if (!_staggerBarEnabled.Value ||
                state.StaggerRoot == null ||
                !eligible)
            {
                SetStaggerLayoutActive(state, false);
                return;
            }

            bool staggerable =
                state.Character.m_staggerDamageFactor > 0f;

            if (_hideForNonStaggerable.Value &&
                !staggerable)
            {
                SetStaggerLayoutActive(state, false);
                return;
            }

            float staggerPercentage =
                Mathf.Clamp01(
                    GetDisplayStaggerPercentage(
                        state.Character));

            // Exact Hud.UpdateStagger visibility rule:
            // >0 resets timer, at 0 it remains visible until timer reaches 1 second.
            if (staggerPercentage > 0f)
                state.HideTimer = 0f;
            else
                state.HideTimer += dt;

            bool isStaggering = state.Character.IsStaggering();
            bool recovering = state.Kind == HudKind.Enemy &&
                _staggerRecoveryEnabled != null &&
                _staggerRecoveryEnabled.Value && isStaggering;

            bool visible =
                staggerPercentage > 0f ||
                state.HideTimer < PlayerLikeHideDelay ||
                recovering;

            SetStaggerLayoutActive(state, visible);

            if (state.StaggerFast != null)
                state.StaggerFast.SetValue(
                    recovering ? 1f : staggerPercentage);

            bool reachedFull =
                staggerPercentage >= 0.999f &&
                state.LastStaggerPercentage < 0.999f;

            bool beganStagger =
                isStaggering &&
                !state.WasStaggering;

            if (reachedFull || beganStagger)
                StartFlash(state);

            UpdateFlash(state);

            state.LastStaggerPercentage =
                staggerPercentage;

            state.WasStaggering =
                isStaggering;
        }

        private void SetStaggerLayoutActive(
            EnemyUiState state,
            bool active)
        {
            if (state == null)
                return;

            if (state.StaggerRoot != null)
                state.StaggerRoot.SetActive(active);

            // The chosen star row height, below the double-height health
            // bar's extra height, and below the stagger bar while it shows
            // (moving with its gap setting).
            float y =
                _starRowHeight.Value -
                GetExtraHealthHeight(state) -
                (active ? Mathf.Abs(_staggerVerticalOffset.Value) : 0f);

            if (state.HasLevel2 &&
                state.Level2 != null)
            {
                state.Level2.anchoredPosition =
                    state.Level2Original +
                    new Vector2(0f, y);
            }

            if (state.HasLevel3 &&
                state.Level3 != null)
            {
                state.Level3.anchoredPosition =
                    state.Level3Original +
                    new Vector2(0f, y);
            }
        }

        private void CreateFlashBorder(
            EnemyUiState state)
        {
            if (state == null ||
                state.StaggerRoot == null)
                return;

            Transform parent =
                state.StaggerRoot.transform;

            state.FlashTop = CreateBorderPiece(
                parent,
                "FlashTop",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 1f),
                new Vector2(2f, 2f));

            state.FlashBottom = CreateBorderPiece(
                parent,
                "FlashBottom",
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, -1f),
                new Vector2(2f, 2f));

            state.FlashLeft = CreateBorderPiece(
                parent, "FlashLeft",
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                new Vector2(-1f, 0f),
                new Vector2(2f, 2f));

            state.FlashRight = CreateBorderPiece(
                parent, "FlashRight",
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0f),
                new Vector2(2f, 2f));

            NativeFrameFlash.Fit(state.StaggerRect, state.FlashTop,
                state.FlashBottom, state.FlashLeft, state.FlashRight);
            SetFlashAlpha(state, 0f);
        }

        private static Image CreateBorderPiece(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);

            go.transform.SetAsLastSibling();
            return image;
        }

        private void StartFlash(EnemyUiState state)
        {
            if (state == null)
                return;

            float duration = GetPlayerStaggerFlashDuration();
            state.FlashStarted = Time.time;
            state.FlashUntil = Time.time + duration;
        }

        private static void UpdateFlash(EnemyUiState state)
        {
            if (state == null)
                return;

            // Align to the complete dark native frame after every
            // stagger-bar resize, never just a two-pixel outline.
            NativeFrameFlash.Fit(state.StaggerRect, state.FlashTop,
                state.FlashBottom, state.FlashLeft, state.FlashRight);

            float duration = state.FlashUntil - state.FlashStarted;
            if (duration <= 0f || Time.time >= state.FlashUntil)
            {
                SetFlashAlpha(state, 0f);
                return;
            }

            float t = Mathf.Clamp01(
                (Time.time - state.FlashStarted) / duration);

            SetFlashAlpha(
                state,
                Mathf.Sin(Mathf.PI * t));
        }

        private static void SetFlashAlpha(
            EnemyUiState state,
            float alpha)
        {
            if (state == null)
                return;

            Color color = _staggerFlashBase;
            color.a *= Mathf.Clamp01(alpha);

            if (state.FlashTop != null)
                state.FlashTop.color = color;
            if (state.FlashBottom != null)
                state.FlashBottom.color = color;
            if (state.FlashLeft != null)
                state.FlashLeft.color = color;
            if (state.FlashRight != null)
                state.FlashRight.color = color;
        }

        private static float GetPlayerStaggerFlashDuration()
        {
            Hud hud = Hud.instance;
            Animator animator =
                hud != null ? hud.m_staggerAnimator : null;

            if (animator != null &&
                animator.runtimeAnimatorController != null)
            {
                AnimationClip[] clips =
                    animator.runtimeAnimatorController.animationClips;

                if (clips != null)
                {
                    foreach (AnimationClip clip in clips)
                    {
                        if (clip != null &&
                            clip.name.IndexOf(
                                "flash",
                                StringComparison.OrdinalIgnoreCase) >= 0 &&
                            clip.length > 0.01f)
                        {
                            return clip.length;
                        }
                    }
                }
            }

            return 0.30f;
        }

        private float GetDisplayStaggerPercentage(
            Character character)
        {
            if (character == null)
                return 0f;

            if (!_syncStaggerForMultiplayer.Value)
                return character.GetStaggerPercentage();

            ZNetView nview = character.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || nview.IsOwner())
                return character.GetStaggerPercentage();

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return character.GetStaggerPercentage();

            return Mathf.Clamp01(
                zdo.GetFloat(
                    StaggerZdoKey,
                    character.GetStaggerPercentage()));
        }

        internal void SyncOwnedStagger(Character character)
        {
            if (!_syncStaggerForMultiplayer.Value ||
                character == null)
                return;

            ZNetView nview = character.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return;

            int id = character.GetInstanceID();

            StaggerSyncState sync;
            if (!_syncStates.TryGetValue(id, out sync))
            {
                sync = new StaggerSyncState();
                _syncStates[id] = sync;
            }

            float now = Time.time;
            if (now < sync.NextSendTime)
                return;

            float value = Mathf.Clamp01(
                character.GetStaggerPercentage());

            float delta = Mathf.Abs(value - sync.LastSent);

            bool endpointChanged =
                (value <= 0.0001f && sync.LastSent > 0.0001f) ||
                (value >= 0.9999f && sync.LastSent < 0.9999f);

            if (sync.LastSent < 0f ||
                delta >= Mathf.Max(
                    0.0001f,
                    _syncMinimumDelta.Value) ||
                endpointChanged)
            {
                zdo.Set(StaggerZdoKey, value);
                sync.LastSent = value;
            }

            sync.NextSendTime =
                now + Mathf.Max(0.02f, _syncInterval.Value);
        }

        internal void UpdateDebugHuds(EnemyHud enemyHud)
        {
            if (enemyHud == null)
                return;

            Player localPlayer = Player.m_localPlayer;
            // Like vanilla shows you to other players: hidden while sneaking.
            if (_debugSeeOwnHealthBar.Value && localPlayer != null && !localPlayer.IsCrouching())
            {
                if (EnemyHudShowHudMethod != null)
                    EnemyHudShowHudMethod.Invoke(enemyHud, new object[] { localPlayer, false });

                SetupHud(enemyHud, localPlayer, false);

                EnemyUiState ownState;
                if (_states.TryGetValue(localPlayer, out ownState) && ownState != null && ownState.Gui != null)
                {
                    // EnemyHud.UpdateHuds positions player HUDs using the game's
                    // normal multiplayer head-point logic. We only keep it visible.
                    // The vanilla update already ran this frame, so a hud
                    // made just now gets placed and filled here, the same way.
                    PlaceOwnNameplate(ownState, localPlayer);
                    ApplyHealthBarScale(ownState);
                    UpdateHealthValue(ownState);
                }
            }
            else if (localPlayer != null)
            {
                EnemyUiState ownState;
                if (_states.TryGetValue(localPlayer, out ownState) &&
                    ownState != null && ownState.Kind == HudKind.Player)
                {
                    enemyHud.RemoveCharacterHud(localPlayer);
                    DestroyState(ownState);
                    _states.Remove(localPlayer);
                }
            }

            UpdateDebugBossHud(enemyHud);
        }

        private static void PlaceOwnNameplate(EnemyUiState state, Player player)
        {
            Camera camera = Utils.GetMainCamera();
            if (camera == null) return;
            Vector3 position = camera.WorldToScreenPointScaled(
                player.GetHeadPoint() + Vector3.up * 0.3f);
            bool visible = position.z > 0f;
            if (state.Gui.activeSelf != visible) state.Gui.SetActive(visible);
            if (!visible) return;
            state.Gui.transform.position = position;

            float health = player.GetHealthPercentage();
            Transform t = state.Gui.transform;
            GuiBar bar;
            Transform fast = t.Find("Health/health_fast");
            if (fast != null && (bar = fast.GetComponent<GuiBar>()) != null) bar.SetValue(health);
            Transform slow = t.Find("Health/health_slow");
            if (slow != null && (bar = slow.GetComponent<GuiBar>()) != null) bar.SetValue(health);
            Transform friendly = t.Find("Health/health_fast_friendly");
            if (friendly != null && (bar = friendly.GetComponent<GuiBar>()) != null) bar.SetValue(health);
        }

        private void UpdateDebugBossHud(EnemyHud enemyHud)
        {
            Player localPlayer = Player.m_localPlayer;

            if (!_debugBossHealthBar.Value || localPlayer == null)
            {
                DestroyDebugBossHud();
                return;
            }

            if (_debugBossGui == null || _debugBossState == null)
                CreateDebugBossHud(enemyHud, localPlayer);

            if (_debugBossGui == null || _debugBossState == null)
                return;

            _debugBossState.Character = localPlayer;
            _debugBossGui.SetActive(true);

            if (_debugBossState.NameText != null)
            {
                _debugBossState.NameText.text =
                    "EVIL " +
                    localPlayer.GetPlayerName().ToUpperInvariant() +
                    " ALTER EGO";
            }

            ApplyBossNameColor(_debugBossState);

            float healthPercentage = localPlayer.GetHealthPercentage();

            Transform fast = _debugBossGui.transform.Find("Health/health_fast");
            Transform slow = _debugBossGui.transform.Find("Health/health_slow");
            GuiBar fastBar = fast != null ? fast.GetComponent<GuiBar>() : null;
            GuiBar slowBar = slow != null ? slow.GetComponent<GuiBar>() : null;

            if (fastBar != null)
                fastBar.SetValue(healthPercentage);
            if (slowBar != null)
                slowBar.SetValue(healthPercentage);

            ApplyHealthBarScale(_debugBossState);
            UpdateNativePalette(_debugBossState, false);
            UpdateBarBackground(_debugBossState);
            UpdateHealthValue(_debugBossState);
            UpdateWeaknessIcons(_debugBossState, true);
            UpdateBossDps(_debugBossState);
        }

        private void CreateDebugBossHud(EnemyHud enemyHud, Player localPlayer)
        {
            if (enemyHud == null || enemyHud.m_baseHudBoss == null || enemyHud.m_hudRoot == null)
                return;

            _debugBossGui = Instantiate(enemyHud.m_baseHudBoss, enemyHud.m_hudRoot.transform);
            _debugBossGui.name = "EnemyHudRework_DebugBoss";
            _debugBossGui.SetActive(true);

            EnemyUiState state = new EnemyUiState
            {
                Character = localPlayer,
                Gui = _debugBossGui,
                Kind = HudKind.Boss,
                IsDebug = true
            };
            Transform nameTransform = _debugBossGui.transform.Find("Name");
            if (nameTransform != null)
            {
                state.NameGraphic = nameTransform.GetComponent<Graphic>();
                state.NameText = nameTransform.GetComponent<TMP_Text>();
                state.NameRect = nameTransform as RectTransform;
                if (state.NameRect != null)
                    state.NameOriginalPosition = state.NameRect.anchoredPosition;
                if (state.NameText != null)
                {
                    state.OriginalNameText = state.NameText.text;
                    state.NameText.text =
                        "EVIL " +
                        localPlayer.GetPlayerName().ToUpperInvariant() +
                        " ALTER EGO";
                }
                if (state.NameGraphic != null)
                    state.NormalNameColor = state.NameGraphic.color;
            }

            Transform health = _debugBossGui.transform.Find("Health");
            if (health != null)
            {
                state.HealthRect = health as RectTransform;
                if (state.HealthRect != null)
                {
                    state.HealthOriginalPosition = state.HealthRect.anchoredPosition;
                    state.HealthOriginalScale = state.HealthRect.localScale;
                    state.HealthOriginalHeight = state.HealthRect.rect.height;

                    CaptureHealthFill(health, "health_fast",
                        out state.HealthFastFill, out state.HealthFastOriginalHeight);
                    CaptureHealthFill(health, "health_slow",
                        out state.HealthSlowFill, out state.HealthSlowOriginalHeight);
                    CaptureHealthFill(health, "health_fast_friendly",
                        out state.HealthFriendlyFill, out state.HealthFriendlyOriginalHeight);
                }
            }

            CreateHealthValue(state);
            CaptureNativePalette(state);
            _debugBossState = state;
            ApplyHealthBarScale(state);
            ApplyBossNameColor(state);
            UpdateHealthValue(state);
            SetupWeaknessIcons(state);
            SetupBossDps(state);
        }

        private void DestroyDebugBossHud()
        {
            if (_debugBossState != null)
            {
                DestroyWeaknessIcons(_debugBossState);
                DestroyBossDps(_debugBossState);
                ForgetNativePalette(_debugBossState);
                if (_debugBossState.HealthValueObject != null)
                    Destroy(_debugBossState.HealthValueObject);
                _debugBossState = null;
            }

            if (_debugBossGui != null)
            {
                Destroy(_debugBossGui);
                _debugBossGui = null;
            }
        }

        private GameObject GetHudGui(
            EnemyHud enemyHud,
            Character character)
        {
            if (EnemyHudHudsField == null ||
                HudDataGuiField == null ||
                enemyHud == null ||
                character == null)
                return null;

            IDictionary huds =
                EnemyHudHudsField.GetValue(enemyHud)
                    as IDictionary;

            if (huds == null || !huds.Contains(character))
                return null;

            object hudData = huds[character];
            return hudData != null
                ? HudDataGuiField.GetValue(hudData) as GameObject
                : null;
        }

        private static Color ParseColor(
            string value,
            Color fallback)
        {
            Color parsed;
            if (!string.IsNullOrEmpty(value) &&
                ColorUtility.TryParseHtmlString(value, out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private void DestroyState(EnemyUiState state)
        {
            if (state == null)
                return;

            RestorePlayerNameplateOpacity(state);
            DestroyCriticalOutline(state);
            DestroyWeaknessIcons(state);
            DestroyBossDps(state);
            DestroyEnhancedEffects(state);
            ForgetNativePalette(state);

            if (state.HealthRect != null)
            {
                state.HealthRect.localScale = state.HealthOriginalScale;
                state.HealthRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical, state.HealthOriginalHeight);
                SetFillHeight(state.HealthFastFill, state.HealthFastOriginalHeight, 1f);
                SetFillHeight(state.HealthSlowFill, state.HealthSlowOriginalHeight, 1f);
                SetFillHeight(state.HealthFriendlyFill, state.HealthFriendlyOriginalHeight, 1f);
                state.HealthRect.anchoredPosition = state.HealthOriginalPosition;
            }

            if (state.StaggerRoot != null)
                Destroy(state.StaggerRoot);

            if (state.HealthValueObject != null)
                Destroy(state.HealthValueObject);
        }

        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        private static class EnemyHudShowHudPatch
        {
            private static void Postfix(
                EnemyHud __instance,
                Character c,
                bool isMount)
            {
                if (Instance != null)
                    Instance.SetupHud(__instance, c, isMount);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static class EnemyHudUpdateHudsPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(
                EnemyHud __instance,
                Player player,
                Sadle sadle,
                float dt)
            {
                if (Instance != null)
                    Instance.UpdateEnemyHuds(player, dt);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), "LateUpdate")]
        private static class EnemyHudLateUpdatePatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(EnemyHud __instance)
            {
                if (Instance != null)
                {
                    Instance.UpdateDebugHuds(__instance);
                    Instance.ApplyDamageRevealAfterHudUpdate(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Character), "UpdateStagger")]
        private static class CharacterUpdateStaggerPatch
        {
            private static void Postfix(Character __instance)
            {
                if (Instance != null)
                    Instance.SyncOwnedStagger(__instance);
            }
        }
    }
}

