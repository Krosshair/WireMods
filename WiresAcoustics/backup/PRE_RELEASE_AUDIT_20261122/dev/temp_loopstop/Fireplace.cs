using System;
using System.Collections.Generic;
using UnityEngine;

public class Fireplace : MonoBehaviour, Hoverable, Interactable, IHasHoverMenu
{
	[Serializable]
	public struct FireworkItem
	{
		public ItemDrop m_fireworkItem;

		public int m_fireworkItemCount;

		public EffectList m_fireworksEffects;
	}

	private ZNetView m_nview;

	private Piece m_piece;

	[Header("Fire")]
	public string m_name = "Fire";

	public float m_startFuel = 3f;

	public float m_maxFuel = 10f;

	public float m_secPerFuel = 3f;

	public bool m_infiniteFuel;

	public bool m_disableCoverCheck;

	public float m_checkTerrainOffset = 0.2f;

	public float m_coverCheckOffset = 0.5f;

	private const float m_minimumOpenSpace = 0.5f;

	public float m_holdRepeatInterval = 0.2f;

	public float m_halfThreshold = 0.5f;

	public bool m_canTurnOff;

	public bool m_canRefill = true;

	public bool m_lowWetOverHalf = true;

	public float m_hoverOffset;

	public GameObject m_enabledObject;

	public GameObject m_enabledObjectLow;

	public GameObject m_enabledObjectHigh;

	public GameObject m_fullObject;

	public GameObject m_halfObject;

	public GameObject m_emptyObject;

	public GameObject m_playerBaseObject;

	public ItemDrop m_fuelItem;

	public SmokeSpawner m_smokeSpawner;

	public GameObject m_snowMelter;

	public float m_snowMelterInterval;

	public EffectList m_fuelAddedEffects = new EffectList();

	public EffectList m_toggleOnEffects = new EffectList();

	[Header("Fireworks")]
	[Range(0f, 60f)]
	public float m_fireworksMaxRandomAngle = 5f;

	public FireworkItem[] m_fireworkItemList;

	[Header("Ignite Pieces")]
	public float m_igniteInterval;

	public float m_igniteChance;

	public int m_igniteSpread = 4;

	public float m_igniteCapsuleRadius;

	public Vector3 m_igniteCapsuleStart;

	public Vector3 m_igniteCapsuleEnd;

	public GameObject m_firePrefab;

	private bool m_blocked;

	private bool m_wet;

	private Heightmap.Biome m_biome;

	private float m_lastUseTime;

	private bool m_checkWaterLevel;

	private WaterVolume m_previousWaterVolume;

	private static int m_solidRayMask = 0;

	private static Collider[] s_tempColliders = new Collider[20];

	public void Awake()
	{
		m_nview = base.gameObject.GetComponent<ZNetView>();
		m_piece = base.gameObject.GetComponent<Piece>();
		if (m_nview.GetZDO() == null)
		{
			return;
		}
		if (m_solidRayMask == 0)
		{
			m_solidRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
		}
		if (m_nview.IsOwner() && m_nview.GetZDO().GetFloat(ZDOVars.s_fuel, -1f) == -1f)
		{
			m_nview.GetZDO().Set(ZDOVars.s_fuel, m_startFuel);
			if (m_startFuel > 0f)
			{
				m_fuelAddedEffects.Create(base.transform.position, base.transform.rotation);
			}
		}
		Vector3 p = (m_enabledObject ? m_enabledObject.transform.position : base.transform.position);
		p.y -= 15f;
		m_checkWaterLevel = Floating.IsUnderWater(p, ref m_previousWaterVolume);
		m_nview.Register("RPC_AddFuel", RPC_AddFuel);
		m_nview.Register<float>("RPC_AddFuelAmount", RPC_AddFuelAmount);
		m_nview.Register<float>("RPC_SetFuelAmount", RPC_SetFuelAmount);
		m_nview.Register("RPC_ToggleOn", RPC_ToggleOn);
		InvokeRepeating("UpdateFireplace", 0f, 2f);
		InvokeRepeating("CheckEnv", 4f, 4f);
		if (m_igniteInterval > 0f && m_igniteCapsuleRadius > 0f)
		{
			InvokeRepeating("UpdateIgnite", m_igniteInterval, m_igniteInterval);
		}
		if ((bool)m_snowMelter && Heightmap.FindBiome(base.transform.position) == Heightmap.Biome.DeepNorth)
		{
			UpdateSnowMelt();
			InvokeRepeating("UpdateSnowMelt", UnityEngine.Random.Range(m_snowMelterInterval, m_snowMelterInterval * 2f), m_snowMelterInterval);
		}
	}

