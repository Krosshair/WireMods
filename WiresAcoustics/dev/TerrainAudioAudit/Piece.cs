using System;
using System.Collections.Generic;
using Splatform;
using UnityEngine;

public class Piece : StaticTarget, IPlaced
{
	[Flags]
	public enum UsageTagFlags
	{
		[DisplayName("$tag_misc")]
		Misc = 1,
		[DisplayName("$tag_crafting")]
		Crafting = 2,
		[DisplayName("$tag_building")]
		Building = 4,
		[DisplayName("$tag_floor")]
		Floor = 8,
		[DisplayName("$tag_wall")]
		Wall = 0x10,
		[DisplayName("$tag_roof")]
		Roof = 0x20,
		[DisplayName("$tag_architecture")]
		Architecture = 0x40,
		[DisplayName("$tag_furniture")]
		Furniture = 0x80,
		[DisplayName("$tag_lighting")]
		Lighting = 0x100,
		[DisplayName("$tag_decor")]
		Decor = 0x200,
		[DisplayName("$tag_storage")]
		Storage = 0x400,
		[DisplayName("$tag_transport")]
		Transport = 0x800,
		[DisplayName("$tag_food")]
		Food = 0x1000,
		[DisplayName("$tag_mead")]
		Meads = 0x2000,
		[DisplayName("$tag_feasts")]
		Feasts = 0x4000,
		[DisplayName("$tag_defense")]
		Defense = 0x8000,
		[DisplayName("$tag_stacks")]
		Stacks = 0x10000,
		[DisplayName("$tag_stairs")]
		Stairs = 0x20000,
		[DisplayName("$tag_doors")]
		Doors = 0x40000,
		[DisplayName("$tag_seasonal")]
		Seasonal = 0x80000
	}

	public enum PieceCategory
	{
		Misc = 0,
		Crafting = 1,
		BuildingWorkbench = 2,
		BuildingStonecutter = 3,
		Furniture = 4,
		DeepNorth = 5,
		Feasts = 6,
		Food = 7,
		Meads = 8,
		Max = 9,
		All = 100
	}

	public enum ComfortGroup
	{
		None,
		Fire,
		Bed,
		Banner,
		Chair,
		Table,
		Carpet,
		Display,
		Decor,
		Garland,
		Lantern,
		Leisure
	}

	[Serializable]
	public class Requirement
	{
		[Header("Resource")]
		public ItemDrop m_resItem;

		public int m_amount = 1;

		public int m_extraAmountOnlyOneIngredient;

		[Header("Item")]
		public int m_amountPerLevel = 1;

		public bool m_upgraderResource;

		[Header("Piece")]
		public bool m_recover = true;

		public int GetAmount(int qualityLevel)
		{
			if (qualityLevel <= 1)
			{
				return m_amount;
			}
			float num = 0f;
			num = ((qualityLevel >= 4) ? (4f + (float)(qualityLevel - 4) / 2f) : ((float)(qualityLevel - 1)));
			return (int)Math.Floor(num * (float)m_amountPerLevel + (float)(m_upgraderResource ? m_amount : 0));
		}
	}

	private static int[] m_tagStats;

	public bool m_targetNonPlayerBuilt = true;

	[Header("Basic stuffs")]
	public Sprite m_icon;

	public string m_name = "";

	public string m_description = "";

	public bool m_enabled = true;

	public PieceCategory m_category;

	public UsageTagFlags m_usage;

	public bool m_isUpgrade;

	[Header("Comfort")]
	public int m_comfort;

	public ComfortGroup m_comfortGroup;

	public GameObject m_comfortObject;

	[Header("Placement rules")]
	public bool m_groundPiece;

	public bool m_allowAltGroundPlacement;

	public bool m_groundOnly;

	public bool m_cultivatedGroundOnly;

	public bool m_waterPiece;

	public bool m_clipGround;

	public bool m_clipEverything;

	public bool m_noInWater;

	public bool m_notOnWood;

	public bool m_notOnTiltingSurface;

	public bool m_inCeilingOnly;

	public bool m_notOnFloor;

	public bool m_noClipping;

	public bool m_onlyInTeleportArea;

	public bool m_allowedInDungeons;

	public bool m_allowedInDeepSnow;

	public bool m_requireDeepSnow;

	public float m_spaceRequirement;

	public bool m_repairPiece;

	public bool m_removePiece;

	public bool m_canRotate = true;

	public bool m_randomInitBuildRotation;

	public bool m_canBeRemoved = true;

	public bool m_canRockJade;

	public bool m_allowRotatedOverlap;

	public bool m_vegetationGroundOnly;

	public List<Piece> m_blockingPieces = new List<Piece>();

	public float m_blockRadius;

	public ZNetView m_mustConnectTo;

