using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MagicaCloth2;
using SoftReferenceableAssets;
using Splatform;
using UnityEngine;
using UnityEngine.Rendering;
using Valheim.SettingsGui;

public class Player : Humanoid
{
	public enum RequirementMode
	{
		CanBuild,
		IsKnown,
		CanAlmostBuild
	}

	public class Food
	{
		public string m_name = "";

		public ItemDrop.ItemData m_item;

		public float m_time;

		public float m_health;

		public float m_stamina;

		public float m_eitr;

		public bool CanEatAgain()
		{
			return m_time < m_item.m_shared.m_foodBurnTime / 2f;
		}
	}

	public class MinorActionData
	{
		public enum ActionType
		{
			Equip,
			Unequip,
			Reload
		}

		public ActionType m_type;

		public ItemDrop.ItemData m_item;

		public string m_progressText = "";

		public float m_time;

		public float m_duration;

		public string m_animation = "";

		public string m_doneAnimation = "";

		public float m_staminaDrain;

		public float m_eitrDrain;

		public EffectList m_startEffect;
	}

	public enum PlacementStatus
	{
		Valid,
		Invalid,
		BlockedbyPlayer,
		NoBuildZone,
		PrivateZone,
		MoreSpace,
		NoTeleportArea,
		ExtensionMissingStation,
		WrongBiome,
		NeedCultivated,
		NeedDirt,
		NotInDungeon,
		NoRayHits,
		DeepSnow,
		NoSnow
	}

	private class RaycastHitComparer : IComparer<RaycastHit>
	{
		public static RaycastHitComparer Instance = new RaycastHitComparer();

		public int Compare(RaycastHit x, RaycastHit y)
		{
			return x.distance.CompareTo(y.distance);
		}
	}

	[Serializable]
	public struct StatusEffectLevel
	{
		public float m_rate;

		public StatusEffect m_se;
	}

	public const string InventoryRowsKey = "invrows";

	private Vector3 m_lastDistCheck;

	private float m_statCheck;

	private float m_timeSinceLastCheck;

	[Header("Effects")]
	public EffectList m_buttonEffects = new EffectList();

	private List<string> m_readyEvents = new List<string>();

	private float m_lastMaxAdrenaline;

	private static List<IPlaced> m_placed = new List<IPlaced>();

	private int m_clusterStatCheck = 10;

	public static string LastEmote;

	public static DateTime LastEmoteTime;

	private float[] m_equipmentModifierValues;

	private static FieldInfo[] s_equipmentModifierSourceFields;

	private static readonly string[] s_equipmentModifierSources = new string[11]
	{
		"m_movementModifier", "m_homeItemsStaminaModifier", "m_heatResistanceModifier", "m_jumpStaminaModifier", "m_attackStaminaModifier", "m_blockStaminaModifier", "m_dodgeStaminaModifier", "m_swimStaminaModifier", "m_sneakStaminaModifier", "m_runStaminaModifier",
		"m_maxAdrenaline"
	};

	private static readonly string[] s_equipmentModifierTooltips = new string[11]
	{
		"$item_movement_modifier", "$base_item_modifier", "$item_heat_modifier", "$se_jumpstamina", "$se_attackstamina", "$se_blockstamina", "$se_dodgestamina", "$se_swimstamina", "$se_sneakstamina", "$se_runstamina",
		"$item_maxadrenaline"
	};

	private float m_baseValueUpdateTimer;

	private float m_rotatePieceTimer;

	private float m_rotatePieceTimeSince;

	private float m_scrollCurrAmount;

	private bool m_altPlace;

	public static Player m_localPlayer = null;

	public static bool m_localPlayerExists = false;

	private static readonly List<Player> s_players = new List<Player>();

	public static List<string> m_addUniqueKeyQueue = new List<string>();

	public static List<string> s_FilterCraft = new List<string>();

	public static bool m_debugMode = false;

	[Header("Player")]
	public float m_maxPlaceDistance = 5f;

	public float m_maxInteractDistance = 5f;

	public float m_scrollSens = 4f;

	public float m_autoPickupRange = 2f;

	public float m_maxCarryWeight = 300f;

	public float m_encumberedStaminaDrain = 10f;

	public float m_hardDeathCooldown = 10f;

	public float m_baseCameraShake = 4f;

	public float m_placeDelay = 0.4f;

	public float m_removeDelay = 0.25f;

	public GameObject m_placeMarker;

	public GameObject m_tombstone;

	public SoftReference<GameObject> m_valkyrie;

	public Sprite m_textIcon;

	public float m_baseHP = 25f;

	public float m_baseStamina = 75f;

	public double m_wakeupTime;

	[Header("Stamina")]
	public float m_staminaRegen = 5f;

	public float m_staminaRegenTimeMultiplier = 1f;

	public float m_staminaRegenDelay = 1f;

	public float m_runStaminaDrain = 10f;

	public float m_sneakStaminaDrain = 5f;

	public float m_swimStaminaDrainMinSkill = 5f;

	public float m_swimStaminaDrainMaxSkill = 2f;

	public float m_dodgeStaminaUsage = 10f;

	public float m_dodgeAdrenaline = 10f;

	public float m_weightStaminaFactor = 0.1f;

	public float m_grapplingStaminaDrain = -1f;

	[Header("Adrenaline")]
	public float m_maxAdrenaline = 100f;

	public AnimationCurve m_adrenalineDegen = new AnimationCurve();

	public AnimationCurve m_adrenalineDegenDelay = new AnimationCurve();

	public float m_perfectDodgeAdrenaline = 10f;

	public float m_perfectDodgeStaminaReturnMultiplier;

	public float m_attackMissAdrenaline = -5f;

	public float m_nonBlockDamageAdrenaline = -5f;

	public float m_staggerEnemyAdrenaline = 5f;

	public AnimationCurve m_adrenalineGainMultiplier = new AnimationCurve();

	public List<StatusEffectLevel> m_adrenalineEffects = new List<StatusEffectLevel>();

	public EffectList m_adrenalinePopEffects = new EffectList();

	[Header("Eitr")]
	public float m_eiterRegen = 5f;

	public float m_eitrRegenDelay = 1f;

	[Header("Deep Snow Player")]
	public GameObject m_snowShovelStrong;

	public GameObject m_snowShovelDefault;

	public EffectList m_snowShovelEffect;

	public EffectList m_snowWalkPiecelEffect;

	public float m_deepSnowBuildHeight = 0.15f;

	[Header("Player Effects")]
	public EffectList m_drownEffects = new EffectList();

	public EffectList m_spawnEffects = new EffectList();

	public EffectList m_removeEffects = new EffectList();

	public EffectList m_dodgeEffects = new EffectList();

	public EffectList m_autopickupEffects = new EffectList();

	public EffectList m_skillLevelupEffects = new EffectList();

	public EffectList m_equipStartEffects = new EffectList();

	public EffectList m_perfectDodgeEffects = new EffectList();

	[Header("Cold Breath Particles")]
	public ParticleSystem m_breathParticles;

	public float m_lowStaminaBreathTime = 1f;

	public float m_highStaminaBreathTime = 3.5f;

	[Space(16f)]
	private Skills m_skills;

	private PieceTable m_buildPieces;

	private bool m_noPlacementCost;

	private const bool m_hideUnavailable = false;

	private static bool m_enableAutoPickup = true;

	private readonly HashSet<string> m_knownRecipes = new HashSet<string>();

	private readonly Dictionary<string, int> m_knownStations = new Dictionary<string, int>();

	private readonly HashSet<string> m_knownMaterial = new HashSet<string>();

	private readonly HashSet<string> m_shownTutorials = new HashSet<string>();

	private readonly HashSet<string> m_uniques = new HashSet<string>();

	private readonly HashSet<string> m_trophies = new HashSet<string>();

	private readonly HashSet<string> m_knownBiome = new HashSet<string>();

	private readonly Dictionary<string, string> m_knownTexts = new Dictionary<string, string>();

	private float m_stationDiscoverTimer;

	private bool m_debugFly;

	private bool m_godMode;

	private bool m_ghostMode;

	private bool m_crownMode;

	private float m_lookPitch;

	private const int m_maxFoods = 3;

	private const float m_foodDrainPerSec = 0.1f;

	private float m_foodUpdateTimer;

	private float m_foodRegenTimer;

	private readonly List<Food> m_foods = new List<Food>();

	private float m_stamina = 100f;

	private float m_maxStamina = 100f;

	private float m_breathParticleTimer;

	private float m_staminaRegenTimer;

	private float m_adrenaline;

	private float m_adrenalineDegenTimer;

	private float m_adrenalineGuardianPower = 10f;

	private float m_eitr;

	private float m_maxEitr;

	private float m_eitrRegenTimer;

	private string m_guardianPower = "";

	private int m_guardianPowerHash;

	public float m_guardianPowerCooldown;

	private StatusEffect m_guardianSE;

	private float m_placePressedTime = -1000f;

	private float m_removePressedTime = -1000f;

	private bool m_blockRemove;

	private float m_lastToolUseTime;

	private GameObject m_placementMarkerInstance;

	private GameObject m_placementGhost;

	private string m_placementGhostLast;

	private PlacementStatus m_placementStatus = PlacementStatus.Invalid;

	private float m_placeRotationDegrees = 22.5f;

	private int m_placeRotation;

	public float m_scrollAmountThreshold = 0.1f;

	private bool m_waitForScrollReturn;

	private int m_buildRemoveDebt;

	private int m_placeRayMask;

	private int m_placeGroundRayMask;

	private int m_placeWaterRayMask;

	private int m_removeRayMask;

	private int m_interactMask;

	private int m_autoPickupMask;

	private int m_terrainMask;

	private int m_pieceMask;

	private readonly List<MinorActionData> m_actionQueue = new List<MinorActionData>();

	private float m_actionQueuePause;

	private string m_actionAnimation;

	private GameObject m_hovering;

	private Character m_hoveringCreature;

	private float m_lastHoverInteractTime;

	private bool m_pvp;

	private float m_updateCoverTimer;

	private float m_coverPercentage;

	private bool m_underRoof = true;

	private float m_nearFireTimer;

	private bool m_isLoading;

	private ItemDrop.ItemData m_weaponLoaded;

	private float m_queuedAttackTimer;

	private float m_queuedSecondAttackTimer;

	private float m_queuedDodgeTimer;

	private Vector3 m_queuedDodgeDir = Vector3.zero;

	private bool m_inDodge;

	private bool m_dodgeInvincible;

	private CraftingStation m_currentStation;

	private bool m_inCraftingStation;

	private Ragdoll m_ragdoll;

	private Piece m_hoveringPiece;

	private Dictionary<Material, float> m_ghostRippleDistance = new Dictionary<Material, float>();

	private bool m_toggleBlock;

	private bool m_attackTowardsPlayerLookDir;

	public float m_blockReload;

	public float m_grappling;

	private string m_emoteState = "";

	private int m_emoteID;

	private bool m_intro;

	private bool m_crouchToggled;

	public bool m_autoRun;

	private bool m_safeInHome;

	private IDoodadController m_doodadController;

	private bool m_attached;

	private string m_attachAnimation = "";

	private bool m_sleeping;

	private bool m_attachedToShip;

	private Transform m_attachPoint;

	private Vector3 m_detachOffset = Vector3.zero;

	private Transform m_attachPointCamera;

	private Collider[] m_attachColliders;

	private int m_modelIndex;

	private Vector3 m_skinColor = Vector3.one;

	private Vector3 m_hairColor = Vector3.one;

	private bool m_teleporting;

	private bool m_distantTeleport;

	private float m_teleportTimer;

	private float m_teleportCooldown;

	private Vector3 m_teleportFromPos;

	private Quaternion m_teleportFromRot;

	private Vector3 m_teleportTargetPos;

	private Quaternion m_teleportTargetRot;

	private bool m_beenHitWhileDodging;

	private Heightmap.Biome m_currentBiome;

	private BiomeSector m_currentBiomeData;

	private float m_biomeTimer;

	private List<string> m_tempUniqueKeys = new List<string>();

	private Collider[] m_colliders = new Collider[100];

	private RaycastHit[] m_raycastHits = new RaycastHit[100];

	private Dictionary<int, int> m_itemUpgradeSeeds = new Dictionary<int, int>();

	private int m_baseValue;

	private int m_baseValueOld = -1;

	private int m_comfortLevel;

	private float m_drownDamageTimer;

	private float m_timeSinceTargeted;

	private float m_timeSinceSensed;

	private float m_stealthFactorUpdateTimer;

	private float m_stealthFactor;

	private float m_stealthFactorTarget;

	private Vector3 m_lastStealthPosition = Vector3.zero;

	private float m_lastVelocity;

	private float m_timeSinceReloadFinished;

	private float m_repeatingMagicAdaptiveTriggerDelayTimer;

	private float m_wakeupTimer = -1f;

	private float m_timeSinceDeath = 999999f;

	private float m_runSkillImproveTimer;

	private float m_swimSkillImproveTimer;

	private float m_sneakSkillImproveTimer;

	private int m_lastBuildStateFrames = 99999;

	private int m_manualSnapPoint = -1;

	private readonly List<PieceTable> m_tempOwnedPieceTables = new List<PieceTable>();

	private readonly List<Transform> m_tempSnapPoints1 = new List<Transform>();

	private readonly List<Transform> m_tempSnapPoints2 = new List<Transform>();

	private readonly List<Piece> m_tempPieces = new List<Piece>();

	[HideInInspector]
	public Dictionary<string, string> m_customData = new Dictionary<string, string>();

	private static int s_attackMask = 0;

	private static readonly int s_crouching = ZSyncAnimation.GetHash("crouching");

	private static readonly int s_animatorTagDodge = ZSyncAnimation.GetHash("dodge");

	private static readonly int s_animatorTagCutscene = ZSyncAnimation.GetHash("cutscene");

	private static readonly int s_animatorTagCrouch = ZSyncAnimation.GetHash("crouch");

	private static readonly int s_animatorTagMinorAction = ZSyncAnimation.GetHash("minoraction");

	private static readonly int s_animatorTagMinorActionFast = ZSyncAnimation.GetHash("minoraction_fast");

	private static readonly int s_animatorTagEmote = ZSyncAnimation.GetHash("emote");

	public const string BaseValueKey = "baseValue";

	private int m_cachedFrame;

	private bool m_cachedAttack;

	private bool m_dodgeInvincibleCached;

	[Header("Seasonal Items")]
	[SerializeField]
	private List<SeasonalItemGroup> m_seasonalItemGroups = new List<SeasonalItemGroup>();

	private SeasonalItemGroup m_currentSeason;

	private readonly RaycastHit[] m_raycastHoverHits = new RaycastHit[64];

	public bool PlacementCostDisabled => m_noPlacementCost;

	public bool AttackTowardsPlayerLookDir
	{
		get
		{
			return m_attackTowardsPlayerLookDir;
		}
		set
		{
			m_attackTowardsPlayerLookDir = value;
		}
	}

	public bool ToggleBlock
	{
		get
		{
			return m_toggleBlock;
		}
		set
		{
			m_toggleBlock = value;
		}
	}

	public bool AlternativePlacementActive => m_altPlace;

	public SeasonalItemGroup CurrentSeason => m_currentSeason;