	private void Start()
	{
		if ((bool)m_playerBaseObject && (bool)m_piece)
		{
			m_playerBaseObject.SetActive(m_piece.IsPlacedByPlayer());
		}
	}

	private double GetTimeSinceLastUpdate()
	{
		DateTime time = ZNet.instance.GetTime();
		DateTime dateTime = new DateTime(m_nview.GetZDO().GetLong(ZDOVars.s_lastTime, time.Ticks));
		TimeSpan timeSpan = time - dateTime;
		m_nview.GetZDO().Set(ZDOVars.s_lastTime, time.Ticks);
		double num = timeSpan.TotalSeconds;
		if (num < 0.0)
		{
			num = 0.0;
		}
		return num;
	}

	private void UpdateFireplace()
	{
		if (!m_nview.IsValid())
		{
			return;
		}
		if (m_nview.IsOwner() && m_secPerFuel > 0f)
		{
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
			double timeSinceLastUpdate = GetTimeSinceLastUpdate();
			bool flag = m_nview.GetZDO().GetInt(ZDOVars.s_state, 1) == 1;
			if (IsBurning() && !m_infiniteFuel && flag)
			{
				float num2 = (float)(timeSinceLastUpdate / (double)m_secPerFuel);
				num -= num2;
				if (num <= 0f)
				{
					num = 0f;
				}
				m_nview.GetZDO().Set(ZDOVars.s_fuel, num);
			}
		}
		UpdateState();
	}

	private void CheckEnv()
	{
		CheckUnderTerrain();
		if (m_enabledObjectLow != null && m_enabledObjectHigh != null)
		{
			CheckWet();
		}
	}

	private void CheckUnderTerrain()
	{
		m_blocked = false;
		if (!m_disableCoverCheck)
		{
			RaycastHit hitInfo;
			if (Heightmap.GetHeight(base.transform.position, out var height) && height > base.transform.position.y + m_checkTerrainOffset)
			{
				m_blocked = true;
			}
			else if (Physics.Raycast(base.transform.position + Vector3.up * m_coverCheckOffset, Vector3.up, out hitInfo, 0.5f, m_solidRayMask))
			{
				m_blocked = true;
			}
			else if ((bool)m_smokeSpawner && m_smokeSpawner.IsBlocked())
			{
				m_blocked = true;
			}
		}
	}

	private void CheckWet()
	{
		m_wet = false;
		bool flag = EnvMan.instance.GetWindIntensity() >= 0.8f;
		bool flag2 = EnvMan.IsWet();
		if (flag || flag2)
		{
			Cover.GetCoverForPoint(base.transform.position + Vector3.up * m_coverCheckOffset, out var coverPercentage, out var underRoof);
			if (flag && coverPercentage < 0.7f)
			{
				m_wet = true;
			}
			else if (flag2 && !underRoof)
			{
				m_wet = true;
			}
		}
	}

