using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WearNTear : MonoBehaviour, IDestructible
{
	public enum MaterialType
	{
		Wood,
		Stone,
		Iron,
		HardWood,
		Marble,
		Ashstone,
		Ancient,
		Ice,
		Timberwood
	}

	private struct BoundData
	{
		public Vector3 m_pos;

		public Quaternion m_rot;

		public Vector3 m_size;
	}

	private struct OldMeshData
	{
		public Renderer m_renderer;

		public Material[] m_materials;

		public Color[] m_color;

		public Color[] m_emissiveColor;
	}

	public static bool m_randomInitialDamage = false;

	public Action m_onDestroyed;

	public Action m_onDamaged;

	[Header("Wear")]
	public GameObject m_new;

	public GameObject m_worn;

	public GameObject m_broken;

	public GameObject m_wet;

	public MeshRenderer m_snow;

	public MeshRenderer m_snowWorn;

	public MeshRenderer m_snowBroken;

	public bool m_noRoofWear = true;

	public float m_roofCheckOffset;

	public bool m_noSupportWear = true;

	public bool m_snowDamageImmune;

	[Tooltip("'Ash Damage' covers both lava and ambient ashlands damage (currently 0... but server modifiers??)")]
	public bool m_ashDamageImmune;

	public bool m_ashDamageResist;

	public bool m_burnable = true;

	public MaterialType m_materialType;

	public bool m_supports = true;

	public Vector3 m_comOffset = Vector3.zero;

	public bool m_forceCorrectCOMCalculation;

	public bool m_staticPosition = true;

	public List<Renderer> m_nonSolidRenderers = new List<Renderer>();

	[Space(16f)]
	public string m_requiredPersistentEvent = "";

	public bool m_takeDamageIfInsideEvent;

	[Min(0f)]
	public float m_eventDamage = 25f;

	[Min(0f)]
	public float m_eventDamageDeviation = 10f;

	public float m_snowBuildup;

	[Space(16f)]
	public Heightmap.Biome m_requiredBiome;

	public float m_outsideRequiredBiomeDamage = 25f;

	[Header("Destruction")]
	public float m_health = 100f;

	public HitData.DamageModifiers m_damages;

	public int m_minToolTier;

	public float m_hitNoise;

	public float m_destroyNoise;

	public bool m_triggerPrivateArea = true;

	[Header("Effects")]
	public EffectList m_destroyedEffect = new EffectList();

	public EffectList m_hitEffect = new EffectList();

	public EffectList m_switchEffect = new EffectList();

	public bool m_autoCreateFragments = true;

	public GameObject[] m_fragmentRoots;

	private const float c_RainDamageTime = 60f;

	private const float c_RainDamage = 5f;

	private const float c_RainDamageMax = 0.5f;

	private const float c_AshDamageTime = 5f;

	private const float c_AshDamageMaxResist = 0.1f;

	private const float c_LavaDamageTime = 2f;

	private const float c_LavaDamage = 70f;

	private const float c_LavaDamageShielded = 30f;

	private const float c_ComTestWidth = 0.2f;

	private const float c_ComMinAngle = 100f;

	private static readonly RaycastHit[] s_raycastHits = new RaycastHit[128];

	private static readonly Collider[] s_tempColliders = new Collider[128];

	private static int s_rayMask = 0;

	private static readonly List<WearNTear> s_allInstances = new List<WearNTear>();

	private static readonly List<Vector3> s_tempSupportPoints = new List<Vector3>();

	private static readonly List<float> s_tempSupportPointValues = new List<float>();

	private static readonly int s_AshlandsDamageShaderID = Shader.PropertyToID("_TakingAshlandsDamage");

	private MaterialPropertyBlock m_propertyBlock;

	private List<Renderer> m_renderers;

	private ZNetView m_nview;

	private Collider[] m_colliders;

	private float m_support = 1f;

	private float m_createTime;

	private int m_myIndex = -1;

	private float m_rainTimer;

	private float m_lastRepair;

	private Piece m_piece;

	private GameObject m_roof;

	private float m_healthPercentage = 100f;

	private bool m_clearCachedSupport;

	private Heightmap m_connectedHeightMap;

	private float m_burnDamageTime;

	private float m_ashDamageTime;

	private float m_ashMaterialValue;

	private float m_lastBurnDamageTime;

	private float m_lastMaterialValueTimeCheck;

	private readonly List<Collider> m_supportColliders = new List<Collider>();

	private readonly List<WearNTear> m_supportWearNTear = new List<WearNTear>();

	private readonly List<Vector3> m_supportPositions = new List<Vector3>();

	private readonly List<float> m_supportValue = new List<float>();

	private WaterVolume m_previousWaterVolume;

	private GameObject m_ashroof;

	private Heightmap.Biome m_biome;

	private Heightmap m_heightmap;

	private float m_groundDist;

	private float m_ashTimer;

	private bool m_inAshlands;

	private int m_shieldChangeID;

	private float m_lavaTimer;

	private float m_lavaValue;

	private bool m_rainWet;

	private bool m_heavySnow;

	private bool m_addPreSnow;

	private float m_lastSupportColorValue;

	private float m_snowDamageTimer;

	private List<BoundData> m_bounds;

	private List<OldMeshData> m_oldMaterials;

	private float m_updateCoverTimer;

	private bool m_haveRoof = true;

	private bool m_haveAshRoof = true;

	private const float c_UpdateCoverFrequency = 4f;

	private const float c_ExtraSnowMeshBoundsHeight = 1.25f;

	private static int? s_terrainLayer = -1;

	private static readonly int s_snowLevel = Shader.PropertyToID("_SnowLevel");

	private static readonly List<MeshRenderer> s_tempRenderers = new List<MeshRenderer>();

	private static readonly List<SkinnedMeshRenderer> s_tempSkinned = new List<SkinnedMeshRenderer>();

	private void Awake()
	{
		if (s_terrainLayer < 0)
		{
			s_terrainLayer = LayerMask.NameToLayer("terrain");
		}
		m_nview = GetComponent<ZNetView>();
		m_piece = GetComponent<Piece>();
		if (m_nview.GetZDO() != null)
		{
			m_nview.Register<HitData>("RPC_Damage", RPC_Damage);
			m_nview.Register<bool>("RPC_Remove", RPC_Remove);
			m_nview.Register("RPC_Repair", RPC_Repair);
			m_nview.Register<float>("RPC_HealthChanged", RPC_HealthChanged);
			m_nview.Register("RPC_ClearCachedSupport", RPC_ClearCachedSupport);
			m_nview.Register<float>("RPC_SetSnow", RPC_SetSnow);
			if (m_autoCreateFragments)
			{
				m_nview.Register("RPC_CreateFragments", RPC_CreateFragments);
			}
			if (s_rayMask == 0)
			{
				s_rayMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
			}
			s_allInstances.Add(this);
			m_myIndex = s_allInstances.Count - 1;
			m_createTime = Time.time;
			m_support = GetMaxSupport();
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_health, m_health);
			if (num.Equals(m_health) && m_randomInitialDamage)
			{
				num = UnityEngine.Random.Range(0.1f * m_health, m_health * 0.6f);
				m_nview.GetZDO().Set(ZDOVars.s_health, num);
			}
			if (Game.m_worldLevel > 0)
			{
				m_health += (float)Game.m_worldLevel * Game.instance.m_worldLevelPieceHPMultiplier * m_health;
			}
			m_updateCoverTimer = UnityEngine.Random.Range(0f, 4f);
			m_healthPercentage = Mathf.Clamp01(num / m_health);
			m_propertyBlock = new MaterialPropertyBlock();
			m_renderers = GetHighlightRenderers();
			SetAshlandsMaterialValue(0f);
			UpdateVisual(triggerEffects: false);
			if ((bool)m_snow)
			{
				Bounds bounds = m_snow.bounds;
				bounds.Expand(Vector3.up * 1.25f);
				bounds.center += Vector3.up * 0.5f * 1.25f;
				m_snow.bounds = bounds;
				m_snow.gameObject.SetActive(value: false);
			}
			if ((bool)m_snowWorn)
			{
				Bounds bounds2 = m_snowWorn.bounds;
				bounds2.Expand(Vector3.up * 1.25f);
				bounds2.center += Vector3.up * 0.5f * 1.25f;
				m_snowWorn.bounds = bounds2;
				m_snowWorn.gameObject.SetActive(value: false);
			}
			if ((bool)m_snowBroken)
			{
				Bounds bounds3 = m_snowBroken.bounds;
				bounds3.Expand(Vector3.up * 1.25f);
				bounds3.center += Vector3.up * 0.5f * 1.25f;
				m_snowBroken.bounds = bounds3;
				m_snowBroken.gameObject.SetActive(value: false);
			}
			m_addPreSnow = m_nview.GetZDO().GetBool(ZDOVars.s_preSnow);
			m_snowBuildup = m_nview.GetZDO().GetFloat(ZDOVars.s_snow);
			UpdateSnowVisual();
			m_snowDamageTimer = (float)ZNet.instance.GetTimeSeconds() + UnityEngine.Random.Range(Game.instance.m_snowDamageEffectIntervalRange.x, Game.instance.m_snowDamageEffectIntervalRange.y);
		}
	}

	private void Start()
	{
		m_connectedHeightMap = Heightmap.FindHeightmap(base.transform.position);
		if (m_connectedHeightMap != null)
		{
			m_connectedHeightMap.m_clearConnectedWearNTearCache += ClearCachedSupport;
		}
	}

	public void UpdateAshlandsMaterialValues(float time)
	{
		SetAshlandsMaterialValue(Mathf.Max(m_lavaTimer, Mathf.Max(m_ashDamageTime, m_burnDamageTime)));
		float num = time - m_lastMaterialValueTimeCheck;
		m_lastMaterialValueTimeCheck = time;
		if (m_ashDamageTime > 0f)
		{
			m_ashDamageTime -= num;
		}
		if (m_burnDamageTime > 0f)
		{
			m_burnDamageTime -= num;
		}
	}

	private void OnDestroy()
	{
		if (m_myIndex != -1)
		{
			s_allInstances[m_myIndex] = s_allInstances[s_allInstances.Count - 1];
			s_allInstances[m_myIndex].m_myIndex = m_myIndex;
			s_allInstances.RemoveAt(s_allInstances.Count - 1);
		}
		if (m_connectedHeightMap != null)
		{
			m_connectedHeightMap.m_clearConnectedWearNTearCache -= ClearCachedSupport;
		}
	}

	private void SetAshlandsMaterialValue(float v)
	{
		if (m_ashMaterialValue != v)
		{
			m_ashMaterialValue = v;
			MaterialMan.instance.SetValue(base.gameObject, s_AshlandsDamageShaderID, v);
		}
	}

	public bool Repair()
	{
		if (!m_nview.IsValid())
		{
			return false;
		}
		if (m_nview.GetZDO().GetFloat(ZDOVars.s_health, m_health) >= m_health)
		{
			return false;
		}
		if (Time.time - m_lastRepair < 1f)
		{
			return false;
		}
		m_lastRepair = Time.time;
		m_nview.InvokeRPC("RPC_Repair");
		return true;
	}

	private void RPC_Repair(long sender)
	{
		if (m_nview.IsValid() && m_nview.IsOwner())
		{
			m_nview.GetZDO().Set(ZDOVars.s_health, m_health);
			m_nview.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", m_health);
		}
	}

	private float GetSupport()
	{
		if (!m_nview.IsValid())
		{
			return GetMaxSupport();
		}
		if (!m_nview.HasOwner())
		{
			return GetMaxSupport();
		}
		if (m_nview.IsOwner())
		{
			return m_support;
		}
		return m_nview.GetZDO().GetFloat(ZDOVars.s_support, GetMaxSupport());
	}

	private float GetSupportColorValue()
	{
		float support = GetSupport();
		GetMaterialProperties(out var maxSupport, out var minSupport, out var _, out var _);
		if (support >= maxSupport)
		{
			return -1f;
		}
		support -= minSupport;
		return Mathf.Clamp01(support / (maxSupport * 0.5f - minSupport));
	}

	public void OnPlaced()
	{
		m_createTime = -1f;
		m_clearCachedSupport = true;
	}

	private List<Renderer> GetHighlightRenderers()
	{
		s_tempRenderers.Clear();
		s_tempSkinned.Clear();
		GetComponentsInChildren(includeInactive: true, s_tempRenderers);
		GetComponentsInChildren(includeInactive: true, s_tempSkinned);
		List<Renderer> list = new List<Renderer>();
		list.AddRange(s_tempRenderers);
		list.AddRange(s_tempSkinned);
		return list;
	}

	public void Highlight()
	{
		float supportColorValue = GetSupportColorValue();
		Color color = new Color(0.6f, 0.8f, 1f);
		if (supportColorValue >= 0f)
		{
			color = ((m_biome != Heightmap.Biome.DeepNorth || !(supportColorValue < Game.instance.m_snowSupportLevel) || !(ZoneSystem.instance.TimeSinceStart() % 1f < 0.5f)) ? Color.Lerp(new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), supportColorValue) : Color.blue);
			Color.RGBToHSV(color, out var H, out var S, out var V);
			S = Mathf.Lerp(1f, 0.5f, supportColorValue);
			V = Mathf.Lerp(1.2f, 0.9f, supportColorValue);
			color = Color.HSVToRGB(H, S, V);
		}
		MaterialMan.instance.SetValue(base.gameObject, ShaderProps._EmissionColor, color * 0.4f);
		MaterialMan.instance.SetValue(base.gameObject, ShaderProps._Color, color);
		CancelInvoke("ResetHighlight");
		Invoke("ResetHighlight", 0.2f);
	}

	private void ResetHighlight()
	{
		MaterialMan.instance.ResetValue(base.gameObject, ShaderProps._Color);
		MaterialMan.instance.ResetValue(base.gameObject, ShaderProps._EmissionColor);
	}

	private void SetupColliders()
	{
		m_colliders = GetComponentsInChildren<Collider>(includeInactive: true);
		m_bounds = new List<BoundData>();
		Collider[] colliders = m_colliders;
		foreach (Collider collider in colliders)
		{
			if (!collider.isTrigger && !(collider.attachedRigidbody != null))
			{
				BoundData item = default(BoundData);
				if (collider is BoxCollider)
				{
					BoxCollider boxCollider = collider as BoxCollider;
					item.m_rot = boxCollider.transform.rotation;
					item.m_pos = boxCollider.transform.position + boxCollider.transform.TransformVector(boxCollider.center);
					item.m_size = new Vector3(boxCollider.transform.lossyScale.x * boxCollider.size.x, boxCollider.transform.lossyScale.y * boxCollider.size.y, boxCollider.transform.lossyScale.z * boxCollider.size.z);
				}
				else
				{
					item.m_rot = Quaternion.identity;
					item.m_pos = collider.bounds.center;
					item.m_size = collider.bounds.size;
				}
				item.m_size.x += 0.3f;
				item.m_size.y += 0.3f;
				item.m_size.z += 0.3f;
				item.m_size *= 0.5f;
				m_bounds.Add(item);
			}
		}
	}

	private bool ShouldUpdate(float time)
	{
		if (!(m_createTime < 0f) && !(time - m_createTime > 30f))
		{
			return m_addPreSnow;
		}
		return true;
	}

	public void UpdateWear(float time)
	{
		if (!m_nview.IsValid())
		{
			return;
		}
		if (m_nview.IsOwner() && ShouldUpdate(time))
		{
			if (ZNetScene.instance.OutsideActiveArea(base.transform.position))
			{
				float maxSupport = GetMaxSupport();
				if (!m_support.Equals(maxSupport))
				{
					m_nview.GetZDO().Set(ZDOVars.s_support, maxSupport);
				}
				return;
			}
			bool flag = ShieldGenerator.IsInsideShieldCached(base.transform.position, ref m_shieldChangeID);
			float num = 0f;
			m_rainWet = !flag && !m_haveRoof && m_noRoofWear && EnvMan.IsWet();
			if ((bool)m_wet)
			{
				m_wet.SetActive(m_rainWet);
			}
			if (m_noRoofWear && !flag && GetHealthPercentage() > 0.5f)
			{
				if (IsWet())
				{
					if (m_rainTimer == 0f)
					{
						m_rainTimer = time;
					}
					else if (time - m_rainTimer > 60f)
					{
						m_rainTimer = time;
						num += 5f;
					}
				}
				else
				{
					m_rainTimer = 0f;
				}
			}
			if (m_noSupportWear)
			{
				UpdateSupport();
				if (!HaveSupport())
				{
					num = 100f;
				}
			}
			if (!string.IsNullOrEmpty(m_requiredPersistentEvent))
			{
				PersistentEventSystem.PersistentEvent activeEvent = PersistentEventSystem.instance.GetActiveEvent(base.transform.position);
				if (m_takeDamageIfInsideEvent && activeEvent != null && activeEvent.internalName == m_requiredPersistentEvent)
				{
					num += m_eventDamage + UnityEngine.Random.Range(0f - m_eventDamageDeviation, m_eventDamageDeviation);
				}
				else if ((activeEvent != null && activeEvent.internalName != m_requiredPersistentEvent) || activeEvent == null)
				{
					num += m_eventDamage + UnityEngine.Random.Range(0f - m_eventDamageDeviation, m_eventDamageDeviation);
				}
			}
			bool flag2 = false;
			UpdateBiome();
			if (m_biome == Heightmap.Biome.DeepNorth)
			{
				bool flag3 = CanHaveSnow(flag);
				if (flag3 && m_snowBuildup < 1f)
				{
					m_heavySnow = false;
					float num2 = (m_addPreSnow ? 1f : EnvMan.instance.GetSnowBuildup());
					if (num2 > 0f)
					{
						if (m_addPreSnow)
						{
							m_snowBuildup = 1f;
							m_addPreSnow = false;
							m_nview.GetZDO().Set(ZDOVars.s_preSnow, value: false);
						}
						else
						{
							m_snowBuildup += num2 * Time.deltaTime * Game.instance.m_snowBuildupSpeed;
						}
						m_nview.GetZDO().Set(ZDOVars.s_snow, m_snowBuildup);
					}
				}
				else if ((!m_snowDamageImmune || ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllHeavySnow)) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoHeavySnow))
				{
					m_heavySnow = flag3;
					if (m_heavySnow)
					{
						m_lastSupportColorValue = GetSupportColorValue();
						if (m_lastSupportColorValue != -1f && m_lastSupportColorValue < Game.instance.m_snowSupportLevel)
						{
							m_heavySnow = true;
							num += Game.instance.m_snowDamage;
							if (ZNet.instance.GetTimeSeconds() >= (double)m_snowDamageTimer)
							{
								m_snowDamageTimer = (float)ZNet.instance.GetTimeSeconds() + UnityEngine.Random.Range(Game.instance.m_snowDamageEffectIntervalRange.x, Game.instance.m_snowDamageEffectIntervalRange.y);
								Game.instance.m_snowDamageEffect.Create(base.transform.position, Quaternion.identity, base.transform);
							}
						}
					}
				}
			}
			if (m_biome == Heightmap.Biome.AshLands)
			{
				m_inAshlands = true;
				if (Game.instance.m_ashDamage > 0f && !flag && !m_ashDamageImmune)
				{
					flag2 = !m_haveAshRoof && (!m_ashDamageResist || GetHealthPercentage() > 0.1f);
					if (flag2)
					{
						if (m_ashTimer == 0f)
						{
							m_ashTimer = time;
						}
						else if (time - m_ashTimer > 5f)
						{
							m_ashTimer = time;
							num += Game.instance.m_ashDamage;
						}
					}
					else
					{
						m_ashTimer = 0f;
					}
				}
				if (!m_staticPosition)
				{
					m_lavaValue = m_heightmap.GetLava(base.transform.position);
				}
				if (m_lavaValue > 0.2f && m_groundDist < 1.5f && !m_ashDamageImmune)
				{
					if (m_lavaTimer == 0f)
					{
						m_lavaTimer = time;
					}
					else if (time - m_lavaTimer > 2f)
					{
						m_lavaTimer = time;
						float num3 = (flag ? 30f : 70f) * m_lavaValue;
						num += num3 * (m_ashDamageResist ? 0.33f : 1f);
					}
				}
				else
				{
					m_lavaTimer = 0f;
				}
			}
			if (m_requiredBiome != Heightmap.Biome.None && !m_requiredBiome.HasFlag(m_biome))
			{
				num += m_outsideRequiredBiomeDamage;
			}
			m_ashDamageTime = (flag2 ? 5 : 0);
			if (num > 0f && !CanBeRemoved())
			{
				num = 0f;
			}
			if (num > 0f)
			{
				float damage = num / 100f * m_health;
				if (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildingFall))
				{
					ApplyDamage(damage);
				}
			}
		}
		if ((bool)m_snow && m_nview.IsValid())
		{
			m_snowBuildup = m_nview.GetZDO().GetFloat(ZDOVars.s_snow);
		}
		UpdateVisual(triggerEffects: true);
	}

	private void UpdateBiome()
	{
		if ((m_biome != Heightmap.Biome.None && (bool)m_heightmap) || m_renderers == null)
		{
			return;
		}
		Vector3 p = base.transform.position;
		ZoneSystem.instance.GetGroundData(ref p, out var _, out var _, out var _, out m_heightmap);
		if (!(m_heightmap != null))
		{
			return;
		}
		m_biome = m_heightmap.GetBiome(base.transform.position);
		float num = 9999f;
		foreach (Renderer renderer in m_renderers)
		{
			if (!m_nonSolidRenderers.Contains(renderer))
			{
				float y = renderer.bounds.min.y;
				if (y < num)
				{
					num = y;
				}
			}
		}
		m_groundDist = num - p.y;
		if (m_staticPosition)
		{
			m_lavaValue = m_heightmap.GetLava(base.transform.position);
		}
	}

	public bool CanHaveSnow(bool forceCover = false)
	{
		return CanHaveSnow(ShieldGenerator.IsInsideShieldCached(base.transform.position, ref m_shieldChangeID), forceCover);
	}

	public bool CanHaveSnow(bool isShielded, bool forceCover = false)
	{
		if ((m_addPreSnow || forceCover) && m_haveRoof && m_roof == null)
		{
			UpdateBiome();
			UpdateCover(100f);
		}
		if (!isShielded)
		{
			return !m_haveRoof;
		}
		return false;
	}

	public void ChangeSnow(float change)
	{
		m_snowBuildup = Mathf.Clamp01(m_snowBuildup + change);
		if (m_nview.IsValid())
		{
			m_nview.InvokeRPC("RPC_SetSnow", m_snowBuildup);
			UpdateSnowVisual();
		}
	}

	private void RPC_SetSnow(long sender, float value)
	{
		if (m_nview.IsValid())
		{
			if (m_nview.IsOwner())
			{
				m_nview.GetZDO().Set(ZDOVars.s_snow, value);
			}
			m_snowBuildup = m_nview.GetZDO().GetFloat(ZDOVars.s_snow);
		}
	}

	public void UpdateSnowVisual()
	{
		if ((bool)m_snow)
		{
			bool flag = m_healthPercentage <= 0.25f && (bool)m_snowBroken;
			bool flag2 = m_healthPercentage <= 0.75f && (bool)m_snowWorn && !flag;
			m_snow.gameObject.SetActive(!flag2 && !flag && m_snowBuildup > 0.25f);
			if ((bool)m_snowWorn)
			{
				m_snowWorn.gameObject.SetActive(flag2 && !flag && m_snowBuildup > 0.25f);
			}
			if ((bool)m_snowBroken)
			{
				m_snowBroken.gameObject.SetActive(!flag2 && flag && m_snowBuildup > 0.25f);
			}
			if (m_snowBuildup > 0.25f)
			{
				MaterialMan.instance.SetValue(base.gameObject, s_snowLevel, Mathf.Clamp01(Utils.Remap(m_snowBuildup, 0.25f, 1f, 0f, 1f)));
			}
		}
	}

	private Vector3 GetCOM()
	{
		return base.transform.position + base.transform.rotation * m_comOffset;
	}

	public bool IsWet()
	{
		if (!m_rainWet)
		{
			return IsUnderWater();
		}
		return true;
	}

	private void ClearCachedSupport()
	{
		m_supportColliders.Clear();
		m_supportPositions.Clear();
		m_supportWearNTear.Clear();
		m_supportValue.Clear();
	}

	private void RPC_ClearCachedSupport(long sender)
	{
		ClearCachedSupport();
	}

	private void UpdateSupport()
	{
		int count = m_supportColliders.Count;
		if (count > 0)
		{
			int num = 0;
			float num2 = 0f;
			for (int i = 0; i < count; i++)
			{
				Collider collider = m_supportColliders[i];
				if (collider == null)
				{
					break;
				}
				WearNTear wearNTear = m_supportWearNTear[i];
				if (wearNTear == null || !wearNTear.m_supports || collider.transform.position != m_supportPositions[i])
				{
					break;
				}
				float support = wearNTear.GetSupport();
				if (support > num2)
				{
					num2 = support;
				}
				if (!support.Equals(m_supportValue[i]))
				{
					break;
				}
				num++;
			}
			if (num == m_supportPositions.Count && num2 >= m_support)
			{
				return;
			}
			ClearCachedSupport();
		}
		if (m_colliders == null)
		{
			SetupColliders();
		}
		GetMaterialProperties(out var maxSupport, out var _, out var horizontalLoss, out var verticalLoss);
		s_tempSupportPoints.Clear();
		s_tempSupportPointValues.Clear();
		Vector3 cOM = GetCOM();
		bool flag = false;
		float num3 = 0f;
		foreach (BoundData bound in m_bounds)
		{
			int num4 = Physics.OverlapBoxNonAlloc(bound.m_pos, bound.m_size, s_tempColliders, bound.m_rot, s_rayMask);
			if (m_clearCachedSupport)
			{
				for (int j = 0; j < num4; j++)
				{
					Collider collider2 = s_tempColliders[j];
					if (collider2.attachedRigidbody != null || collider2.isTrigger || m_colliders.Contains(collider2))
					{
						continue;
					}
					WearNTear componentInParent = collider2.GetComponentInParent<WearNTear>();
					if (!(componentInParent == null))
					{
						if (componentInParent.m_nview.IsOwner())
						{
							componentInParent.ClearCachedSupport();
						}
						else if (componentInParent.m_nview.IsValid())
						{
							componentInParent.m_nview.InvokeRPC(componentInParent.m_nview.GetZDO().GetOwner(), "RPC_ClearCachedSupport");
						}
					}
				}
				m_clearCachedSupport = false;
			}
			for (int k = 0; k < num4; k++)
			{
				Collider collider3 = s_tempColliders[k];
				if (collider3.attachedRigidbody != null || collider3.isTrigger || m_colliders.Contains(collider3))
				{
					continue;
				}
				if (collider3.gameObject.layer == s_terrainLayer)
				{
					flag = true;
					continue;
				}
				WearNTear componentInParent2 = collider3.GetComponentInParent<WearNTear>();
				if (componentInParent2 == null)
				{
					bool num5 = !m_support.Equals(maxSupport);
					m_support = maxSupport;
					ClearCachedSupport();
					if (num5)
					{
						m_nview.GetZDO().Set(ZDOVars.s_support, m_support);
					}
					return;
				}
				if (!componentInParent2.m_supports)
				{
					continue;
				}
				float num6 = Vector3.Distance(cOM, componentInParent2.GetCOM()) + 0.1f;
				float num7 = Vector3.Distance(cOM, componentInParent2.transform.position) + 0.1f;
				if (num7 < num6 && !m_forceCorrectCOMCalculation)
				{
					num6 = num7;
				}
				float support2 = componentInParent2.GetSupport();
				num3 = Mathf.Max(num3, support2 - horizontalLoss * num6 * support2);
				Vector3 vector = FindSupportPoint(cOM, componentInParent2, collider3);
				if (vector.y < cOM.y + 0.05f)
				{
					Vector3 normalized = (vector - cOM).normalized;
					if (normalized.y < 0f)
					{
						float t = Mathf.Acos(1f - Mathf.Abs(normalized.y)) / (MathF.PI / 2f);
						float num8 = Mathf.Lerp(horizontalLoss, verticalLoss, t);
						float b = support2 - num8 * num6 * support2;
						num3 = Mathf.Max(num3, b);
					}
					float item = support2 - verticalLoss * num6 * support2;
					s_tempSupportPoints.Add(vector);
					s_tempSupportPointValues.Add(item);
					m_supportColliders.Add(collider3);
					m_supportWearNTear.Add(componentInParent2);
					m_supportPositions.Add(collider3.transform.position);
					m_supportValue.Add(componentInParent2.GetSupport());
				}
			}
		}
		if (flag)
		{
			bool num9 = !m_support.Equals(maxSupport);
			m_support = maxSupport;
			if (num9)
			{
				m_nview.GetZDO().Set(ZDOVars.s_support, m_support);
			}
			return;
		}
		if (s_tempSupportPoints.Count > 0)
		{
			int count2 = s_tempSupportPoints.Count;
			for (int l = 0; l < count2 - 1; l++)
			{
				Vector3 vector2 = s_tempSupportPoints[l] - cOM;
				vector2.y = 0f;
				for (int m = l + 1; m < count2; m++)
				{
					float num10 = (s_tempSupportPointValues[l] + s_tempSupportPointValues[m]) * 0.5f;
					if (!(num10 <= num3))
					{
						Vector3 to = s_tempSupportPoints[m] - cOM;
						to.y = 0f;
						if (Vector3.Angle(vector2, to) >= 100f)
						{
							num3 = num10;
						}
					}
				}
			}
		}
		float support3 = m_support;
		m_support = Mathf.Min(num3, maxSupport);
		if (!m_support.Equals(support3))
		{
			m_nview.GetZDO().Set(ZDOVars.s_support, m_support);
		}
		if (!HaveSupport())
		{
			ClearCachedSupport();
		}
	}

	private static Vector3 FindSupportPoint(Vector3 com, WearNTear wnt, Collider otherCollider)
	{
		MeshCollider meshCollider = otherCollider as MeshCollider;
		if (meshCollider != null && !meshCollider.convex)
		{
			if (meshCollider.Raycast(new Ray(com, Vector3.down), out var hitInfo, 10f))
			{
				return hitInfo.point;
			}
			return (com + wnt.GetCOM()) * 0.5f;
		}
		return otherCollider.ClosestPoint(com);
	}

	private bool HaveSupport()
	{
		return m_support >= GetMinSupport();
	}

	private bool IsUnderWater()
	{
		return Floating.IsUnderWater(base.transform.position, ref m_previousWaterVolume);
	}

	public void UpdateCover(float dt)
	{
		m_updateCoverTimer += dt;
		if (!(m_updateCoverTimer <= 4f))
		{
			if (EnvMan.IsWet() || m_biome == Heightmap.Biome.DeepNorth)
			{
				m_haveRoof = HaveRoof();
			}
			if (m_inAshlands)
			{
				m_haveAshRoof = HaveAshRoof();
			}
			m_updateCoverTimer = 0f;
		}
	}

	private bool HaveRoof()
	{
		if ((bool)m_roof)
		{
			return true;
		}
		return RoofCheck(base.transform, base.transform.position, out m_roof, m_roofCheckOffset);
	}

	public static bool RoofCheck(Transform obj, Vector3 position, out GameObject roofObject, float heightOffset = 0f)
	{
		int num = Physics.SphereCastNonAlloc(position + new Vector3(0f, heightOffset, 0f), 0.1f, Vector3.up, s_raycastHits, 100f, s_rayMask);
		for (int i = 0; i < num; i++)
		{
			RaycastHit raycastHit = s_raycastHits[i];
			if (!raycastHit.collider.transform.IsAncestor(obj) && !raycastHit.collider.gameObject.CompareTag("leaky"))
			{
				roofObject = raycastHit.collider.gameObject;
				return true;
			}
		}
		roofObject = null;
		return false;
	}

	private bool HaveAshRoof()
	{
		if ((bool)m_ashroof)
		{
			return true;
		}
		int num = Physics.SphereCastNonAlloc(base.transform.position, 0.1f, Vector3.up, s_raycastHits, 100f, s_rayMask);
		for (int i = 0; i < num; i++)
		{
			RaycastHit raycastHit = s_raycastHits[i];
			if (raycastHit.collider.gameObject != base.gameObject && (raycastHit.collider.transform.parent == null || raycastHit.collider.transform.parent.gameObject != base.gameObject))
			{
				m_ashroof = raycastHit.collider.gameObject;
				return true;
			}
		}
		return false;
	}

	private void RPC_HealthChanged(long peer, float health)
	{
		float health2 = health / m_health;
		m_healthPercentage = Mathf.Clamp01(health / m_health);
		ClearCachedSupport();
		SetHealthVisual(health2, triggerEffects: true);
	}

	private void UpdateVisual(bool triggerEffects)
	{
		if (m_nview.IsValid())
		{
			SetHealthVisual(GetHealthPercentage(), triggerEffects);
		}
	}

	private void SetHealthVisual(float health, bool triggerEffects)
	{
		if (m_worn == null && m_broken == null && m_new == null)
		{
			return;
		}
		if (health > 0.75f)
		{
			if (m_worn != m_new)
			{
				m_worn.SetActive(value: false);
			}
			if (m_broken != m_new)
			{
				m_broken.SetActive(value: false);
			}
			m_new.SetActive(value: true);
		}
		else if (health > 0.25f)
		{
			if (triggerEffects && !m_worn.activeSelf)
			{
				m_switchEffect.Create(base.transform.position, base.transform.rotation, base.transform);
			}
			if (m_new != m_worn)
			{
				m_new.SetActive(value: false);
			}
			if (m_broken != m_worn)
			{
				m_broken.SetActive(value: false);
			}
			m_worn.SetActive(value: true);
		}
		else
		{
			if (triggerEffects && !m_broken.activeSelf)
			{
				m_switchEffect.Create(base.transform.position, base.transform.rotation, base.transform);
			}
			if (m_new != m_broken)
			{
				m_new.SetActive(value: false);
			}
			if (m_worn != m_broken)
			{
				m_worn.SetActive(value: false);
			}
			m_broken.SetActive(value: true);
		}
		UpdateSnowVisual();
	}

	public void OnSleep()
	{
		if (m_nview.IsOwner())
		{
			AddPreSnow();
		}
	}

	public void AddPreSnow()
	{
		m_addPreSnow = true;
	}

	public float GetHealthPercentage()
	{
		if (!m_nview.IsValid())
		{
			return 1f;
		}
		return m_healthPercentage;
	}

	public DestructibleType GetDestructibleType()
	{
		return DestructibleType.Default;
	}

	public void Damage(HitData hit)
	{
		if (m_nview.IsValid())
		{
			m_nview.InvokeRPC("RPC_Damage", hit);
		}
	}

	private bool CanBeRemoved()
	{
		if ((bool)m_piece)
		{
			return m_piece.CanBeRemoved();
		}
		return true;
	}

	private void RPC_Damage(long sender, HitData hit)
	{
		if (!m_nview.IsValid() || !m_nview.IsOwner() || m_nview.GetZDO().GetFloat(ZDOVars.s_health, m_health) <= 0f)
		{
			return;
		}
		hit.ApplyResistance(m_damages, out var significantModifier);
		float totalDamage = hit.GetTotalDamage();
		DamageText.instance.ShowText(significantModifier, hit.m_point, totalDamage);
		if (totalDamage <= 0f)
		{
			return;
		}
		if (m_triggerPrivateArea)
		{
			Character attacker = hit.GetAttacker();
			if ((bool)attacker)
			{
				bool destroyed = totalDamage >= m_nview.GetZDO().GetFloat(ZDOVars.s_health, m_health);
				PrivateArea.OnObjectDamaged(base.transform.position, attacker, destroyed);
			}
		}
		if (!hit.CheckToolTier(m_minToolTier, alwaysAllowTierZero: true))
		{
			DamageText.instance.ShowText(DamageText.TextType.TooHard, hit.m_point, 0f);
			return;
		}
		ApplyDamage(totalDamage, hit);
		if (hit.m_hitType != HitData.HitType.CinderFire && hit.m_hitType != HitData.HitType.AshlandsOcean)
		{
			ZDOID gamepadEffectsExclusiveToPlayer = (((object)hit.GetAttacker() != null) ? hit.GetAttacker().GetZDOID() : ZDOID.None);
			m_hitEffect.Create(hit.m_point, Quaternion.identity, base.transform, 1f, -1, gamepadEffectsExclusiveToPlayer);
			if (hit.GetTotalPhysicalDamage() > 0f)
			{
				m_hitEffect.Create(hit.m_point, Quaternion.identity, base.transform, 1f, -1, gamepadEffectsExclusiveToPlayer);
			}
		}
		if (m_hitNoise > 0f && hit.m_hitType != HitData.HitType.CinderFire)
		{
			Player closestPlayer = Player.GetClosestPlayer(hit.m_point, 10f);
			if ((bool)closestPlayer)
			{
				closestPlayer.AddNoise(m_hitNoise);
			}
		}
		if (m_onDamaged != null)
		{
			m_onDamaged();
		}
		if (hit.m_damage.m_fire > 3f && !IsWet())
		{
			m_burnDamageTime = 3f;
		}
	}

	public bool ApplyDamage(float damage, HitData hitData = null)
	{
		float num = m_nview.GetZDO().GetFloat(ZDOVars.s_health, m_health);
		if (num <= 0f)
		{
			return false;
		}
		num -= damage;
		m_nview.GetZDO().Set(ZDOVars.s_health, num);
		if (Terminal.m_showTests && Terminal.m_testList.ContainsKey("damage"))
		{
			Terminal.Log(string.Format("Damage WNT: {0} took {1} damage from {2}", base.gameObject.name, damage, (hitData == null) ? ((object)"UNKNOWN") : ((object)hitData)));
		}
		if (num <= 0f)
		{
			Destroy(hitData);
		}
		else
		{
			m_nview.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", num);
		}
		return true;
	}

	public void Remove(bool blockDrop = false)
	{
		if (m_nview.IsValid())
		{
			m_nview.InvokeRPC("RPC_Remove", blockDrop);
		}
	}

	private void RPC_Remove(long sender, bool blockDrop)
	{
		if (m_nview.IsValid() && m_nview.IsOwner())
		{
			Destroy(null, blockDrop);
		}
	}

	private void Destroy(HitData hitData = null, bool blockDrop = false)
	{
		Bed component = GetComponent<Bed>();
		if (component != null && m_nview.IsOwner() && Game.instance != null)
		{
			Game.instance.RemoveCustomSpawnPoint(component.GetSpawnPoint());
		}
		m_nview.GetZDO().Set(ZDOVars.s_health, 0f);
		m_nview.GetZDO().Set(ZDOVars.s_support, 0f);
		m_support = 0f;
		m_health = 0f;
		ClearCachedSupport();
		if ((bool)m_piece && !blockDrop)
		{
			m_piece.DropResources(hitData);
		}
		if (m_onDestroyed != null)
		{
			m_onDestroyed();
		}
		if (m_destroyNoise > 0f && (hitData == null || hitData.m_hitType != HitData.HitType.CinderFire))
		{
			Player closestPlayer = Player.GetClosestPlayer(base.transform.position, 10f);
			if ((bool)closestPlayer)
			{
				closestPlayer.AddNoise(m_destroyNoise);
			}
		}
		ZDOID gamepadEffectsExclusiveToPlayer = ZDOID.None;
		if (hitData != null && hitData.GetAttacker() != null)
		{
			gamepadEffectsExclusiveToPlayer = hitData.GetAttacker().GetZDOID();
		}
		m_destroyedEffect.Create(base.transform.position, base.transform.rotation, base.transform, 1f, -1, gamepadEffectsExclusiveToPlayer);
		if (m_autoCreateFragments)
		{
			m_nview.InvokeRPC(ZNetView.Everybody, "RPC_CreateFragments");
		}
		ZNetScene.instance.Destroy(base.gameObject);
	}

	private void RPC_CreateFragments(long peer)
	{
		ResetHighlight();
		if (m_fragmentRoots != null && m_fragmentRoots.Length != 0)
		{
			GameObject[] fragmentRoots = m_fragmentRoots;
			foreach (GameObject obj in fragmentRoots)
			{
				obj.SetActive(value: true);
				Destructible.CreateFragments(obj, visibleOnly: false);
			}
		}
		else
		{
			Destructible.CreateFragments(base.gameObject);
		}
	}

	private float GetMaxSupport()
	{
		GetMaterialProperties(out var maxSupport, out var _, out var _, out var _);
		return maxSupport;
	}

	private float GetMinSupport()
	{
		GetMaterialProperties(out var _, out var minSupport, out var _, out var _);
		return minSupport;
	}

	private void GetMaterialProperties(out float maxSupport, out float minSupport, out float horizontalLoss, out float verticalLoss)
	{
		switch (m_materialType)
		{
		case MaterialType.Wood:
			maxSupport = 100f;
			minSupport = 10f;
			verticalLoss = 0.125f;
			horizontalLoss = 0.2f;
			break;
		case MaterialType.HardWood:
			maxSupport = 140f;
			minSupport = 10f;
			verticalLoss = 0.1f;
			horizontalLoss = 1f / 6f;
			break;
		case MaterialType.Stone:
			maxSupport = 1000f;
			minSupport = 100f;
			verticalLoss = 0.125f;
			horizontalLoss = 1f;
			break;
		case MaterialType.Iron:
			maxSupport = 1500f;
			minSupport = 20f;
			verticalLoss = 1f / 13f;
			horizontalLoss = 1f / 13f;
			break;
		case MaterialType.Marble:
			maxSupport = 1500f;
			minSupport = 100f;
			verticalLoss = 0.125f;
			horizontalLoss = 0.5f;
			break;
		case MaterialType.Ashstone:
			maxSupport = 2000f;
			minSupport = 100f;
			verticalLoss = 0.1f;
			horizontalLoss = 1f / 3f;
			break;
		case MaterialType.Ancient:
			maxSupport = 5000f;
			minSupport = 100f;
			verticalLoss = 1f / 15f;
			horizontalLoss = 0.25f;
			break;
		case MaterialType.Ice:
			maxSupport = 1000f;
			minSupport = 100f;
			verticalLoss = 0.125f;
			horizontalLoss = 1f / 3f;
			break;
		case MaterialType.Timberwood:
			maxSupport = 200f;
			minSupport = 10f;
			verticalLoss = 1f / 13f;
			horizontalLoss = 0.2f;
			break;
		default:
			maxSupport = 0f;
			minSupport = 0f;
			verticalLoss = 0f;
			horizontalLoss = 0f;
			break;
		}
	}

	public static List<WearNTear> GetAllInstances()
	{
		return s_allInstances;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
