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
        public const string PluginVersion = "0.8.7";

        private const string StaggerZdoKey = "EnemyHudRework_Stagger";
        private const float HealthHeightMultiplier = 2f;
        private const float PlayerLikeHideDelay = 1f;

        internal static EnemyHudReworkPlugin Instance;

        public enum NameColorMode { Pastel, Normal, Icons, None }
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
            public bool? IsAdvanced { get; set; }
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

        private ConfigEntry<bool> _syncStaggerForMultiplayer;
        private const float SyncInterval = 0.10f;      // least time between stagger updates
        private const float SyncMinimumDelta = 0.01f;  // least change before another is sent

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

        // A damage trail clipped to start where its bar's fill ends.
        private sealed class TrailClip
        {
            public RectMask2D Mask;
            public bool Bubble; // the bubble bar's trail, else the health trail
            public float Shown = -1f; public int Layout = -1;
        }

        private sealed class EnemyUiState
        {
            public WeaknessUi Weakness;
            public BubbleUi Bubble;
            public float BubbleLift;
            public ZNetView BubbleView;
            public uint BubbleRevision = uint.MaxValue; // MaxValue: read again
            public float BubbleMax, BubbleDamage;
            public bool BubbleFoe, BubbleSeen, BubbleTimed;
            public long BubbleUntil;
            public float BubbleFlashStart = -1f, BubbleFlashShown;
            public HitPredictionUi Prediction;
            public bool PredictShowStagger;
            public float StaggerFlashAlpha, CritFlashAlpha;
            public float StaggerFlashShown, CritFlashShown;
            public float LastStarY = float.NaN;
            public Character Character;
            public BaseAI Ai;
            public GameObject Gui;
            public object HudRecord; // the game's own record for this nameplate
            // What was last drawn, so unchanged frames do nothing.
            public bool HpDrawn; public int HpA, HpB, HpLayout; public float HpLift; public string HpName;
            public int ScaleLayout = -1; public float ScaleHeight;
            public string UpperSource, UpperValue;
            public bool ClampKnown; public Bounds ClampBounds;
            public HudKind Kind;
            public bool IsDebug;
            public CanvasGroup PlayerOpacityGroup;
            public bool PlayerFadeGroupAdded;
            public float PlayerOriginalAlpha = 1f;
            public float LastAimedAt = -999f; // players: when your crosshair was last on them
            public TrailClip[] Trails; public bool TrailsHidden;

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
            public Graphic[] IconGraphics; public bool IconsHidden;

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


            public RectTransform Level2;
            public TMP_Text LevelNumber; public GameObject LevelSecondStar;
            public int StarLevel = -1; public GameObject[] OtherStars;
            public StatusUi Status;
            public EffectUi Effects;
            public RectTransform Level3;
            public Vector2 Level2Original;
            public Vector2 Level3Original;
            public bool HasLevel2;
            public bool HasLevel3;
            // Players have no star row: an invisible copy of a creature's
            // star stands in for it, so their icons line up the same way.
            public RectTransform IconRow;
            public Vector2 IconRowOriginal;
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

            // The game calls this every frame for every shown nameplate:
            // one dictionary lookup once it is set up.
            EnemyUiState known;
            if (_states.TryGetValue(character, out known) && known != null && known.Gui != null)
                return;

            object record;
            GameObject gui = GetHudGui(enemyHud, character, out record);
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
                HudRecord = record,
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

            // Both star groups start switched on in the game's HUD prefab, and
            // the game only sorts them out while it is showing the HUD on
            // hover. A HUD this mod shows first (on damage, kept on screen)
            // would show stars on a normal creature. The level never changes,
            // so set the right ones once here.
            ApplyLevelStars(state, character);

            if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
            {
                CreateStaggerBar(state);
                // Made the same way and at the same moment as the stagger
                // bar: a copy of the fresh health bar, kept hidden until a
                // bubble shows.
                state.Bubble = CreateWorldBubble(state);
            }

            CreateHealthValue(state);
            SetupCriticalOutline(state);
            if (state.Kind == HudKind.Player) CreatePlayerIconRow(state);
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

        private static void ApplyLevelStars(EnemyUiState state, Character character)
        {
            if (state == null || character == null) return;
            int level = character.GetLevel();
            state.StarLevel = level;
            if (Instance != null && !Instance._starCount.Value)
            {
                // Off: exactly the game's own stars (none past two), and
                // other mods' star rows are left alone.
                if (state.Level2 != null && state.Level2.gameObject.activeSelf != (level == 2))
                    state.Level2.gameObject.SetActive(level == 2);
                if (state.Level3 != null && state.Level3.gameObject.activeSelf != (level == 3))
                    state.Level3.gameObject.SetActive(level == 3);
                if (state.LevelSecondStar != null && !state.LevelSecondStar.activeSelf)
                    state.LevelSecondStar.SetActive(true);
                if (state.LevelNumber != null && state.LevelNumber.gameObject.activeSelf)
                    state.LevelNumber.gameObject.SetActive(false);
                return;
            }
            // Other mods' own star rows (any "level_" object besides the
            // game's two) give way to ours; their pictures aren't touched.
            if (state.OtherStars == null && state.Gui != null)
            {
                var others = new List<GameObject>();
                foreach (Transform t in state.Gui.transform)
                    if (t.name.StartsWith("level_", StringComparison.Ordinal) &&
                        t != state.Level2 && t != state.Level3)
                        others.Add(t.gameObject);
                state.OtherStars = others.ToArray();
            }
            if (state.OtherStars != null)
                foreach (GameObject o in state.OtherStars)
                    if (o != null && o.activeSelf) o.SetActive(false);
            // Past two stars (level mods): the two-star row with its second
            // star swapped for the star count, in the stars' own yellow and
            // the HP numbers' font.
            bool high = level >= 4;
            if (state.Level2 != null && state.Level2.gameObject.activeSelf != (level == 2))
                state.Level2.gameObject.SetActive(level == 2);
            bool three = level == 3 || high;
            if (state.Level3 != null && state.Level3.gameObject.activeSelf != three)
                state.Level3.gameObject.SetActive(three);
            if (state.Level3 == null) return;
            if (high && state.LevelNumber == null) CreateLevelNumber(state);
            if (state.LevelSecondStar != null && state.LevelSecondStar.activeSelf == high)
                state.LevelSecondStar.SetActive(!high);
            if (state.LevelNumber != null)
            {
                if (state.LevelNumber.gameObject.activeSelf != high)
                    state.LevelNumber.gameObject.SetActive(high);
                if (high) state.LevelNumber.text = (level - 1).ToString();
            }
        }

        // Runs after every other mod's nameplate update (ours is last): if
        // anything switched our stars around, they're put back. A few
        // reads; only acts when something actually changed them.
        private static void GuardLevelStars(EnemyUiState state)
        {
            int level = state.StarLevel;
            if (level < 1 || !Instance._starCount.Value) return;
            bool high = level >= 4;
            bool wrong =
                (state.Level2 != null && state.Level2.gameObject.activeSelf != (level == 2)) ||
                (state.Level3 != null && state.Level3.gameObject.activeSelf != (level == 3 || high)) ||
                (state.LevelSecondStar != null && state.LevelSecondStar.activeSelf == high) ||
                (state.LevelNumber != null && state.LevelNumber.gameObject.activeSelf != high);
            if (!wrong && state.OtherStars != null)
                foreach (GameObject o in state.OtherStars)
                    if (o != null && o.activeSelf) { wrong = true; break; }
            if (wrong && state.Character != null) ApplyLevelStars(state, state.Character);
        }

        private static readonly Color StarYellow = new Color(1f, 0.8174443f, 0.3382353f, 1f);

        private static void CreateLevelNumber(EnemyUiState state)
        {
            Transform second = state.Level3.Find("star (1)");
            TMP_Text font = state.HealthValueText != null ? state.HealthValueText : state.NameText;
            if (second == null || font == null) return;
            state.LevelSecondStar = second.gameObject;
            RectTransform starRect = (RectTransform)second;
            GameObject go = new GameObject("WireLevelNumber", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(state.Level3, false);
            r.anchorMin = starRect.anchorMin;
            r.anchorMax = starRect.anchorMax;
            r.pivot = starRect.pivot;
            r.anchoredPosition = starRect.anchoredPosition;
            r.sizeDelta = starRect.sizeDelta;
            TMP_Text t = go.GetComponent<TMP_Text>();
            t.font = font.font;
            t.fontSharedMaterial = font.fontSharedMaterial;
            t.fontStyle = font.fontStyle;
            t.fontSize = starRect.sizeDelta.y;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Center;
            t.color = StarYellow;
            t.raycastTarget = false;
            state.LevelNumber = t;
        }

        // ---- What you're looking at ----
        // One check for everything that cares (hit predictions, Dynamic
        // icons, Dynamic HP numbers): the game's own hover creature, read
        // once a frame. Enemies you just looked away from are remembered
        // with the time, for the delay settings; that list is tiny and
        // empties itself.
        private ConfigEntry<bool> _dynamicHp;
        private ConfigEntry<bool> _starCount;
        private ConfigEntry<bool> _enemyAimFade;
        // Players: the crosshair check the nameplate fading uses, with one
        // delay for both it and their Dynamic HP numbers.
        private ConfigEntry<bool> _playerDynamicHp;
        private ConfigEntry<float> _playerAimDelay;
        private ConfigEntry<float> _lookAwayDelay; // one delay for all of them

        private bool AimedAtRecently(EnemyUiState state)
        {
            return Time.time <= state.LastAimedAt + _playerAimDelay.Value;
        }
        private ConfigEntry<float> _dynamicHpDelay;
        private Character _looked;
        private struct LookedAway { public Character C; public float At; public bool IconsShown; }
        private readonly List<LookedAway> _lookedAway = new List<LookedAway>();

        private void UpdateLookTarget(Character lookedAt)
        {
            // Nothing uses the memory with all three off: just follow the
            // target (hit predictions read it directly) and keep no list.
            if (!_dynamicMode.Value && !_dynamicHp.Value && !_enemyAimFade.Value)
            {
                if (_lookedAway.Count > 0) _lookedAway.Clear();
                _looked = lookedAt;
                return;
            }
            float now = Time.time;
            if (!ReferenceEquals(lookedAt, _looked))
            {
                if (_looked != null) KeepInFocusUntil(_looked, now);
                _looked = lookedAt;
                MarkIconsDirty(lookedAt);
            }
            if (_lookedAway.Count == 0) return;
            float iconDelay = _dynamicDelay.Value, hpDelay = _dynamicHpDelay.Value;
            for (int i = _lookedAway.Count - 1; i >= 0; i--)
            {
                LookedAway e = _lookedAway[i];
                if (e.IconsShown && now >= e.At + iconDelay)
                {
                    e.IconsShown = false;
                    _lookedAway[i] = e;
                    MarkIconsDirty(e.C);
                }
                if (e.C == null || (!e.IconsShown && now >= e.At + hpDelay))
                    _lookedAway.RemoveAt(i);
            }
        }

        // Counts a creature as looked at until the given time (then its
        // delays run from there). Looking away uses "now"; a hit or parry
        // reveal uses the end of the reveal, so revealed creatures get their
        // HP numbers, icons and full opacity exactly like looked-at ones.
        private void KeepInFocusUntil(Character c, float until)
        {
            if (c == null) return;
            for (int i = 0; i < _lookedAway.Count; i++)
            {
                if (!ReferenceEquals(_lookedAway[i].C, c)) continue;
                LookedAway e = _lookedAway[i];
                e.At = Mathf.Max(e.At, until);
                e.IconsShown = true;
                _lookedAway[i] = e;
                MarkIconsDirty(c);
                return;
            }
            _lookedAway.Add(new LookedAway { C = c, At = until, IconsShown = true });
            MarkIconsDirty(c);
        }

        internal void NoteReveal(Character c)
        {
            if (!_dynamicMode.Value && !_dynamicHp.Value && !_enemyAimFade.Value) return;
            KeepInFocusUntil(c, Time.time + DamageShowSeconds);
        }

        private void MarkIconsDirty(Character c)
        {
            EnemyUiState st;
            if (c != null && _dynamicMode.Value && _states.TryGetValue(c, out st) && st != null && st.Weakness != null)
                st.Weakness.Dirty = true;
        }

        // Looked at now, or looked away less than delay seconds ago.
        private bool LookedAtRecently(Character c, float delay)
        {
            if (c == null) return false;
            if (ReferenceEquals(c, _looked)) return true;
            float now = Time.time;
            for (int i = 0; i < _lookedAway.Count; i++)
                if (ReferenceEquals(_lookedAway[i].C, c)) return now < _lookedAway[i].At + delay;
            return false;
        }

        // Bumped on any setting change: layouts are redone once after it.
        private int _layoutVersion = 1;
        private static int _layoutVersionStatic => Instance != null ? Instance._layoutVersion : 0;
        private bool _hadPlayerNameplate;

        internal void UpdateEnemyHuds(
            Player localPlayer,
            float dt)
        {
            List<Character> remove = null;
            Character lookedAt = localPlayer != null ? localPlayer.GetHoverCreature() : null;
            UpdateLookTarget(lookedAt);
            ProcessPendingRanks();
            // The crosshair ray is only needed while another player's
            // nameplate is around (never in single player).
            Player directlyTargeted = _playerNameplateAimFade != null &&
                (_playerNameplateAimFade.Value || _playerDynamicHp.Value) && _hadPlayerNameplate
                ? GetCrosshairPlayer(localPlayer) : null;
            float frameTime = Time.time;
            bool anyPlayerNameplate = false;

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

                if (state.Kind == HudKind.Player && directlyTargeted != null &&
                    ReferenceEquals(state.Character, directlyTargeted))
                    state.LastAimedAt = frameTime;

                if (state.Kind == HudKind.Enemy)
                {
                    GuardLevelStars(state);
                    UpdateStatusName(state, isEnemy);
                }
                else if (state.Kind == HudKind.Boss)
                    ApplyBossNameColor(state);

                if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
                {
                    ApplyStaggerPosition(state);
                    UpdateStaggerBar(state, isEnemy, dt);
                }

                UpdateWorldBubble(state, isEnemy);
                UpdateHealthValue(state);
                UpdateHitPrediction(state, isEnemy, localPlayer, lookedAt, dt);
                UpdateCriticalOutline(state);
                UpdateSharedRanks(state);
                UpdateWeaknessIcons(state, isEnemy);
                UpdateBossDps(state);
                UpdateEnhancedEffects(state, isEnemy);
                UpdateStatusIcons(state);
                ApplyFrameFlash(state);
                UpdateViewportVisibility(state);
                UpdatePlayerNameplateOpacity(
                    state, localPlayer, directlyTargeted, dt);
                if (state.Kind == HudKind.Player && state.Character != localPlayer)
                    anyPlayerNameplate = true;
            }
            _hadPlayerNameplate = anyPlayerNameplate;
            UpdateIconFlashes();

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

        // The game turns its awareness icons on and off itself every frame.
        // Switching them off after it would make the UI rebuild each time,
        // so instead their pictures are made invisible once, and the game
        // can switch the empty objects as much as it likes.
        private static void SetAwarenessIconsHidden(EnemyUiState state, bool hidden)
        {
            if (state.IconsHidden == hidden) return;
            state.IconsHidden = hidden;
            if (state.IconGraphics == null)
            {
                var list = new List<Graphic>();
                if (state.AlertedIcon != null) list.AddRange(state.AlertedIcon.GetComponentsInChildren<Graphic>(true));
                if (state.AwareIcon != null) list.AddRange(state.AwareIcon.GetComponentsInChildren<Graphic>(true));
                state.IconGraphics = list.ToArray();
            }
            foreach (Graphic g in state.IconGraphics)
                if (g != null) g.enabled = !hidden;
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
                string wanted = source;
                if (_uppercaseNames.Value)
                {
                    if (!ReferenceEquals(state.UpperSource, source))
                    {
                        state.UpperSource = source;
                        state.UpperValue = source.ToUpperInvariant();
                    }
                    wanted = state.UpperValue;
                }
                if (!string.Equals(state.NameText.text, wanted, System.StringComparison.Ordinal))
                    state.NameText.text = wanted;
            }

            if (state.NameGraphic == null)
                return;

            // Icons: vanilla shows and hides them itself.
            if (state.Ai == null ||
                _nameColorMode.Value == NameColorMode.Icons)
            {
                SetAwarenessIconsHidden(state, false);
                state.NameGraphic.color = state.NormalNameColor;
                return;
            }

            SetAwarenessIconsHidden(state, true);

            if (_nameColorMode.Value == NameColorMode.None)
            {
                state.NameGraphic.color = state.NormalNameColor;
                return;
            }

            bool pastel = _nameColorMode.Value == NameColorMode.Pastel;
            Color alertedColor;
            Color awareColor;

            if (isEnemy)
            {
                alertedColor = (pastel
                    ? new Color32(0xFF, 0x7A, 0x76, 255)
                    : new Color32(0xF2, 0x10, 0x0C, 255));
                awareColor = (pastel
                    ? new Color32(0xFF, 0xE5, 0x80, 255)
                    : new Color32(0xFE, 0xDB, 0x00, 255));
            }
            else
            {
                alertedColor = (pastel
                    ? new Color32(0x83, 0xE5, 0xA7, 255)
                    : new Color32(0x18, 0xC9, 0x62, 255));
                awareColor = (pastel
                    ? new Color32(0x86, 0xBE, 0xFF, 255)
                    : new Color32(0x1E, 0x86, 0xFF, 255));
            }

            if (state.Ai.IsAlerted())
                state.NameGraphic.color = alertedColor;
            else if (state.Ai.HaveTarget())
                state.NameGraphic.color = awareColor;
            else
                state.NameGraphic.color = state.NormalNameColor;
        }

        // A setting from an older config layout, read once and removed.
        private void ApplyBossNameColor(EnemyUiState state)
        {
            if (state == null ||
                state.Kind != HudKind.Boss ||
                state.NameGraphic == null)
                return;

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
                state.HealthValueText.color = Color.white;
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
                (state.Kind == HudKind.Player &&
                 !_doublePlayerHealthBar.Value) ||
                (state.Kind == HudKind.Enemy && _dynamicHp.Value && !state.IsDebug &&
                 !LookedAtRecently(state.Character, _dynamicHpDelay.Value)) ||
                (state.Kind == HudKind.Player && _playerDynamicHp.Value && !state.IsDebug &&
                 state.Character != Player.m_localPlayer && !AimedAtRecently(state));

            float hpNow = state.Character != null ? Mathf.Max(0f, state.Character.GetHealth()) : 0f;
            float maxNow = state.Character != null ? Mathf.Max(1f, state.Character.GetMaxHealth()) : 1f;
            int keyA, keyB;
            if (mode == "None" || playerCounterDisabled) { keyA = -1; keyB = -1; }
            else if (mode == "Percentage") { keyA = Mathf.Clamp(Mathf.RoundToInt(hpNow / maxNow * 100f), 0, 100); keyB = -2; }
            else { keyA = Mathf.CeilToInt(hpNow); keyB = mode == "Current HP/Max HP" ? Mathf.CeilToInt(maxNow) : -3; }
            string nameNow = state.NameText != null ? state.NameText.text : null;
            if (state.HpDrawn && keyA == state.HpA && keyB == state.HpB &&
                state.HpLayout == _layoutVersion && state.HpLift == state.BubbleLift &&
                ReferenceEquals(nameNow, state.HpName))
                return;
            state.HpDrawn = true;
            state.HpA = keyA; state.HpB = keyB;
            state.HpLayout = _layoutVersion; state.HpLift = state.BubbleLift; state.HpName = nameNow;

            if (mode == "None" || playerCounterDisabled)
            {
                state.HealthValueObject.SetActive(false);
                if (state.NameRect != null)
                    state.NameRect.anchoredPosition = NameBase(state);
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
            state.HealthValueText.color = Color.white;

            if (state.Kind == HudKind.Boss && state.HealthRect != null)
            {
                UpdateBossHealthValueLayout(state, Mathf.Clamp01(hp / max));
                return;
            }

            if (IsDoubleHealthBar(state) && state.HealthRect != null)
            {
                if (state.NameRect != null)
                    state.NameRect.anchoredPosition = NameBase(state);

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
                    NameBase(state) +
                    Vector2.left * ((valueWidth + gap) * 0.5f);

                state.HealthValueRect.anchoredPosition =
                    NameBase(state) +
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
                state.NameRect.anchoredPosition = NameBase(state);

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
            float height = state.HealthOriginalHeight + extraLocalHeight;
            if (state.ScaleLayout == _layoutVersion && state.HealthRect.sizeDelta.y == state.ScaleHeight)
                return;

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
            state.ScaleLayout = _layoutVersion;
            state.ScaleHeight = state.HealthRect.sizeDelta.y;
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

            Vector2 pos =
                state.HealthOriginalPosition +
                Vector2.down *
                (_staggerVerticalOffset.Value +
                 GetExtraHealthHeight(state));
            if (state.StaggerRect.anchoredPosition != pos)
                state.StaggerRect.anchoredPosition = pos;
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

            // An enemy that can't be staggered never gets a stagger bar.
            if (!staggerable)
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
                recovering ||
                state.PredictShowStagger;

            SetStaggerLayoutActive(state, visible);

            if (state.StaggerFast != null)
                state.StaggerFast.SetValue(
                    recovering ? 1f : staggerPercentage);

            // The game keeps the meter full during a stagger and lets it
            // drain; any hit then tops it back up to full (bows always do).
            // That's no new stagger: a fill only counts while not staggering.
            bool reachedFull =
                staggerPercentage >= 0.999f &&
                state.LastStaggerPercentage < 0.999f &&
                !isStaggering && !state.WasStaggering;

            bool beganStagger =
                isStaggering &&
                !state.WasStaggering;

            // Not when that hit killed it: the bar is gone with it. A flash
            // already running stops if it dies.
            bool dead = state.Character.GetHealth() <= 0f;
            if ((reachedFull || beganStagger) && !dead)
                StartFlash(state);
            else if (dead && state.FlashUntil > Time.time)
                state.FlashUntil = Time.time;

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

            if (state.StaggerRoot != null && state.StaggerRoot.activeSelf != active)
                state.StaggerRoot.SetActive(active);

            // The chosen star row height, below the double-height health
            // bar's extra height, and below the stagger bar while it shows
            // (moving with its gap setting).
            float y =
                _starRowHeight.Value -
                GetExtraHealthHeight(state) -
                (active ? Mathf.Abs(_staggerVerticalOffset.Value) : 0f);

            // The stars only move when that height changes.
            if (state.LastStarY == y)
                return;
            state.LastStarY = y;
            if (state.Weakness != null)
                state.Weakness.Dirty = true;

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

            if (state.IconRow != null)
                state.IconRow.anchoredPosition =
                    state.IconRowOriginal + new Vector2(0f, y);
        }

        // Made once per player nameplate: the creature HUD's two-star row,
        // placed the same distance from the health bar, nothing drawn.
        private static void CreatePlayerIconRow(EnemyUiState state)
        {
            if (state.Level2 != null || state.HealthRect == null || state.Gui == null) return;
            EnemyHud hud = EnemyHud.instance;
            if (hud == null || hud.m_baseHud == null) return;
            RectTransform star = hud.m_baseHud.transform.Find("level_2") as RectTransform;
            RectTransform health = hud.m_baseHud.transform.Find("Health") as RectTransform;
            if (star == null || health == null) return;
            GameObject copy = UnityEngine.Object.Instantiate(star.gameObject, state.Gui.transform, false);
            copy.name = "WireIconRow";
            foreach (Graphic g in copy.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
            copy.SetActive(true);
            RectTransform r = (RectTransform)copy.transform;
            // The health bar hangs from the top of its nameplate and the
            // star row sits on the middle, and a player's nameplate is
            // shorter: measure both from the middle so the gap matches.
            RectTransform playerHealth = state.HealthRect;
            Vector2 baseBar = FromMiddle(health, health.anchoredPosition);
            Vector2 playerBar = FromMiddle(playerHealth, state.HealthOriginalPosition);
            r.anchoredPosition = FromMiddle(star, star.anchoredPosition) - baseBar + playerBar -
                FromMiddle(r, Vector2.zero);
            state.IconRow = r;
            state.IconRowOriginal = r.anchoredPosition;
            state.LastStarY = float.NaN;
        }

        // Where a child's anchored position puts it, measured from the
        // middle of its parent.
        private static Vector2 FromMiddle(RectTransform r, Vector2 anchored)
        {
            RectTransform parent = r.parent as RectTransform;
            if (parent == null) return anchored;
            Vector2 a = (r.anchorMin + r.anchorMax) * 0.5f - new Vector2(0.5f, 0.5f);
            return anchored + Vector2.Scale(a, parent.rect.size);
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
                VanillaFlash(t));
        }

        // The game's own flash animation (stagger_flash): full white on the
        // very first frame, then eases back down to the frame's own color
        // (2t^3 - 3t^2 + 1). It hits, it doesn't fade in.
        internal static float VanillaFlash(float t)
        {
            if (t < 0f || t >= 1f) return 0f;
            return (2f * t - 3f) * t * t + 1f;
        }

        private static void SetFlashAlpha(
            EnemyUiState state,
            float alpha)
        {
            if (state == null)
                return;

            // Drawn like the game's own flash: the bar's dark frame itself
            // lights up (see ApplyFrameFlash).
            state.StaggerFlashAlpha = Mathf.Clamp01(alpha);
        }

        // Length of the game's stagger flash animation, read once from your
        // HUD (asking the animator for its clips makes a new list each time).
        private static float _flashDuration = -1f;

        private static float GetPlayerStaggerFlashDuration()
        {
            if (_flashDuration > 0f) return _flashDuration;
            float found = ReadPlayerStaggerFlashDuration();
            if (Hud.instance != null) _flashDuration = found; // known once the HUD exists
            return found;
        }

        private static float ReadPlayerStaggerFlashDuration()
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

            ZNetView nview = CharacterView(character);
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

        private static readonly AccessTools.FieldRef<Character, ZNetView> CharacterView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        // Runs on every character's stagger tick, so it bails out as early
        // as it can: alone in the world (nobody to tell), or not staggered
        // and nothing left to send. Only staggered creatures are tracked.
        internal void SyncOwnedStagger(Character character)
        {
            if (!_syncStaggerForMultiplayer.Value ||
                character == null ||
                ZNet.instance == null ||
                ZNet.instance.GetPeers().Count == 0)
                return;

            float value = Mathf.Clamp01(
                character.GetStaggerPercentage());
            int id = character.GetInstanceID();
            StaggerSyncState sync;
            bool tracked = _syncStates.TryGetValue(id, out sync);
            if (value <= 0.0001f && (!tracked || sync.LastSent <= 0.0001f))
            {
                if (tracked) _syncStates.Remove(id);
                return;
            }

            ZNetView nview = CharacterView(character);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return;

            if (!tracked)
            {
                sync = new StaggerSyncState();
                _syncStates[id] = sync;
            }

            float now = Time.time;
            if (now < sync.NextSendTime)
                return;

            float delta = Mathf.Abs(value - sync.LastSent);

            bool endpointChanged =
                (value <= 0.0001f && sync.LastSent > 0.0001f) ||
                (value >= 0.9999f && sync.LastSent < 0.9999f);

            if (sync.LastSent < 0f ||
                delta >= Mathf.Max(
                    0.0001f,
                    SyncMinimumDelta) ||
                endpointChanged)
            {
                zdo.Set(StaggerZdoKey, value);
                sync.LastSent = value;
            }

            sync.NextSendTime =
                now + SyncInterval;
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
            UpdateBarBackground(_debugBossState);
            UpdateHealthValue(_debugBossState);
            if (_debugBossState.Weakness != null) _debugBossState.Weakness.Dirty = true; // debug name changes
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
            Character character,
            out object hudData)
        {
            hudData = null;
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

            hudData = huds[character];
            return hudData != null
                ? HudDataGuiField.GetValue(hudData) as GameObject
                : null;
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
            DestroyWorldBubble(state);
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