	public float m_connectRadius;

	public bool m_mustBeAboveConnected;

	public bool m_noVines;

	public int m_extraPlacementDistance;

	[BitMask(typeof(Heightmap.Biome))]
	public Heightmap.Biome m_onlyInBiome;

	[Header("Harvest")]
	public bool m_harvest;

	public float m_harvestRadius;

	public float m_harvestRadiusMaxLevel;

	[Header("Effects")]
	public EffectList m_placeEffect = new EffectList();

	[Header("Requirements")]
	public string m_dlc = "";

	public CraftingStation m_craftingStation;

	public float m_returnResourceHeightOffset = 1f;

	public Requirement[] m_resources = Array.Empty<Requirement>();

	public GameObject m_destroyedLootPrefab;

	private ZNetView m_nview;

	private long m_creator;

	private int m_creatorPlatformUserIDIndex = -1;

	private int m_myListIndex = -1;

	private static int s_ghostLayer = 0;

	private static int s_pieceRayMask = 0;

	private static int s_harvestRayMask = 0;

	private static readonly Collider[] s_pieceColliders = new Collider[2000];

	private static readonly List<Piece> s_allPieces = new List<Piece>();

	private static readonly HashSet<Piece> s_allComfortPieces = new HashSet<Piece>();

	private void Awake()
	{
		m_nview = GetComponent<ZNetView>();
		s_allPieces.Add(this);
		m_myListIndex = s_allPieces.Count - 1;
		if (m_comfort > 0)
		{
			s_allComfortPieces.Add(this);
		}
		if ((bool)m_nview && m_nview.IsValid())
		{
			m_creator = m_nview.GetZDO().GetLong(ZDOVars.s_creator, 0L);
			m_creatorPlatformUserIDIndex = m_nview.GetZDO().GetInt(ZDOVars.s_creatorIndex, -1);
		}
		if (s_ghostLayer == 0)
		{
			s_ghostLayer = LayerMask.NameToLayer("ghost");
		}
		if (s_pieceRayMask == 0)
		{
			s_pieceRayMask = LayerMask.GetMask("piece", "piece_nonsolid");
		}
		if (s_harvestRayMask == 0)
		{
			s_harvestRayMask = LayerMask.GetMask("piece", "piece_nonsolid", "item");
		}
		if (m_harvest)
		{
			Transform transform = base.transform.Find("_GhostOnly");
			if ((object)transform != null)
			{
				float skillFactor = Player.m_localPlayer.GetSkillFactor(Skills.SkillType.Farming);
				float num = Mathf.Lerp(m_harvestRadius, m_harvestRadiusMaxLevel, skillFactor);
				transform.localScale = new Vector3(num, num, num);
			}
		}
	}

	public void OnPlaced()
	{
		if (!m_harvest)
		{
			return;
		}
		float skillFactor = Player.m_localPlayer.GetSkillFactor(Skills.SkillType.Farming);
		float radius = Mathf.Lerp(m_harvestRadius, m_harvestRadiusMaxLevel, skillFactor);
		int num = Physics.OverlapSphereNonAlloc(base.transform.position, radius, s_pieceColliders, s_harvestRayMask);
		for (int i = 0; i < num; i++)
		{
			Pickable component = s_pieceColliders[i].gameObject.GetComponent<Pickable>();
			if ((object)component != null && component.m_harvestable && component.CanBePicked())
			{
				component.Interact(Player.m_localPlayer, repeat: false, alt: false);
			}
		}
		ZNetScene.instance.Destroy(base.gameObject);
	}

	private void OnDestroy()
	{
		if (m_myListIndex >= 0)
		{
			s_allPieces[m_myListIndex] = s_allPieces[s_allPieces.Count - 1];
			s_allPieces[m_myListIndex].m_myListIndex = m_myListIndex;
			s_allPieces.RemoveAt(s_allPieces.Count - 1);
			m_myListIndex = -1;
		}
		if (m_comfort > 0)
		{
			s_allComfortPieces.Remove(this);
		}
	}

	public bool CanBeRemoved()
	{
		Container componentInChildren = GetComponentInChildren<Container>();
		if (componentInChildren != null)
		{
			return componentInChildren.CanBeRemoved();
		}
		Ship componentInChildren2 = GetComponentInChildren<Ship>();
		if (componentInChildren2 != null)
		{
			return componentInChildren2.CanBeRemoved();
		}
		return true;
	}