	private void UpdateState()
	{
		float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
		bool flag = num >= m_halfThreshold;
		bool flag2 = num <= 0f;
		if (m_lowWetOverHalf)
		{
			_ = !m_wet;
		}
		else
			_ = 0;
		if (IsBurning())
		{
			if ((bool)m_enabledObject)
			{
				m_enabledObject.SetActive(value: true);
			}
			if ((bool)m_enabledObjectHigh && (bool)m_enabledObjectLow)
			{
				if (m_enabledObjectHigh.activeSelf != !m_wet)
				{
					m_enabledObjectHigh.SetActive(!m_wet);
				}
				if (m_enabledObjectLow.activeSelf != m_wet)
				{
					m_enabledObjectLow.SetActive(m_wet);
				}
			}
			if (m_canTurnOff && m_wet && m_nview.IsOwner() && m_nview.GetZDO().GetInt(ZDOVars.s_state, 1) == 1)
			{
				m_nview.InvokeRPC("RPC_ToggleOn");
			}
		}
		else
		{
			if ((bool)m_enabledObject)
			{
				m_enabledObject.SetActive(value: false);
			}
			if ((bool)m_enabledObjectHigh && (bool)m_enabledObjectLow)
			{
				if (m_enabledObjectLow.activeSelf)
				{
					m_enabledObjectLow.SetActive(value: false);
				}
				if (m_enabledObjectHigh.activeSelf)
				{
					m_enabledObjectHigh.SetActive(value: false);
				}
			}
		}
		if ((bool)m_fullObject && (bool)m_halfObject)
		{
			m_fullObject.SetActive(flag);
			m_halfObject.SetActive(!flag);
		}
		if (!m_emptyObject)
		{
			return;
		}
		if (flag2)
		{
			if ((bool)m_fullObject && m_fullObject.activeSelf)
			{
				m_fullObject.SetActive(value: false);
			}
			if ((bool)m_halfObject && m_halfObject.activeSelf)
			{
				m_halfObject.SetActive(value: false);
			}
		}
		if (m_emptyObject.activeSelf != flag2)
		{
			m_emptyObject.SetActive(flag2);
		}
	}

	private void UpdateSnowMelt()
	{
		if (IsBurning() && m_nview.IsOwner() && TerrainComp.ValidTCForAllAffectedHeightmaps(base.transform.position, m_snowMelter.GetComponent<TerrainOp>().m_settings.m_paintRadius))
		{
			UnityEngine.Object.Instantiate(m_snowMelter, base.transform.position, base.transform.rotation);
		}
	}

	public string GetHoverText()
	{
		if (!m_nview.IsValid() || m_infiniteFuel)
		{
			return "";
		}
		float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
		string text = m_name;
		if (m_canRefill)
		{
			text += $"\n( $piece_fire_fuel {Mathf.Ceil(num)}/{(int)m_maxFuel} )\n[<color=yellow><b>$KEY_Use</b></color>] $piece_use {m_fuelItem.m_itemData.m_shared.m_name}\n[<color=yellow><b>$KEY_HotbarUse</b></color>] $piece_useitem";
		}
		else if (m_canTurnOff && num > 0f)
		{
			text += "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_use";
		}
		return Localization.instance.Localize(text);
	}

	public string GetHoverName()
	{
		return m_name;
	}

	public void AddFuel(float fuel)
	{
		if ((bool)m_nview && m_nview.IsValid())
		{
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
			if ((fuel < 0f && num > 0f) || (fuel > 0f && num < m_maxFuel))
			{
				m_nview.InvokeRPC("RPC_AddFuelAmount", fuel);
			}
		}
	}

	public bool Interact(Humanoid user, bool hold, bool alt)
	{
		if (hold)
		{
			if (m_holdRepeatInterval <= 0f)
			{
				return false;
			}
			if (Time.time - m_lastUseTime < m_holdRepeatInterval)
			{
				return false;
			}
		}
		if (!m_nview.HasOwner())
		{
			m_nview.ClaimOwnership();
		}
		float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
		if (m_canTurnOff && !hold && !alt && num > 0f)
		{
			m_nview.InvokeRPC("RPC_ToggleOn");
			return true;
		}
		if (m_canRefill)
		{
			Inventory inventory = user.GetInventory();
			if (inventory != null)
			{
				if (m_infiniteFuel)
				{
					return false;
				}
				if (inventory.HaveItem(m_fuelItem.m_itemData.m_shared.m_name))
				{
					if ((float)Mathf.CeilToInt(num) >= m_maxFuel)
					{
						user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantaddmore", m_fuelItem.m_itemData.m_shared.m_name));
						return false;
					}
					user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_fireadding", m_fuelItem.m_itemData.m_shared.m_name));
					inventory.RemoveItem(m_fuelItem.m_itemData.m_shared.m_name, 1);
					m_nview.InvokeRPC("RPC_AddFuel");
					return true;
				}
				user.Message(MessageHud.MessageType.Center, "$msg_outof " + m_fuelItem.m_itemData.m_shared.m_name);
				return false;
			}
		}
		return false;
	}