	protected override void Awake()
	{
		base.Awake();
		s_players.Add(this);
		m_skills = GetComponent<Skills>();
		SetupAwake();
		m_equipmentModifierValues = new float[s_equipmentModifierSources.Length];
		if (s_equipmentModifierSourceFields == null)
		{
			s_equipmentModifierSourceFields = new FieldInfo[s_equipmentModifierSources.Length];
			for (int i = 0; i < s_equipmentModifierSources.Length; i++)
			{
				s_equipmentModifierSourceFields[i] = typeof(ItemDrop.ItemData.SharedData).GetField(s_equipmentModifierSources[i], BindingFlags.Instance | BindingFlags.Public);
			}
			if (s_equipmentModifierSources.Length != s_equipmentModifierTooltips.Length)
			{
				ZLog.LogError("Equipment modifier tooltip missmatch in player!");
			}
		}
		if (m_nview.GetZDO() == null)
		{
			return;
		}
		m_placeRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
		m_placeWaterRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "Water", "vehicle");
		m_removeRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
		m_interactMask = LayerMask.GetMask("item", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "character", "character_net", "terrain", "vehicle", "character_ghost");
		m_autoPickupMask = LayerMask.GetMask("item");
		m_terrainMask = LayerMask.GetMask("terrain");
		m_pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
		Inventory inventory = m_inventory;
		inventory.m_onChanged = (Action)Delegate.Combine(inventory.m_onChanged, new Action(OnInventoryChanged));
		if (s_attackMask == 0)
		{
			s_attackMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
		}
		m_nview.Register("OnDeath", RPC_OnDeath);
		m_nview.Register("RPC_HitWhileDodging", RPC_HitWhileDodging);
		if (m_nview.IsOwner())
		{
			m_nview.Register<int, string, int>("Message", RPC_Message);
			m_nview.Register<bool, bool>("OnTargeted", RPC_OnTargeted);
			m_nview.Register<float>("UseStamina", RPC_UseStamina);
			if ((bool)MusicMan.instance)
			{
				MusicMan.instance.TriggerMusic("Wakeup");
			}
			UpdateKnownRecipesList();
			UpdateAvailablePiecesList();
			SetupPlacementGhost();
			m_dodgeInvincibleCached = m_nview.GetZDO().GetBool(ZDOVars.s_dodgeinv);
		}
		m_placeRotation = UnityEngine.Random.Range(0, 16);
		float f = UnityEngine.Random.Range(0f, MathF.PI * 2f);
		SetLookDir(new Vector3(Mathf.Cos(f), 0f, Mathf.Sin(f)));
		FaceLookDirection();
		AddQueuedKeys();
		UpdateCurrentSeason();
		m_attackTowardsPlayerLookDir = PlatformPrefs.GetInt("AttackTowardsPlayerLookDir", 1) == 1;
		m_toggleBlock = PlayerPrefs.GetInt("ToggleBlock", 0) == 1;
	}

	protected override void OnEnable()
	{
		base.OnEnable();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
	}

	public void SetLocalPlayer()
	{
		if (!(m_localPlayer == this))
		{
			m_localPlayer = this;
			m_localPlayerExists = true;
			Game.instance.IncrementPlayerStat(PlayerStatType.WorldLoads);
			ZNet.instance.SetReferencePosition(base.transform.position);
			EnvMan.instance.SetForceEnvironment("");
			AddQueuedKeys();
		}
	}

	private void AddQueuedKeys()
	{
		if (m_addUniqueKeyQueue.Count <= 0)
		{
			return;
		}
		foreach (string item in m_addUniqueKeyQueue)
		{
			AddUniqueKey(item);
		}
		m_addUniqueKeyQueue.Clear();
	}

	public void SetPlayerID(long playerID, string name)
	{
		if (m_nview.GetZDO() != null && GetPlayerID() == 0L)
		{
			m_nview.GetZDO().Set(ZDOVars.s_playerID, playerID);
			m_nview.GetZDO().Set(ZDOVars.s_playerName, name);
		}
	}

	public long GetPlayerID()
	{
		if (!m_nview.IsValid())
		{
			return 0L;
		}
		return m_nview.GetZDO().GetLong(ZDOVars.s_playerID, 0L);
	}

	public string GetPlayerName()
	{
		if (!m_nview.IsValid())
		{
			return "";
		}
		return m_nview.GetZDO().GetString(ZDOVars.s_playerName, "...");
	}

	public override string GetHoverText()
	{
		return "";
	}

	public override string GetHoverName()
	{
		return CensorShittyWords.FilterUGC(GetPlayerName(), UGCType.CharacterName, GetPlayerID());
	}

	protected override void Start()
	{
		base.Start();
		InvalidateCachedLiquidDepth();
	}

	protected override void OnDestroy()
	{
		ZDO zDO = m_nview.GetZDO();
		if (zDO != null && ZNet.instance != null)
		{
			ZLog.LogWarning("Player destroyed sec:" + zDO.GetSector().ToString() + "  pos:" + base.transform.position.ToString() + "  zdopos:" + zDO.GetPosition().ToString() + "  ref " + ZNet.instance.GetReferencePosition().ToString());
		}
		if ((bool)m_placementGhost)
		{
			UnityEngine.Object.Destroy(m_placementGhost);
			m_placementGhost = null;
		}
		base.OnDestroy();
		s_players.Remove(this);
		if (m_localPlayer == this)
		{
			ZLog.LogWarning("Local player destroyed");
			m_localPlayerExists = false;
			m_localPlayer = null;
		}
	}

	private void FixedUpdate()
	{
		float fixedDeltaTime = Time.fixedDeltaTime;
		UpdateAwake(fixedDeltaTime);
		if (m_nview.GetZDO() == null)
		{
			return;
		}
		UpdateTargeted(fixedDeltaTime);
		UpdateBreathParticles(fixedDeltaTime);
		if (!m_nview.IsOwner())
		{
			return;
		}
		if (m_localPlayer != this)
		{
			ZLog.Log("Destroying old local player");
			ZNetScene.instance.Destroy(base.gameObject);
		}
		else if (!IsDead())
		{
			UpdateActionQueue(fixedDeltaTime);
			PlayerAttackInput(fixedDeltaTime);
			UpdateAttach();
			UpdateDoodadControls(fixedDeltaTime);
			UpdateCrouch(fixedDeltaTime);
			UpdateDodge(fixedDeltaTime);
			UpdateCover(fixedDeltaTime);
			UpdateStations(fixedDeltaTime);
			UpdateGuardianPower(fixedDeltaTime);
			UpdateBaseValue(fixedDeltaTime);
			UpdateStats(fixedDeltaTime);
			UpdateTeleport(fixedDeltaTime);
			AutoPickup(fixedDeltaTime);
			EdgeOfWorldKill(fixedDeltaTime);
			UpdateBiome(fixedDeltaTime);
			UpdateStealth(fixedDeltaTime);
			if ((bool)GameCamera.instance && m_attachPointCamera == null && Vector3.Distance(GameCamera.instance.transform.position, base.transform.position) < 2f)
			{
				SetVisible(visible: false);
			}
			AudioMan.instance.SetIndoor(InShelter() || ShieldGenerator.IsInsideShield(base.transform.position));
		}
	}

	private void Update()
	{
		bool flag = InventoryGui.IsVisible();
		if (ZInput.InputLayout != InputLayout.Default && ZInput.IsGamepadActive() && !flag && ZInput.GetButtonUp("JoyAltPlace") && ZInput.GetButton("JoyAltKeys"))
		{
			m_altPlace = !m_altPlace;
			if (MessageHud.instance != null)
			{
				string text = Localization.instance.Localize("$hud_altplacement");
				string text2 = (m_altPlace ? Localization.instance.Localize("$hud_on") : Localization.instance.Localize("$hud_off"));
				MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text + " " + text2);
			}
		}
		UpdateClothFix();
		if (Terminal.m_showTests)
		{
			Terminal.m_testList["Player Deepnorth Fade"] = WorldGenerator.DeepNorthWaveFade(base.transform.position.x, base.transform.position.z).ToString("0.00") + " / " + WorldGenerator.CreateDeepNorthGap(base.transform.position.x, base.transform.position.z).ToString("0.00");
		}
		if (!m_nview.IsValid() || !m_nview.IsOwner())
		{
			return;
		}
		bool flag2 = TakeInput();
		m_lastBuildStateFrames++;
		UpdateHover();
		bool num;
		if (flag2)
		{
			if (m_debugMode && Console.instance.IsCheatsEnabled())
			{
				if (ZInput.GetKeyDown(KeyCode.Z))
				{
					ToggleDebugFly();
				}
				if (ZInput.GetKeyDown(KeyCode.B))
				{
					ToggleNoPlacementCost();
				}
				if (ZInput.GetKeyDown(KeyCode.K))
				{
					Console.instance.TryRunCommand("killenemies");
				}
				if (ZInput.GetKeyDown(KeyCode.L))
				{
					Console.instance.TryRunCommand("removedrops");
				}
			}
			bool alt = ((ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()) ? ZInput.GetButton("JoyAltKeys") : (ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace")));
			if ((ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) && !Hud.InRadial())
			{
				if ((bool)m_hovering)
				{
					Interact(m_hovering, hold: false, alt);
				}
				else if (m_doodadController != null)
				{
					StopDoodadControl();
				}
			}
			else if ((ZInput.GetButton("Use") || ZInput.GetButton("JoyUse")) && !Hud.InRadial() && (bool)m_hovering)
			{
				Interact(m_hovering, hold: true, alt);
			}
			bool flag3 = !Hud.InRadial() && ZInput.GetButtonUp("JoyHide") && ZInput.GetButtonLastPressedTimer("JoyHide") < 0.33f;
			if (ZInput.InputLayout == InputLayout.Default || !ZInput.IsGamepadActive())
			{
				if (ZInput.GetButtonDown("Hide"))
				{
					goto IL_0320;
				}
				if (flag3 && !ZInput.GetButton("JoyAltKeys"))
				{
					num = !InPlaceMode();
					goto IL_031e;
				}
			}
			else if (!InPlaceMode() && flag3)
			{
				num = !ZInput.GetButton("JoyAltKeys");
				goto IL_031e;
			}
			goto IL_036b;
		}
		goto IL_04de;
		IL_031e:
		if (num)
		{
			goto IL_0320;
		}
		goto IL_036b;
		IL_04de:
		UpdateControllerTriggerFeedback(flag2);
		UpdateGyro(flag2);
		if (m_blockReload > 0f)
		{
			m_blockReload -= Time.deltaTime;
		}
		if (m_grappling > 0f)
		{
			if (m_grapplingStaminaDrain > 0f && !IsOnGround())
			{
				UseStamina(Time.deltaTime * m_grapplingStaminaDrain);
			}
			m_grappling -= Time.deltaTime;
		}
		UpdatePlacement(flag2, Time.deltaTime);
		UpdateStats();
		return;
		IL_0320:
		if (GetRightItem() != null || GetLeftItem() != null)
		{
			if (!InAttack() && !InDodge())
			{
				HideHandItems();
			}
		}
		else if ((!IsSwimming() || IsOnGround()) && !InDodge())
		{
			ShowHandItems();
		}
		goto IL_036b;
		IL_036b:
		if (ZInput.GetButtonDown("ToggleWalk") && !Hud.InRadial())
		{
			SetWalk(!GetWalk());
			if (GetWalk())
			{
				Message(MessageHud.MessageType.TopLeft, "$msg_walk $hud_on");
			}
			else
			{
				Message(MessageHud.MessageType.TopLeft, "$msg_walk $hud_off");
			}
		}
		HandleRadialInput();
		bool flag4 = ZInput.IsGamepadActive() && !ZInput.GetButton("JoyAltKeys");
		bool flag5 = ZInput.InputLayout == InputLayout.Default && ZInput.GetButtonDown("JoyGP");
		bool flag6 = ZInput.IsNonClassicFunctionality() && ZInput.GetButton("JoyLStick") && ZInput.GetButton("JoyRStick");
		if (!Hud.InRadial() && !Hud.IsPieceSelectionVisible() && (ZInput.GetButtonDown("GP") || (flag4 && (flag5 || flag6))))
		{
			StartGuardianPower();
		}
		bool flag7 = ZInput.GetButtonDown("JoyAutoPickup") && ZInput.GetButton("JoyAltKeys");
		if (ZInput.GetButtonDown("AutoPickup") || flag7)
		{
			m_enableAutoPickup = !m_enableAutoPickup;
			Message(MessageHud.MessageType.TopLeft, "$hud_autopickup:" + (m_enableAutoPickup ? "$hud_on" : "$hud_off"));
		}
		for (int i = 1; i <= 8; i++)
		{
			if (ZInput.GetButtonDown($"Hotbar{i}") || ZInput.GetButtonDown($"Hotbar{i}Alt"))
			{
				UseHotbarItem(i);
			}
		}
		goto IL_04de;
	}

	private void UpdateControllerTriggerFeedback(bool takeInput)
	{
		if (!ControllerTriggerFeedback.IsEnabled)
		{
			return;
		}
		if (!takeInput)
		{
			ControllerTriggerFeedback.DisableTriggerFeedback();
			return;
		}
		string actionPath = ZInput.instance.GetButtonDef("JoyAttack").GetActionPath();
		if (actionPath != null)
		{
			string[] array = actionPath.Split('/');
			if (array.Length > 1 && array[1] != "rightTrigger")
			{
				return;
			}
		}
		ItemDrop.ItemData leftItem = GetLeftItem();
		ItemDrop.ItemData rightItem = GetRightItem();
		bool num = leftItem != null && leftItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;
		ControllerTriggerFeedback.FeedbackMode triggerFeedback = ControllerTriggerFeedback.FeedbackMode.SlopeFeedback;
		if (num && leftItem.m_shared.m_skillType == Skills.SkillType.Crossbows)
		{
			triggerFeedback = ControllerTriggerFeedback.FeedbackMode.Weapon;
		}
		bool flag = (rightItem != null && rightItem.m_shared.m_attack.m_attackEitr > 0f) || (rightItem != null && rightItem.m_shared.m_attack.m_reloadEitrDrain > 0f) || (leftItem != null && leftItem.m_shared.m_attack.m_attackEitr > 0f);
		bool flag2 = false;
		if (flag)
		{
			ItemDrop.ItemData itemData = rightItem ?? leftItem;
			Attack attack = itemData.m_shared.m_attack;
			float num2 = 0.9f;
			if (attack.m_burstInterval > 0f && (double)attack.m_burstInterval <= 0.5)
			{
				int vibrationFrequency = (int)(1f / attack.m_burstInterval);
				bool flag3 = InAttack();
				ControllerTriggerFeedback.SetVibrationFrequency(vibrationFrequency);
				flag2 = GetEitr() > attack.GetAttackEitr(this, itemData) * num2;
				if (flag3)
				{
					m_repeatingMagicAdaptiveTriggerDelayTimer += Time.unscaledDeltaTime;
				}
				else
				{
					m_repeatingMagicAdaptiveTriggerDelayTimer = 0f;
				}
				triggerFeedback = ((flag3 && m_repeatingMagicAdaptiveTriggerDelayTimer > 0.5f) ? ControllerTriggerFeedback.FeedbackMode.Vibration : ControllerTriggerFeedback.FeedbackMode.None);
			}
			else if (attack.m_requiresReload)
			{
				triggerFeedback = ControllerTriggerFeedback.FeedbackMode.Weapon;
				if (IsWeaponLoaded())
				{
					flag2 = true;
				}
			}
			else
			{
				triggerFeedback = ControllerTriggerFeedback.FeedbackMode.Feedback;
				flag2 = GetEitr() > attack.GetAttackEitr(this, itemData) * num2;
			}
		}
		bool flag4 = IsReloadActionQueued();
		if (flag4)
		{
			m_timeSinceReloadFinished = 0f;
		}
		else
		{
			m_timeSinceReloadFinished += Time.unscaledDeltaTime;
		}
		if (num && !flag4 && m_timeSinceReloadFinished > 0.5f)
		{
			ControllerTriggerFeedback.SetTriggerFeedback(triggerFeedback);
		}
		else if (flag && flag2 && !flag4)
		{
			ControllerTriggerFeedback.SetTriggerFeedback(triggerFeedback);
		}
		else
		{
			ControllerTriggerFeedback.DisableTriggerFeedback();
		}
	}

	private void UpdateGyro(bool takeInput)
	{
		if (ZInput.PlatformSupportsGyro(PlatformManager.DistributionPlatform.Platform) && takeInput && GamepadMotionSensor.m_motionSensorActive)
		{
			Vector2 gyro = ZInput.instance.GetGyro(GamepadMotionSensor.m_motionSensTighteningThreshold);
			Vector2 scale = new Vector2(GamepadSettings.m_motionSensorXAxisSensitivity, GamepadSettings.m_motionSensorYAxisSensitivity);
			gyro.Scale(scale);
			SetMouseLook(gyro);
		}
	}

	private void UpdateClothFix()
	{
		float magnitude = GetVelocity().magnitude;
		if (magnitude > 0.01f && m_lastVelocity < 0.01f)
		{
			ResetCloth();
			Terminal.Increment("resetcloth");
		}
		m_lastVelocity = magnitude;
	}

	private bool CheckKeyboardRadialPressed()
	{
		if (!ZInput.GetButtonDown("OpenRadial"))
		{
			return ZInput.GetButtonDown("OpenEmote");
		}
		return true;
	}

	private bool CheckGamepadRadialBuildPressed()
	{
		return false;
	}

	private void HandleRadialInput()
	{
		if (!Hud.InRadial())
		{
			if (!Hud.instance.m_radialMenu.CanOpen)
			{
				Hud.instance.m_radialMenu.CanOpen = ZInput.GetButtonDown("JoyRadial") || CheckGamepadRadialBuildPressed() || CheckKeyboardRadialPressed();
			}
			bool flag = false;
			bool flag2 = ZInput.GetButtonPressedTimer("JoyRadial") > 0.33f;
			if ((!ZInput.GetButton("JoyAltKeys") && flag) || (!ZInput.GetButton("JoyAltKeys") && flag2 && !InPlaceMode()) || CheckKeyboardRadialPressed())
			{
				if (!Hud.IsPieceSelectionVisible())
				{
					Hud.instance.m_radialMenu.Open(Hud.instance.m_config);
				}
			}
			else if ((!InPlaceMode() && !ZInput.GetButton("JoyAltKeys") && ZInput.GetButtonUp("JoySit")) || ZInput.GetButtonDown("Sit"))
			{
				if (InEmote() && IsSitting())
				{
					StopEmote();
				}
				else
				{
					StartEmote("sit", oneshot: false);
				}
			}
		}
		else if (!Hud.instance.m_radialMenu.CanThrow && (ZInput.GetButtonDown("JoyRadialSecondaryInteract") || ZInput.GetButtonDown("RadialSecondaryInteract")))
		{
			Hud.instance.m_radialMenu.CanThrow = true;
		}
	}

	private void UpdateStats()
	{
		if (IsDebugFlying())
		{
			return;
		}
		m_statCheck += Time.deltaTime;
		if (m_statCheck < 2.5f)
		{
			return;
		}
		m_statCheck = 0f;
		PlayerProfile playerProfile = Game.instance.GetPlayerProfile();
		m_timeSinceLastCheck += 2.5f;
		float num = Vector3.Distance(base.transform.position, m_lastDistCheck);
		if (!(num > 1f))
		{
			return;
		}
		playerProfile.IncrementStat(IsSafeInHome() ? PlayerStatType.TimeInBase : PlayerStatType.TimeOutOfBase, m_timeSinceLastCheck);
		if (num < 60f)
		{
			playerProfile.IncrementStat(PlayerStatType.DistanceTraveled, num);
			if (Ship.GetLocalShip() != null)
			{
				if (m_localPlayer.GetControlledShip() != null)
				{
					playerProfile.IncrementStat(PlayerStatType.DistanceSailHelm, num);
				}
				playerProfile.IncrementStat(PlayerStatType.DistanceSail, num);
			}
			else if (IsOnGround())
			{
				playerProfile.IncrementStat(IsRunning() ? PlayerStatType.DistanceRun : PlayerStatType.DistanceWalk, num);
			}
			else
			{
				playerProfile.IncrementStat(PlayerStatType.DistanceAir, num);
			}
		}
		if (base.transform.position.z > 10350f)
		{
			Game.instance.IncrementPlayerStat(PlayerStatType.ExploreNorth, m_timeSinceLastCheck);
			if (Achievements.IsCleanNoMap())
			{
				Game.instance.IncrementPlayerStat(PlayerStatType.ExploreNorthNoMap, m_timeSinceLastCheck);
			}
		}
		if (base.transform.position.z < -10350f)
		{
			Game.instance.IncrementPlayerStat(PlayerStatType.ExploreSouth, m_timeSinceLastCheck);
			if (Achievements.IsCleanNoMap())
			{
				Game.instance.IncrementPlayerStat(PlayerStatType.ExploreSouthNoMap, m_timeSinceLastCheck);
			}
		}
		if (base.transform.position.x < -10350f)
		{
			Game.instance.IncrementPlayerStat(PlayerStatType.ExploreEast, m_timeSinceLastCheck);
			if (Achievements.IsCleanNoMap())
			{
				Game.instance.IncrementPlayerStat(PlayerStatType.ExploreEastNoMap, m_timeSinceLastCheck);
			}
		}
		if (base.transform.position.x > 10350f)
		{
			Game.instance.IncrementPlayerStat(PlayerStatType.ExploreWest, m_timeSinceLastCheck);
			if (Achievements.IsCleanNoMap())
			{
				Game.instance.IncrementPlayerStat(PlayerStatType.ExploreWestNoMap, m_timeSinceLastCheck);
			}
		}
		m_timeSinceLastCheck = 0f;
		m_lastDistCheck = base.transform.position;
	}

	private float GetBuildStamina()
	{
		float attackStamina = GetRightItem().m_shared.m_attack.m_attackStamina;
		attackStamina *= 1f + GetEquipmentHomeItemModifier();
		m_seman.ModifyHomeItemStaminaUsage(attackStamina, ref attackStamina);
		if (m_buildPieces.m_skill != Skills.SkillType.None)
		{
			float skillFactor = GetSkillFactor(m_buildPieces.m_skill);
			attackStamina -= attackStamina * 0.5f * skillFactor;
		}
		return attackStamina;
	}

	private void UpdatePlacement(bool takeInput, float dt)
	{
		UpdateWearNTearHover();
		ItemDrop.ItemData rightItem;
		Piece hoveringPiece;
		object obj;
		if (InPlaceMode() && !IsDead())
		{
			if (!takeInput)
			{
				return;
			}
			UpdateBuildGuiInput();
			if (Hud.IsPieceSelectionVisible())
			{
				return;
			}
			rightItem = GetRightItem();
			if ((ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltKeys")) && (ZInput.GetButtonDown("JoyLStick") || ZInput.GetButtonDown("JoyRStick") || ZInput.GetButtonDown("JoyButtonA") || ZInput.GetButtonDown("JoyButtonB") || ZInput.GetButtonDown("JoyButtonX") || ZInput.GetButtonDown("JoyButtonY") || ZInput.GetButtonDown("JoyDPadUp") || ZInput.GetButtonDown("JoyDPadDown") || ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyDPadRight")))
			{
				m_blockRemove = true;
			}
			if ((ZInput.GetButtonDown("Remove") || ZInput.GetButtonDown("JoyRemove")) && (ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltKeys")) && (ZInput.InputLayout == InputLayout.Default || !ZInput.IsGamepadActive()))
			{
				CopyPiece();
				m_blockRemove = true;
			}
			else if (!m_blockRemove && ((!ZInput.IsGamepadActive() && ZInput.GetButtonUp("Remove")) || ZInput.GetButtonUp("JoyRemove")))
			{
				m_removePressedTime = Time.time;
			}
			if (!ZInput.GetButton("AltPlace") && !ZInput.GetButton("JoyAltKeys"))
			{
				m_blockRemove = false;
			}
			hoveringPiece = GetHoveringPiece();
			if ((bool)hoveringPiece)
			{
				Feast component = hoveringPiece.GetComponent<Feast>();
				if ((object)component != null)
				{
					obj = component;
					goto IL_0188;
				}
			}
			obj = null;
			goto IL_0188;
		}
		if ((bool)m_placementGhost)
		{
			m_placementGhost.SetActive(value: false);
		}
		return;
		IL_0188:
		Feast feast = (Feast)obj;
		object obj2;
		if ((bool)hoveringPiece)
		{
			ItemDrop component2 = hoveringPiece.GetComponent<ItemDrop>();
			if ((object)component2 != null)
			{
				obj2 = component2;
				goto IL_01a2;
			}
		}
		obj2 = null;
		goto IL_01a2;
		IL_01a2:
		ItemDrop itemDrop = (ItemDrop)obj2;
		bool flag = (rightItem.m_shared.m_buildPieces.m_canRemovePieces && (!hoveringPiece || (!feast && (!itemDrop || !itemDrop.IsPiece())))) || (rightItem.m_shared.m_buildPieces.m_canRemoveFeasts && (!hoveringPiece || (bool)feast || ((bool)itemDrop && itemDrop.IsPiece())));
		if (Time.time - m_removePressedTime < 0.2f && flag && Time.time - m_lastToolUseTime > m_removeDelay)
		{
			m_removePressedTime = -9999f;
			if (HaveStamina(rightItem.m_shared.m_attack.m_attackStamina))
			{
				if (RemovePiece())
				{
					m_lastToolUseTime = Time.time;
					AddNoise(50f);
					UseStamina(GetBuildStamina());
					if (m_buildPieces.m_skill != Skills.SkillType.None && m_buildRemoveDebt < 20)
					{
						m_buildRemoveDebt++;
						if (m_buildPieces.m_skill == Skills.SkillType.Crafting)
						{
							Game.instance.IncrementPlayerStat(PlayerStatType.BuildPiecesRemoved);
						}
					}
					if (rightItem.m_shared.m_useDurability)
					{
						rightItem.m_durability -= GetPlaceDurability(rightItem) * Game.m_durabilityRate;
					}
					rightItem.m_shared.m_destroyEffect.Create(hoveringPiece.transform.position, Quaternion.identity, null, 1f, -1, GetZDOID());
				}
			}
			else
			{
				Hud.instance.StaminaBarEmptyFlash();
			}
		}
		if ((ZInput.GetButtonDown("Attack") || ZInput.GetButtonDown("JoyPlace")) && !Hud.InRadial())
		{
			m_placePressedTime = Time.time;
		}
		if (Time.time - m_placePressedTime < 0.2f && Time.time - m_lastToolUseTime > m_placeDelay)
		{
			m_placePressedTime = -9999f;
			if (ZInput.GetButton("JoyAltKeys"))
			{
				CopyPiece();
				m_blockRemove = true;
			}
			else
			{
				Piece selectedPiece = m_buildPieces.GetSelectedPiece();
				if (selectedPiece != null)
				{
					if (HaveStamina(rightItem.m_shared.m_attack.m_attackStamina))
					{
						if (selectedPiece.m_repairPiece)
						{
							Repair(rightItem, selectedPiece);
						}
						else if (selectedPiece.m_removePiece && flag)
						{
							RemovePiece();
						}
						else if (m_placementGhost != null)
						{
							if (m_noPlacementCost || HaveRequirements(selectedPiece, RequirementMode.CanBuild))
							{
								if (TryPlacePiece(selectedPiece))
								{
									Hud.instance.m_buildUi.AddRecentPiece(GetSelectedPiece());
									m_lastToolUseTime = Time.time;
									if (!ZoneSystem.instance.GetGlobalKey(selectedPiece.FreeBuildKey()))
									{
										ConsumeResources(selectedPiece.m_resources, 0);
									}
									UseStamina(GetBuildStamina());
									if (m_buildPieces.m_skill != Skills.SkillType.None)
									{
										if (m_buildPieces.m_skill == Skills.SkillType.Crafting)
										{
											Game.instance.IncrementPlayerStat(PlayerStatType.BuiltPieces);
										}
										if (m_buildRemoveDebt > 0)
										{
											m_buildRemoveDebt--;
										}
										else
										{
											RaiseSkill(m_buildPieces.m_skill);
											if (m_buildPieces.m_skill == Skills.SkillType.Crafting)
											{
												Game.instance.IncrementPlayerStat(PlayerStatType.BuiltPiecesNoDebt);
											}
										}
									}
									if (rightItem.m_shared.m_useDurability)
									{
										rightItem.m_durability -= GetPlaceDurability(rightItem) * Game.m_durabilityRate;
									}
									rightItem.m_shared.m_buildEffect.Create(base.transform.position, Quaternion.identity, null, 1f, -1, GetZDOID());
								}
							}
							else
							{
								Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
							}
						}
					}
					else
					{
						Hud.instance.StaminaBarEmptyFlash();
					}
				}
			}
		}
		if ((bool)m_placementGhost)
		{
			m_placementGhost.gameObject.GetComponent<IPieceMarker>()?.ShowBuildMarker();
		}
		if ((bool)hoveringPiece)
		{
			hoveringPiece.gameObject.GetComponent<IPieceMarker>()?.ShowHoverMarker();
		}
		if ((bool)m_placementGhost)
		{
			Piece component3 = m_placementGhost.GetComponent<Piece>();
			if ((object)component3 != null && component3.m_canRotate && m_placementGhost.activeInHierarchy)
			{
				m_scrollCurrAmount += ZInput.GetMouseScrollWheel();
				if (m_scrollCurrAmount > m_scrollAmountThreshold)
				{
					m_scrollCurrAmount = 0f;
					m_placeRotation++;
				}
				if (m_scrollCurrAmount < 0f - m_scrollAmountThreshold)
				{
					m_scrollCurrAmount = 0f;
					m_placeRotation--;
				}
			}
		}
		float num = 0f;
		bool flag2 = false;
		if (ZInput.IsGamepadActive())
		{
			switch (ZInput.InputLayout)
			{
			case InputLayout.Alternative1:
			case InputLayout.Alternative2:
			{
				bool button = ZInput.GetButton("JoyRotate");
				bool button2 = ZInput.GetButton("JoyRotateRight");
				flag2 = button || button2;
				if (button)
				{
					num = 0.5f;
				}
				else if (button2)
				{
					num = -0.5f;
				}
				break;
			}
			case InputLayout.Default:
				num = ZInput.GetJoyRightStickX();
				flag2 = ZInput.GetButton("JoyRotate") && Mathf.Abs(num) > 0.5f;
				break;
			}
		}
		if (flag2)
		{
			if (m_rotatePieceTimer == 0f)
			{
				if (num < 0f)
				{
					m_placeRotation++;
				}
				else
				{
					m_placeRotation--;
				}
			}
			else if (m_rotatePieceTimer > 0.25f)
			{
				if (num < 0f)
				{
					m_placeRotation++;
				}
				else
				{
					m_placeRotation--;
				}
				m_rotatePieceTimer = 0.17f;
			}
			m_rotatePieceTimer += dt;
		}
		else
		{
			m_rotatePieceTimer = 0f;
		}
		foreach (KeyValuePair<Material, float> item in m_ghostRippleDistance)
		{
			item.Key.SetFloat("_RippleDistance", ZInput.GetKey(KeyCode.LeftControl) ? item.Value : 0f);
		}
	}

	private void UpdateBreathParticles(float dt)
	{
		if (EnvMan.IsFreezing())
		{
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_stamina, m_maxStamina);
			m_breathParticleTimer += dt;
			float num2 = Mathf.Lerp(m_lowStaminaBreathTime, m_highStaminaBreathTime, num / GetMaxStamina());
			if (m_breathParticleTimer > num2)
			{
				m_breathParticleTimer = 0f;
				m_breathParticles.Emit(UnityEngine.Random.Range(4, 6));
				m_breathParticles.Play();
			}
		}
	}

	private float GetPlaceDurability(ItemDrop.ItemData tool)
	{
		float num = tool.m_shared.m_useDurabilityDrain;
		if (tool.m_shared.m_placementDurabilitySkill != Skills.SkillType.None)
		{
			float skillFactor = GetSkillFactor(tool.m_shared.m_placementDurabilitySkill);
			num -= num * tool.m_shared.m_placementDurabilityMax * skillFactor;
		}
		return num;
	}

	private void UpdateBuildGuiInputAlternative1()
	{
		if (!Hud.IsPieceSelectionVisible() && ZInput.GetButtonDown("JoyBuildMenu") && !PlayerController.HasInputDelay && !Hud.InRadial() && m_lastBuildStateFrames > 1)
		{
			m_lastBuildStateFrames = 0;
			for (int i = 0; i < m_buildPieces.m_selectedPiece.Length; i++)
			{
				m_buildPieces.m_lastSelectedPiece[i] = m_buildPieces.m_selectedPiece[i];
			}
			Hud.instance.TogglePieceSelection();
		}
	}

	private void UpdateBuildGuiInput()
	{
		if (ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive())
		{
			UpdateBuildGuiInputAlternative1();
		}
		else
		{
			if (Hud.IsPieceSelectionVisible())
			{
				return;
			}
			if (Hud.instance.IsQuickPieceSelectEnabled())
			{
				if (!Hud.IsPieceSelectionVisible() && ZInput.GetButtonDown("BuildMenu") && !PlayerController.HasInputDelay && !Hud.InRadial())
				{
					Hud.instance.TogglePieceSelection();
				}
			}
			else if (ZInput.GetButtonDown("BuildMenu") && !PlayerController.HasInputDelay && !Hud.InRadial())
			{
				Hud.instance.TogglePieceSelection();
			}
			if (ZInput.GetButtonDown("JoyUse") && !PlayerController.HasInputDelay && !Hud.InRadial())
			{
				Hud.instance.TogglePieceSelection();
			}
		}
	}

	public void PlayButtonSound()
	{
		if ((bool)m_localPlayer)
		{
			m_buttonEffects?.Create(m_localPlayer.transform.position, Quaternion.identity);
		}
	}

	public bool SetSelectedPiece(Piece p)
	{
		if (m_buildPieces.GetPieceIndex(p, out var index, out var category))
		{
			SetBuildCategory(category);
			SetSelectedPiece(index);
			return true;
		}
		return false;
	}

	public void SetSelectedPiece(Vector2Int p)
	{
		if ((bool)m_buildPieces && m_buildPieces.GetSelectedIndex() != p)
		{
			m_buildPieces.SetSelected(p);
			SetupPlacementGhost();
		}
	}

	public Piece GetPiece(Vector2Int p)
	{
		if (!(m_buildPieces != null))
		{
			return null;
		}
		return m_buildPieces.GetPiece(p);
	}

	public bool IsPieceAvailable(Piece piece)
	{
		if (m_buildPieces != null)
		{
			return m_buildPieces.IsPieceAvailable(piece);
		}
		return false;
	}

	public Piece GetSelectedPiece()
	{
		if (!(m_buildPieces != null))
		{
			return null;
		}
		return m_buildPieces.GetSelectedPiece();
	}

	private void LateUpdate()
	{
		if (m_nview.IsValid())
		{
			UpdateEmote();
			if (m_nview.IsOwner())
			{
				ZNet.instance.SetReferencePosition(base.transform.position);
				UpdatePlacementGhost(flashGuardStone: false);
			}
		}
	}

	public void UpdateEvents()
	{
		if (!RandEventSystem.instance)
		{
			return;
		}
		m_readyEvents.Clear();
		foreach (RandomEvent @event in RandEventSystem.instance.m_events)
		{
			if (RandEventSystem.instance.PlayerIsReadyForEvent(this, @event))
			{
				m_readyEvents.Add(@event.m_name);
			}
		}
		if ((bool)ZNet.instance)
		{
			RandEventSystem.SetRandomEventsNeedsRefresh();
			ZNet.instance.m_serverSyncedPlayerData["possibleEvents"] = string.Join(",", m_readyEvents);
		}
	}

	private void SetupAwake()
	{
		if (m_nview.GetZDO() == null)
		{
			m_animator.SetBool("wakeup", value: false);
			return;
		}
		bool flag = m_nview.GetZDO().GetBool(ZDOVars.s_wakeup, defaultValue: true);
		m_animator.SetBool("wakeup", flag);
		if (flag)
		{
			m_wakeupTimer = 0f;
		}
	}

	private void UpdateAwake(float dt)
	{
		if (!(m_wakeupTimer >= 0f))
		{
			return;
		}
		m_wakeupTimer += dt;
		if (m_wakeupTimer > 1f)
		{
			m_wakeupTimer = -1f;
			m_animator.SetBool("wakeup", value: false);
			if (m_nview.IsOwner())
			{
				m_nview.GetZDO().Set(ZDOVars.s_wakeup, value: false);
			}
		}
	}

	private void EdgeOfWorldKill(float dt)
	{
		if (!IsDead())
		{
			float num = Utils.DistanceXZ(Vector3.zero, base.transform.position);
			float num2 = 10420f;
			if (num > num2 && (IsSwimming() || base.transform.position.y < 30f))
			{
				Vector3 vector = Vector3.Normalize(base.transform.position);
				float num3 = Utils.LerpStep(num2, 10500f, num) * 10f;
				m_body.MovePosition(m_body.position + vector * num3 * dt);
			}
			if (num > num2 && base.transform.position.y < -10f)
			{
				HitData hitData = new HitData();
				hitData.m_damage.m_damage = 99999f;
				hitData.m_hitType = HitData.HitType.EdgeOfWorld;
				Damage(hitData);
			}
		}
	}

	private void AutoPickup(float dt)
	{
		if (IsTeleporting() || !m_enableAutoPickup)
		{
			return;
		}
		Vector3 vector = base.transform.position + Vector3.up;
		int num = Physics.OverlapSphereNonAlloc(vector, m_autoPickupRange, m_colliders, m_autoPickupMask);
		for (int i = 0; i < num; i++)
		{
			Collider collider = m_colliders[i];
			if (!collider.attachedRigidbody)
			{
				continue;
			}
			ItemDrop component = collider.attachedRigidbody.GetComponent<ItemDrop>();
			FloatingTerrainDummy floatingTerrainDummy = null;
			if (component == null && (bool)(floatingTerrainDummy = collider.attachedRigidbody.gameObject.GetComponent<FloatingTerrainDummy>()) && (bool)floatingTerrainDummy && floatingTerrainDummy.m_parent != null)
			{
				component = floatingTerrainDummy.m_parent.gameObject.GetComponent<ItemDrop>();
			}
			if (component == null || !component.m_autoPickup || component.IsPiece() || HaveUniqueKey(component.m_itemData.m_shared.m_name) || !component.GetComponent<ZNetView>().IsValid())
			{
				continue;
			}
			if (!component.CanPickup())
			{
				component.RequestOwn();
			}
			else
			{
				if (component.InTar())
				{
					continue;
				}
				component.Load();
				if (!m_inventory.CanAddItem(component.m_itemData) || component.m_itemData.GetWeight() + m_inventory.GetTotalWeight() > GetMaxCarryWeight())
				{
					continue;
				}
				float num2 = Vector3.Distance(component.transform.position, vector);
				if (num2 > m_autoPickupRange)
				{
					continue;
				}
				if (num2 < 0.3f)
				{
					Pickup(component.gameObject);
					continue;
				}
				Vector3 vector2 = Vector3.Normalize(vector - component.transform.position);
				float num3 = 15f;
				Vector3 vector3 = vector2 * num3 * dt;
				component.transform.position += vector3;
				if ((bool)floatingTerrainDummy)
				{
					floatingTerrainDummy.transform.position += vector3;
				}
			}
		}
	}

	private void PlayerAttackInput(float dt)
	{
		if (InPlaceMode())
		{
			return;
		}
		ItemDrop.ItemData currentWeapon = GetCurrentWeapon();
		UpdateWeaponLoading(currentWeapon, dt);
		if (currentWeapon != null && currentWeapon.m_shared.m_attack.m_bowDraw)
		{
			UpdateAttackBowDraw(currentWeapon, dt);
		}
		else
		{
			if (m_attack)
			{
				m_queuedAttackTimer = 0.5f;
				m_queuedSecondAttackTimer = 0f;
			}
			if (m_secondaryAttack)
			{
				m_queuedSecondAttackTimer = 0.5f;
				m_queuedAttackTimer = 0f;
			}
			m_queuedAttackTimer -= Time.fixedDeltaTime;
			m_queuedSecondAttackTimer -= Time.fixedDeltaTime;
			if ((m_queuedAttackTimer > 0f || m_attackHold) && StartAttack(null, secondaryAttack: false))
			{
				m_queuedAttackTimer = 0f;
			}
			if ((m_queuedSecondAttackTimer > 0f || m_secondaryAttackHold) && StartAttack(null, secondaryAttack: true))
			{
				m_queuedSecondAttackTimer = 0f;
			}
		}
		if (m_currentAttack != null && m_currentAttack.m_loopingAttack && !(m_currentAttackIsSecondary ? m_secondaryAttackHold : m_attackHold))
		{
			m_currentAttack.Abort();
		}
	}

	private void UpdateWeaponLoading(ItemDrop.ItemData weapon, float dt)
	{
		if (weapon == null || !weapon.m_shared.m_attack.m_requiresReload)
		{
			SetWeaponLoaded(null);
		}
		else if (m_weaponLoaded != weapon && weapon.m_shared.m_attack.m_requiresReload && !IsReloadActionQueued() && TryUseEitr(weapon.m_shared.m_attack.m_reloadEitrDrain))
		{
			QueueReloadAction();
		}
	}

	private void CancelReloadAction()
	{
		foreach (MinorActionData item in m_actionQueue)
		{
			if (item.m_type == MinorActionData.ActionType.Reload)
			{
				m_actionQueue.Remove(item);
				break;
			}
		}
	}

	public override void ResetLoadedWeapon()
	{
		SetWeaponLoaded(null);
		foreach (MinorActionData item in m_actionQueue)
		{
			if (item.m_type == MinorActionData.ActionType.Reload)
			{
				m_actionQueue.Remove(item);
				break;
			}
		}
	}

	private void SetWeaponLoaded(ItemDrop.ItemData weapon)
	{
		if (weapon != m_weaponLoaded)
		{
			m_weaponLoaded = weapon;
			m_nview.GetZDO().Set(ZDOVars.s_weaponLoaded, weapon != null);
		}
	}

	public override bool IsWeaponLoaded()
	{
		if (!m_nview.IsValid())
		{
			return false;
		}
		if (!m_nview.IsOwner())
		{
			return m_nview.GetZDO().GetBool(ZDOVars.s_weaponLoaded);
		}
		return m_weaponLoaded != null;
	}

	private void UpdateAttackBowDraw(ItemDrop.ItemData weapon, float dt)
	{
		if (m_blocking || InMinorAction() || IsAttached())
		{
			m_attackDrawTime = -1f;
			if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
			{
				m_zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: false);
			}
			return;
		}
		float num = weapon.GetDrawStaminaDrain();
		float drawEitrDrain = weapon.GetDrawEitrDrain();
		if ((double)GetAttackDrawPercentage() >= 1.0)
		{
			num *= 0.5f;
		}
		num += num * GetEquipmentAttackStaminaModifier();
		m_seman.ModifyAttackStaminaUsage(num, ref num);
		bool flag = num <= 0f || HaveStamina();
		bool flag2 = drawEitrDrain <= 0f || HaveEitr();
		if (m_attackDrawTime < 0f)
		{
			if (!m_attackHold)
			{
				m_attackDrawTime = 0f;
			}
		}
		else if (m_attackHold && flag && m_attackDrawTime >= 0f)
		{
			if (m_attackDrawTime == 0f)
			{
				if (!weapon.m_shared.m_attack.StartDraw(this, weapon))
				{
					m_attackDrawTime = -1f;
					return;
				}
				weapon.m_shared.m_holdStartEffect.Create(base.transform.position, Quaternion.identity, base.transform);
			}
			m_attackDrawTime += Time.fixedDeltaTime;
			if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
			{
				m_zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: true);
				m_zanim.SetFloat("drawpercent", GetAttackDrawPercentage());
			}
			UseStamina(num * dt);
			UseEitr(drawEitrDrain * dt);
		}
		else if (m_attackDrawTime > 0f)
		{
			if (flag && flag2)
			{
				StartAttack(null, secondaryAttack: false);
			}
			if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
			{
				m_zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: false);
			}
			m_attackDrawTime = 0f;
		}
	}

	protected override bool HaveQueuedChain()
	{
		if ((m_queuedAttackTimer > 0f || m_attackHold) && GetCurrentWeapon() != null && m_currentAttack != null)
		{
			return m_currentAttack.CanStartChainAttack();
		}
		return false;
	}

	private void UpdateBaseValue(float dt)
	{
		m_baseValueUpdateTimer += dt;
		if (m_baseValueUpdateTimer > 2f)
		{
			m_baseValueUpdateTimer = 0f;
			m_baseValue = EffectArea.GetBaseValue(base.transform.position, 20f);
			m_comfortLevel = SE_Rested.CalculateComfortLevel(this);
			float stat = Game.instance.GetPlayerProfile().GetStat(PlayerStatType.MaxComfort, DifficultyRequirement.Any);
			if ((float)m_comfortLevel > stat)
			{
				Game.instance.GetPlayerProfile().SetStat(PlayerStatType.MaxComfort, m_comfortLevel);
			}
			if (m_baseValueOld != m_baseValue)
			{
				m_baseValueOld = m_baseValue;
				ZNet.instance.m_serverSyncedPlayerData["baseValue"] = m_baseValue.ToString();
				m_nview.GetZDO().Set(ZDOVars.s_baseValue, m_baseValue);
				RandEventSystem.SetRandomEventsNeedsRefresh();
			}
		}
	}

	public int GetComfortLevel()
	{
		if (m_nview == null)
		{
			return 0;
		}
		return m_comfortLevel;
	}

	public int GetBaseValue()
	{
		if (!m_nview.IsValid())
		{
			return 0;
		}
		if (m_nview.IsOwner())
		{
			return m_baseValue;
		}
		return m_nview.GetZDO().GetInt(ZDOVars.s_baseValue);
	}

	public bool IsSafeInHome()
	{
		return m_safeInHome;
	}

	private void UpdateBiome(float dt)
	{
		if (InIntro())
		{
			return;
		}
		if (m_biomeTimer == 0f)
		{
			Location location = Location.GetLocation(base.transform.position, checkDungeons: false);
			if ((object)location != null && !string.IsNullOrEmpty(location.m_discoverLabel))
			{
				AddKnownLocationName(location.m_discoverLabel);
			}
		}
		m_biomeTimer += dt;
		if (m_biomeTimer > 1f)
		{
			m_biomeTimer = 0f;
			BiomeSector biomeSector = WorldGenerator.instance.GetBiomeSector(base.transform.position);
			Heightmap.Biome biome = WorldGenerator.instance.GetBiome(base.transform.position);
			int num = Minimap.instance.m_textureSize / 2;
			float num2 = Minimap.instance.m_pixelSize / 2f;
			int num3 = (int)((base.transform.position.x - num2) / Minimap.instance.m_pixelSize + (float)num);
			int num4 = (int)((base.transform.position.y - num2) / Minimap.instance.m_pixelSize + (float)num);
			if (biomeSector.Biome != biome)
			{
				ZLog.LogWarning($"GetBiome error {biomeSector.Biome} -> {biome} {base.transform.position} -> {num3}, {num4}");
			}
			if (m_currentBiomeData != biomeSector)
			{
				m_currentBiome = biomeSector.BiomeType.Biome;
				m_currentBiomeData = biomeSector;
				AddKnownBiome(biomeSector);
			}
		}
	}

	public Heightmap.Biome GetCurrentBiome()
	{
		return m_currentBiome;
	}

	public BiomeSector GetCurrentBiomeData()
	{
		if (m_currentBiomeData == null)
		{
			return BiomeSector.Empty;
		}
		return m_currentBiomeData;
	}

	public override void RaiseSkill(Skills.SkillType skill, float value = 1f)
	{
		if (skill != Skills.SkillType.None)
		{
			float multiplier = 1f;
			m_seman.ModifyRaiseSkill(skill, ref multiplier);
			value *= multiplier;
			m_skills.RaiseSkill(skill, value);
		}
	}

	private void UpdateStats(float dt)
	{
		if (InIntro() || IsTeleporting())
		{
			return;
		}
		m_timeSinceDeath += dt;
		UpdateModifiers();
		UpdateFood(dt, forceUpdate: false);
		bool flag = IsEncumbered();
		float maxStamina = GetMaxStamina();
		float num = 1f;
		if (IsBlocking())
		{
			num *= 0.8f;
		}
		if ((IsSwimming() && !IsOnGround()) || InAttack() || InDodge() || m_wallRunning || flag)
		{
			num = 0f;
		}
		float num2 = (m_staminaRegen + (1f - m_stamina / maxStamina) * m_staminaRegen * m_staminaRegenTimeMultiplier) * num;
		float staminaMultiplier = 1f;
		m_seman.ModifyStaminaRegen(ref staminaMultiplier);
		num2 *= staminaMultiplier;
		m_staminaRegenTimer -= dt;
		if (m_stamina < maxStamina && m_staminaRegenTimer <= 0f)
		{
			m_stamina = Mathf.Min(maxStamina, m_stamina + num2 * dt * Game.m_staminaRegenRate);
		}
		m_nview.GetZDO().Set(ZDOVars.s_stamina, m_stamina);
		float adrenaline = GetAdrenaline();
		float maxAdrenaline = GetMaxAdrenaline();
		if (maxAdrenaline > 0f)
		{
			m_lastMaxAdrenaline = maxAdrenaline;
		}
		m_adrenalineDegenTimer -= dt;
		if (adrenaline > 0f && m_adrenalineDegenTimer <= 0f)
		{
			float time = adrenaline / m_lastMaxAdrenaline;
			float num3 = m_adrenalineDegen.Evaluate(time) * dt;
			AddAdrenaline(0f - num3);
		}
		m_nview.GetZDO().Set(ZDOVars.s_adrenaline, m_adrenaline);
		float maxEitr = GetMaxEitr();
		float num4 = 1f;
		if (IsBlocking())
		{
			num4 *= 0.8f;
		}
		if (InAttack() || InDodge())
		{
			num4 = 0f;
		}
		float num5 = (m_eiterRegen + (1f - m_eitr / maxEitr) * m_eiterRegen) * num4;
		float eitrMultiplier = 1f;
		m_seman.ModifyEitrRegen(ref eitrMultiplier);
		eitrMultiplier += GetEquipmentEitrRegenModifier();
		num5 *= eitrMultiplier;
		m_eitrRegenTimer -= dt;
		if (m_eitr < maxEitr && m_eitrRegenTimer <= 0f)
		{
			m_eitr = Mathf.Min(maxEitr, m_eitr + num5 * dt);
		}
		m_nview.GetZDO().Set(ZDOVars.s_eitr, m_eitr);
		if (flag)
		{
			if (m_moveDir.magnitude > 0.1f)
			{
				UseStamina(m_encumberedStaminaDrain * dt);
			}
			m_seman.AddStatusEffect(SEMan.s_statusEffectEncumbered, resetTime: false, 0, 0f, -1);
			ShowTutorial("encumbered");
		}
		else
		{
			m_seman.RemoveStatusEffect(SEMan.s_statusEffectEncumbered);
		}
		StatusEffect statusEffect = m_seman.GetStatusEffect(SEMan.s_statusEffectSoftDeath);
		if (statusEffect != null)
		{
			float num6 = m_hardDeathCooldown - m_timeSinceDeath;
			if (Mathf.Abs(statusEffect.m_ttl - statusEffect.GetDuration() - num6) > 0.1f)
			{
				statusEffect.SetDuration(statusEffect.m_ttl - num6);
			}
		}
		else if (!HardDeath())
		{
			m_seman.AddStatusEffect(SEMan.s_statusEffectSoftDeath, resetTime: false, 0, 0f, -1);
		}
		UpdateEnvStatusEffects(dt);
	}

	public float GetEquipmentEitrRegenModifier()
	{
		float num = 0f;
		if (m_chestItem != null)
		{
			num += m_chestItem.m_shared.m_eitrRegenModifier;
		}
		if (m_legItem != null)
		{
			num += m_legItem.m_shared.m_eitrRegenModifier;
		}
		if (m_helmetItem != null)
		{
			num += m_helmetItem.m_shared.m_eitrRegenModifier;
		}
		if (m_shoulderItem != null)
		{
			num += m_shoulderItem.m_shared.m_eitrRegenModifier;
		}
		if (m_leftItem != null)
		{
			num += m_leftItem.m_shared.m_eitrRegenModifier;
		}
		if (m_rightItem != null)
		{
			num += m_rightItem.m_shared.m_eitrRegenModifier;
		}
		if (m_utilityItem != null)
		{
			num += m_utilityItem.m_shared.m_eitrRegenModifier;
		}
		return num;
	}

	private void UpdateEnvStatusEffects(float dt)
	{
		m_nearFireTimer += dt;
		HitData.DamageModifiers damageModifiers = GetDamageModifiers();
		bool flag = m_nearFireTimer < 0.25f;
		bool flag2 = m_seman.HaveStatusEffect(SEMan.s_statusEffectBurning);
		bool flag3 = InShelter();
		HitData.DamageModifier modifier = damageModifiers.GetModifier(HitData.DamageType.Frost);
		bool flag4 = EnvMan.IsFreezing();
		bool num = EnvMan.IsCold();
		bool flag5 = EnvMan.IsWet();
		bool flag6 = IsSensed();
		bool flag7 = m_seman.HaveStatusEffect(SEMan.s_statusEffectWet);
		bool flag8 = IsSitting();
		bool flag9 = EffectArea.IsPointInsideArea(base.transform.position, EffectArea.Type.WarmCozyArea, 1f);
		bool flag10 = ShieldGenerator.IsInsideShield(base.transform.position);
		bool flag11 = flag4 && !flag && !flag3;
		bool flag12 = (num && !flag) || (flag4 && flag && !flag3) || (flag4 && !flag && flag3);
		if (modifier == HitData.DamageModifier.Resistant || modifier == HitData.DamageModifier.VeryResistant || modifier == HitData.DamageModifier.SlightlyResistant || flag9)
		{
			flag11 = false;
			flag12 = false;
		}
		if (flag5 && !m_underRoof && !flag10)
		{
			m_seman.AddStatusEffect(SEMan.s_statusEffectWet, resetTime: true, 0, 0f, -1);
		}
		if (flag3)
		{
			m_seman.AddStatusEffect(SEMan.s_statusEffectShelter, resetTime: false, 0, 0f, -1);
		}
		else
		{
			m_seman.RemoveStatusEffect(SEMan.s_statusEffectShelter);
		}
		if (flag)
		{
			m_seman.AddStatusEffect(SEMan.s_statusEffectCampFire, resetTime: false, 0, 0f, -1);
		}
		else
		{
			m_seman.RemoveStatusEffect(SEMan.s_statusEffectCampFire);
		}
		bool flag13 = !flag6 && (flag8 || flag3) && !flag12 && !flag11 && (!flag7 || flag9) && !flag2 && flag;
		if (flag13)
		{
			m_seman.AddStatusEffect(SEMan.s_statusEffectResting, resetTime: false, 0, 0f, -1);
		}
		else
		{
			m_seman.RemoveStatusEffect(SEMan.s_statusEffectResting);
		}
		m_safeInHome = flag13 && flag3 && (float)GetBaseValue() >= 1f;
		if (flag11)
		{
			if (!m_seman.RemoveStatusEffect(SEMan.s_statusEffectCold, quiet: true))
			{
				m_seman.AddStatusEffect(SEMan.s_statusEffectFreezing, resetTime: false, 0, 0f, -1);
			}
		}
		else if (flag12)
		{
			if (!m_seman.RemoveStatusEffect(SEMan.s_statusEffectFreezing, quiet: true) && (bool)m_seman.AddStatusEffect(SEMan.s_statusEffectCold, resetTime: false, 0, 0f, -1))
			{
				ShowTutorial("cold");
			}
		}
		else
		{
			m_seman.RemoveStatusEffect(SEMan.s_statusEffectCold);
			m_seman.RemoveStatusEffect(SEMan.s_statusEffectFreezing);
		}
	}

	public bool CanEat(ItemDrop.ItemData item, bool showMessages)
	{
		foreach (Food food in m_foods)
		{
			if (food.m_item.m_shared.m_name == item.m_shared.m_name)
			{
				if (food.CanEatAgain())
				{
					return true;
				}
				if (showMessages)
				{
					Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_nomore", item.m_shared.m_name));
				}
				return false;
			}
		}
		foreach (Food food2 in m_foods)
		{
			if (food2.CanEatAgain())
			{
				return true;
			}
		}
		if (m_foods.Count >= 3)
		{
			if (showMessages)
			{
				Message(MessageHud.MessageType.Center, "$msg_isfull");
			}
			return false;
		}
		return true;
	}

	private Food GetMostDepletedFood()
	{
		Food food = null;
		foreach (Food food2 in m_foods)
		{
			if (food2.CanEatAgain() && (food == null || food2.m_time < food.m_time))
			{
				food = food2;
			}
		}
		return food;
	}

	public void ClearFood()
	{
		m_foods.Clear();
	}

	public bool RemoveOneFood()
	{
		if (m_foods.Count == 0)
		{
			return false;
		}
		m_foods.RemoveAt(UnityEngine.Random.Range(0, m_foods.Count));
		return true;
	}

	public bool EatFood(ItemDrop.ItemData item)
	{
		if (!CanEat(item, showMessages: false))
		{
			return false;
		}
		string text = "";
		if (item.m_shared.m_food > 0f)
		{
			text = text + " +" + item.m_shared.m_food + " $item_food_health ";
		}
		if (item.m_shared.m_foodStamina > 0f)
		{
			text = text + " +" + item.m_shared.m_foodStamina + " $item_food_stamina ";
		}
		if (item.m_shared.m_foodEitr > 0f)
		{
			text = text + " +" + item.m_shared.m_foodEitr + " $item_food_eitr ";
		}
		Message(MessageHud.MessageType.Center, text);
		foreach (Food food2 in m_foods)
		{
			if (food2.m_item.m_shared.m_name == item.m_shared.m_name)
			{
				if (food2.CanEatAgain())
				{
					food2.m_time = item.m_shared.m_foodBurnTime;
					food2.m_health = item.m_shared.m_food;
					food2.m_stamina = item.m_shared.m_foodStamina;
					food2.m_eitr = item.m_shared.m_foodEitr;
					Game.instance.IncrementPlayerStat(PlayerStatType.FoodEaten);
					Game.instance.GetPlayerProfile().IncrementStatFoodEaten(item.m_shared.m_name, 1f, item.m_cheated);
					UpdateFood(0f, forceUpdate: true);
					return true;
				}
				return false;
			}
		}
		if (m_foods.Count < 3)
		{
			Food food = new Food();
			food.m_name = item.m_dropPrefab.name;
			food.m_item = item;
			food.m_time = item.m_shared.m_foodBurnTime;
			food.m_health = item.m_shared.m_food;
			food.m_stamina = item.m_shared.m_foodStamina;
			food.m_eitr = item.m_shared.m_foodEitr;
			m_foods.Add(food);
			Game.instance.IncrementPlayerStat(PlayerStatType.FoodEaten);
			Game.instance.GetPlayerProfile().IncrementStatFoodEaten(item.m_shared.m_name, 1f, item.m_cheated);
			UpdateFood(0f, forceUpdate: true);
			return true;
		}
		Food mostDepletedFood = GetMostDepletedFood();
		if (mostDepletedFood != null)
		{
			mostDepletedFood.m_name = item.m_dropPrefab.name;
			mostDepletedFood.m_item = item;
			mostDepletedFood.m_time = item.m_shared.m_foodBurnTime;
			mostDepletedFood.m_health = item.m_shared.m_food;
			mostDepletedFood.m_stamina = item.m_shared.m_foodStamina;
			Game.instance.IncrementPlayerStat(PlayerStatType.FoodEaten);
			Game.instance.GetPlayerProfile().IncrementStatFoodEaten(item.m_shared.m_name, 1f, item.m_cheated);
			UpdateFood(0f, forceUpdate: true);
			return true;
		}
		return false;
	}

	private void UpdateFood(float dt, bool forceUpdate)
	{
		float num = 1f;
		m_foodUpdateTimer += dt * Game.m_foodRate;
		if (m_foodUpdateTimer >= num || forceUpdate)
		{
			m_foodUpdateTimer -= num;
			foreach (Food food in m_foods)
			{
				food.m_time -= num;
				float f = Mathf.Clamp01(food.m_time / food.m_item.m_shared.m_foodBurnTime);
				f = Mathf.Pow(f, 0.3f);
				food.m_health = food.m_item.m_shared.m_food * f;
				food.m_stamina = food.m_item.m_shared.m_foodStamina * f;
				food.m_eitr = food.m_item.m_shared.m_foodEitr * f;
				if (food.m_time <= 0f)
				{
					Message(MessageHud.MessageType.Center, "$msg_food_done");
					m_foods.Remove(food);
					break;
				}
			}
			GetTotalFoodValue(out var hp, out var stamina, out var eitr);
			SetMaxHealth(hp, flashBar: true);
			SetMaxStamina(stamina, flashBar: true);
			SetMaxEitr(eitr, flashBar: true);
			if (eitr > 0f)
			{
				ShowTutorial("eitr");
			}
		}
		if (forceUpdate)
		{
			return;
		}
		m_foodRegenTimer += dt;
		if (!(m_foodRegenTimer >= 10f))
		{
			return;
		}
		m_foodRegenTimer = 0f;
		float num2 = 0f;
		foreach (Food food2 in m_foods)
		{
			num2 += food2.m_item.m_shared.m_foodRegen;
		}
		if (num2 > 0f)
		{
			float regenMultiplier = 1f;
			m_seman.ModifyHealthRegen(ref regenMultiplier);
			num2 *= regenMultiplier;
			Heal(num2);
		}
	}

	private void GetTotalFoodValue(out float hp, out float stamina, out float eitr)
	{
		hp = m_baseHP;
		stamina = m_baseStamina;
		eitr = 0f;
		foreach (Food food in m_foods)
		{
			hp += food.m_health;
			stamina += food.m_stamina;
			eitr += food.m_eitr;
		}
	}

	public float GetBaseFoodHP()
	{
		return m_baseHP;
	}

	public List<Food> GetFoods()
	{
		return m_foods;
	}

	public void OnSpawned(bool spawnValkyrie)
	{
		m_spawnEffects.Create(base.transform.position, Quaternion.identity);
		if (spawnValkyrie)
		{
			SetIntro(intro: true);
			SpawnValkyrie();
		}
		if (TryGetUniqueKeyValue("invrows", out var value) && int.TryParse(value, out var result))
		{
			SetInventorySize(result);
		}
		else
		{
			AddUniqueKeyValue("invrows", 4.ToString());
		}
	}

	private void SpawnValkyrie()
	{
		m_valkyrie.Load();
		UnityEngine.Object.Instantiate(m_valkyrie.Asset, base.transform.position, Quaternion.identity).GetComponent<ZNetView>().HoldReferenceTo(m_valkyrie);
		m_valkyrie.Release();
	}

	protected override bool CheckRun(Vector3 moveDir, float dt)
	{
		if (!base.CheckRun(moveDir, dt))
		{
			return false;
		}
		bool flag = HaveStamina();
		float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Run);
		float num = Mathf.Lerp(1f, 0.5f, skillFactor);
		float num2 = m_runStaminaDrain * num;
		num2 -= num2 * GetEquipmentMovementModifier();
		num2 += num2 * GetEquipmentRunStaminaModifier();
		m_seman.ModifyRunStaminaDrain(num2, ref num2, moveDir);
		UseStamina(dt * num2 * Game.m_moveStaminaRate);
		if (HaveStamina())
		{
			m_runSkillImproveTimer += dt;
			if (m_runSkillImproveTimer > 1f)
			{
				m_runSkillImproveTimer = 0f;
				RaiseSkill(Skills.SkillType.Run);
			}
			ClearActionQueue();
			return true;
		}
		if (flag)
		{
			Hud.instance.StaminaBarEmptyFlash();
		}
		return false;
	}

	private void UpdateModifiers()
	{
		if (s_equipmentModifierSourceFields == null)
		{
			return;
		}
		for (int i = 0; i < m_equipmentModifierValues.Length; i++)
		{
			float num = 0f;
			if (m_rightItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_rightItem.m_shared);
			}
			if (m_leftItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_leftItem.m_shared);
			}
			if (m_chestItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_chestItem.m_shared);
			}
			if (m_legItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_legItem.m_shared);
			}
			if (m_helmetItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_helmetItem.m_shared);
			}
			if (m_shoulderItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_shoulderItem.m_shared);
			}
			if (m_utilityItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_utilityItem.m_shared);
			}
			if (m_trinketItem != null)
			{
				num += (float)s_equipmentModifierSourceFields[i].GetValue(m_trinketItem.m_shared);
			}
			m_equipmentModifierValues[i] = num;
		}
	}

	public void AppendEquipmentModifierTooltips(ItemDrop.ItemData item, StringBuilder sb)
	{
		for (int i = 0; i < m_equipmentModifierValues.Length; i++)
		{
			if (!(s_equipmentModifierSourceFields[i].GetValue(item.m_shared) is float num) || num == 0f)
			{
				continue;
			}
			float equipmentModifierPlusSE = GetEquipmentModifierPlusSE(i);
			if (i >= 10)
			{
				sb.AppendFormat($"\n{s_equipmentModifierTooltips[i]}: <color=orange>{num}</color>");
				continue;
			}
			sb.AppendFormat("\n" + s_equipmentModifierTooltips[i] + ": <color=orange>" + (num * 100f).ToString("+0;-0") + "%</color>");
			if (equipmentModifierPlusSE != num)
			{
				sb.AppendFormat(" ($item_total:<color=yellow>" + (GetEquipmentModifierPlusSE(i) * 100f).ToString("+0;-0") + "%</color>)");
			}
		}
	}

	public void OnSkillLevelup(Skills.SkillType skill, float level)
	{
		m_skillLevelupEffects.Create(m_head.position, m_head.rotation, m_head, 1f, -1, GetZDOID());
	}

	protected override void OnJump()
	{
		ClearActionQueue();
		float staminaUse = m_jumpStaminaUsage - m_jumpStaminaUsage * GetEquipmentMovementModifier() + m_jumpStaminaUsage * GetEquipmentJumpStaminaModifier();
		m_seman.ModifyJumpStaminaUsage(staminaUse, ref staminaUse);
		UseStamina(staminaUse * Game.m_moveStaminaRate);
		Game.instance.IncrementPlayerStat(PlayerStatType.Jumps);
	}

	protected override void OnSwimming(Vector3 targetVel, float dt)
	{
		base.OnSwimming(targetVel, dt);
		if (targetVel.magnitude > 0.1f)
		{
			float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Swim);
			float num = Mathf.Lerp(m_swimStaminaDrainMinSkill, m_swimStaminaDrainMaxSkill, skillFactor);
			num += num * GetEquipmentSwimStaminaModifier();
			m_seman.ModifySwimStaminaUsage(num, ref num);
			UseStamina(dt * num * Game.m_moveStaminaRate);
			m_swimSkillImproveTimer += dt;
			if (m_swimSkillImproveTimer > 1f)
			{
				m_swimSkillImproveTimer = 0f;
				RaiseSkill(Skills.SkillType.Swim);
			}
		}
		if (!HaveStamina())
		{
			m_drownDamageTimer += dt;
			if (m_drownDamageTimer > 1f)
			{
				m_drownDamageTimer = 0f;
				float damage = Mathf.Ceil(GetMaxHealth() / 20f);
				HitData hitData = new HitData();
				hitData.m_damage.m_damage = damage;
				hitData.m_point = GetCenterPoint();
				hitData.m_dir = Vector3.down;
				hitData.m_pushForce = 10f;
				hitData.m_hitType = HitData.HitType.Drowning;
				Damage(hitData);
				Vector3 position = base.transform.position;
				position.y = GetLiquidLevel();
				m_drownEffects.Create(position, base.transform.rotation);
			}
		}
	}

	protected override bool TakeInput()
	{
		bool result = (!Chat.instance || !Chat.instance.HasFocus()) && !Console.IsVisible() && !TextInput.IsVisible() && !StoreGui.IsVisible() && !InventoryGui.IsVisible() && !Menu.IsVisible() && (!TextViewer.instance || !TextViewer.instance.IsVisible()) && !Minimap.IsOpen() && !GameCamera.InFreeFly() && !PlayerCustomizaton.IsBarberGuiVisible() && !Hud.instance.m_buildUi.SearchFieldFocused;
		if (IsDead() || InCutscene() || IsTeleporting())
		{
			result = false;
		}
		return result;
	}

	public void UseHotbarItem(int index)
	{
		ItemDrop.ItemData itemAt = m_inventory.GetItemAt(index - 1, 0);
		if (itemAt != null)
		{
			UseItem(null, itemAt, fromInventoryGui: false);
		}
	}

	public bool RequiredCraftingStation(Recipe recipe, int qualityLevel, bool checkLevel)
	{
		CraftingStation requiredStation = recipe.GetRequiredStation(qualityLevel);
		if (m_currentStation != null && m_currentStation.m_upgrader)
		{
			return true;
		}
		if (requiredStation != null)
		{
			if (m_currentStation == null)
			{
				return false;
			}
			if (requiredStation.m_name != m_currentStation.m_name)
			{
				return false;
			}
			if (checkLevel)
			{
				int requiredStationLevel = recipe.GetRequiredStationLevel(qualityLevel);
				if (m_currentStation.GetLevel() < requiredStationLevel)
				{
					return false;
				}
			}
		}
		else if (m_currentStation != null && !m_currentStation.m_showBasicRecipies)
		{
			return false;
		}
		return true;
	}

	public bool HaveRequirements(Recipe recipe, bool discover, int qualityLevel, int amount = 1)
	{
		if (discover)
		{
			if ((bool)recipe.m_craftingStation && !KnowStationLevel(recipe.m_craftingStation.m_name, recipe.m_minStationLevel))
			{
				return false;
			}
		}
		else if (!RequiredCraftingStation(recipe, qualityLevel, checkLevel: true))
		{
			return false;
		}
		if (recipe.m_item.m_itemData.m_shared.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(recipe.m_item.m_itemData.m_shared.m_dlc))
		{
			return false;
		}
		if (!HaveRequirementItems(recipe, discover, qualityLevel, amount))
		{
			return false;
		}
		return true;
	}

	private bool HaveRequirementItems(Recipe piece, bool discover, int qualityLevel, int amount = 1)
	{
		CraftingStation currentCraftingStation = GetCurrentCraftingStation();
		Piece.Requirement[] resources = piece.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if ((!discover && currentCraftingStation != null && currentCraftingStation.m_upgrader != requirement.m_upgraderResource) || (currentCraftingStation == null && requirement.m_upgraderResource) || !requirement.m_resItem)
			{
				continue;
			}
			if (discover)
			{
				if (requirement.m_amount <= 0)
				{
					continue;
				}
				if (piece.m_requireOnlyOneIngredient)
				{
					if (m_knownMaterial.Contains(requirement.m_resItem.m_itemData.m_shared.m_name))
					{
						return true;
					}
				}
				else if (!m_knownMaterial.Contains(requirement.m_resItem.m_itemData.m_shared.m_name))
				{
					return false;
				}
				continue;
			}
			int num = requirement.GetAmount(qualityLevel) * amount;
			int num2 = 0;
			for (int j = 1; j < requirement.m_resItem.m_itemData.m_shared.m_maxQuality + 1; j++)
			{
				int num3 = m_inventory.CountItems(requirement.m_resItem.m_itemData.m_shared.m_name, j);
				if (num3 > num2)
				{
					num2 = num3;
				}
			}
			if (piece.m_requireOnlyOneIngredient)
			{
				if (num2 >= num)
				{
					return true;
				}
			}
			else if (num2 < num)
			{
				return false;
			}
		}
		if (piece.m_requireOnlyOneIngredient)
		{
			return false;
		}
		return true;
	}

	public ItemDrop.ItemData GetFirstRequiredItem(Inventory inventory, Recipe recipe, int qualityLevel, out int amount, out int extraAmount, int craftMultiplier = 1)
	{
		CraftingStation currentCraftingStation = GetCurrentCraftingStation();
		Piece.Requirement[] resources = recipe.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if ((currentCraftingStation != null && currentCraftingStation.m_upgrader != requirement.m_upgraderResource) || (currentCraftingStation == null && requirement.m_upgraderResource) || !requirement.m_resItem)
			{
				continue;
			}
			int num = requirement.GetAmount(qualityLevel) * craftMultiplier;
			for (int j = 0; j <= requirement.m_resItem.m_itemData.m_shared.m_maxQuality; j++)
			{
				if (m_inventory.CountItems(requirement.m_resItem.m_itemData.m_shared.m_name, j) >= num)
				{
					amount = num;
					extraAmount = requirement.m_extraAmountOnlyOneIngredient;
					return inventory.GetItem(requirement.m_resItem.m_itemData.m_shared.m_name, j);
				}
			}
		}
		amount = 0;
		extraAmount = 0;
		return null;
	}

	public bool HaveRequirements(Piece piece, RequirementMode mode)
	{
		if ((bool)piece.m_craftingStation)
		{
			if (mode == RequirementMode.IsKnown || mode == RequirementMode.CanAlmostBuild)
			{
				if (!m_knownStations.ContainsKey(piece.m_craftingStation.m_name))
				{
					return false;
				}
			}
			else if (!CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, base.transform.position) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
			{
				return false;
			}
		}
		if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc))
		{
			return false;
		}
		if (mode != RequirementMode.IsKnown && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()))
		{
			return true;
		}
		Piece.Requirement[] resources = piece.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if (!requirement.m_resItem || requirement.m_amount <= 0)
			{
				continue;
			}
			switch (mode)
			{
			case RequirementMode.IsKnown:
				if (!m_knownMaterial.Contains(requirement.m_resItem.m_itemData.m_shared.m_name))
				{
					return false;
				}
				break;
			case RequirementMode.CanAlmostBuild:
				if (!m_inventory.HaveItem(requirement.m_resItem.m_itemData.m_shared.m_name))
				{
					return false;
				}
				break;
			case RequirementMode.CanBuild:
				if (m_inventory.CountItems(requirement.m_resItem.m_itemData.m_shared.m_name) < requirement.m_amount)
				{
					return false;
				}
				break;
			}
		}
		return true;
	}

	public void ConsumeResources(Piece.Requirement[] requirements, int qualityLevel, int itemQuality = -1, int multiplier = 1)
	{
		CraftingStation currentCraftingStation = GetCurrentCraftingStation();
		foreach (Piece.Requirement requirement in requirements)
		{
			if ((!(currentCraftingStation != null) || currentCraftingStation.m_upgrader == requirement.m_upgraderResource) && (!(currentCraftingStation == null) || !requirement.m_upgraderResource) && (bool)requirement.m_resItem)
			{
				int num = requirement.GetAmount(qualityLevel) * multiplier;
				if (num > 0)
				{
					m_inventory.RemoveItem(requirement.m_resItem.m_itemData.m_shared.m_name, num, itemQuality);
				}
			}
		}
	}

	private void UpdateHover()
	{
		if (InPlaceMode() || IsDead() || m_doodadController != null)
		{
			m_hovering = null;
			m_hoveringCreature = null;
		}
		else
		{
			FindHoverObject(out m_hovering, out m_hoveringCreature);
		}
	}

	public bool IsMaterialKnown(string sharedName)
	{
		return m_knownMaterial.Contains(sharedName);
	}

	private bool CheckCanRemovePiece(Piece piece)
	{
		if (!m_noPlacementCost && piece.m_craftingStation != null && !CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, base.transform.position) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
		{
			Message(MessageHud.MessageType.Center, "$msg_missingstation");
			return false;
		}
		return true;
	}

	private bool CopyPiece()
	{
		if (Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out var hitInfo, 50f, m_removeRayMask) && Vector3.Distance(hitInfo.point, m_eye.position) < m_maxPlaceDistance)
		{
			Piece piece = hitInfo.collider.GetComponentInParent<Piece>();
			if (piece == null && (bool)hitInfo.collider.GetComponent<Heightmap>())
			{
				piece = TerrainModifier.FindClosestModifierPieceInRange(hitInfo.point, 2.5f);
			}
			if ((bool)piece)
			{
				if (SetSelectedPiece(piece))
				{
					m_placeRotation = (int)Math.Round(piece.transform.rotation.eulerAngles.y / m_placeRotationDegrees);
					return true;
				}
				Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
				return false;
			}
		}
		return false;
	}

	private bool RemovePiece()
	{
		if (Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out var hitInfo, 50f, m_removeRayMask) && Vector3.Distance(hitInfo.point, m_eye.position) < m_maxPlaceDistance)
		{
			Piece piece = hitInfo.collider.GetComponentInParent<Piece>();
			if (piece == null && (bool)hitInfo.collider.GetComponent<Heightmap>())
			{
				piece = TerrainModifier.FindClosestModifierPieceInRange(hitInfo.point, 2.5f);
			}
			if ((bool)piece)
			{
				if (!piece.m_canBeRemoved)
				{
					return false;
				}
				if (Location.IsInsideNoBuildLocation(piece.transform.position))
				{
					Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
					return false;
				}
				if (!PrivateArea.CheckAccess(piece.transform.position))
				{
					Message(MessageHud.MessageType.Center, "$msg_privatezone");
					return false;
				}
				if (!CheckCanRemovePiece(piece))
				{
					return false;
				}
				ZNetView component = piece.GetComponent<ZNetView>();
				if (component == null)
				{
					return false;
				}
				if (!piece.CanBeRemoved())
				{
					Message(MessageHud.MessageType.Center, "$msg_cantremovenow");
					return false;
				}
				piece.GetComponent<IRemoved>()?.OnRemoved();
				WearNTear component2 = piece.GetComponent<WearNTear>();
				if ((bool)component2)
				{
					component2.Remove();
				}
				else
				{
					Character component3 = piece.GetComponent<Character>();
					if ((object)component3 != null)
					{
						component3.Damage(new HitData(1E+10f));
					}
					else
					{
						ZLog.Log("Removing non WNT object with hammer " + piece.name);
						component.ClaimOwnership();
						piece.DropResources();
						piece.m_placeEffect.Create(piece.transform.position, piece.transform.rotation, piece.gameObject.transform, 1f, -1, GetZDOID());
						m_removeEffects.Create(piece.transform.position, Quaternion.identity, null, 1f, -1, GetZDOID());
						ZNetScene.instance.Destroy(piece.gameObject);
					}
				}
				ItemDrop.ItemData rightItem = GetRightItem();
				if (rightItem != null)
				{
					FaceLookDirection();
					m_zanim.SetTrigger(rightItem.m_shared.m_attack.m_attackAnimation);
				}
				return true;
			}
		}
		return false;
	}

	public void FaceLookDirection()
	{
		base.transform.rotation = GetLookYaw();
		Physics.SyncTransforms();
	}

	public bool TryPlacePiece(Piece piece)
	{
		UpdatePlacementGhost(flashGuardStone: true);
		switch (m_placementStatus)
		{
		case PlacementStatus.NoBuildZone:
			Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
			return false;
		case PlacementStatus.BlockedbyPlayer:
			Message(MessageHud.MessageType.Center, "$msg_blocked");
			return false;
		case PlacementStatus.PrivateZone:
			Message(MessageHud.MessageType.Center, "$msg_privatezone");
			return false;
		case PlacementStatus.MoreSpace:
			Message(MessageHud.MessageType.Center, "$msg_needspace");
			return false;
		case PlacementStatus.NoTeleportArea:
			Message(MessageHud.MessageType.Center, "$msg_noteleportarea");
			return false;
		case PlacementStatus.Invalid:
		case PlacementStatus.NoRayHits:
			Message(MessageHud.MessageType.Center, "$msg_invalidplacement");
			return false;
		case PlacementStatus.ExtensionMissingStation:
			Message(MessageHud.MessageType.Center, "$msg_extensionmissingstation");
			return false;
		case PlacementStatus.WrongBiome:
			Message(MessageHud.MessageType.Center, "$msg_wrongbiome");
			return false;
		case PlacementStatus.NeedCultivated:
			Message(MessageHud.MessageType.Center, "$msg_needcultivated");
			return false;
		case PlacementStatus.NeedDirt:
			Message(MessageHud.MessageType.Center, "$msg_needdirt");
			return false;
		case PlacementStatus.NotInDungeon:
			Message(MessageHud.MessageType.Center, "$msg_notindungeon");
			return false;
		case PlacementStatus.DeepSnow:
			Message(MessageHud.MessageType.Center, "$msg_snowtoodeep");
			return false;
		case PlacementStatus.NoSnow:
			Message(MessageHud.MessageType.Center, "$msg_nosnow");
			return false;
		default:
		{
			ZLog.Log("Placed " + piece.gameObject.name);
			bool cheated = (m_inventory.ItemCheated(piece.m_resources) || m_localPlayer.NoCostCheat()) && !PlayerProfile.s_bypassCheatChecks;
			Gogan.LogEvent("Game", "PlacedPiece", piece.gameObject.name, 0L);
			Game.instance.IncrementPlayerStat(PlayerStatType.Builds, 1f, cheated);
			Game.instance.GetPlayerProfile().IncrementStatBuildPiecePlaced(piece.m_name, 1f, cheated);
			PlacePiece(piece, m_placementGhost.transform.position, m_placementGhost.transform.rotation, doAttack: true, cheated);
			return true;
		}
		}
	}

	public void PlacePiece(Piece piece, Vector3 pos, Quaternion rot, bool doAttack = true, bool cheated = false)
	{
		GameObject original = piece.gameObject;
		TerrainModifier.SetTriggerOnPlaced(trigger: true);
		GameObject gameObject = UnityEngine.Object.Instantiate(original, pos, rot);
		TerrainModifier.SetTriggerOnPlaced(trigger: false);
		CraftingStation componentInChildren = gameObject.GetComponentInChildren<CraftingStation>();
		if ((bool)componentInChildren)
		{
			AddKnownStation(componentInChildren);
		}
		Piece component = gameObject.GetComponent<Piece>();
		if ((bool)component)
		{
			component.SetCreator(GetPlayerID(), PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
		}
		gameObject.GetComponent<PrivateArea>()?.Setup(Game.instance.GetPlayerProfile().GetName());
		gameObject.GetComponent<WearNTear>()?.OnPlaced();
		if (doAttack)
		{
			ItemDrop.ItemData rightItem = GetRightItem();
			if (rightItem != null)
			{
				FaceLookDirection();
				m_zanim.SetTrigger(rightItem.m_shared.m_attack.m_attackAnimation);
			}
		}
		if (piece.m_randomInitBuildRotation)
		{
			m_placeRotation = UnityEngine.Random.Range(0, 16);
		}
		gameObject.gameObject.GetComponent<ItemDrop>()?.MakePiece(sendRPC: true);
		m_placed.Clear();
		gameObject.GetComponents(m_placed);
		foreach (IPlaced item in m_placed)
		{
			item.OnPlaced();
		}
		piece.m_placeEffect.Create(pos, rot, gameObject.transform, 1f, -1, GetZDOID());
		if (Physics.Raycast(pos, Vector3.down, out var hitInfo, 200f, m_terrainMask))
		{
			ZLog.Log(gameObject.name + " placed at height " + hitInfo.distance);
			float stat = Game.instance.GetPlayerProfile().GetStat(PlayerStatType.MaxBuildingHeight, DifficultyRequirement.Any);
			if (hitInfo.distance > stat && IsBuiltOnMyFoundation(pos))
			{
				Game.instance.GetPlayerProfile().SetStat(PlayerStatType.MaxBuildingHeight, hitInfo.distance, cheated);
			}
		}
		ZLog.Log(gameObject.name + " placed at world height " + pos.y);
		if (!Character.InInterior(pos))
		{
			float stat2 = Game.instance.GetPlayerProfile().GetStat(PlayerStatType.MaxBuildingHeightWorld, DifficultyRequirement.Any);
			if (pos.y > stat2 && IsBuiltOnMyFoundation(pos))
			{
				Game.instance.GetPlayerProfile().SetStat(PlayerStatType.MaxBuildingHeightWorld, pos.y, cheated);
			}
		}
		if (cheated)
		{
			gameObject.GetComponent<ZNetView>()?.GetZDO().SetCheated(isCheated: true);
		}
		AddNoise(50f);
		if (m_clusterStatCheck-- <= 0)
		{
			Piece.CheckClusteredBuildPieceStats();
			m_clusterStatCheck = 20;
		}
	}

	public bool IsBuiltOnMyFoundation(Vector3 pos)
	{
		int num = Physics.RaycastNonAlloc(pos, Vector3.down, m_raycastHits, 200f, m_pieceMask);
		int num2 = 0;
		int num3 = 0;
		long playerID = GetPlayerID();
		for (int i = 0; i < num; i++)
		{
			Piece componentInParent = m_raycastHits[i].collider.GetComponentInParent<Piece>();
			if ((object)componentInParent != null)
			{
				num2++;
				if (componentInParent.GetCreator() == playerID)
				{
					num3++;
				}
			}
		}
		if (num2 >= Achievements.m_instance.m_buildFoundationMinPieces && (float)num3 / (float)num2 > Achievements.m_instance.m_buildFoundationMinCreatorPercent)
		{
			return true;
		}
		return false;
	}

	public override bool IsPlayer()
	{
		return true;
	}

	public PieceTable GetBuildTool()
	{
		return m_buildPieces;
	}

	public void GetBuildSelection(out Piece go, out Vector2Int id, out int total, out Piece.PieceCategory category, out PieceTable pieceTable)
	{
		category = m_buildPieces.GetSelectedCategory();
		pieceTable = m_buildPieces;
		if (m_buildPieces.GetAvailablePiecesInSelectedCategory() == 0)
		{
			go = null;
			id = Vector2Int.zero;
			total = 0;
		}
		else
		{
			GameObject selectedPrefab = m_buildPieces.GetSelectedPrefab();
			go = (selectedPrefab ? selectedPrefab.GetComponent<Piece>() : null);
			id = m_buildPieces.GetSelectedIndex();
			total = m_buildPieces.GetAvailablePiecesInSelectedCategory();
		}
	}

	public List<Piece> GetBuildPieces()
	{
		if (!(m_buildPieces != null))
		{
			return null;
		}
		return m_buildPieces.GetPiecesInSelectedCategory();
	}

	public int GetAvailableBuildPiecesInCategory(Piece.PieceCategory cat)
	{
		if (!(m_buildPieces != null))
		{
			return 0;
		}
		return m_buildPieces.GetAvailablePiecesInCategory(cat);
	}

	private void RPC_OnDeath(long sender)
	{
		m_visual.SetActive(value: false);
	}

	private void CreateDeathEffects()
	{
		GameObject[] array = m_deathEffects.Create(base.transform.position, base.transform.rotation, base.transform);
		for (int i = 0; i < array.Length; i++)
		{
			Ragdoll component = array[i].GetComponent<Ragdoll>();
			if ((bool)component)
			{
				Vector3 velocity = m_body.linearVelocity;
				if (m_pushForce.magnitude * 0.5f > velocity.magnitude)
				{
					velocity = m_pushForce * 0.5f;
				}
				component.Setup(velocity, 0f, 0f, 0f, null);
				OnRagdollCreated(component);
				m_ragdoll = component;
			}
		}
	}

	public void UnequipDeathDropItems()
	{
		if (m_rightItem != null)
		{
			UnequipItem(m_rightItem, triggerEquipEffects: false);
		}
		if (m_leftItem != null)
		{
			UnequipItem(m_leftItem, triggerEquipEffects: false);
		}
		if (m_ammoItem != null)
		{
			UnequipItem(m_ammoItem, triggerEquipEffects: false);
		}
		if (m_utilityItem != null)
		{
			UnequipItem(m_utilityItem, triggerEquipEffects: false);
		}
		if (m_trinketItem != null)
		{
			UnequipItem(m_trinketItem, triggerEquipEffects: false);
		}
	}

	public void CreateTombStone()
	{
		if (m_inventory.NrOfItems() != 0 && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepInventory))
		{
			if (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped))
			{
				UnequipAllItems();
			}
			if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathDeleteItems) || ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped))
			{
				m_inventory.RemoveUnequipped();
			}
			if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip))
			{
				UnequipAllItems();
			}
			GameObject obj = UnityEngine.Object.Instantiate(m_tombstone, GetCenterPoint(), base.transform.rotation);
			obj.GetComponent<Container>().GetInventory().MoveInventoryToGrave(m_inventory);
			TombStone component = obj.GetComponent<TombStone>();
			PlayerProfile playerProfile = Game.instance.GetPlayerProfile();
			component.Setup(playerProfile.GetName(), playerProfile.GetPlayerID());
		}
	}

	private bool HardDeath()
	{
		return m_timeSinceDeath > m_hardDeathCooldown;
	}

	public void ClearHardDeath()
	{
		m_timeSinceDeath = m_hardDeathCooldown + 1f;
	}

	public override void OnDeath()
	{
		if (!m_nview.IsOwner())
		{
			Debug.Log("OnDeath call but not the owner");
			return;
		}
		bool flag = HardDeath();
		m_nview.GetZDO().Set(ZDOVars.s_dead, value: true);
		m_nview.InvokeRPC(ZNetView.Everybody, "OnDeath");
		Game.instance.IncrementPlayerStat(PlayerStatType.Deaths);
		Game.instance.GetPlayerProfile().SetStat(PlayerStatType.ConsecutiveDaysSurvived, 0f);
		switch (m_lastHit.m_hitType)
		{
		case HitData.HitType.Undefined:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByUndefined);
			break;
		case HitData.HitType.EnemyHit:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByEnemyHit);
			break;
		case HitData.HitType.PlayerHit:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByPlayerHit);
			break;
		case HitData.HitType.Fall:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByFall);
			break;
		case HitData.HitType.Drowning:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByDrowning);
			break;
		case HitData.HitType.Burning:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByBurning);
			break;
		case HitData.HitType.Freezing:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByFreezing);
			break;
		case HitData.HitType.Poisoned:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByPoisoned);
			break;
		case HitData.HitType.Water:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByWater);
			break;
		case HitData.HitType.Smoke:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathBySmoke);
			break;
		case HitData.HitType.EdgeOfWorld:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByEdgeOfWorld);
			break;
		case HitData.HitType.Impact:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByImpact);
			break;
		case HitData.HitType.Cart:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByCart);
			break;
		case HitData.HitType.Tree:
		{
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByTree);
			if (m_lastHit.m_toolTier <= 5)
			{
				Game.instance.IncrementPlayerStat((PlayerStatType)(152 + m_lastHit.m_toolTier));
			}
			else
			{
				ZLog.LogError($"Invalid tool tier {m_lastHit.m_toolTier} for death by tree player stat.");
			}
			PlayerStatType variant = (PlayerStatType)m_lastHit.m_variant;
			if (variant >= PlayerStatType.TreeFir && variant <= PlayerStatType.TreeSnowPine)
			{
				Game.instance.IncrementPlayerStat(variant);
			}
			break;
		}
		case HitData.HitType.Self:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathBySelf);
			break;
		case HitData.HitType.Structural:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByStructural);
			break;
		case HitData.HitType.Turret:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByTurret);
			break;
		case HitData.HitType.Boat:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByBoat);
			break;
		case HitData.HitType.Stalagtite:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByStalagtite);
			break;
		case HitData.HitType.Catapult:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByCatapult);
			break;
		case HitData.HitType.CinderFire:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByCinderFire);
			break;
		case HitData.HitType.AshlandsOcean:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByAshlandsOcean);
			break;
		case HitData.HitType.Incinerator:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByIncinerator);
			break;
		case HitData.HitType.DrawBridge:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByDrawBridge);
			break;
		case HitData.HitType.AshlandsLava:
			Game.instance.IncrementPlayerStat(PlayerStatType.DeathByAshlandsLava);
			break;
		default:
			ZLog.LogWarning("Not implemented death type " + m_lastHit.m_hitType);
			break;
		}
		Game.instance.GetPlayerProfile().SetDeathPoint(base.transform.position);
		CreateDeathEffects();
		CreateTombStone();
		m_foods.Clear();
		if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathSkillsReset))
		{
			m_skills.Clear();
		}
		else if (flag)
		{
			m_skills.OnDeath();
		}
		m_seman.RemoveAllStatusEffects();
		Game.instance.RequestRespawn(10f, afterDeath: true);
		m_timeSinceDeath = 0f;
		if (!flag)
		{
			Message(MessageHud.MessageType.TopLeft, "$msg_softdeath");
		}
		Message(MessageHud.MessageType.Center, "$msg_youdied");
		ShowTutorial("death");
		Minimap.instance.AddPin(base.transform.position, Minimap.PinType.Death, $"$hud_mapday {EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds())}", save: true, isChecked: false, 0L);
		if (m_onDeath != null)
		{
			m_onDeath();
		}
		string eventLabel = "biome:" + GetCurrentBiome();
		Gogan.LogEvent("Game", "Death", eventLabel, 0L);
	}

	public void OnRespawn()
	{
		m_nview.GetZDO().Set(ZDOVars.s_dead, value: false);
		SetHealth(GetMaxHealth());
	}

	private void SetupPlacementGhost()
	{
		if ((bool)m_placementGhost)
		{
			UnityEngine.Object.Destroy(m_placementGhost);
			m_placementGhost = null;
		}
		if (m_buildPieces == null || IsDead())
		{
			return;
		}
		GameObject selectedPrefab = m_buildPieces.GetSelectedPrefab();
		if (selectedPrefab == null)
		{
			return;
		}
		Piece component = selectedPrefab.GetComponent<Piece>();
		if (component.m_repairPiece || component.m_removePiece)
		{
			return;
		}
		bool flag = false;
		TerrainModifier componentInChildren = selectedPrefab.GetComponentInChildren<TerrainModifier>();
		if ((bool)componentInChildren)
		{
			flag = componentInChildren.enabled;
			componentInChildren.enabled = false;
		}
		TerrainOp.m_forceDisableTerrainOps = true;
		ZNetView.m_forceDisableInit = true;
		_ = m_placementGhost;
		m_placementGhost = UnityEngine.Object.Instantiate(selectedPrefab);
		Piece component2 = m_placementGhost.GetComponent<Piece>();
		if ((object)component2 != null && component2.m_randomInitBuildRotation)
		{
			m_placeRotation = UnityEngine.Random.Range(0, 16);
		}
		m_placementGhost.gameObject.GetComponent<ItemDrop>()?.MakePiece();
		MagicaCloth componentInChildren2 = m_placementGhost.GetComponentInChildren<MagicaCloth>();
		if ((object)componentInChildren2 != null)
		{
			componentInChildren2.enabled = false;
		}
		ZNetView.m_forceDisableInit = false;
		TerrainOp.m_forceDisableTerrainOps = false;
		m_placementGhost.name = selectedPrefab.name;
		if (m_placementGhostLast != m_placementGhost.name)
		{
			m_manualSnapPoint = -1;
		}
		m_placementGhostLast = m_placementGhost.name;
		if ((bool)componentInChildren)
		{
			componentInChildren.enabled = flag;
		}
		Joint[] componentsInChildren = m_placementGhost.GetComponentsInChildren<Joint>();
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren[i]);
		}
		Rigidbody[] componentsInChildren2 = m_placementGhost.GetComponentsInChildren<Rigidbody>();
		for (int i = 0; i < componentsInChildren2.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren2[i]);
		}
		ParticleSystemForceField[] componentsInChildren3 = m_placementGhost.GetComponentsInChildren<ParticleSystemForceField>();
		for (int i = 0; i < componentsInChildren3.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren3[i]);
		}
		Demister[] componentsInChildren4 = m_placementGhost.GetComponentsInChildren<Demister>();
		for (int i = 0; i < componentsInChildren4.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren4[i]);
		}
		Collider[] componentsInChildren5 = m_placementGhost.GetComponentsInChildren<Collider>();
		foreach (Collider collider in componentsInChildren5)
		{
			if (((1 << collider.gameObject.layer) & m_placeRayMask) == 0)
			{
				ZLog.Log("Disabling " + collider.gameObject.name + "  " + LayerMask.LayerToName(collider.gameObject.layer));
				collider.enabled = false;
			}
		}
		Transform[] componentsInChildren6 = m_placementGhost.GetComponentsInChildren<Transform>();
		int layer = LayerMask.NameToLayer("ghost");
		Transform[] array = componentsInChildren6;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].gameObject.layer = layer;
		}
		TerrainModifier[] componentsInChildren7 = m_placementGhost.GetComponentsInChildren<TerrainModifier>();
		for (int i = 0; i < componentsInChildren7.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren7[i]);
		}
		GuidePoint[] componentsInChildren8 = m_placementGhost.GetComponentsInChildren<GuidePoint>();
		for (int i = 0; i < componentsInChildren8.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren8[i]);
		}
		LightLod[] componentsInChildren9 = m_placementGhost.GetComponentsInChildren<LightLod>();
		for (int i = 0; i < componentsInChildren9.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren9[i]);
		}
		LightFlicker[] componentsInChildren10 = m_placementGhost.GetComponentsInChildren<LightFlicker>();
		for (int i = 0; i < componentsInChildren10.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren10[i]);
		}
		Light[] componentsInChildren11 = m_placementGhost.GetComponentsInChildren<Light>();
		for (int i = 0; i < componentsInChildren11.Length; i++)
		{
			UnityEngine.Object.Destroy(componentsInChildren11[i]);
		}
		AudioSource[] componentsInChildren12 = m_placementGhost.GetComponentsInChildren<AudioSource>();
		for (int i = 0; i < componentsInChildren12.Length; i++)
		{
			componentsInChildren12[i].enabled = false;
		}
		ZSFX[] componentsInChildren13 = m_placementGhost.GetComponentsInChildren<ZSFX>();
		for (int i = 0; i < componentsInChildren13.Length; i++)
		{
			componentsInChildren13[i].enabled = false;
		}
		WispSpawner componentInChildren3 = m_placementGhost.GetComponentInChildren<WispSpawner>();
		if ((bool)componentInChildren3)
		{
			UnityEngine.Object.Destroy(componentInChildren3);
		}
		Windmill componentInChildren4 = m_placementGhost.GetComponentInChildren<Windmill>();
		if ((bool)componentInChildren4)
		{
			componentInChildren4.enabled = false;
		}
		ParticleSystem[] componentsInChildren14 = m_placementGhost.GetComponentsInChildren<ParticleSystem>();
		for (int i = 0; i < componentsInChildren14.Length; i++)
		{
			componentsInChildren14[i].gameObject.SetActive(value: false);
		}
		Transform transform = m_placementGhost.transform.Find("_GhostOnly");
		if ((bool)transform)
		{
			transform.gameObject.SetActive(value: true);
		}
		m_placementGhost.transform.position = base.transform.position;
		m_placementGhost.transform.localScale = selectedPrefab.transform.localScale;
		m_ghostRippleDistance.Clear();
		CleanupGhostMaterials<MeshRenderer>(m_placementGhost);
		CleanupGhostMaterials<SkinnedMeshRenderer>(m_placementGhost);
	}

	public static bool IsPlacementGhost(GameObject obj)
	{
		if ((bool)m_localPlayer)
		{
			return (object)obj == m_localPlayer.m_placementGhost;
		}
		return false;
	}

	private void CleanupGhostMaterials<T>(GameObject ghost) where T : Renderer
	{
		T[] componentsInChildren = m_placementGhost.GetComponentsInChildren<T>();
		foreach (T val in componentsInChildren)
		{
			if (val.sharedMaterial == null)
			{
				continue;
			}
			Material[] sharedMaterials = val.sharedMaterials;
			for (int j = 0; j < sharedMaterials.Length; j++)
			{
				Material material = new Material(sharedMaterials[j]);
				if (material.HasProperty("_RippleDistance"))
				{
					m_ghostRippleDistance[material] = material.GetFloat("_RippleDistance");
				}
				material.SetFloat("_ValueNoise", 0f);
				material.SetFloat("_TriplanarLocalPos", 1f);
				sharedMaterials[j] = material;
			}
			val.sharedMaterials = sharedMaterials;
			val.shadowCastingMode = ShadowCastingMode.Off;
		}
	}

	private void SetPlacementGhostValid(bool valid)
	{
		m_placementGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(!valid);
	}

	protected override void SetPlaceMode(PieceTable buildPieces)
	{
		base.SetPlaceMode(buildPieces);
		m_buildPieces = buildPieces;
		UpdateAvailablePiecesList();
	}

	public void SetBuildCategory(int index)
	{
		if (m_buildPieces != null)
		{
			m_buildPieces.SetCategory(index);
			UpdateAvailablePiecesList();
		}
	}

	public void SetBuildCategory(Piece.PieceCategory category)
	{
		if (m_buildPieces != null)
		{
			m_buildPieces.SetCategory(category);
			UpdateAvailablePiecesList();
		}
	}

	public override bool InPlaceMode()
	{
		return m_buildPieces != null;
	}

	public bool InRepairMode()
	{
		if (InPlaceMode())
		{
			Piece selectedPiece = m_buildPieces.GetSelectedPiece();
			if ((object)selectedPiece != null)
			{
				if (!selectedPiece.m_repairPiece)
				{
					return selectedPiece.m_removePiece;
				}
				return true;
			}
		}
		return false;
	}

	public PlacementStatus GetPlacementStatus()
	{
		return m_placementStatus;
	}

	public bool CanRotatePiece()
	{
		if (InPlaceMode())
		{
			Piece selectedPiece = m_buildPieces.GetSelectedPiece();
			if ((object)selectedPiece != null)
			{
				return selectedPiece.m_canRotate;
			}
		}
		return false;
	}

	private void Repair(ItemDrop.ItemData toolItem, Piece repairPiece)
	{
		if (!InPlaceMode())
		{
			return;
		}
		Piece hoveringPiece = GetHoveringPiece();
		if (!hoveringPiece || !CheckCanRemovePiece(hoveringPiece) || !PrivateArea.CheckAccess(hoveringPiece.transform.position))
		{
			return;
		}
		bool flag = false;
		WearNTear component = hoveringPiece.GetComponent<WearNTear>();
		if ((bool)component && component.Repair())
		{
			flag = true;
		}
		if (flag)
		{
			FaceLookDirection();
			m_zanim.SetTrigger(toolItem.m_shared.m_attack.m_attackAnimation);
			hoveringPiece.m_placeEffect.Create(hoveringPiece.transform.position, hoveringPiece.transform.rotation, null, 1f, -1, GetZDOID());
			Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_repaired", hoveringPiece.m_name));
			UseStamina(GetBuildStamina());
			UseEitr(toolItem.m_shared.m_attack.m_attackEitr);
			if (toolItem.m_shared.m_useDurability)
			{
				toolItem.m_durability -= toolItem.m_shared.m_useDurabilityDrain * Game.m_durabilityRate;
			}
		}
		else
		{
			Message(MessageHud.MessageType.TopLeft, hoveringPiece.m_name + " $msg_doesnotneedrepair");
		}
	}

	private void UpdateWearNTearHover()
	{
		if (!InPlaceMode())
		{
			m_hoveringPiece = null;
			return;
		}
		m_hoveringPiece = null;
		if (!Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out var hitInfo, 50f, m_removeRayMask) || !(Vector3.Distance(m_eye.position, hitInfo.point) < m_maxPlaceDistance))
		{
			return;
		}
		Piece piece = (m_hoveringPiece = hitInfo.collider.GetComponentInParent<Piece>());
		if ((bool)piece)
		{
			WearNTear component = piece.GetComponent<WearNTear>();
			if ((bool)component)
			{
				component.Highlight();
			}
		}
	}

	public Piece GetHoveringPiece()
	{
		if (!InPlaceMode())
		{
			return null;
		}
		return m_hoveringPiece;
	}

	private void UpdatePlacementGhost(bool flashGuardStone)
	{
		if (m_placementGhost == null)
		{
			if ((bool)m_placementMarkerInstance)
			{
				m_placementMarkerInstance.SetActive(value: false);
			}
			return;
		}
		bool flag = ((ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()) ? m_altPlace : (ZInput.GetButton("AltPlace") || (ZInput.GetButton("JoyAltPlace") && !ZInput.GetButton("JoyRotate"))));
		Piece component = m_placementGhost.GetComponent<Piece>();
		bool water = component.m_waterPiece || component.m_noInWater;
		Quaternion quaternion;
		int manualSnapPoint;
		bool num;
		if (PieceRayTest(out var point, out var normal, out var piece, out var heightmap, out var waterSurface, water))
		{
			m_placementStatus = PlacementStatus.Valid;
			quaternion = Quaternion.Euler(0f, m_placeRotationDegrees * (float)m_placeRotation, 0f);
			if (m_placementMarkerInstance == null)
			{
				m_placementMarkerInstance = UnityEngine.Object.Instantiate(m_placeMarker, point, Quaternion.identity);
			}
			m_placementMarkerInstance.SetActive(value: true);
			m_placementMarkerInstance.transform.position = point;
			m_placementMarkerInstance.transform.rotation = Quaternion.LookRotation(normal, quaternion * Vector3.forward);
			if (component.m_groundOnly || component.m_groundPiece || component.m_cultivatedGroundOnly)
			{
				m_placementMarkerInstance.SetActive(value: false);
			}
			WearNTear wearNTear = ((piece != null) ? piece.GetComponent<WearNTear>() : null);
			StationExtension component2 = component.GetComponent<StationExtension>();
			if (component2 != null)
			{
				CraftingStation craftingStation = component2.FindClosestStationInRange(point);
				if ((bool)craftingStation)
				{
					component2.StartConnectionEffect(craftingStation);
				}
				else
				{
					component2.StopConnectionEffect();
					m_placementStatus = PlacementStatus.ExtensionMissingStation;
				}
				if (component2.OtherExtensionInRange(component.m_spaceRequirement))
				{
					m_placementStatus = PlacementStatus.MoreSpace;
				}
			}
			if (component.m_blockRadius > 0f && component.m_blockingPieces.Count > 0)
			{
				Collider[] array = Physics.OverlapSphere(point, component.m_blockRadius, LayerMask.GetMask("piece"));
				for (int i = 0; i < array.Length; i++)
				{
					Piece componentInParent = array[i].gameObject.GetComponentInParent<Piece>();
					if ((object)componentInParent == null || !(componentInParent != component))
					{
						continue;
					}
					foreach (Piece blockingPiece in component.m_blockingPieces)
					{
						if (blockingPiece.m_name == componentInParent.m_name)
						{
							m_placementStatus = PlacementStatus.MoreSpace;
							break;
						}
					}
				}
			}
			if (component.m_mustConnectTo != null)
			{
				ZNetView zNetView = null;
				Collider[] array = Physics.OverlapSphere(component.transform.position, component.m_connectRadius);
				for (int i = 0; i < array.Length; i++)
				{
					ZNetView componentInParent2 = array[i].GetComponentInParent<ZNetView>();
					if ((object)componentInParent2 == null || !(componentInParent2 != m_nview) || !componentInParent2.name.Contains(component.m_mustConnectTo.name))
					{
						continue;
					}
					if (component.m_mustBeAboveConnected)
					{
						Physics.Raycast(component.transform.position, Vector3.down, out var hitInfo);
						if (hitInfo.transform.GetComponentInParent<ZNetView>() != componentInParent2)
						{
							continue;
						}
					}
					zNetView = componentInParent2;
					break;
				}
				if (!zNetView)
				{
					m_placementStatus = PlacementStatus.Invalid;
				}
			}
			if ((bool)wearNTear && !wearNTear.m_supports)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_waterPiece && waterSurface == null && !flag)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_noInWater && waterSurface != null)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_groundPiece && heightmap == null)
			{
				m_placementGhost.SetActive(value: false);
				m_placementStatus = PlacementStatus.Invalid;
				return;
			}
			if (component.m_groundOnly && heightmap == null)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_cultivatedGroundOnly && (heightmap == null || !heightmap.IsCultivated(point)))
			{
				m_placementStatus = PlacementStatus.NeedCultivated;
			}
			if (component.m_vegetationGroundOnly)
			{
				bool flag2 = heightmap == null;
				if (!flag2)
				{
					Heightmap.Biome biome = heightmap.GetBiome(point);
					float vegetationMask = heightmap.GetVegetationMask(point);
					flag2 = ((biome == Heightmap.Biome.AshLands) ? (vegetationMask > 0.1f) : (vegetationMask < 0.25f));
				}
				if (flag2)
				{
					m_placementStatus = PlacementStatus.NeedDirt;
				}
			}
			if (component.m_notOnWood && (bool)piece && (bool)wearNTear && (wearNTear.m_materialType == WearNTear.MaterialType.Wood || wearNTear.m_materialType == WearNTear.MaterialType.HardWood))
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_notOnTiltingSurface && normal.y < 0.8f)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_inCeilingOnly && normal.y > -0.5f)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_notOnFloor && normal.y > 0.1f)
			{
				m_placementStatus = PlacementStatus.Invalid;
			}
			if (component.m_onlyInTeleportArea && !EffectArea.IsPointInsideArea(point, EffectArea.Type.Teleport))
			{
				m_placementStatus = PlacementStatus.NoTeleportArea;
			}
			if (!component.m_allowedInDungeons && InInterior() && !EnvMan.instance.CheckInteriorBuildingOverride() && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DungeonBuild))
			{
				m_placementStatus = PlacementStatus.NotInDungeon;
			}
			if (!component.m_allowedInDeepSnow && m_currentBiome == Heightmap.Biome.DeepNorth && (bool)heightmap && heightmap.GetCultivationMask(point) > m_deepSnowBuildHeight)
			{
				m_placementStatus = PlacementStatus.DeepSnow;
			}
			if (component.m_requireDeepSnow && (m_currentBiome != Heightmap.Biome.DeepNorth || heightmap == null || heightmap.GetCultivationMask(point) <= 0f))
			{
				m_placementStatus = PlacementStatus.NoSnow;
			}
			if ((bool)heightmap)
			{
				normal = Vector3.up;
				if (Terminal.m_showTests)
				{
					Terminal.m_testList["Build paintmask"] = heightmap.GetPaintMask(point).ToString();
				}
			}
			m_placementGhost.SetActive(value: true);
			manualSnapPoint = m_manualSnapPoint;
			if (!ZInput.GetButton("JoyAltKeys") && !Hud.IsPieceSelectionVisible() && Minimap.instance.m_mode != Minimap.MapMode.Large && !Console.IsVisible() && !Chat.instance.HasFocus())
			{
				if (ZInput.GetButtonDown("TabLeft") || (ZInput.GetButtonUp("JoyPrevSnap") && ZInput.GetButtonLastPressedTimer("JoyPrevSnap") < 0.33f))
				{
					m_manualSnapPoint--;
				}
				if (ZInput.GetButtonDown("TabRight") || (ZInput.GetButtonUp("JoyNextSnap") && ZInput.GetButtonLastPressedTimer("JoyNextSnap") < 0.33f))
				{
					m_manualSnapPoint++;
				}
			}
			m_tempSnapPoints1.Clear();
			m_placementGhost.GetComponent<Piece>().GetSnapPoints(m_tempSnapPoints1);
			if (m_manualSnapPoint < -1)
			{
				m_manualSnapPoint = m_tempSnapPoints1.Count - 1;
			}
			if (m_manualSnapPoint >= m_tempSnapPoints1.Count)
			{
				m_manualSnapPoint = -1;
			}
			if (((component.m_groundPiece || component.m_clipGround) && (bool)heightmap) || component.m_clipEverything)
			{
				GameObject selectedPrefab = m_buildPieces.GetSelectedPrefab();
				TerrainModifier component3 = selectedPrefab.GetComponent<TerrainModifier>();
				TerrainOp component4 = selectedPrefab.GetComponent<TerrainOp>();
				if (((bool)component3 || (bool)component4) && component.m_allowAltGroundPlacement)
				{
					if (!ZInput.IsNonClassicFunctionality() || !ZInput.IsGamepadActive())
					{
						if (component.m_groundPiece && !ZInput.GetButton("AltPlace"))
						{
							num = !ZInput.GetButton("JoyAltPlace");
							goto IL_077d;
						}
					}
					else if (component.m_groundPiece)
					{
						num = !m_altPlace;
						goto IL_077d;
					}
				}
				goto IL_079f;
			}
			Collider[] componentsInChildren = m_placementGhost.GetComponentsInChildren<Collider>();
			if (componentsInChildren.Length != 0)
			{
				m_placementGhost.transform.position = point + normal * 50f;
				m_placementGhost.transform.rotation = quaternion;
				Vector3 vector = Vector3.zero;
				float num2 = 999999f;
				Collider[] array = componentsInChildren;
				foreach (Collider collider in array)
				{
					if (collider.isTrigger || !collider.enabled)
					{
						continue;
					}
					MeshCollider meshCollider = collider as MeshCollider;
					if (!(meshCollider != null) || meshCollider.convex)
					{
						Vector3 vector2 = collider.ClosestPoint(point);
						float num3 = Vector3.Distance(vector2, point);
						if (num3 < num2)
						{
							vector = vector2;
							num2 = num3;
						}
					}
				}
				Vector3 vector3 = m_placementGhost.transform.position - vector;
				if (component.m_waterPiece)
				{
					vector3.y = 3f;
				}
				m_placementGhost.transform.position = point + ((m_manualSnapPoint < 0) ? vector3 : (quaternion * -m_tempSnapPoints1[m_manualSnapPoint].localPosition));
				m_placementGhost.transform.rotation = quaternion;
			}
			goto IL_0947;
		}
		if ((bool)m_placementMarkerInstance)
		{
			m_placementMarkerInstance.SetActive(value: false);
		}
		m_placementGhost.SetActive(value: false);
		m_placementStatus = PlacementStatus.NoRayHits;
		goto IL_0b31;
		IL_079f:
		m_placementGhost.transform.position = point + ((m_manualSnapPoint < 0) ? Vector3.zero : (quaternion * -m_tempSnapPoints1[m_manualSnapPoint].localPosition));
		m_placementGhost.transform.rotation = quaternion;
		goto IL_0947;
		IL_0947:
		if (manualSnapPoint != m_manualSnapPoint)
		{
			Message(MessageHud.MessageType.Center, "$msg_snapping " + ((m_manualSnapPoint == -1) ? "$msg_snapping_auto" : m_tempSnapPoints1[m_manualSnapPoint].name));
		}
		if (!flag)
		{
			m_tempPieces.Clear();
			if (FindClosestSnapPoints(m_placementGhost.transform, 0.5f, out var a, out var b, m_tempPieces))
			{
				_ = b.parent.position;
				Vector3 vector4 = b.position - (a.position - m_placementGhost.transform.position);
				if (!IsOverlappingOtherPiece(vector4, m_placementGhost.transform.rotation, m_placementGhost.name, m_tempPieces, component.m_allowRotatedOverlap))
				{
					m_placementGhost.transform.position = vector4;
				}
			}
		}
		if (Location.IsInsideNoBuildLocation(m_placementGhost.transform.position))
		{
			m_placementStatus = PlacementStatus.NoBuildZone;
		}
		PrivateArea component5 = component.GetComponent<PrivateArea>();
		float radius = (component5 ? component5.m_radius : 0f);
		bool wardCheck = component5 != null;
		if (!PrivateArea.CheckAccess(m_placementGhost.transform.position, radius, flashGuardStone, wardCheck))
		{
			m_placementStatus = PlacementStatus.PrivateZone;
		}
		if (CheckPlacementGhostVSPlayers())
		{
			m_placementStatus = PlacementStatus.BlockedbyPlayer;
		}
		if (component.m_onlyInBiome != Heightmap.Biome.None && (Heightmap.FindBiome(m_placementGhost.transform.position) & component.m_onlyInBiome) == 0)
		{
			m_placementStatus = PlacementStatus.WrongBiome;
		}
		if (component.m_noClipping && TestGhostClipping(m_placementGhost, 0.2f))
		{
			m_placementStatus = PlacementStatus.Invalid;
		}
		goto IL_0b31;
		IL_077d:
		if (num)
		{
			float groundHeight = ZoneSystem.instance.GetGroundHeight(base.transform.position);
			point.y = groundHeight;
		}
		goto IL_079f;
		IL_0b31:
		SetPlacementGhostValid(m_placementStatus == PlacementStatus.Valid);
	}

	private bool IsOverlappingOtherPiece(Vector3 p, Quaternion rotation, string pieceName, List<Piece> pieces, bool allowRotatedOverlap)
	{
		foreach (Piece tempPiece in m_tempPieces)
		{
			if (Vector3.Distance(p, tempPiece.transform.position) < 0.05f && (!allowRotatedOverlap || !(Quaternion.Angle(tempPiece.transform.rotation, rotation) > 10f)) && tempPiece.gameObject.name.CustomStartsWith(pieceName))
			{
				return true;
			}
		}
		return false;
	}

	private bool FindClosestSnapPoints(Transform ghost, float maxSnapDistance, out Transform a, out Transform b, List<Piece> pieces)
	{
		m_tempSnapPoints1.Clear();
		ghost.GetComponent<Piece>().GetSnapPoints(m_tempSnapPoints1);
		m_tempSnapPoints2.Clear();
		m_tempPieces.Clear();
		Piece.GetSnapPoints(ghost.transform.position, 10f, m_tempSnapPoints2, m_tempPieces);
		float num = 9999999f;
		a = null;
		b = null;
		if (m_manualSnapPoint >= 0)
		{
			if (FindClosestSnappoint(m_tempSnapPoints1[m_manualSnapPoint].position, m_tempSnapPoints2, maxSnapDistance, out var closest, out var _))
			{
				a = m_tempSnapPoints1[m_manualSnapPoint];
				b = closest;
				return true;
			}
			return false;
		}
		foreach (Transform item in m_tempSnapPoints1)
		{
			if (FindClosestSnappoint(item.position, m_tempSnapPoints2, maxSnapDistance, out var closest2, out var distance2) && distance2 < num)
			{
				num = distance2;
				a = item;
				b = closest2;
			}
		}
		return a != null;
	}

	private bool FindClosestSnappoint(Vector3 p, List<Transform> snapPoints, float maxDistance, out Transform closest, out float distance)
	{
		closest = null;
		distance = 999999f;
		foreach (Transform snapPoint in snapPoints)
		{
			float num = Vector3.Distance(snapPoint.position, p);
			if (!(num > maxDistance) && num < distance)
			{
				closest = snapPoint;
				distance = num;
			}
		}
		return closest != null;
	}

	private bool TestGhostClipping(GameObject ghost, float maxPenetration)
	{
		Collider[] componentsInChildren = ghost.GetComponentsInChildren<Collider>();
		Collider[] array = Physics.OverlapSphere(ghost.transform.position, 10f, m_placeRayMask);
		Collider[] array2 = componentsInChildren;
		foreach (Collider collider in array2)
		{
			Collider[] array3 = array;
			foreach (Collider collider2 in array3)
			{
				if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, collider2, collider2.transform.position, collider2.transform.rotation, out var _, out var distance) && distance > maxPenetration)
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool CheckPlacementGhostVSPlayers()
	{
		if (m_placementGhost == null)
		{
			return false;
		}
		List<Character> list = new List<Character>();
		Character.GetCharactersInRange(base.transform.position, 30f, list);
		Collider[] componentsInChildren = m_placementGhost.GetComponentsInChildren<Collider>();
		foreach (Collider collider in componentsInChildren)
		{
			if (collider.isTrigger || !collider.enabled || collider.gameObject == m_placementGhost)
			{
				continue;
			}
			MeshCollider meshCollider = collider as MeshCollider;
			if (meshCollider != null && !meshCollider.convex)
			{
				continue;
			}
			foreach (Character item in list)
			{
				CapsuleCollider collider2 = item.GetCollider();
				if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation, collider2, collider2.transform.position, collider2.transform.rotation, out var _, out var _))
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool PieceRayTest(out Vector3 point, out Vector3 normal, out Piece piece, out Heightmap heightmap, out Collider waterSurface, bool water)
	{
		int layerMask = m_placeRayMask;
		if (water)
		{
			layerMask = m_placeWaterRayMask;
		}
		if (Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out var hitInfo, 50f, layerMask))
		{
			float num = m_maxPlaceDistance;
			if ((bool)m_placementGhost)
			{
				Piece component = m_placementGhost.GetComponent<Piece>();
				if ((object)component != null)
				{
					num += (float)component.m_extraPlacementDistance;
				}
			}
			if ((bool)hitInfo.collider && !hitInfo.collider.attachedRigidbody && Vector3.Distance(m_eye.position, hitInfo.point) < num)
			{
				point = hitInfo.point;
				normal = hitInfo.normal;
				piece = hitInfo.collider.GetComponentInParent<Piece>();
				heightmap = hitInfo.collider.GetComponent<Heightmap>();
				if (hitInfo.collider.gameObject.layer == LayerMask.NameToLayer("Water"))
				{
					waterSurface = hitInfo.collider;
				}
				else
				{
					waterSurface = null;
				}
				return true;
			}
		}
		point = Vector3.zero;
		normal = Vector3.zero;
		piece = null;
		heightmap = null;
		waterSurface = null;
		return false;
	}

	private void FindHoverObject(out GameObject hover, out Character hoverCreature)
	{
		hover = null;
		hoverCreature = null;
		int num = Physics.RaycastNonAlloc(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, m_raycastHoverHits, 50f, m_interactMask);
		Array.Sort(m_raycastHoverHits, 0, num, RaycastHitComparer.Instance);
		for (int i = 0; i < num; i++)
		{
			RaycastHit raycastHit = m_raycastHoverHits[i];
			if ((bool)raycastHit.collider.attachedRigidbody && raycastHit.collider.attachedRigidbody.gameObject == base.gameObject)
			{
				continue;
			}
			if (hoverCreature == null)
			{
				Character character = (raycastHit.collider.attachedRigidbody ? raycastHit.collider.attachedRigidbody.GetComponent<Character>() : raycastHit.collider.GetComponent<Character>());
				if (character != null && (!character.GetBaseAI() || !character.GetBaseAI().IsSleeping()) && !ParticleMist.IsMistBlocked(GetCenterPoint(), character.GetCenterPoint()))
				{
					hoverCreature = character;
				}
			}
			Hoverable componentInParent = raycastHit.collider.GetComponentInParent<Hoverable>();
			if (Vector3.Distance(m_eye.position, raycastHit.point) < m_maxInteractDistance + (componentInParent?.GetHoverOffset() ?? 0f))
			{
				if (raycastHit.collider.GetComponent<Hoverable>() != null)
				{
					hover = raycastHit.collider.gameObject;
				}
				else if ((bool)raycastHit.collider.attachedRigidbody)
				{
					hover = raycastHit.collider.attachedRigidbody.gameObject;
				}
				else
				{
					hover = raycastHit.collider.gameObject;
				}
			}
			break;
		}
	}

	private void Interact(GameObject go, bool hold, bool alt)
	{
		if (InAttack() || InDodge() || (hold && Time.time - m_lastHoverInteractTime < 0.2f))
		{
			return;
		}
		Interactable componentInParent = go.GetComponentInParent<Interactable>();
		if (componentInParent != null)
		{
			m_lastHoverInteractTime = Time.time;
			if (componentInParent.Interact(this, hold, alt))
			{
				DoInteractAnimation((componentInParent as MonoBehaviour).gameObject);
			}
		}
	}

	private void UpdateStations(float dt)
	{
		m_stationDiscoverTimer += dt;
		if (m_stationDiscoverTimer > 1f)
		{
			m_stationDiscoverTimer = 0f;
			CraftingStation.UpdateKnownStationsInRange(this);
		}
		if (m_currentStation != null)
		{
			if (!m_currentStation.InUseDistance(this))
			{
				InventoryGui.instance.Hide();
				SetCraftingStation(null);
				return;
			}
			if (!InventoryGui.IsVisible())
			{
				SetCraftingStation(null);
				return;
			}
			m_currentStation.PokeInUse();
			if (!AlwaysRotateCamera())
			{
				Vector3 normalized = (m_currentStation.transform.position - base.transform.position).normalized;
				normalized.y = 0f;
				normalized.Normalize();
				Quaternion to = Quaternion.LookRotation(normalized);
				base.transform.rotation = Quaternion.RotateTowards(base.transform.rotation, to, m_turnSpeed * dt);
			}
			m_zanim.SetInt("crafting", m_currentStation.m_useAnimation);
			m_inCraftingStation = true;
		}
		else if (m_inCraftingStation)
		{
			m_zanim.SetInt("crafting", 0);
			m_inCraftingStation = false;
			if (InventoryGui.IsVisible())
			{
				InventoryGui.instance.Hide();
			}
		}
	}

	public void SetCraftingStation(CraftingStation station)
	{
		if (!(m_currentStation == station))
		{
			if ((bool)station)
			{
				AddKnownStation(station);
				station.PokeInUse();
				HideHandItems();
			}
			m_currentStation = station;
		}
	}

	public CraftingStation GetCurrentCraftingStation()
	{
		return m_currentStation;
	}

	private void UpdateCover(float dt)
	{
		m_updateCoverTimer += dt;
		if (m_updateCoverTimer > 1f)
		{
			m_updateCoverTimer = 0f;
			Cover.GetCoverForPoint(GetCenterPoint(), out m_coverPercentage, out m_underRoof);
		}
	}

	public Character GetHoverCreature()
	{
		return m_hoveringCreature;
	}

	public override GameObject GetHoverObject()
	{
		return m_hovering;
	}

	public override void OnNearFire(Vector3 point)
	{
		m_nearFireTimer = 0f;
	}

	public bool InShelter()
	{
		if (m_coverPercentage >= 0.8f)
		{
			return m_underRoof;
		}
		return false;
	}

	public float GetStamina()
	{
		return m_stamina;
	}

	public override float GetMaxStamina()
	{
		return m_maxStamina;
	}

	public float GetAdrenaline()
	{
		return m_adrenaline;
	}

	public override float GetMaxAdrenaline()
	{
		return m_maxAdrenaline + base.GetMaxAdrenaline();
	}

	public float GetEitr()
	{
		return m_eitr;
	}

	public override float GetMaxEitr()
	{
		return m_maxEitr;
	}

	public override float GetEitrPercentage()
	{
		return m_eitr / m_maxEitr;
	}

	public override float GetStaminaPercentage()
	{
		return m_stamina / m_maxStamina;
	}

	public void SetGodMode(bool godMode)
	{
		m_godMode = godMode;
	}

	public override bool InGodMode()
	{
		return m_godMode;
	}

	public void SetGhostMode(bool ghostmode)
	{
		m_ghostMode = ghostmode;
	}

	public override bool InGhostMode()
	{
		return m_ghostMode;
	}

	public void SetCrownMode(bool crownMode)
	{
		if (m_nview.IsOwner())
		{
			m_crownMode = crownMode;
			m_nview.GetZDO().Set(ZDOVars.s_crowned, crownMode);
		}
	}

	public bool InCrownMode()
	{
		if (m_nview == null || !m_nview.IsValid())
		{
			return false;
		}
		if (m_nview.IsOwner())
		{
			return m_crownMode;
		}
		return m_nview.GetZDO().GetBool(ZDOVars.s_crowned);
	}

	public override bool IsDebugFlying()
	{
		if (m_nview == null || !m_nview.IsValid())
		{
			return false;
		}
		if (m_nview.IsOwner())
		{
			return m_debugFly;
		}
		return m_nview.GetZDO().GetBool(ZDOVars.s_debugFly);
	}

	public override void AddEitr(float v)
	{
		m_eitr += v;
		if (m_eitr > m_maxEitr)
		{
			m_eitr = m_maxEitr;
		}
	}

	public override void AddStamina(float v)
	{
		m_stamina += v;
		if (m_stamina > m_maxStamina)
		{
			m_stamina = m_maxStamina;
		}
	}

	public override void AddAdrenaline(float v)
	{
		float maxAdrenaline = GetMaxAdrenaline();
		if (v > 0f && maxAdrenaline > 0f)
		{
			float time = GetAdrenaline() / GetMaxAdrenaline();
			m_adrenalineDegenTimer = m_adrenalineDegenDelay.Evaluate(time);
			v *= Game.m_adrenalineRate;
			v *= m_adrenalineGainMultiplier.Evaluate(time);
			m_seman.ModifyAdrenaline(v, ref v);
		}
		if (v < 0f || (v > 0f && m_adrenaline < maxAdrenaline))
		{
			m_adrenaline += v;
		}
		if (m_adrenaline >= maxAdrenaline && maxAdrenaline > 0f)
		{
			List<ItemDrop.ItemData> allItems = GetInventory().GetAllItems();
			bool flag = false;
			foreach (ItemDrop.ItemData item in allItems)
			{
				if (item.m_equipped && item.m_shared.m_fullAdrenalineSE != null)
				{
					flag = true;
					StatusEffect statusEffect = GetSEMan().GetStatusEffect(item.m_shared.m_fullAdrenalineSE.NameHash());
					if (statusEffect != null)
					{
						statusEffect.ResetTime();
					}
					else
					{
						GetSEMan().AddStatusEffect(item.m_shared.m_fullAdrenalineSE, resetTime: false, 0, 0f, -1);
					}
				}
			}
			m_adrenaline = (flag ? 0f : maxAdrenaline);
			if (flag)
			{
				m_adrenalinePopEffects.Create(base.transform.position, Quaternion.identity);
			}
		}
		if (m_adrenaline < 0f)
		{
			m_adrenaline = 0f;
		}
		StatusEffect statusEffect2 = null;
		for (int num = m_adrenalineEffects.Count - 1; num >= 0; num--)
		{
			StatusEffectLevel statusEffectLevel = m_adrenalineEffects[num];
			if (m_adrenaline >= statusEffectLevel.m_rate)
			{
				statusEffect2 = statusEffectLevel.m_se;
				break;
			}
		}
		if (m_adrenalineEffects.Count <= 0 || (!(((statusEffect2 == null) ? null : GetSEMan().GetStatusEffect(statusEffect2.NameHash())) != statusEffect2) && (!(statusEffect2 == null) || !(GetSEMan().GetStatusEffect(m_adrenalineEffects[0].m_se.NameHash()) != null))))
		{
			return;
		}
		foreach (StatusEffectLevel adrenalineEffect in m_adrenalineEffects)
		{
			GetSEMan().RemoveStatusEffect(adrenalineEffect.m_se.NameHash());
		}
		if (statusEffect2 != null)
		{
			GetSEMan().AddStatusEffect(statusEffect2.NameHash(), resetTime: true, 0, 0f, -1);
			Hud.instance.AdrenalineBarFlash();
		}
	}

	public override void UseEitr(float v)
	{
		if (v == 0f)
		{
			return;
		}
		v *= Game.m_eitrRate;
		if (m_nview.IsValid())
		{
			if (m_nview.IsOwner())
			{
				RPC_UseEitr(0L, v);
				return;
			}
			m_nview.InvokeRPC("UseEitr", v);
		}
	}

	private void RPC_UseEitr(long sender, float v)
	{
		if (v != 0f)
		{
			m_eitr -= v;
			if (m_eitr < 0f)
			{
				m_eitr = 0f;
			}
			m_eitrRegenTimer = m_eitrRegenDelay;
		}
	}

	public override bool HaveEitr(float amount = 0f)
	{
		if (m_nview.IsValid() && !m_nview.IsOwner())
		{
			return m_nview.GetZDO().GetFloat(ZDOVars.s_eitr, m_maxEitr) > amount;
		}
		return m_eitr > amount;
	}

	public override void UseStamina(float v)
	{
		if (v == 0f || float.IsNaN(v))
		{
			return;
		}
		v *= Game.m_staminaRate;
		if (m_nview.IsValid())
		{
			if (m_nview.IsOwner())
			{
				RPC_UseStamina(0L, v);
				return;
			}
			m_nview.InvokeRPC("UseStamina", v);
		}
	}

	private void RPC_UseStamina(long sender, float v)
	{
		if (v != 0f)
		{
			m_stamina -= v;
			if (m_stamina < 0f)
			{
				m_stamina = 0f;
			}
			m_staminaRegenTimer = m_staminaRegenDelay;
		}
	}

	public override bool HaveStamina(float amount = 0f)
	{
		if (m_nview.IsValid() && !m_nview.IsOwner())
		{
			return m_nview.GetZDO().GetFloat(ZDOVars.s_stamina, m_maxStamina) > amount;
		}
		return m_stamina > amount;
	}

	public void Save(ZPackage pkg)
	{
		pkg.Write(34);
		pkg.Write(GetMaxHealth());
		pkg.Write(GetHealth());
		pkg.Write(GetMaxStamina());
		pkg.Write(m_timeSinceDeath);
		pkg.Write(m_guardianPower);
		pkg.Write(m_guardianPowerCooldown);
		m_inventory.Save(pkg);
		pkg.Write(m_knownRecipes.Count);
		foreach (string knownRecipe in m_knownRecipes)
		{
			pkg.Write(knownRecipe);
		}
		pkg.Write(m_knownStations.Count);
		foreach (KeyValuePair<string, int> knownStation in m_knownStations)
		{
			pkg.Write(knownStation.Key);
			pkg.Write(knownStation.Value);
		}
		pkg.Write(m_knownMaterial.Count);
		foreach (string item in m_knownMaterial)
		{
			pkg.Write(item);
		}
		pkg.Write(m_shownTutorials.Count);
		foreach (string shownTutorial in m_shownTutorials)
		{
			pkg.Write(shownTutorial);
		}
		pkg.Write(m_uniques.Count);
		foreach (string unique in m_uniques)
		{
			pkg.Write(unique);
		}
		pkg.Write(m_trophies.Count);
		foreach (string trophy in m_trophies)
		{
			pkg.Write(trophy);
		}
		pkg.Write(m_knownBiome.Count);
		foreach (string item2 in m_knownBiome)
		{
			pkg.Write(item2);
		}
		pkg.Write(m_knownTexts.Count);
		foreach (KeyValuePair<string, string> knownText in m_knownTexts)
		{
			pkg.Write(knownText.Key.Replace("\u0016", ""));
			pkg.Write(knownText.Value.Replace("\u0016", ""));
		}
		pkg.Write(m_beardItem);
		pkg.Write(m_hairItem);
		pkg.Write(m_skinColor);
		pkg.Write(m_hairColor);
		pkg.Write(m_modelIndex);
		pkg.Write(m_foods.Count);
		foreach (Food food in m_foods)
		{
			pkg.Write(food.m_name);
			pkg.Write(food.m_time);
		}
		m_skills.Save(pkg);
		pkg.Write(m_customData.Count);
		foreach (KeyValuePair<string, string> customDatum in m_customData)
		{
			pkg.Write(customDatum.Key);
			pkg.Write(customDatum.Value);
		}
		pkg.Write(GetStamina());
		pkg.Write(GetMaxEitr());
		pkg.Write(GetEitr());
		if ((bool)Hud.instance)
		{
			pkg.Write(Hud.instance.m_buildUi.SaveToBinary());
		}
		else
		{
			pkg.Write(new byte[0]);
		}
		pkg.Write(CharacterDrop.s_pseudoCounter.Count);
		foreach (KeyValuePair<int, int> item3 in CharacterDrop.s_pseudoCounter)
		{
			pkg.Write(item3.Key);
			pkg.Write(item3.Value);
		}
		pkg.Write(m_itemUpgradeSeeds.Count);
		foreach (KeyValuePair<int, int> itemUpgradeSeed in m_itemUpgradeSeeds)
		{
			pkg.Write(itemUpgradeSeed.Key);
			pkg.Write(itemUpgradeSeed.Value);
		}
	}

	public void Load(ZPackage pkg)
	{
		m_isLoading = true;
		UnequipAllItems();
		Version.PlayerData playerData = (Version.PlayerData)pkg.ReadInt();
		if (playerData >= Version.PlayerData.MaxHealth)
		{
			SetMaxHealth(pkg.ReadSingle(), flashBar: false);
		}
		float num = pkg.ReadSingle();
		float maxHealth = GetMaxHealth();
		if (num <= 0f || num > maxHealth || float.IsNaN(num))
		{
			num = maxHealth;
		}
		SetHealth(num);
		if (playerData >= Version.PlayerData.MaxStamina)
		{
			float stamina = pkg.ReadSingle();
			SetMaxStamina(stamina, flashBar: false);
			m_stamina = stamina;
		}
		if (playerData >= Version.PlayerData.FirstSpawn && playerData < Version.PlayerData.MovedFirstSpawn)
		{
			pkg.ReadBool();
		}
		if (playerData >= Version.PlayerData.TimeSinceDeath)
		{
			m_timeSinceDeath = pkg.ReadSingle();
		}
		if (playerData >= Version.PlayerData.GuardianPower)
		{
			string guardianPower = pkg.ReadString();
			SetGuardianPower(guardianPower);
		}
		if (playerData >= Version.PlayerData.GuardianPowerCooldown)
		{
			m_guardianPowerCooldown = pkg.ReadSingle();
		}
		if (playerData == Version.PlayerData.Original)
		{
			pkg.ReadZDOID();
		}
		m_inventory.Load(pkg);
		int num2 = pkg.ReadInt();
		for (int i = 0; i < num2; i++)
		{
			string item = pkg.ReadString();
			m_knownRecipes.Add(item);
		}
		if (playerData < Version.PlayerData.Stations)
		{
			int num3 = pkg.ReadInt();
			for (int j = 0; j < num3; j++)
			{
				pkg.ReadString();
			}
		}
		else
		{
			int num4 = pkg.ReadInt();
			for (int k = 0; k < num4; k++)
			{
				string key = pkg.ReadString();
				int value = pkg.ReadInt();
				m_knownStations.Add(key, value);
			}
		}
		int num5 = pkg.ReadInt();
		for (int l = 0; l < num5; l++)
		{
			string item2 = pkg.ReadString();
			m_knownMaterial.Add(item2);
		}
		if (playerData < Version.PlayerData.RemoveTutorials || playerData >= Version.PlayerData.ReAddTutorials)
		{
			int num6 = pkg.ReadInt();
			for (int m = 0; m < num6; m++)
			{
				string item3 = pkg.ReadString();
				m_shownTutorials.Add(item3);
			}
		}
		if (playerData >= Version.PlayerData.Uniques)
		{
			int num7 = pkg.ReadInt();
			for (int n = 0; n < num7; n++)
			{
				string item4 = pkg.ReadString();
				m_uniques.Add(item4);
			}
		}
		if (playerData >= Version.PlayerData.Trophies)
		{
			int num8 = pkg.ReadInt();
			for (int num9 = 0; num9 < num8; num9++)
			{
				string item5 = pkg.ReadString();
				m_trophies.Add(item5);
			}
		}
		if (playerData >= Version.PlayerData.ChunkedNorth || playerData == Version.PlayerData.AbandonedDN)
		{
			int num10 = pkg.ReadInt();
			for (int num11 = 0; num11 < num10; num11++)
			{
				string item6 = pkg.ReadString();
				m_knownBiome.Add(item6);
			}
		}
		else if (playerData >= Version.PlayerData.KnownBiomes)
		{
			int num12 = pkg.ReadInt();
			for (int num13 = 0; num13 < num12; num13++)
			{
				Heightmap.Biome biome = (Heightmap.Biome)pkg.ReadInt();
				m_knownBiome.Add(BiomeSector.GetBiomeName(biome));
			}
		}
		if (playerData >= Version.PlayerData.KnownTexts)
		{
			int num14 = pkg.ReadInt();
			for (int num15 = 0; num15 < num14; num15++)
			{
				string key2 = pkg.ReadString();
				string value2 = pkg.ReadString();
				m_knownTexts[key2] = value2;
			}
		}
		if (playerData >= Version.PlayerData.SkinHair)
		{
			string beard = pkg.ReadString();
			string hair = pkg.ReadString();
			SetBeard(beard);
			SetHair(hair);
		}
		if (playerData >= Version.PlayerData.SkinHairColor)
		{
			Vector3 skinColor = pkg.ReadVector3();
			Vector3 hairColor = pkg.ReadVector3();
			SetSkinColor(skinColor);
			SetHairColor(hairColor);
		}
		if (playerData >= Version.PlayerData.PlayerModel)
		{
			int playerModel = pkg.ReadInt();
			SetPlayerModel(playerModel);
		}
		if (playerData >= Version.PlayerData.Food)
		{
			m_foods.Clear();
			int num16 = pkg.ReadInt();
			for (int num17 = 0; num17 < num16; num17++)
			{
				if (playerData >= Version.PlayerData.Food2)
				{
					Food food = new Food();
					food.m_name = pkg.ReadString();
					if (playerData >= Version.PlayerData.FoodTime)
					{
						food.m_time = pkg.ReadSingle();
					}
					else
					{
						food.m_health = pkg.ReadSingle();
						if (playerData >= Version.PlayerData.FoodStamina)
						{
							food.m_stamina = pkg.ReadSingle();
						}
					}
					GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(food.m_name);
					if (itemPrefab == null)
					{
						ZLog.LogWarning("Failed to find food item " + food.m_name);
						continue;
					}
					food.m_item = itemPrefab.GetComponent<ItemDrop>().m_itemData;
					m_foods.Add(food);
				}
				else
				{
					pkg.ReadString();
					pkg.ReadSingle();
					pkg.ReadSingle();
					pkg.ReadSingle();
					pkg.ReadSingle();
					pkg.ReadSingle();
					pkg.ReadSingle();
					if (playerData >= Version.PlayerData.BurnRate)
					{
						pkg.ReadSingle();
					}
				}
			}
		}
		if (playerData >= Version.PlayerData.Skills)
		{
			m_skills.Load(pkg);
		}
		if (playerData >= Version.PlayerData.EitrStamina)
		{
			int num18 = pkg.ReadInt();
			for (int num19 = 0; num19 < num18; num19++)
			{
				string key3 = pkg.ReadString();
				string value3 = pkg.ReadString();
				m_customData[key3] = value3;
			}
			m_stamina = Mathf.Clamp(pkg.ReadSingle(), 0f, m_maxStamina);
			SetMaxEitr(pkg.ReadSingle(), flashBar: false);
			m_eitr = Mathf.Clamp(pkg.ReadSingle(), 0f, m_maxEitr);
		}
		if (playerData < Version.PlayerData.AshlandMaterials)
		{
			if (m_knownMaterial.Contains("$item_flametal"))
			{
				ZLog.DevLog("Pre ashlands character loaded, replacing flametal with ancient as known material.");
				m_knownMaterial.Remove("$item_flametal");
				m_knownMaterial.Add("$item_flametal_old");
			}
			if (m_knownMaterial.Contains("$item_flametalore"))
			{
				ZLog.DevLog("Pre ashlands character loaded, replacing flametal ore with ancient as known material.");
				m_knownMaterial.Remove("$item_flametalore");
				m_knownMaterial.Add("$item_flametalore_old");
			}
		}
		if (playerData >= Version.PlayerData.ChunkedNorth || playerData == Version.PlayerData.AbandonedDN)
		{
			byte[] data = pkg.ReadByteArray();
			Hud.instance?.m_buildUi.LoadFromBinary(data);
		}
		if (playerData >= Version.PlayerData.PostLaunch)
		{
			int num20 = pkg.ReadInt();
			for (int num21 = 0; num21 < num20; num21++)
			{
				int key4 = pkg.ReadInt();
				int value4 = pkg.ReadInt();
				CharacterDrop.s_pseudoCounter[key4] = value4;
			}
			num20 = pkg.ReadInt();
			for (int num22 = 0; num22 < num20; num22++)
			{
				int key5 = pkg.ReadInt();
				int value5 = pkg.ReadInt();
				m_itemUpgradeSeeds[key5] = value5;
			}
		}
		m_isLoading = false;
		UpdateAvailablePiecesList();
		EquipInventoryItems();
		UpdateEvents();
	}

	private void EquipInventoryItems()
	{
		foreach (ItemDrop.ItemData equippedItem in m_inventory.GetEquippedItems())
		{
			if (!EquipItem(equippedItem, triggerEquipEffects: false))
			{
				equippedItem.m_equipped = false;
			}
		}
	}

	public override bool CanMove()
	{
		if (m_teleporting)
		{
			return false;
		}
		if (InCutscene())
		{
			return false;
		}
		if (IsEncumbered() && !HaveStamina())
		{
			return false;
		}
		return base.CanMove();
	}

	public override bool IsEncumbered()
	{
		return m_inventory.GetTotalWeight() > GetMaxCarryWeight();
	}

	public float GetMaxCarryWeight()
	{
		float limit = m_maxCarryWeight;
		m_seman.ModifyMaxCarryWeight(limit, ref limit);
		return limit * Game.m_carryWeightRate;
	}

	public void SetInventorySize(int rows)
	{
		rows = Mathf.Clamp(rows, 0, 9);
		m_inventory.SetHeight(rows);
		AddUniqueKeyValue("invrows", rows.ToString());
		InventoryGui.instance.SetInventorySize(rows);
		DropInvalidItems();
	}

	public override bool HaveUniqueKey(string name)
	{
		return m_uniques.Contains(name);
	}

	public bool HaveUniqueKeyValue(string key, string value)
	{
		key = key.ToLower();
		value = value.ToLower();
		foreach (string unique in m_uniques)
		{
			string[] array = unique.Split(' ');
			if (array.Length >= 2 && array[0].ToLower() == key && array[1].ToLower() == value)
			{
				return true;
			}
		}
		return false;
	}

	public bool TryGetUniqueKeyValue(string key, out string value)
	{
		key = key.ToLower();
		foreach (string unique in m_uniques)
		{
			string[] array = unique.Split(' ');
			if (array.Length >= 2 && array[0].ToLower() == key)
			{
				value = array[1];
				return true;
			}
		}
		value = null;
		return false;
	}

	public bool RemoveUniqueKeyValue(string key)
	{
		key = key.ToLower();
		int count = m_uniques.Count;
		m_uniques.RemoveWhere(delegate(string x)
		{
			string[] array = x.Split(' ');
			return (array.Length >= 2 && array[0].ToLower() == key) ? true : false;
		});
		if (m_uniques.Count != count)
		{
			ZoneSystem.instance?.UpdateWorldRates();
			UpdateEvents();
			return true;
		}
		return false;
	}

	public void AddUniqueKeyValue(string key, string value)
	{
		if (TryGetUniqueKeyValue(key, out var value2))
		{
			if (value2 == value)
			{
				return;
			}
			RemoveUniqueKeyValue(key);
			ZLog.Log("removed old key: " + key + " " + value2);
		}
		AddUniqueKey(key + " " + value);
	}

	public override void AddUniqueKey(string name)
	{
		if (!string.IsNullOrEmpty(name))
		{
			if (!m_uniques.Contains(name))
			{
				m_uniques.Add(name);
			}
			ZoneSystem.instance?.UpdateWorldRates();
			UpdateEvents();
		}
	}

	public override bool RemoveUniqueKey(string name)
	{
		if (m_uniques.Contains(name))
		{
			m_uniques.Remove(name);
			ZoneSystem.instance.UpdateWorldRates();
			UpdateEvents();
			return true;
		}
		return false;
	}

	public List<string> GetUniqueKeys()
	{
		m_tempUniqueKeys.Clear();
		m_tempUniqueKeys.AddRange(m_uniques);
		return m_tempUniqueKeys;
	}

	public void ResetUniqueKeys()
	{
		m_uniques.Clear();
	}

	public bool IsBiomeKnown(BiomeSector biome)
	{
		return m_knownBiome.Contains(biome.GetName());
	}

	private void AddKnownBiome(BiomeSector biome)
	{
		string text = biome.GetName();
		if (!m_knownBiome.Contains(text))
		{
			m_knownBiome.Add(text);
			if ((biome.Biome != Heightmap.Biome.Meadows && biome.Biome != Heightmap.Biome.None) || biome.AltBiomes.Count > 0)
			{
				string text2 = biome.GetName();
				MessageHud.instance.ShowBiomeFoundMsg(text2, playStinger: true);
			}
			if (biome.Biome == Heightmap.Biome.BlackForest && !ZoneSystem.instance.GetGlobalKey("defeated_eikthyr"))
			{
				ShowTutorial("blackforest");
			}
			Gogan.LogEvent("Game", "BiomeFound", text, 0L);
		}
		if (biome.Biome == Heightmap.Biome.BlackForest)
		{
			ShowTutorial("haldor");
		}
		if (biome.Biome == Heightmap.Biome.AshLands)
		{
			ShowTutorial("ashlands");
		}
	}

	public void AddKnownLocationName(string label)
	{
		if (!m_shownTutorials.Contains(label))
		{
			m_shownTutorials.Add(label);
			MessageHud.instance.ShowBiomeFoundMsg(label, playStinger: true);
		}
	}

	public bool IsRecipeKnown(string name)
	{
		return m_knownRecipes.Contains(name);
	}

	private void AddKnownRecipe(Recipe recipe)
	{
		if (!m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name))
		{
			m_knownRecipes.Add(recipe.m_item.m_itemData.m_shared.m_name);
			MessageHud.instance.QueueUnlockMsg(recipe.m_item.m_itemData.GetIcon(), "$msg_newrecipe", recipe.m_item.m_itemData.m_shared.m_name);
			Gogan.LogEvent("Game", "RecipeFound", recipe.m_item.m_itemData.m_shared.m_name, 0L);
		}
	}

	private void AddKnownPiece(Piece piece)
	{
		if (!m_knownRecipes.Contains(piece.m_name))
		{
			m_knownRecipes.Add(piece.m_name);
			Hud.instance.m_buildUi.AddRecentPiece(piece);
			string topic = ((piece.m_category == Piece.PieceCategory.Feasts || piece.m_category == Piece.PieceCategory.Food || piece.m_category == Piece.PieceCategory.Meads) ? "$msg_newdish" : "$msg_newpiece");
			MessageHud.instance.QueueUnlockMsg(piece.m_icon, topic, piece.m_name);
			Gogan.LogEvent("Game", "PieceFound", piece.m_name, 0L);
		}
	}

	public void AddKnownStation(CraftingStation station)
	{
		int level = station.GetLevel();
		if (m_knownStations.TryGetValue(station.m_name, out var value))
		{
			if (value < level)
			{
				m_knownStations[station.m_name] = level;
				MessageHud.instance.QueueUnlockMsg(station.m_icon, "$msg_newstation_level", station.m_name + " $msg_level " + level);
				UpdateKnownRecipesList();
			}
		}
		else
		{
			m_knownStations.Add(station.m_name, level);
			MessageHud.instance.QueueUnlockMsg(station.m_icon, "$msg_newstation", station.m_name);
			Gogan.LogEvent("Game", "StationFound", station.m_name, 0L);
			UpdateKnownRecipesList();
		}
	}

	private bool KnowStationLevel(string name, int level)
	{
		if (m_knownStations.TryGetValue(name, out var value))
		{
			return value >= level;
		}
		return false;
	}

	public void AddKnownText(string label, string text)
	{
		if (label.Length == 0)
		{
			ZLog.LogWarning("Text " + text + " Is missing label");
		}
		else if (!m_knownTexts.ContainsKey(label.Replace("\u0016", "")))
		{
			m_knownTexts.Add(label, text);
			Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_newtext", label), 0, m_textIcon);
		}
	}

	public List<KeyValuePair<string, string>> GetKnownTexts()
	{
		return m_knownTexts.ToList();
	}

	public void AddKnownItem(ItemDrop.ItemData item)
	{
		if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy)
		{
			AddTrophy(item);
		}
		if (!m_knownMaterial.Contains(item.m_shared.m_name))
		{
			m_knownMaterial.Add(item.m_shared.m_name);
			if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material)
			{
				MessageHud.instance.QueueUnlockMsg(item.GetIcon(), "$msg_newmaterial", item.m_shared.m_name);
			}
			else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy)
			{
				MessageHud.instance.QueueUnlockMsg(item.GetIcon(), "$msg_newtrophy", item.m_shared.m_name);
			}
			else
			{
				MessageHud.instance.QueueUnlockMsg(item.GetIcon(), "$msg_newitem", item.m_shared.m_name);
			}
			Gogan.LogEvent("Game", "ItemFound", item.m_shared.m_name, 0L);
			UpdateKnownRecipesList();
			UpdateEvents();
		}
	}

	private void AddTrophy(ItemDrop.ItemData item)
	{
		if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy)
		{
			return;
		}
		string item2;
		if (item.m_dropPrefab != null)
		{
			item2 = item.m_dropPrefab.name;
		}
		else
		{
			GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item.m_shared);
			if (!(itemPrefab != null))
			{
				ZLog.LogError("Trying to add known trophy that is missing itemprefab in the database: " + item.m_shared.m_name);
				return;
			}
			item2 = itemPrefab.name;
		}
		if (!m_trophies.Contains(item2))
		{
			m_trophies.Add(item2);
		}
	}

	public List<string> GetTrophies()
	{
		List<string> list = new List<string>();
		list.AddRange(m_trophies);
		return list;
	}

	private void UpdateKnownRecipesList()
	{
		if (Game.instance == null)
		{
			return;
		}
		foreach (Recipe recipe in ObjectDB.instance.m_recipes)
		{
			bool flag = m_currentSeason != null && m_currentSeason.Recipes.Contains(recipe);
			if ((recipe.m_enabled || flag) && (bool)recipe.m_item && !m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name) && HaveRequirements(recipe, discover: true, 0))
			{
				AddKnownRecipe(recipe);
			}
		}
		m_tempOwnedPieceTables.Clear();
		m_inventory.GetAllPieceTables(m_tempOwnedPieceTables);
		bool flag2 = false;
		foreach (PieceTable tempOwnedPieceTable in m_tempOwnedPieceTables)
		{
			foreach (GameObject piece in tempOwnedPieceTable.m_pieces)
			{
				Piece component = piece.GetComponent<Piece>();
				bool flag3 = m_currentSeason != null && m_currentSeason.Pieces.Contains(piece);
				if ((component.m_enabled || flag3) && !m_knownRecipes.Contains(component.m_name) && HaveRequirements(component, RequirementMode.IsKnown))
				{
					AddKnownPiece(component);
					flag2 = true;
				}
			}
		}
		if (flag2)
		{
			UpdateAvailablePiecesList();
		}
	}

	private void UpdateAvailablePiecesList()
	{
		if (m_buildPieces != null)
		{
			m_buildPieces.UpdateAvailable(m_knownRecipes, this, hideUnavailable: false, m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllPiecesUnlocked));
		}
		SetupPlacementGhost();
	}

	private void UpdateCurrentSeason()
	{
		m_currentSeason = null;
		foreach (SeasonalItemGroup seasonalItemGroup in m_seasonalItemGroups)
		{
			if (seasonalItemGroup.IsInSeason())
			{
				m_currentSeason = seasonalItemGroup;
				break;
			}
		}
	}

	public override void Message(MessageHud.MessageType type, string msg, int amount = 0, Sprite icon = null, bool log = false)
	{
		if (m_nview == null || !m_nview.IsValid())
		{
			return;
		}
		if (m_nview.IsOwner())
		{
			if ((bool)MessageHud.instance)
			{
				MessageHud.instance.ShowMessage(type, msg, amount, icon, showDespiteHiddenHUD: false, log);
			}
		}
		else
		{
			m_nview.InvokeRPC("Message", (int)type, msg, amount);
		}
	}

	private void RPC_Message(long sender, int type, string msg, int amount)
	{
		if (m_nview.IsOwner() && (bool)MessageHud.instance)
		{
			MessageHud.instance.ShowMessage((MessageHud.MessageType)type, msg, amount);
		}
	}

	public static Player GetPlayer(long playerID)
	{
		foreach (Player s_player in s_players)
		{
			if (s_player.GetPlayerID() == playerID)
			{
				return s_player;
			}
		}
		return null;
	}

	public static Player GetClosestPlayer(Vector3 point, float maxRange)
	{
		Player result = null;
		float num = 999999f;
		foreach (Player s_player in s_players)
		{
			float num2 = Vector3.Distance(s_player.transform.position, point);
			if (num2 < num && num2 < maxRange)
			{
				num = num2;
				result = s_player;
			}
		}
		return result;
	}

	public static bool IsPlayerInRange(Vector3 point, float range, long playerID)
	{
		foreach (Player s_player in s_players)
		{
			if (s_player.GetPlayerID() == playerID)
			{
				return Utils.DistanceXZ(s_player.transform.position, point) < range;
			}
		}
		return false;
	}

	public static void MessageAllInRange(Vector3 point, float range, MessageHud.MessageType type, string msg, Sprite icon = null)
	{
		foreach (Player s_player in s_players)
		{
			if (Vector3.Distance(s_player.transform.position, point) < range)
			{
				s_player.Message(type, msg, 0, icon);
			}
		}
	}

	public static int GetPlayersInRangeXZ(Vector3 point, float range)
	{
		int num = 0;
		foreach (Player s_player in s_players)
		{
			if (Utils.DistanceXZ(s_player.transform.position, point) < range)
			{
				num++;
			}
		}
		return num;
	}

	public static void GetPlayersInRange(Vector3 point, float range, List<Player> players)
	{
		foreach (Player s_player in s_players)
		{
			if (Vector3.Distance(s_player.transform.position, point) < range)
			{
				players.Add(s_player);
			}
		}
	}

	public static bool IsPlayerInRange(Vector3 point, float range)
	{
		foreach (Player s_player in s_players)
		{
			if (Vector3.Distance(s_player.transform.position, point) < range)
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsPlayerInRange(Vector3 point, float range, float minNoise)
	{
		foreach (Player s_player in s_players)
		{
			if (Vector3.Distance(s_player.transform.position, point) < range)
			{
				float noiseRange = s_player.GetNoiseRange();
				if (range <= noiseRange && noiseRange >= minNoise)
				{
					return true;
				}
			}
		}
		return false;
	}

	public static Player GetPlayerNoiseRange(Vector3 point, float maxNoiseRange = 100f)
	{
		foreach (Player s_player in s_players)
		{
			float num = Vector3.Distance(s_player.transform.position, point);
			float num2 = Mathf.Min(s_player.GetNoiseRange(), maxNoiseRange);
			if (num < num2)
			{
				return s_player;
			}
		}
		return null;
	}

	public static List<Player> GetAllPlayers()
	{
		return s_players;
	}

	public static Player GetRandomPlayer()
	{
		if (s_players.Count == 0)
		{
			return null;
		}
		return s_players[UnityEngine.Random.Range(0, s_players.Count)];
	}

	public void GetAvailableRecipes(ref List<Recipe> available)
	{
		available.Clear();
		foreach (Recipe recipe in ObjectDB.instance.m_recipes)
		{
			bool flag = m_currentSeason != null && m_currentSeason.Recipes.Contains(recipe);
			if ((!recipe.m_enabled && !flag) || !recipe.m_item)
			{
				continue;
			}
			if (s_FilterCraft.Count > 0)
			{
				bool flag2 = false;
				for (int i = 0; i < s_FilterCraft.Count && (s_FilterCraft[i].Length <= 0 || (!recipe.m_item.name.ToLower().Contains(s_FilterCraft[i].ToLower()) && !recipe.m_item.m_itemData.m_shared.m_name.ToLower().Contains(s_FilterCraft[i].ToLower()) && !Localization.instance.Localize(recipe.m_item.m_itemData.m_shared.m_name).ToLower().Contains(s_FilterCraft[i].ToLower()))); i++)
				{
					if (i + 1 == s_FilterCraft.Count)
					{
						flag2 = true;
					}
				}
				if (flag2)
				{
					continue;
				}
			}
			if ((recipe.m_item.m_itemData.m_shared.m_dlc.Length <= 0 || DLCMan.instance.IsDLCInstalled(recipe.m_item.m_itemData.m_shared.m_dlc)) && (m_knownRecipes.Contains(recipe.m_item.m_itemData.m_shared.m_name) || m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllRecipesUnlocked)) && (RequiredCraftingStation(recipe, 1, checkLevel: false) || m_noPlacementCost))
			{
				available.Add(recipe);
			}
		}
	}

	private void OnInventoryChanged()
	{
		if (m_isLoading)
		{
			return;
		}
		List<ItemDrop.ItemData> allItems = m_inventory.GetAllItems();
		PlayerProfile playerProfile = Game.instance.GetPlayerProfile();
		foreach (ItemDrop.ItemData item in allItems)
		{
			AddKnownItem(item);
			bool flag = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && item.m_pickedUp;
			if (!item.m_pickedUp)
			{
				item.m_pickedUp = true;
				playerProfile.IncrementStat(PlayerStatType.ItemsPickedUp);
				playerProfile.IncrementStatItemPickup(item.m_shared.m_name, item.m_stack, item.m_cheated);
			}
			else if (flag && (!playerProfile.m_playerStats[1].m_itemPickupStats.ContainsKey(item.m_shared.m_name) || !playerProfile.m_playerStats[Achievements.GetCurrentAchievementDifficultyIndex()].m_itemPickupStats.ContainsKey(item.m_shared.m_name)))
			{
				playerProfile.IncrementStat(PlayerStatType.ItemsPickedUp);
				playerProfile.IncrementStatItemPickup(item.m_shared.m_name, item.m_stack, item.m_cheated);
			}
			if (item.m_shared.m_name == "$item_hammer")
			{
				ShowTutorial("hammer");
			}
			else if (item.m_shared.m_name == "$item_hoe")
			{
				ShowTutorial("hoe");
			}
			else if (item.m_shared.m_name == "$item_bellfragment")
			{
				ShowTutorial("bellfragment");
			}
			else if (item.m_shared.m_name == "$item_pickaxe_antler")
			{
				ShowTutorial("pickaxe");
			}
			else if (item.m_shared.m_name.CustomStartsWith("$item_shield"))
			{
				ShowTutorial("shield");
			}
			else if (item.m_shared.m_name.CustomStartsWith("$item_mold"))
			{
				ShowTutorial("mold");
			}
			else if (item.m_shared.m_name == "$item_frozenking_drop")
			{
				ShowTutorial("sacrificialblood");
			}
			if (item.m_shared.m_name == "$item_trophy_eikthyr")
			{
				ShowTutorial("boss_trophy");
			}
			if (item.m_shared.m_name == "$item_wishbone")
			{
				ShowTutorial("wishbone");
			}
			else if (item.m_shared.m_name == "$item_copperore" || item.m_shared.m_name == "$item_tinore")
			{
				ShowTutorial("ore");
			}
			else if (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f)
			{
				ShowTutorial("food");
			}
			else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket)
			{
				ShowTutorial("trinket");
			}
		}
		UpdateKnownRecipesList();
		UpdateAvailablePiecesList();
	}

	public bool InDebugFlyMode()
	{
		return m_debugFly;
	}

	public void ShowTutorial(string name, bool force = false)
	{
		if (!HaveSeenTutorial(name))
		{
			Tutorial.instance.ShowText(name, force);
		}
	}

	public void SetSeenTutorial(string name)
	{
		if (name.Length != 0 && !m_shownTutorials.Contains(name))
		{
			m_shownTutorials.Add(name);
		}
	}

	public bool HaveSeenTutorial(string name)
	{
		if (name.Length == 0)
		{
			return false;
		}
		return m_shownTutorials.Contains(name);
	}

	public static bool IsSeenTutorialsCleared()
	{
		if ((bool)m_localPlayer)
		{
			return m_localPlayer.m_shownTutorials.Count == 0;
		}
		return true;
	}

	public static void ResetSeenTutorials()
	{
		if ((bool)m_localPlayer)
		{
			m_localPlayer.m_shownTutorials.Clear();
		}
	}

	public void SetMouseLook(Vector2 mouseLook)
	{
		Quaternion quaternion = m_lookYaw * Quaternion.Euler(0f, mouseLook.x, 0f);
		if (PlayerCustomizaton.IsBarberGuiVisible())
		{
			if (Vector3.Dot(base.transform.rotation * Vector3.forward, m_lookYaw * Vector3.forward) > 0f)
			{
				SetMouseLookBackward();
			}
			if (Vector3.Dot(base.transform.rotation * Vector3.forward, quaternion * Vector3.forward) < 0f)
			{
				m_lookYaw = quaternion;
			}
		}
		else
		{
			m_lookYaw = quaternion;
		}
		m_lookPitch = Mathf.Clamp(m_lookPitch - mouseLook.y, -89f, 89f);
		UpdateEyeRotation();
		m_lookDir = m_eye.forward;
		if (m_lookTransitionTime > 0f && mouseLook != Vector2.zero)
		{
			m_lookTransitionTime = 0f;
		}
	}

	public void SetMouseLookForward(bool includePitch = true)
	{
		m_lookYaw = Quaternion.Euler(0f, base.transform.rotation.eulerAngles.y, 0f);
		if (includePitch)
		{
			m_lookPitch = 0f;
		}
	}

	public void SetMouseLookBackward(bool includePitch = true)
	{
		m_lookYaw = Quaternion.Euler(0f, base.transform.rotation.eulerAngles.y + 180f, 0f);
		if (includePitch)
		{
			m_lookPitch = 0f;
		}
	}

	protected override void UpdateEyeRotation()
	{
		m_eye.rotation = m_lookYaw * Quaternion.Euler(m_lookPitch, 0f, 0f);
	}

	public Ragdoll GetRagdoll()
	{
		return m_ragdoll;
	}

	public void OnDodgeMortal()
	{
		m_dodgeInvincible = false;
	}

	private float GetDodgeStaminaUse()
	{
		float staminaUse = m_dodgeStaminaUsage - m_dodgeStaminaUsage * GetEquipmentMovementModifier() + m_dodgeStaminaUsage * GetEquipmentDodgeStaminaModifier();
		m_seman.ModifyDodgeStaminaUsage(staminaUse, ref staminaUse);
		float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Dodge);
		float num = Mathf.Lerp(1f, 0.5f, skillFactor);
		return staminaUse * num;
	}

	private void UpdateDodge(float dt)
	{
		m_queuedDodgeTimer -= dt;
		if (m_queuedDodgeTimer > 0f && IsOnGround() && !IsDead() && !InAttack() && !IsEncumbered() && !InDodge() && !IsStaggering())
		{
			float dodgeStaminaUse = GetDodgeStaminaUse();
			if (HaveStamina(dodgeStaminaUse))
			{
				ClearActionQueue();
				m_queuedDodgeTimer = 0f;
				m_dodgeInvincible = true;
				base.transform.rotation = Quaternion.LookRotation(m_queuedDodgeDir);
				m_body.rotation = base.transform.rotation;
				m_zanim.SetTrigger("dodge");
				AddNoise(5f);
				UseStamina(dodgeStaminaUse);
				m_dodgeEffects.Create(base.transform.position, Quaternion.identity, base.transform, 1f, -1, GetZDOID());
			}
			else
			{
				Hud.instance.StaminaBarEmptyFlash();
			}
		}
		bool flag = m_animator.GetBool(s_animatorTagDodge) || GetNextOrCurrentAnimHash() == s_animatorTagDodge;
		bool flag2 = flag && m_dodgeInvincible;
		if (m_dodgeInvincibleCached != flag2)
		{
			m_nview.GetZDO().Set(ZDOVars.s_dodgeinv, flag2);
		}
		m_dodgeInvincibleCached = flag2;
		if (!m_inDodge)
		{
			m_beenHitWhileDodging = false;
		}
		m_inDodge = flag;
	}

	public override bool IsDodgeInvincible()
	{
		if (!m_nview.IsValid())
		{
			return false;
		}
		if (m_nview.IsOwner())
		{
			return m_dodgeInvincibleCached;
		}
		return m_nview.GetZDO().GetBool(ZDOVars.s_dodgeinv);
	}

	public override bool InDodge()
	{
		if (!m_nview.IsValid() || !m_nview.IsOwner())
		{
			return false;
		}
		return m_inDodge;
	}

	public override bool IsDead()
	{
		return m_nview.GetZDO()?.GetBool(ZDOVars.s_dead) ?? false;
	}

	private void Dodge(Vector3 dodgeDir)
	{
		if (!IsEncumbered())
		{
			m_queuedDodgeTimer = 0.5f;
			m_queuedDodgeDir = dodgeDir;
			m_skills.RaiseSkill(Skills.SkillType.Dodge, 0.1f);
		}
	}

	public void HitWhileDodging()
	{
		m_nview.InvokeRPC("RPC_HitWhileDodging");
	}

	private void RPC_HitWhileDodging(long sender)
	{
		if (m_nview.IsOwner() && !m_beenHitWhileDodging)
		{
			m_beenHitWhileDodging = true;
			m_perfectDodgeEffects.Create(base.transform.position, Quaternion.identity, base.transform, 1f, -1, GetZDOID());
			float dodgeStaminaUse = GetDodgeStaminaUse();
			AddStamina(dodgeStaminaUse * m_perfectDodgeStaminaReturnMultiplier);
			AddAdrenaline(m_perfectDodgeAdrenaline);
			m_skills.RaiseSkill(Skills.SkillType.Dodge);
		}
	}

	protected override bool AlwaysRotateCamera()
	{
		ItemDrop.ItemData currentWeapon = GetCurrentWeapon();
		if ((currentWeapon != null && m_currentAttack != null && m_lastCombatTimer < 1f && m_currentAttack.m_attackType != Attack.AttackType.None && !m_attackTowardsPlayerLookDir) || IsDrawingBow() || m_blocking)
		{
			return true;
		}
		if (currentWeapon != null && currentWeapon.m_shared.m_alwaysRotate && m_moveDir.magnitude < 0.01f)
		{
			return true;
		}
		if (m_currentAttack != null && m_currentAttack.m_loopingAttack && InAttack())
		{
			return true;
		}
		if (InPlaceMode())
		{
			Vector3 vector = GetLookYaw() * Vector3.forward;
			Vector3 forward = base.transform.forward;
			if (Vector3.Angle(vector, forward) > 95f)
			{
				return true;
			}
		}
		return false;
	}

	public override bool TeleportTo(Vector3 pos, Quaternion rot, bool distantTeleport)
	{
		if (!m_nview.IsOwner())
		{
			m_nview.InvokeRPC("RPC_TeleportTo", pos, rot, distantTeleport);
			return false;
		}
		if (IsTeleporting())
		{
			return false;
		}
		if (m_teleportCooldown < 2f)
		{
			return false;
		}
		m_teleporting = true;
		m_distantTeleport = distantTeleport;
		m_teleportTimer = 0f;
		m_teleportCooldown = 0f;
		InvalidateCachedLiquidDepth();
		m_teleportFromPos = base.transform.position;
		m_teleportFromRot = base.transform.rotation;
		m_teleportTargetPos = pos;
		m_teleportTargetRot = rot;
		return true;
	}

	private void UpdateTeleport(float dt)
	{
		if (!m_teleporting)
		{
			m_teleportCooldown += dt;
			return;
		}
		m_teleportCooldown = 0f;
		m_teleportTimer += dt;
		if (!(m_teleportTimer > 2f))
		{
			return;
		}
		Vector3 dir = m_teleportTargetRot * Vector3.forward;
		base.transform.position = m_teleportTargetPos;
		base.transform.rotation = m_teleportTargetRot;
		m_body.linearVelocity = Vector3.zero;
		m_maxAirAltitude = base.transform.position.y;
		EnvMan.instance.ForceInstantEnvironmentSwitch();
		SetLookDir(dir);
		if ((!(m_teleportTimer > 8f) && m_distantTeleport) || !ZNetScene.instance.IsAreaReady(m_teleportTargetPos))
		{
			return;
		}
		float height = 0f;
		if (ZoneSystem.instance.FindFloor(m_teleportTargetPos, out height))
		{
			m_teleportTimer = 0f;
			m_teleporting = false;
			ResetCloth();
		}
		else if (m_teleportTimer > 15f || !m_distantTeleport)
		{
			if (m_distantTeleport)
			{
				Vector3 position = base.transform.position;
				position.y = ZoneSystem.instance.GetSolidHeight(m_teleportTargetPos) + 0.5f;
				base.transform.position = position;
			}
			else
			{
				base.transform.rotation = m_teleportFromRot;
				base.transform.position = m_teleportFromPos;
				m_maxAirAltitude = base.transform.position.y;
				Message(MessageHud.MessageType.Center, "$msg_portal_blocked");
			}
			m_teleportTimer = 0f;
			m_teleporting = false;
			ResetCloth();
		}
	}

	public override bool IsTeleporting()
	{
		return m_teleporting;
	}

	public bool ShowTeleportAnimation()
	{
		if (m_teleporting)
		{
			return m_distantTeleport;
		}
		return false;
	}

	public void SetPlayerModel(int index)
	{
		if (m_modelIndex != index)
		{
			m_modelIndex = index;
			m_visEquipment.SetModel(index);
		}
	}

	public int GetPlayerModel()
	{
		return m_modelIndex;
	}

	public void SetSkinColor(Vector3 color)
	{
		if (!(color == m_skinColor))
		{
			m_skinColor = color;
			m_visEquipment.SetSkinColor(m_skinColor);
		}
	}

	public void SetHairColor(Vector3 color)
	{
		if (!(m_hairColor == color))
		{
			m_hairColor = color;
			m_visEquipment.SetHairColor(m_hairColor);
		}
	}

	public Vector3 GetHairColor()
	{
		return m_hairColor;
	}

	protected override void SetupVisEquipment(VisEquipment visEq, bool isRagdoll)
	{
		base.SetupVisEquipment(visEq, isRagdoll);
		visEq.SetModel(m_modelIndex);
		visEq.SetSkinColor(m_skinColor);
		visEq.SetHairColor(m_hairColor);
	}

	public override bool CanConsumeItem(ItemDrop.ItemData item, bool checkWorldLevel = false)
	{
		if (!base.CanConsumeItem(item, checkWorldLevel))
		{
			return false;
		}
		if (item.m_shared.m_food > 0f && !CanEat(item, showMessages: true))
		{
			return false;
		}
		if ((bool)item.m_shared.m_consumeStatusEffect)
		{
			StatusEffect consumeStatusEffect = item.m_shared.m_consumeStatusEffect;
			if (m_seman.HaveStatusEffect(item.m_shared.m_consumeStatusEffect.NameHash()) || m_seman.HaveStatusEffectCategory(consumeStatusEffect.m_category))
			{
				Message(MessageHud.MessageType.Center, "$msg_cantconsume");
				return false;
			}
		}
		return true;
	}

	public override bool ConsumeItem(Inventory inventory, ItemDrop.ItemData item, bool checkWorldLevel = false)
	{
		if (!CanConsumeItem(item, checkWorldLevel))
		{
			return false;
		}
		if ((bool)item.m_shared.m_consumeStatusEffect)
		{
			_ = item.m_shared.m_consumeStatusEffect;
			m_seman.AddStatusEffect(item.m_shared.m_consumeStatusEffect, resetTime: true, 0, 0f, -1);
		}
		if (item.m_shared.m_food > 0f)
		{
			EatFood(item);
		}
		inventory.RemoveOneItem(item);
		return true;
	}

	public void SetIntro(bool intro)
	{
		if (m_intro != intro)
		{
			m_intro = intro;
			m_zanim.SetBool("intro", intro);
		}
	}

	public override bool InIntro()
	{
		return m_intro;
	}

	public override bool InCutscene()
	{
		if (GetCurrentAnimHash() == s_animatorTagCutscene)
		{
			return true;
		}
		if (InIntro())
		{
			return true;
		}
		if (m_sleeping)
		{
			return true;
		}
		if (CinematicsManager.IsPlaying())
		{
			return true;
		}
		return base.InCutscene();
	}

	public void SetMaxStamina(float stamina, bool flashBar)
	{
		if (flashBar && Hud.instance != null && stamina > m_maxStamina)
		{
			Hud.instance.StaminaBarUppgradeFlash();
		}
		m_maxStamina = stamina;
		m_stamina = Mathf.Clamp(m_stamina, 0f, m_maxStamina);
	}

	private void SetMaxEitr(float eitr, bool flashBar)
	{
		if (flashBar && Hud.instance != null && eitr > m_maxEitr)
		{
			Hud.instance.EitrBarUppgradeFlash();
		}
		m_maxEitr = eitr;
		m_eitr = Mathf.Clamp(m_eitr, 0f, m_maxEitr);
	}

	public void SetMaxHealth(float health, bool flashBar)
	{
		if (flashBar && Hud.instance != null && health > GetMaxHealth())
		{
			Hud.instance.FlashHealthBar();
		}
		SetMaxHealth(health);
	}

	public override bool IsPVPEnabled()
	{
		if (!m_nview.IsValid())
		{
			return false;
		}
		if (m_nview.IsOwner())
		{
			return m_pvp;
		}
		return m_nview.GetZDO().GetBool(ZDOVars.s_pvp);
	}

	public void SetPVP(bool enabled)
	{
		if (m_pvp != enabled)
		{
			m_pvp = enabled;
			m_nview.GetZDO().Set(ZDOVars.s_pvp, m_pvp);
			if (m_pvp)
			{
				Message(MessageHud.MessageType.Center, "$msg_pvpon");
			}
			else
			{
				Message(MessageHud.MessageType.Center, "$msg_pvpoff");
			}
		}
	}

	public bool CanSwitchPVP()
	{
		return m_lastCombatTimer > 10f;
	}

	public bool NoCostCheat()
	{
		return m_noPlacementCost;
	}

	public bool StartEmote(string emote, bool oneshot = true)
	{
		if (!CanMove() || InAttack() || IsDrawingBow() || IsAttached() || IsAttachedToShip())
		{
			return false;
		}
		SetCrouch(crouch: false);
		int num = m_nview.GetZDO().GetInt(ZDOVars.s_emoteID);
		m_nview.GetZDO().Set(ZDOVars.s_emoteID, num + 1);
		m_nview.GetZDO().Set(ZDOVars.s_emote, emote);
		m_nview.GetZDO().Set(ZDOVars.s_emoteOneshot, oneshot);
		LastEmote = emote;
		LastEmoteTime = DateTime.Now;
		return true;
	}

	protected override void StopEmote()
	{
		if (m_nview.GetZDO().GetString(ZDOVars.s_emote) != "")
		{
			int num = m_nview.GetZDO().GetInt(ZDOVars.s_emoteID);
			m_nview.GetZDO().Set(ZDOVars.s_emoteID, num + 1);
			m_nview.GetZDO().Set(ZDOVars.s_emote, "");
		}
	}

	private void UpdateEmote()
	{
		if (m_nview.IsOwner() && InEmote() && m_moveDir != Vector3.zero)
		{
			StopEmote();
		}
		int num = m_nview.GetZDO().GetInt(ZDOVars.s_emoteID);
		if (num == m_emoteID)
		{
			return;
		}
		m_emoteID = num;
		if (!string.IsNullOrEmpty(m_emoteState))
		{
			m_animator.SetBool("emote_" + m_emoteState, value: false);
		}
		m_emoteState = "";
		m_animator.SetTrigger("emote_stop");
		string text = m_nview.GetZDO().GetString(ZDOVars.s_emote);
		if (!string.IsNullOrEmpty(text))
		{
			bool num2 = m_nview.GetZDO().GetBool(ZDOVars.s_emoteOneshot);
			m_animator.ResetTrigger("emote_stop");
			if (num2)
			{
				m_animator.SetTrigger("emote_" + text);
				return;
			}
			m_emoteState = text;
			m_animator.SetBool("emote_" + text, value: true);
		}
	}

	public override bool InEmote()
	{
		if (string.IsNullOrEmpty(m_emoteState))
		{
			return GetCurrentAnimHash() == s_animatorTagEmote;
		}
		return true;
	}

	public override bool IsCrouching()
	{
		return GetCurrentAnimHash() == s_animatorTagCrouch;
	}

	private void UpdateCrouch(float dt)
	{
		if (m_crouchToggled)
		{
			if (!HaveStamina() || IsSwimming() || InBed() || InPlaceMode() || m_run || IsBlocking() || IsFlying())
			{
				SetCrouch(crouch: false);
			}
			bool flag = InAttack() || IsDrawingBow();
			m_zanim.SetBool(s_crouching, m_crouchToggled && !flag);
		}
		else
		{
			m_zanim.SetBool(s_crouching, value: false);
		}
	}

	protected override void SetCrouch(bool crouch)
	{
		m_crouchToggled = crouch;
	}

	public void SetGuardianPower(string name)
	{
		m_guardianPower = name;
		m_guardianPowerHash = ((!string.IsNullOrEmpty(name)) ? name.GetStableHashCode() : 0);
		m_guardianSE = ObjectDB.instance.GetStatusEffect(m_guardianPowerHash);
		if ((bool)ZoneSystem.instance && !string.IsNullOrEmpty(name))
		{
			AddUniqueKey(name);
		}
	}

	public string GetGuardianPowerName()
	{
		return m_guardianPower;
	}

	public void GetGuardianPowerHUD(out StatusEffect se, out float cooldown)
	{
		se = m_guardianSE;
		cooldown = m_guardianPowerCooldown;
	}

	public bool StartGuardianPower()
	{
		if (m_guardianSE == null)
		{
			return false;
		}
		if ((InAttack() && !HaveQueuedChain()) || InDodge() || !CanMove() || IsKnockedBack() || IsStaggering() || InMinorAction())
		{
			return false;
		}
		if (m_guardianPowerCooldown > 0f)
		{
			Message(MessageHud.MessageType.Center, "$hud_powernotready");
			return false;
		}
		m_zanim.SetTrigger("gpower");
		Game.instance.IncrementPlayerStat(PlayerStatType.UseGuardianPower);
		string prefabName = Utils.GetPrefabName(m_guardianSE.name);
		switch (prefabName)
		{
		case "GP_Eikthyr":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerEikthyr);
			break;
		case "GP_TheElder":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerElder);
			break;
		case "GP_Bonemass":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerBonemass);
			break;
		case "GP_Moder":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerModer);
			break;
		case "GP_Yagluth":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerYagluth);
			break;
		case "GP_Queen":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerQueen);
			break;
		case "GP_Ashlands":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerAshlands);
			break;
		case "GP_DeepNorth":
			Game.instance.IncrementPlayerStat(PlayerStatType.UsePowerDeepNorth);
			break;
		default:
			ZLog.LogWarning("Missing stat for guardian power: " + prefabName);
			break;
		}
		return true;
	}

	public bool ActivateGuardianPower()
	{
		if (m_guardianPowerCooldown > 0f)
		{
			return false;
		}
		if (m_guardianSE == null)
		{
			return false;
		}
		List<Player> list = new List<Player>();
		GetPlayersInRange(base.transform.position, 10f, list);
		foreach (Player item in list)
		{
			item.GetSEMan().AddStatusEffect(m_guardianSE.NameHash(), resetTime: true, 0, 0f, -1);
		}
		if (m_adrenalineGuardianPower != 0f)
		{
			AddAdrenaline(m_adrenalineGuardianPower);
		}
		m_guardianPowerCooldown = m_guardianSE.m_cooldown;
		return false;
	}

	private void UpdateGuardianPower(float dt)
	{
		m_guardianPowerCooldown -= dt;
		if (m_guardianPowerCooldown < 0f)
		{
			m_guardianPowerCooldown = 0f;
		}
	}

	public override void AttachStart(Transform attachPoint, GameObject colliderRoot, bool hideWeapons, bool isBed, bool onShip, string attachAnimation, Vector3 detachOffset, Transform cameraPos = null)
	{
		if (m_attached)
		{
			return;
		}
		m_attached = true;
		m_attachedToShip = onShip;
		m_attachPoint = attachPoint;
		m_detachOffset = detachOffset;
		m_attachAnimation = attachAnimation;
		m_attachPointCamera = cameraPos;
		m_zanim.SetBool(attachAnimation, value: true);
		m_nview.GetZDO().Set(ZDOVars.s_inBed, isBed);
		if (colliderRoot != null)
		{
			m_attachColliders = colliderRoot.GetComponentsInChildren<Collider>();
			ZLog.Log("Ignoring " + m_attachColliders.Length + " colliders");
			Collider[] attachColliders = m_attachColliders;
			foreach (Collider collider in attachColliders)
			{
				Physics.IgnoreCollision(m_collider, collider, ignore: true);
			}
		}
		if (hideWeapons)
		{
			HideHandItems();
		}
		UpdateAttach();
		ResetCloth();
	}

	private void UpdateAttach()
	{
		if (m_attached)
		{
			if (m_attachPoint != null)
			{
				base.transform.position = m_attachPoint.position;
				base.transform.rotation = m_attachPoint.rotation;
				Rigidbody componentInParent = m_attachPoint.GetComponentInParent<Rigidbody>();
				m_body.useGravity = false;
				m_body.linearVelocity = (componentInParent ? componentInParent.GetPointVelocity(base.transform.position) : Vector3.zero);
				m_body.angularVelocity = Vector3.zero;
				m_maxAirAltitude = base.transform.position.y;
			}
			else
			{
				AttachStop();
			}
		}
	}

	public override bool IsAttached()
	{
		if (!m_attached)
		{
			return base.IsAttached();
		}
		return true;
	}

	public Transform GetAttachPoint()
	{
		return m_attachPoint;
	}

	public Transform GetAttachCameraPoint()
	{
		return m_attachPointCamera;
	}

	public void ResetAttachCameraPoint()
	{
		m_attachPointCamera = null;
	}

	public override bool IsAttachedToShip()
	{
		if (m_attached)
		{
			return m_attachedToShip;
		}
		return false;
	}

	public override bool IsRiding()
	{
		if (m_doodadController != null && m_doodadController.IsValid())
		{
			return m_doodadController is Sadle;
		}
		return false;
	}

	public override bool InBed()
	{
		if (!m_nview.IsValid())
		{
			return false;
		}
		return m_nview.GetZDO().GetBool(ZDOVars.s_inBed);
	}

	public override void AttachStop()
	{
		if (m_sleeping || !m_attached)
		{
			return;
		}
		if (m_attachPoint != null)
		{
			base.transform.position = m_attachPoint.TransformPoint(m_detachOffset);
		}
		if (m_attachColliders != null)
		{
			Collider[] attachColliders = m_attachColliders;
			foreach (Collider collider in attachColliders)
			{
				if ((bool)collider)
				{
					Physics.IgnoreCollision(m_collider, collider, ignore: false);
				}
			}
			m_attachColliders = null;
		}
		m_body.useGravity = true;
		m_attached = false;
		m_attachPoint = null;
		m_attachPointCamera = null;
		m_zanim.SetBool(m_attachAnimation, value: false);
		m_nview.GetZDO().Set(ZDOVars.s_inBed, value: false);
		ResetCloth();
	}

	public void StartDoodadControl(IDoodadController shipControl)
	{
		m_doodadController = shipControl;
		ZLog.Log("Doodad controlls set " + shipControl.GetControlledComponent().gameObject.name);
	}

	public void StopDoodadControl()
	{
		if (m_doodadController != null)
		{
			if (m_doodadController.IsValid())
			{
				m_doodadController.OnUseStop(this);
			}
			ZLog.Log("Stop doodad controlls");
			m_doodadController = null;
		}
	}

	private void SetDoodadControlls(ref Vector3 moveDir, ref Vector3 lookDir, ref bool run, ref bool autoRun, bool block)
	{
		if (m_doodadController.IsValid())
		{
			m_doodadController.ApplyControlls(moveDir, lookDir, run, autoRun, block);
		}
		moveDir = Vector3.zero;
		autoRun = false;
		run = false;
	}

	public Ship GetControlledShip()
	{
		if (m_doodadController != null && m_doodadController.IsValid())
		{
			return m_doodadController.GetControlledComponent() as Ship;
		}
		return null;
	}

	public IDoodadController GetDoodadController()
	{
		return m_doodadController;
	}

	private void UpdateDoodadControls(float dt)
	{
		if (m_doodadController == null)
		{
			return;
		}
		if (!m_doodadController.IsValid())
		{
			StopDoodadControl();
			return;
		}
		Vector3 forward = m_doodadController.GetControlledComponent().transform.forward;
		forward.y = 0f;
		forward.Normalize();
		Quaternion to = Quaternion.LookRotation(forward);
		base.transform.rotation = Quaternion.RotateTowards(base.transform.rotation, to, 100f * dt);
		if (Vector3.Distance(m_doodadController.GetPosition(), base.transform.position) > m_maxInteractDistance)
		{
			StopDoodadControl();
		}
	}

	public bool IsSleeping()
	{
		return m_sleeping;
	}

	public void SetSleeping(bool sleep)
	{
		if (m_sleeping != sleep)
		{
			m_sleeping = sleep;
			if (!sleep)
			{
				Message(MessageHud.MessageType.Center, "$msg_goodmorning");
				m_seman.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true, 0, 0f, -1);
				m_wakeupTime = ZNet.instance.GetTimeSeconds();
				Game.instance.IncrementPlayerStat(PlayerStatType.Sleep);
			}
		}
	}

	public void SurvivedOneDay()
	{
		Game.instance.IncrementPlayerStat(PlayerStatType.ConsecutiveDaysSurvived);
		float stat = Game.instance.GetPlayerProfile().GetStat(PlayerStatType.ConsecutiveDaysSurvived, DifficultyRequirement.Any);
		float stat2 = Game.instance.GetPlayerProfile().GetStat(PlayerStatType.ConsecutiveDaysSurvivedMax, DifficultyRequirement.Any);
		if (stat > stat2)
		{
			Game.instance.GetPlayerProfile().SetStat(PlayerStatType.ConsecutiveDaysSurvivedMax, stat);
		}
	}

	public void SetControls(Vector3 movedir, bool attack, bool attackHold, bool secondaryAttack, bool secondaryAttackHold, bool block, bool blockHold, bool jump, bool crouch, bool run, bool autoRun, bool dodge = false)
	{
		if ((IsAttached() || InEmote()) && (movedir != Vector3.zero || attack || secondaryAttack || block || blockHold || jump || crouch) && GetDoodadController() == null)
		{
			attack = false;
			attackHold = false;
			secondaryAttack = false;
			secondaryAttackHold = false;
			StopEmote();
			AttachStop();
		}
		if (m_doodadController != null)
		{
			SetDoodadControlls(ref movedir, ref m_lookDir, ref run, ref autoRun, blockHold);
			if (jump || attack || secondaryAttack || dodge)
			{
				attack = false;
				attackHold = false;
				secondaryAttack = false;
				secondaryAttackHold = false;
				StopDoodadControl();
			}
		}
		if (run)
		{
			m_walk = false;
		}
		if (!m_autoRun)
		{
			Vector3 lookDir = m_lookDir;
			lookDir.y = 0f;
			lookDir.Normalize();
			m_moveDir = movedir.z * lookDir + movedir.x * Vector3.Cross(Vector3.up, lookDir);
		}
		bool flag = false;
		if (!m_autoRun && autoRun && !InPlaceMode())
		{
			m_autoRun = true;
			SetCrouch(crouch: false);
			m_moveDir = m_lookDir;
			m_moveDir.y = 0f;
			m_moveDir.Normalize();
		}
		else if (m_autoRun)
		{
			if (attack || jump || dodge || crouch || movedir != Vector3.zero || InPlaceMode() || attackHold || secondaryAttackHold)
			{
				m_autoRun = false;
			}
			else if (autoRun || blockHold)
			{
				m_moveDir = m_lookDir;
				m_moveDir.y = 0f;
				m_moveDir.Normalize();
				flag = true;
			}
		}
		m_attack = attack;
		m_attackHold = attackHold;
		m_secondaryAttack = secondaryAttack;
		m_secondaryAttackHold = secondaryAttackHold;
		m_run = run;
		if (m_toggleBlock)
		{
			if (block)
			{
				m_blocking = !m_blocking;
			}
		}
		else
		{
			m_blocking = blockHold;
		}
		if (flag)
		{
			m_blocking = false;
		}
		if (crouch)
		{
			SetCrouch(!m_crouchToggled);
		}
		if (ZInput.InputLayout == InputLayout.Default || !ZInput.IsGamepadActive())
		{
			if (!(jump || dodge))
			{
				return;
			}
			if (m_blocking)
			{
				Vector3 dodgeDir = m_moveDir;
				if (dodgeDir.magnitude < 0.1f)
				{
					dodgeDir = -m_lookDir;
					dodgeDir.y = 0f;
					dodgeDir.Normalize();
				}
				Dodge(dodgeDir);
			}
			else if (IsCrouching() || m_crouchToggled || dodge)
			{
				Vector3 dodgeDir2 = m_moveDir;
				if (dodgeDir2.magnitude < 0.1f)
				{
					dodgeDir2 = m_lookDir;
					dodgeDir2.y = 0f;
					dodgeDir2.Normalize();
				}
				Dodge(dodgeDir2);
			}
			else
			{
				Jump();
			}
		}
		else
		{
			if (!ZInput.IsNonClassicFunctionality())
			{
				return;
			}
			if (dodge)
			{
				if (m_blocking)
				{
					Vector3 dodgeDir3 = m_moveDir;
					if (dodgeDir3.magnitude < 0.1f)
					{
						dodgeDir3 = -m_lookDir;
						dodgeDir3.y = 0f;
						dodgeDir3.Normalize();
					}
					Dodge(dodgeDir3);
				}
				else if (IsCrouching() || m_crouchToggled)
				{
					Vector3 dodgeDir4 = m_moveDir;
					if (dodgeDir4.magnitude < 0.1f)
					{
						dodgeDir4 = m_lookDir;
						dodgeDir4.y = 0f;
						dodgeDir4.Normalize();
					}
					Dodge(dodgeDir4);
				}
			}
			if (jump)
			{
				Jump();
			}
		}
	}

	private void UpdateTargeted(float dt)
	{
		m_timeSinceTargeted += dt;
		m_timeSinceSensed += dt;
	}

	public override void OnTargeted(bool sensed, bool alerted)
	{
		if (sensed)
		{
			if (m_timeSinceSensed > 0.5f)
			{
				m_timeSinceSensed = 0f;
				m_nview.InvokeRPC("OnTargeted", sensed, alerted);
			}
		}
		else if (m_timeSinceTargeted > 0.5f)
		{
			m_timeSinceTargeted = 0f;
			m_nview.InvokeRPC("OnTargeted", sensed, alerted);
		}
	}

	private void RPC_OnTargeted(long sender, bool sensed, bool alerted)
	{
		m_timeSinceTargeted = 0f;
		if (sensed)
		{
			m_timeSinceSensed = 0f;
		}
		if (alerted)
		{
			MusicMan.instance.ResetCombatTimer();
		}
	}

	protected override void OnDamaged(HitData hit)
	{
		base.OnDamaged(hit);
		if (hit.GetTotalDamage() > GetMaxHealth() / 10f)
		{
			Hud.instance.DamageFlash();
		}
	}

	public bool IsTargeted()
	{
		return m_timeSinceTargeted < 1f;
	}

	public bool IsSensed()
	{
		return m_timeSinceSensed < 1f;
	}

	protected override void ApplyArmorDamageMods(ref HitData.DamageModifiers mods)
	{
		if (m_chestItem != null)
		{
			mods.Apply(m_chestItem.m_shared.m_damageModifiers);
		}
		if (m_legItem != null)
		{
			mods.Apply(m_legItem.m_shared.m_damageModifiers);
		}
		if (m_helmetItem != null)
		{
			mods.Apply(m_helmetItem.m_shared.m_damageModifiers);
		}
		if (m_shoulderItem != null)
		{
			mods.Apply(m_shoulderItem.m_shared.m_damageModifiers);
		}
	}

	public override float GetBodyArmor()
	{
		float armor = 0f;
		if (m_chestItem != null)
		{
			armor += m_chestItem.GetArmor();
		}
		if (m_legItem != null)
		{
			armor += m_legItem.GetArmor();
		}
		if (m_helmetItem != null)
		{
			armor += m_helmetItem.GetArmor();
		}
		if (m_shoulderItem != null)
		{
			armor += m_shoulderItem.GetArmor();
		}
		m_seman.ApplyArmorMods(ref armor);
		return armor;
	}

	public bool TryGetArmorDifference(ItemDrop.ItemData item, out float difference)
	{
		switch (item.m_shared.m_itemType)
		{
		case ItemDrop.ItemData.ItemType.Helmet:
			if (m_helmetItem == null)
			{
				difference = item.m_shared.m_armor;
			}
			else if (item == m_helmetItem)
			{
				difference = 0f - item.m_shared.m_armor;
			}
			else
			{
				difference = GetBodyArmor() - m_helmetItem.m_shared.m_armor + item.m_shared.m_armor - GetBodyArmor();
			}
			return true;
		case ItemDrop.ItemData.ItemType.Chest:
			if (m_chestItem == null)
			{
				difference = item.m_shared.m_armor;
			}
			else if (item == m_chestItem)
			{
				difference = 0f - item.m_shared.m_armor;
			}
			else
			{
				difference = GetBodyArmor() - m_chestItem.m_shared.m_armor + item.m_shared.m_armor - GetBodyArmor();
			}
			return true;
		case ItemDrop.ItemData.ItemType.Legs:
			if (m_legItem == null)
			{
				difference = item.m_shared.m_armor;
			}
			else if (item == m_legItem)
			{
				difference = 0f - item.m_shared.m_armor;
			}
			else
			{
				difference = GetBodyArmor() - m_legItem.m_shared.m_armor + item.m_shared.m_armor - GetBodyArmor();
			}
			return true;
		case ItemDrop.ItemData.ItemType.Shoulder:
			if (m_shoulderItem == null)
			{
				difference = item.m_shared.m_armor;
			}
			else if (item == m_shoulderItem)
			{
				difference = 0f - item.m_shared.m_armor;
			}
			else
			{
				difference = GetBodyArmor() - m_shoulderItem.m_shared.m_armor + item.m_shared.m_armor - GetBodyArmor();
			}
			return true;
		default:
			difference = 0f;
			return false;
		}
	}

	protected override void OnSneaking(float dt)
	{
		float t = Mathf.Pow(m_skills.GetSkillFactor(Skills.SkillType.Sneak), 0.5f);
		float num = Mathf.Lerp(1f, 0.25f, t);
		float num2 = dt * m_sneakStaminaDrain * num;
		num2 += num2 * GetEquipmentSneakStaminaModifier();
		m_seman.ModifySneakStaminaUsage(num2, ref num2);
		UseStamina(num2);
		if (!HaveStamina())
		{
			Hud.instance.StaminaBarEmptyFlash();
		}
		m_sneakSkillImproveTimer += dt;
		if (m_sneakSkillImproveTimer > 1f)
		{
			m_sneakSkillImproveTimer = 0f;
			if (BaseAI.InStealthRange(this))
			{
				RaiseSkill(Skills.SkillType.Sneak);
			}
			else
			{
				RaiseSkill(Skills.SkillType.Sneak, 0.1f);
			}
		}
	}

	private void UpdateStealth(float dt)
	{
		m_stealthFactorUpdateTimer += dt;
		if (m_stealthFactorUpdateTimer > 0.5f)
		{
			m_stealthFactorUpdateTimer = 0f;
			m_stealthFactorTarget = 0f;
			if (IsCrouching())
			{
				m_lastStealthPosition = base.transform.position;
				float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Sneak);
				float lightFactor = StealthSystem.instance.GetLightFactor(GetCenterPoint());
				m_stealthFactorTarget = Mathf.Lerp(0.5f + lightFactor * 0.5f, 0.2f + lightFactor * 0.4f, skillFactor);
				m_stealthFactorTarget = Mathf.Clamp01(m_stealthFactorTarget);
				m_seman.ModifyStealth(m_stealthFactorTarget, ref m_stealthFactorTarget);
				m_stealthFactorTarget = Mathf.Clamp01(m_stealthFactorTarget);
			}
			else
			{
				m_stealthFactorTarget = 1f;
			}
		}
		float num = Mathf.MoveTowards(m_stealthFactor, m_stealthFactorTarget, dt / 4f);
		if (!m_stealthFactor.Equals(num))
		{
			m_nview.GetZDO().Set(ZDOVars.s_stealth, num);
		}
		m_stealthFactor = num;
	}

	public override float GetStealthFactor()
	{
		if (!m_nview.IsValid())
		{
			return 0f;
		}
		if (m_nview.IsOwner())
		{
			return m_stealthFactor;
		}
		return m_nview.GetZDO().GetFloat(ZDOVars.s_stealth);
	}

	public override bool InAttack()
	{
		if (MonoUpdaters.UpdateCount == m_cachedFrame)
		{
			return m_cachedAttack;
		}
		m_cachedFrame = MonoUpdaters.UpdateCount;
		if (GetNextOrCurrentAnimHash() == Humanoid.s_animatorTagAttack)
		{
			m_cachedAttack = true;
			return true;
		}
		for (int i = 1; i < m_animator.layerCount; i++)
		{
			if ((m_animator.IsInTransition(i) ? m_animator.GetNextAnimatorStateInfo(i).tagHash : m_animator.GetCurrentAnimatorStateInfo(i).tagHash) == Humanoid.s_animatorTagAttack)
			{
				m_cachedAttack = true;
				return true;
			}
		}
		m_cachedAttack = false;
		return false;
	}

	private float GetEquipmentModifier(int index)
	{
		if (m_equipmentModifierValues != null)
		{
			return m_equipmentModifierValues[index];
		}
		return 0f;
	}

	public override float GetEquipmentMovementModifier()
	{
		return GetEquipmentModifier(0);
	}

	public override float GetEquipmentHomeItemModifier()
	{
		return GetEquipmentModifier(1);
	}

	public override float GetEquipmentHeatResistanceModifier()
	{
		return GetEquipmentModifier(2);
	}

	public override float GetEquipmentJumpStaminaModifier()
	{
		return GetEquipmentModifier(3);
	}

	public override float GetEquipmentAttackStaminaModifier()
	{
		return GetEquipmentModifier(4);
	}

	public override float GetEquipmentBlockStaminaModifier()
	{
		return GetEquipmentModifier(5);
	}

	public override float GetEquipmentDodgeStaminaModifier()
	{
		return GetEquipmentModifier(6);
	}

	public override float GetEquipmentSwimStaminaModifier()
	{
		return GetEquipmentModifier(7);
	}

	public override float GetEquipmentSneakStaminaModifier()
	{
		return GetEquipmentModifier(8);
	}

	public override float GetEquipmentRunStaminaModifier()
	{
		return GetEquipmentModifier(9);
	}

	public override float GetEquipmentMaxAdrenaline()
	{
		return GetEquipmentModifier(10);
	}

	private float GetEquipmentModifierPlusSE(int index)
	{
		float staminaUse = m_equipmentModifierValues[index];
		switch (index)
		{
		case 3:
			m_seman.ModifyJumpStaminaUsage(1f, ref staminaUse, minZero: false);
			break;
		case 4:
			m_seman.ModifyAttackStaminaUsage(1f, ref staminaUse, minZero: false);
			break;
		case 5:
			m_seman.ModifyBlockStaminaUsage(1f, ref staminaUse, minZero: false);
			break;
		case 6:
			m_seman.ModifyDodgeStaminaUsage(1f, ref staminaUse, minZero: false);
			break;
		case 7:
			m_seman.ModifySwimStaminaUsage(1f, ref staminaUse, minZero: false);
			break;
		case 8:
			m_seman.ModifySneakStaminaUsage(1f, ref staminaUse, minZero: false);
			break;
		case 9:
			m_seman.ModifyRunStaminaDrain(1f, ref staminaUse, Vector3.zero, minZero: false);
			break;
		}
		return staminaUse;
	}

	protected override float GetJogSpeedFactor()
	{
		return 1f + GetEquipmentMovementModifier();
	}

	protected override float GetRunSpeedFactor()
	{
		float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Run);
		return (1f + skillFactor * 0.25f) * (1f + GetEquipmentMovementModifier() * 1.5f);
	}

	public override bool InMinorAction()
	{
		int tagHash = m_animator.GetCurrentAnimatorStateInfo(1).tagHash;
		if (tagHash == s_animatorTagMinorAction || tagHash == s_animatorTagMinorActionFast)
		{
			return true;
		}
		if (m_animator.IsInTransition(1))
		{
			int tagHash2 = m_animator.GetNextAnimatorStateInfo(1).tagHash;
			if (tagHash2 != s_animatorTagMinorAction)
			{
				return tagHash2 == s_animatorTagMinorActionFast;
			}
			return true;
		}
		return false;
	}

	public override bool InMinorActionSlowdown()
	{
		if (m_animator.GetCurrentAnimatorStateInfo(1).tagHash == s_animatorTagMinorAction)
		{
			return true;
		}
		if (m_animator.IsInTransition(1))
		{
			return m_animator.GetNextAnimatorStateInfo(1).tagHash == s_animatorTagMinorAction;
		}
		return false;
	}

	public override bool GetRelativePosition(out ZDOID parent, out string attachJoint, out Vector3 relativePos, out Quaternion relativeRot, out Vector3 relativeVel)
	{
		if (m_attached && (bool)m_attachPoint)
		{
			ZNetView componentInParent = m_attachPoint.GetComponentInParent<ZNetView>();
			if ((bool)componentInParent && componentInParent.IsValid())
			{
				parent = componentInParent.GetZDO().m_uid;
				if (componentInParent.GetComponent<Character>() != null)
				{
					attachJoint = m_attachPoint.name;
					relativePos = Vector3.zero;
					relativeRot = Quaternion.identity;
				}
				else
				{
					attachJoint = "";
					relativePos = componentInParent.transform.InverseTransformPoint(base.transform.position);
					relativeRot = Quaternion.Inverse(componentInParent.transform.rotation) * base.transform.rotation;
				}
				relativeVel = Vector3.zero;
				return true;
			}
		}
		return base.GetRelativePosition(out parent, out attachJoint, out relativePos, out relativeRot, out relativeVel);
	}

	public override Skills GetSkills()
	{
		return m_skills;
	}

	public override float GetRandomSkillFactor(Skills.SkillType skill)
	{
		return m_skills.GetRandomSkillFactor(skill);
	}

	public override float GetSkillFactor(Skills.SkillType skill)
	{
		return m_skills.GetSkillFactor(skill);
	}

	protected override void DoDamageCameraShake(HitData hit)
	{
		float totalStaggerDamage = hit.m_damage.GetTotalStaggerDamage();
		if ((bool)GameCamera.instance && totalStaggerDamage > 0f)
		{
			float num = Mathf.Clamp01(totalStaggerDamage / GetMaxHealth());
			GameCamera.instance.AddShake(base.transform.position, 50f, m_baseCameraShake * num, continous: false);
		}
	}

	protected override void DamageArmorDurability(HitData hit)
	{
		List<ItemDrop.ItemData> list = new List<ItemDrop.ItemData>();
		if (m_chestItem != null)
		{
			list.Add(m_chestItem);
		}
		if (m_legItem != null)
		{
			list.Add(m_legItem);
		}
		if (m_helmetItem != null)
		{
			list.Add(m_helmetItem);
		}
		if (m_shoulderItem != null)
		{
			list.Add(m_shoulderItem);
		}
		if (list.Count != 0)
		{
			float num = hit.GetTotalPhysicalDamage() + hit.GetTotalElementalDamage();
			if (!(num <= 0f))
			{
				int index = UnityEngine.Random.Range(0, list.Count);
				ItemDrop.ItemData itemData = list[index];
				itemData.m_durability = Mathf.Max(0f, itemData.m_durability - num);
			}
		}
	}

	protected override bool ToggleEquipped(ItemDrop.ItemData item)
	{
		if (!item.IsEquipable())
		{
			return false;
		}
		if (InAttack())
		{
			return true;
		}
		if (item.m_shared.m_equipDuration <= 0f)
		{
			if (IsItemEquiped(item))
			{
				UnequipItem(item);
			}
			else
			{
				EquipItem(item);
			}
		}
		else if (IsItemEquiped(item))
		{
			QueueUnequipAction(item);
		}
		else
		{
			QueueEquipAction(item);
		}
		return true;
	}

	public void GetActionProgress(out string name, out float progress, out MinorActionData data)
	{
		if (TryGetFirstElementProgress(out var firstElement, out var progress2))
		{
			data = firstElement;
			name = firstElement.m_progressText;
			progress = progress2;
		}
		else
		{
			data = null;
			name = null;
			progress = 0f;
		}
	}

	public void GetActionProgress(out string name, out float progress)
	{
		GetActionProgress(out name, out progress, out var _);
	}

	private bool TryGetFirstElementProgress(out MinorActionData firstElement, out float progress)
	{
		firstElement = null;
		progress = 0f;
		if (m_actionQueue.Count > 0)
		{
			firstElement = m_actionQueue[0];
			if (firstElement.m_duration > 0f)
			{
				progress = Mathf.Clamp01(firstElement.m_time / firstElement.m_duration);
			}
			return true;
		}
		return false;
	}

	public int GetActionQueueCount()
	{
		return m_actionQueue.Count;
	}

	private void UpdateActionQueue(float dt)
	{
		if (m_actionQueuePause > 0f)
		{
			m_actionQueuePause -= dt;
			if (m_actionAnimation != null)
			{
				m_zanim.SetBool(m_actionAnimation, value: false);
				m_actionAnimation = null;
			}
			return;
		}
		if (InAttack())
		{
			if (m_actionAnimation != null)
			{
				m_zanim.SetBool(m_actionAnimation, value: false);
				m_actionAnimation = null;
			}
			return;
		}
		if (m_actionQueue.Count == 0)
		{
			if (m_actionAnimation != null)
			{
				m_zanim.SetBool(m_actionAnimation, value: false);
				m_actionAnimation = null;
			}
			return;
		}
		MinorActionData minorActionData = m_actionQueue[0];
		if (m_actionAnimation != null && m_actionAnimation != minorActionData.m_animation)
		{
			m_zanim.SetBool(m_actionAnimation, value: false);
			m_actionAnimation = null;
		}
		m_zanim.SetBool(minorActionData.m_animation, value: true);
		m_actionAnimation = minorActionData.m_animation;
		if (minorActionData.m_time == 0f && minorActionData.m_startEffect != null)
		{
			minorActionData.m_startEffect.Create(base.transform.position, Quaternion.identity);
		}
		if (minorActionData.m_staminaDrain > 0f)
		{
			UseStamina(minorActionData.m_staminaDrain * dt);
		}
		if (minorActionData.m_eitrDrain > 0f)
		{
			UseEitr(minorActionData.m_eitrDrain * dt);
		}
		minorActionData.m_time += dt;
		if (minorActionData.m_time > minorActionData.m_duration)
		{
			m_actionQueue.RemoveAt(0);
			m_zanim.SetBool(m_actionAnimation, value: false);
			m_actionAnimation = null;
			if (!string.IsNullOrEmpty(minorActionData.m_doneAnimation))
			{
				m_zanim.SetTrigger(minorActionData.m_doneAnimation);
			}
			switch (minorActionData.m_type)
			{
			case MinorActionData.ActionType.Equip:
				EquipItem(minorActionData.m_item);
				break;
			case MinorActionData.ActionType.Unequip:
				UnequipItem(minorActionData.m_item);
				break;
			case MinorActionData.ActionType.Reload:
				SetWeaponLoaded(minorActionData.m_item);
				break;
			}
			m_actionQueuePause = 0.3f;
		}
	}

	private void QueueEquipAction(ItemDrop.ItemData item)
	{
		if (item == null)
		{
			return;
		}
		if (IsEquipActionQueued(item))
		{
			RemoveEquipAction(item);
			return;
		}
		CancelReloadAction();
		MinorActionData minorActionData = new MinorActionData();
		minorActionData.m_item = item;
		minorActionData.m_type = MinorActionData.ActionType.Equip;
		minorActionData.m_duration = item.m_shared.m_equipDuration;
		minorActionData.m_progressText = "$hud_equipping " + item.m_shared.m_name;
		minorActionData.m_animation = "equipping";
		if (minorActionData.m_duration >= 1f)
		{
			minorActionData.m_startEffect = m_equipStartEffects;
		}
		m_actionQueue.Add(minorActionData);
	}

	private void QueueUnequipAction(ItemDrop.ItemData item)
	{
		if (item != null)
		{
			if (IsEquipActionQueued(item))
			{
				RemoveEquipAction(item);
				return;
			}
			CancelReloadAction();
			MinorActionData minorActionData = new MinorActionData();
			minorActionData.m_item = item;
			minorActionData.m_type = MinorActionData.ActionType.Unequip;
			minorActionData.m_duration = item.m_shared.m_equipDuration;
			minorActionData.m_progressText = "$hud_unequipping " + item.m_shared.m_name;
			minorActionData.m_animation = "equipping";
			m_actionQueue.Add(minorActionData);
		}
	}

	private void QueueReloadAction()
	{
		if (!IsReloadActionQueued())
		{
			ItemDrop.ItemData currentWeapon = GetCurrentWeapon();
			if (currentWeapon != null && currentWeapon.m_shared.m_attack.m_requiresReload && !(m_grappling > 0f) && !(m_blockReload > 0f))
			{
				MinorActionData minorActionData = new MinorActionData();
				minorActionData.m_item = currentWeapon;
				minorActionData.m_type = MinorActionData.ActionType.Reload;
				minorActionData.m_duration = currentWeapon.GetWeaponLoadingTime();
				minorActionData.m_progressText = "$hud_reloading " + currentWeapon.m_shared.m_name;
				minorActionData.m_animation = currentWeapon.m_shared.m_attack.m_reloadAnimation;
				minorActionData.m_doneAnimation = currentWeapon.m_shared.m_attack.m_reloadAnimation + "_done";
				minorActionData.m_staminaDrain = currentWeapon.m_shared.m_attack.m_reloadStaminaDrain;
				minorActionData.m_eitrDrain = currentWeapon.m_shared.m_attack.m_reloadEitrDrain;
				m_actionQueue.Add(minorActionData);
			}
		}
	}

	protected override void ClearActionQueue()
	{
		m_actionQueue.Clear();
	}

	public override void RemoveEquipAction(ItemDrop.ItemData item)
	{
		if (item == null)
		{
			return;
		}
		foreach (MinorActionData item2 in m_actionQueue)
		{
			if (item2.m_item == item)
			{
				m_actionQueue.Remove(item2);
				break;
			}
		}
	}

	public bool IsEquipActionQueued(ItemDrop.ItemData item)
	{
		if (item == null)
		{
			return false;
		}
		foreach (MinorActionData item2 in m_actionQueue)
		{
			if ((item2.m_type == MinorActionData.ActionType.Equip || item2.m_type == MinorActionData.ActionType.Unequip) && item2.m_item == item)
			{
				return true;
			}
		}
		return false;
	}

	private bool IsReloadActionQueued()
	{
		foreach (MinorActionData item in m_actionQueue)
		{
			if (item.m_type == MinorActionData.ActionType.Reload)
			{
				return true;
			}
		}
		return false;
	}

	public void ResetCharacter()
	{
		m_guardianPowerCooldown = 0f;
		ResetSeenTutorials();
		m_knownRecipes.Clear();
		m_knownStations.Clear();
		m_knownMaterial.Clear();
		m_uniques.Clear();
		m_trophies.Clear();
		m_skills.Clear();
		m_knownBiome.Clear();
		m_knownTexts.Clear();
	}

	public void ResetCharacterKnownItems()
	{
		m_knownRecipes.Clear();
		m_knownStations.Clear();
		m_knownMaterial.Clear();
		m_trophies.Clear();
	}

	public bool ToggleDebugFly()
	{
		m_debugFly = !m_debugFly;
		m_nview.GetZDO().Set(ZDOVars.s_debugFly, m_debugFly);
		Message(MessageHud.MessageType.TopLeft, "Debug fly:" + m_debugFly);
		return m_debugFly;
	}

	public void SetNoPlacementCost(bool value)
	{
		if (value != m_noPlacementCost)
		{
			ToggleNoPlacementCost();
		}
	}

	public bool ToggleNoPlacementCost()
	{
		m_noPlacementCost = !m_noPlacementCost;
		Message(MessageHud.MessageType.TopLeft, "No placement cost:" + m_noPlacementCost);
		UpdateAvailablePiecesList();
		return m_noPlacementCost;
	}

	public bool IsKnownMaterial(string name)
	{
		return m_knownMaterial.Contains(name);
	}

	public float GetItemUpgradeChance(int hash)
	{
		if (!m_itemUpgradeSeeds.TryGetValue(hash, out var value))
		{
			value = GetPlayerName().GetStableHashCode() + hash;
		}
		UnityEngine.Random.InitState(value);
		if (Terminal.m_showTests && Terminal.m_testList.ContainsKey("upgrader"))
		{
			ZLog.Log($"Upgrader seed: {value} for hash {hash}");
		}
		float value2 = UnityEngine.Random.value;
		m_itemUpgradeSeeds[hash] = UnityEngine.Random.Range(0, 99999999);
		return value2;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