	public void DropResources(HitData hitData = null)
	{
		if (ZoneSystem.instance.GetGlobalKey(FreeBuildKey()))
		{
			return;
		}
		Container container = null;
		Feast component = base.gameObject.GetComponent<Feast>();
		Requirement[] resources = m_resources;
		foreach (Requirement requirement in resources)
		{
			if (requirement.m_resItem == null || !requirement.m_recover)
			{
				continue;
			}
			GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(Utils.GetPrefabName(requirement.m_resItem.name));
			int dropCount = requirement.m_amount;
			if ((bool)component)
			{
				dropCount = (int)Math.Floor((float)dropCount * component.GetStackPercentige());
			}
			if (dropCount <= 0)
			{
				continue;
			}
			itemPrefab = Game.instance.CheckDropConversion(hitData, requirement.m_resItem, itemPrefab, ref dropCount);
			if (!IsPlacedByPlayer())
			{
				dropCount = Mathf.Max(1, dropCount / 3);
			}
			if ((bool)m_destroyedLootPrefab)
			{
				while (dropCount > 0)
				{
					ItemDrop.ItemData itemData = itemPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
					itemData.m_dropPrefab = itemPrefab;
					itemData.m_stack = Mathf.Min(dropCount, itemData.m_shared.m_maxStackSize);
					dropCount -= itemData.m_stack;
					if (container == null || !container.GetInventory().HaveEmptySlot())
					{
						container = UnityEngine.Object.Instantiate(m_destroyedLootPrefab, base.transform.position + Vector3.up * m_returnResourceHeightOffset, Quaternion.identity).GetComponent<Container>();
					}
					if (m_nview.GetZDO().IsCheated() && !PlayerProfile.s_bypassCheatChecks)
					{
						itemData.m_cheated = true;
					}
					container.GetInventory().AddItem(itemData);
				}
				continue;
			}
			while (dropCount > 0)
			{
				ItemDrop component2 = UnityEngine.Object.Instantiate(itemPrefab, base.transform.position + Vector3.up * m_returnResourceHeightOffset, Quaternion.identity).GetComponent<ItemDrop>();
				component2.SetStack(Mathf.Min(dropCount, component2.m_itemData.m_shared.m_maxStackSize));
				ItemDrop.OnCreateNew(component2);
				if (m_nview.GetZDO().IsCheated() && !PlayerProfile.s_bypassCheatChecks)
				{
					component2.m_itemData.m_cheated = true;
				}
				dropCount -= component2.m_itemData.m_stack;
			}
		}
	}

	public override bool IsPriorityTarget()
	{
		if (!base.IsPriorityTarget())
		{
			return false;
		}
		if (!m_targetNonPlayerBuilt)
		{
			return IsPlacedByPlayer();
		}
		return true;
	}

	public override bool IsRandomTarget()
	{
		if (!base.IsRandomTarget())
		{
			return false;
		}
		if (!m_targetNonPlayerBuilt)
		{
			return IsPlacedByPlayer();
		}
		return true;
	}

	public void SetCreator(long uid, PlatformUserID platformUserID)
	{
		if (!(m_nview == null) && m_nview.IsOwner() && GetCreator() == 0L)
		{
			m_creator = uid;
			m_nview.GetZDO().Set(ZDOVars.s_creator, uid);
			int value = (m_creatorPlatformUserIDIndex = ZNet.World.m_playerHistory.FindIndex((ZNet.CrossNetworkUserInfo a) => a.m_id == platformUserID));
			m_nview.GetZDO().Set(ZDOVars.s_creatorIndex, value);
		}
	}

	public long GetCreator()
	{
		return m_creator;
	}

	public int GetCreatorPlatformUserIdIndex()
	{
		return m_creatorPlatformUserIDIndex;
	}

	public bool IsCreator()
	{
		long creator = GetCreator();
		long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
		return creator == playerID;
	}

	public bool IsPlacedByPlayer()
	{
		return GetCreator() != 0;
	}

	public void SetInvalidPlacementHeightlight(bool enabled)
	{
		if (enabled)
		{
			MaterialMan.instance.SetValue(base.gameObject, ShaderProps._Color, Color.red);
			MaterialMan.instance.SetValue(base.gameObject, ShaderProps._EmissionColor, Color.red * 0.7f);
		}
		else
		{
			MaterialMan.instance.ResetValue(base.gameObject, ShaderProps._Color);
			MaterialMan.instance.ResetValue(base.gameObject, ShaderProps._EmissionColor);
		}
	}

	public static void GetSnapPoints(Vector3 point, float radius, List<Transform> points, List<Piece> pieces)
	{
		int num = Physics.OverlapSphereNonAlloc(point, radius, s_pieceColliders, s_pieceRayMask);
		for (int i = 0; i < num; i++)
		{
			Piece componentInParent = s_pieceColliders[i].GetComponentInParent<Piece>();
			if (componentInParent != null)
			{
				componentInParent.GetSnapPoints(points);
				pieces.Add(componentInParent);
			}
		}
	}