	public bool UseItem(Humanoid user, ItemDrop.ItemData item)
	{
		if (!m_canRefill)
		{
			return false;
		}
		if (item.m_shared.m_name == m_fuelItem.m_itemData.m_shared.m_name && !m_infiniteFuel)
		{
			if ((float)Mathf.CeilToInt(m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)) >= m_maxFuel)
			{
				user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantaddmore", item.m_shared.m_name));
				return true;
			}
			Inventory inventory = user.GetInventory();
			user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_fireadding", item.m_shared.m_name));
			inventory.RemoveItem(item, 1);
			m_nview.InvokeRPC("RPC_AddFuel");
			return true;
		}
		for (int i = 0; i < m_fireworkItemList.Length; i++)
		{
			if (item.m_shared.m_name == m_fireworkItemList[i].m_fireworkItem.m_itemData.m_shared.m_name)
			{
				if (!IsBurning())
				{
					user.Message(MessageHud.MessageType.Center, "$msg_firenotburning");
					return true;
				}
				if (user.GetInventory().CountItems(m_fireworkItemList[i].m_fireworkItem.m_itemData.m_shared.m_name) < m_fireworkItemList[i].m_fireworkItemCount)
				{
					user.Message(MessageHud.MessageType.Center, "$msg_toofew " + m_fireworkItemList[i].m_fireworkItem.m_itemData.m_shared.m_name);
					return true;
				}
				user.GetInventory().RemoveItem(item.m_shared.m_name, m_fireworkItemList[i].m_fireworkItemCount);
				user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_throwinfire", item.m_shared.m_name));
				float x = UnityEngine.Random.Range(0f - m_fireworksMaxRandomAngle, m_fireworksMaxRandomAngle);
				float z = UnityEngine.Random.Range(0f - m_fireworksMaxRandomAngle, m_fireworksMaxRandomAngle);
				Quaternion baseRot = Quaternion.Euler(x, 0f, z);
				m_fireworkItemList[i].m_fireworksEffects.Create(base.transform.position, baseRot);
				m_fuelAddedEffects.Create(base.transform.position, base.transform.rotation);
				return true;
			}
		}
		return false;
	}

	private void RPC_AddFuel(long sender)
	{
		if (m_nview.IsOwner())
		{
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
			if (!((float)Mathf.CeilToInt(num) >= m_maxFuel))
			{
				num = Mathf.Clamp(num, 0f, m_maxFuel);
				num += 1f;
				num = Mathf.Clamp(num, 0f, m_maxFuel);
				m_nview.GetZDO().Set(ZDOVars.s_fuel, num);
				m_fuelAddedEffects.Create(base.transform.position, base.transform.rotation);
				UpdateState();
			}
		}
	}

	private void RPC_ToggleOn(long sender)
	{
		if (m_nview.IsOwner())
		{
			bool flag = m_nview.GetZDO().GetInt(ZDOVars.s_state, 1) == 1;
			m_nview.GetZDO().Set(ZDOVars.s_state, (!flag) ? 1 : 2);
			m_toggleOnEffects.Create(base.transform.position, Quaternion.identity, null, 1f, (!flag) ? 1 : 2);
		}
		UpdateState();
	}

	private void RPC_AddFuelAmount(long sender, float amount)
	{
		if (m_nview.IsOwner())
		{
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
			num = Mathf.Clamp(num + amount, 0f, m_maxFuel);
			m_nview.GetZDO().Set(ZDOVars.s_fuel, num);
			m_fuelAddedEffects.Create(base.transform.position, base.transform.rotation);
			UpdateState();
		}
	}

	public void SetFuel(float fuel)
	{
		if ((bool)m_nview && m_nview.IsValid())
		{
			float num = m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
			fuel = Mathf.Clamp(fuel, 0f, m_maxFuel);
			if (fuel != num)
			{
				m_nview.InvokeRPC("RPC_SetFuelAmount", fuel);
			}
		}
	}

	private void RPC_SetFuelAmount(long sender, float fuel)
	{
		if (m_nview.IsOwner())
		{
			m_nview.GetZDO().Set(ZDOVars.s_fuel, fuel);
			m_fuelAddedEffects.Create(base.transform.position, base.transform.rotation);
			UpdateState();
		}
	}

	public bool CanBeRemoved()
	{
		return !IsBurning();
	}

	public bool IsBurning()
	{
		if (m_blocked)
		{
			return false;
		}
		if (m_nview.GetZDO().GetInt(ZDOVars.s_state, 1) != 1)
		{
			return false;
		}
		if (m_checkWaterLevel && Floating.IsUnderWater(m_enabledObject ? m_enabledObject.transform.position : base.transform.position, ref m_previousWaterVolume))
		{
			return false;
		}
		if (!(m_nview.GetZDO().GetFloat(ZDOVars.s_fuel) > 0f))
		{
			return m_infiniteFuel;
		}
		return true;
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.white;
		Gizmos.DrawWireSphere(base.transform.position + Vector3.up * m_coverCheckOffset, 0.5f);
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireCube(base.transform.position + Vector3.up * m_checkTerrainOffset, new Vector3(1f, 0.01f, 1f));
		Gizmos.color = Color.red;
		Utils.DrawGizmoCapsule(base.transform.position + m_igniteCapsuleStart, base.transform.position + m_igniteCapsuleEnd, m_igniteCapsuleRadius);
	}

	private void UpdateIgnite()
	{
		if (!m_nview.IsValid() || !m_nview.IsOwner() || !m_firePrefab || !CanIgnite() || !IsBurning())
		{
			return;
		}
		int num = Physics.OverlapCapsuleNonAlloc(base.transform.position + m_igniteCapsuleStart, base.transform.position + m_igniteCapsuleEnd, m_igniteCapsuleRadius, s_tempColliders);
		for (int i = 0; i < num; i++)
		{
			Collider collider = s_tempColliders[i];
			if (!(collider.gameObject == base.gameObject) && (!(collider.transform.parent != null) || !(collider.transform.parent.gameObject == base.gameObject)) && !collider.isTrigger && UnityEngine.Random.Range(0f, 1f) <= m_igniteChance && Cinder.CanBurn(collider, collider.transform.position, out var _))
			{
				UnityEngine.Object.Instantiate(m_firePrefab, collider.transform.position + Utils.RandomVector3(-0.1f, 0.1f), Quaternion.identity).GetComponent<CinderSpawner>()?.Setup(m_igniteSpread, collider.gameObject);
			}
		}
	}

	public bool TryGetItems(Player player, out List<string> items)
	{
		items = new List<string>();
		if (m_infiniteFuel)
		{
			return false;
		}
		if (!CanUseItems(player))
		{
			return true;
		}
		items.Add(m_fuelItem.m_itemData.m_shared.m_name);
		return true;
	}

	public bool CanUseItems(Player player, bool sendErrorMessage = true)
	{
		if (m_infiniteFuel)
		{
			return false;
		}
		if (!player.GetInventory().HaveItem(m_fuelItem.m_itemData.m_shared.m_name))
		{
			if (sendErrorMessage)
			{
				player.Message(MessageHud.MessageType.Center, "$msg_outof " + m_fuelItem.m_itemData.m_shared.m_name);
			}
			return false;
		}
		if (!((float)Mathf.CeilToInt(m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)) >= m_maxFuel))
		{
			return true;
		}
		if (sendErrorMessage)
		{
			player.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantaddmore", m_fuelItem.m_itemData.m_shared.m_name));
		}
		return false;
	}

	public bool CanIgnite()
	{
		return CinderSpawner.CanSpawnCinder(base.transform, ref m_biome);
	}

	public float GetHoverOffset()
	{
		return m_hoverOffset;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
