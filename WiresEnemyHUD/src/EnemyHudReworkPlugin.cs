using System;
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
        public const string PluginVersion = "1.0.0";

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
        private string _debugBossNameSource;
        private GuiBar _debugBossFast, _debugBossSlow;
        private bool _debugBossBarsLooked;

        private static readonly FieldInfo EnemyHudHudsField = AccessTools.Field(typeof(EnemyHud), "m_huds");

        private static readonly Type HudDataType = AccessTools.Inner(typeof(EnemyHud), "HudData");

        private static readonly FieldInfo HudDataGuiField =
            HudDataType != null ? AccessTools.Field(HudDataType, "m_gui") : null;

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
            public string RawName; // the game's hover name OriginalNameText was made from
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
            public GuiBar BossFastBar; public bool BossFastLooked;
            public GuiBar[] OwnBars; // debug: your own nameplate's bars

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

            // Patch class by class: if a game update breaks one patch, the
            // rest still load instead of the whole mod going dark.
            _harmony = new Harmony(PluginGuid);
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(EnemyHudReworkPlugin).Assembly))
            {
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Logger.LogError("Couldn't apply " + type.Name + ": " + e.Message);
                }
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            DestroyDebugBossHud();
            StopPlayerEffectCycles();
            OnPresetDestroy();
            DestroyAllBossDps();
            DestroyVisibilityOptions();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Instance = null;
        }

        // Bumped on any setting change: layouts are redone once after it.
        private int _layoutVersion = 1;
        private static int _layoutVersionStatic => Instance != null ? Instance._layoutVersion : 0;
        private bool _hadPlayerNameplate;

        internal void UpdateEnemyHuds(Player localPlayer, float dt)
        {
            Prof.Begin(Prof.Frame);
            try { UpdateEnemyHudsTimed(localPlayer, dt); }
            finally { Prof.End(Prof.Frame); }
        }

        private void UpdateEnemyHudsTimed(Player localPlayer, float dt)
        {
            List<Character> remove = null;
            Character lookedAt = localPlayer != null ? localPlayer.GetHoverCreature() : null;
            Prof.Begin(Prof.Look); UpdateLookTarget(lookedAt); Prof.End(Prof.Look);
            Prof.Begin(Prof.Ranks); ProcessPendingRanks(); Prof.End(Prof.Ranks);
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

                if (character == null || state == null || state.Gui == null)
                {
                    if (remove == null)
                        remove = new List<Character>();

                    remove.Add(character);
                    continue;
                }

                if (state.Kind != HudKind.Player && character.GetHealth() <= 0f)
                {
                    HideDeadHud(character);
                    continue;
                }

                bool isEnemy = localPlayer == null || BaseAI.IsEnemy(localPlayer, character);

                Prof.Begin(Prof.Scale); ApplyHealthBarScale(state); Prof.End(Prof.Scale);

                if (state.Kind == HudKind.Player && directlyTargeted != null &&
                    ReferenceEquals(state.Character, directlyTargeted))
                    state.LastAimedAt = frameTime;

                if (state.Kind == HudKind.Enemy)
                {
                    GuardLevelStars(state);
                    Prof.Begin(Prof.Name); UpdateStatusName(state, isEnemy); Prof.End(Prof.Name);
                }
                else if (state.Kind == HudKind.Boss)
                    ApplyBossNameColor(state);

                if (state.Kind == HudKind.Enemy || state.Kind == HudKind.Player)
                {
                    ApplyStaggerPosition(state);
                    Prof.Begin(Prof.Stagger); UpdateStaggerBar(state, isEnemy, dt); Prof.End(Prof.Stagger);
                }

                Prof.Begin(Prof.Bubble); UpdateWorldBubble(state, isEnemy); Prof.End(Prof.Bubble);
                Prof.Begin(Prof.Health); UpdateHealthValue(state); Prof.End(Prof.Health);
                Prof.Begin(Prof.Prediction); UpdateHitPrediction(state, isEnemy, localPlayer, lookedAt); Prof.End(Prof.Prediction);
                Prof.Begin(Prof.Outline); UpdateCriticalOutline(state); Prof.End(Prof.Outline);
                Prof.Begin(Prof.SharedRanks); UpdateSharedRanks(state); Prof.End(Prof.SharedRanks);
                Prof.Begin(Prof.Weakness); UpdateWeaknessIcons(state, isEnemy); Prof.End(Prof.Weakness);
                Prof.Begin(Prof.BossDps); UpdateBossDps(state); Prof.End(Prof.BossDps);
                Prof.Begin(Prof.Enhanced); UpdateEnhancedEffects(state, isEnemy); Prof.End(Prof.Enhanced);
                Prof.Begin(Prof.Status); UpdateStatusIcons(state); Prof.End(Prof.Status);
                Prof.Begin(Prof.Flash); ApplyFrameFlash(state); Prof.End(Prof.Flash);
                Prof.Begin(Prof.Viewport); UpdateViewportVisibility(state); Prof.End(Prof.Viewport);
                Prof.Begin(Prof.PlayerOpacity);
                UpdatePlayerNameplateOpacity(state, localPlayer);
                Prof.End(Prof.PlayerOpacity);
                if (state.Kind == HudKind.Player && state.Character != localPlayer)
                    anyPlayerNameplate = true;
            }
            _hadPlayerNameplate = anyPlayerNameplate;
            Prof.Begin(Prof.IconFlashes); UpdateIconFlashes(); Prof.End(Prof.IconFlashes);

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

        [HarmonyPatch(typeof(Character), "SetHealth")]
        private static class EnemyDeathInstantHide
        {
            private static void Postfix(Character __instance, float health)
            {
                if (health <= 0f && __instance != null && !__instance.IsPlayer())
                    Instance?.HideDeadHud(__instance);
            }
        }
        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        private static class EnemyHudShowHudPatch
        {
            private static bool Prefix(Character c)
            {
                return c == null || c.IsPlayer() || c.GetHealth() > 0f;
            }

            private static void Postfix(EnemyHud __instance, Character c, bool isMount)
            {
                Prof.Begin(Prof.ShowHud);
                if (Instance != null)
                    Instance.SetupHud(__instance, c, isMount);
                Prof.End(Prof.ShowHud);
            }
        }

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static class EnemyHudUpdateHudsPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player player, float dt)
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