	public static void GetAllPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)
	{
		foreach (Piece s_allPiece in s_allPieces)
		{
			if (s_allPiece.gameObject.layer != s_ghostLayer && Vector3.Distance(p, s_allPiece.transform.position) < radius)
			{
				pieces.Add(s_allPiece);
			}
		}
	}

	public static void GetAllComfortPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)
	{
		foreach (Piece s_allComfortPiece in s_allComfortPieces)
		{
			if (s_allComfortPiece.gameObject.layer != s_ghostLayer && Vector3.Distance(p, s_allComfortPiece.transform.position) < radius)
			{
				pieces.Add(s_allComfortPiece);
			}
		}
	}

	public static void CheckClusteredBuildPieceStats()
	{
		long playerID = Player.m_localPlayer.GetPlayerID();
		if (m_tagStats == null)
		{
			m_tagStats = new int[20];
		}
		else
		{
			for (int i = 0; i < m_tagStats.Length; i++)
			{
				m_tagStats[i] = 0;
			}
		}
		UsageTagFlags[] array = (UsageTagFlags[])Enum.GetValues(typeof(UsageTagFlags));
		foreach (Piece s_allPiece in s_allPieces)
		{
			if (s_allPiece.GetCreator() != playerID)
			{
				continue;
			}
			UsageTagFlags usage = s_allPiece.m_usage;
			for (int j = 0; j < array.Length; j++)
			{
				if ((usage & array[j]) != 0)
				{
					m_tagStats[j]++;
				}
			}
		}
		PlayerProfile playerProfile = Game.instance.GetPlayerProfile();
		for (int k = 0; k < m_tagStats.Length; k++)
		{
			int num = m_tagStats[k];
			if (num > 0)
			{
				PlayerStatType stat = (PlayerStatType)(171 + k);
				float stat2 = playerProfile.GetStat(stat, DifficultyRequirement.Any);
				if ((float)num > stat2)
				{
					playerProfile.SetStat(stat, num);
				}
			}
		}
		CheckBuildLeniencyUnlocks();
	}

	private static void CheckBuildLeniencyUnlocks()
	{
		foreach (AchievementList achievementList in Achievements.m_instance.m_achievementLists)
		{
			foreach (Achievement achievement in achievementList.m_achievements)
			{
				if (achievement.m_lenientBuildAchievement && !achievement.m_unlocked)
				{
					CheckLenientBuildAchUnlocked(achievement, afterLocalPlayerExists: true, useTagStats: true);
				}
			}
		}
	}

	public static void CheckLenientBuildAchUnlocked(Achievement buildAchievement, bool afterLocalPlayerExists, bool useTagStats)
	{
		float num = 0f;
		float[] array = new float[20];
		foreach (Achievement.PlayerStatRequirement item in buildAchievement.m_statTrigger)
		{
			array[(int)(item.m_stat - 171)] = item.m_amountAboveEquals;
			num += item.m_amountAboveEquals;
		}
		float num2 = 0f;
		float num3 = 1.15f;
		if (useTagStats)
		{
			for (int i = 0; i < m_tagStats.Length; i++)
			{
				int num4 = m_tagStats[i];
				float num5 = array[i];
				float num6 = Mathf.Min(num4, num5 * num3);
				num2 += num6;
			}
		}
		else
		{
			List<Achievement.PlayerStatRequirement> statTrigger = buildAchievement.m_statTrigger;
			PlayerProfile playerProfile = Game.instance.GetPlayerProfile();
			for (int j = 0; j < statTrigger.Count; j++)
			{
				float stat = playerProfile.GetStat(statTrigger[j].m_stat, DifficultyRequirement.Any);
				float amountAboveEquals = statTrigger[j].m_amountAboveEquals;
				float num7 = Mathf.Min(stat, amountAboveEquals * num3);
				num2 += num7;
			}
		}
		bool flag = true;
		if (afterLocalPlayerExists)
		{
			flag = Achievements.CanGetAchievements();
		}
		if (num2 > num && flag)
		{
			buildAchievement.m_unlocked = true;
			Achievements.AchievementEvent(buildAchievement, synchronize: true, afterLocalPlayerExists);
		}
	}

	public void GetSnapPoints(List<Transform> points)
	{
		for (int i = 0; i < base.transform.childCount; i++)
		{
			Transform child = base.transform.GetChild(i);
			if (child.CompareTag("snappoint"))
			{
				points.Add(child);
			}
		}
	}

	public GlobalKeys FreeBuildKey()
	{
		if (GetComponent<ItemDrop>() != null || GetComponent<Feast>() != null)
		{
			return GlobalKeys.NoCraftCost;
		}
		return GlobalKeys.NoBuildCost;
	}

	public int GetComfort()
	{
		if (m_comfortObject != null && !m_comfortObject.activeInHierarchy)
		{
			return 0;
		}
		return m_comfort;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
