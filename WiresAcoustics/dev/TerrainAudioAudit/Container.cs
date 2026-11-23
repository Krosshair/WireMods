using System;
using System.Collections.Generic;
using UnityEngine;

public class Container : MonoBehaviour, Hoverable, Interactable
{
	public enum PrivacySetting
	{
		Private,
		Group,
		Public
	}

	private float m_lastTakeAllTime;

	public Action m_onTakeAllSuccess;

	public string m_name = "Container";

	public Sprite m_bkg;

	public int m_width = 3;

	public int m_height = 2;

	public PrivacySetting m_privacy = PrivacySetting.Public;

	public bool m_checkGuardStone;

	public bool m_autoDestroyEmpty;

	public DropTable m_defaultItems = new DropTable();

	public GameObject m_open;

	public GameObject m_closed;

	public EffectList m_openEffects = new EffectList();

	public EffectList m_closeEffects = new EffectList();

	public ZNetView m_rootObjectOverride;

	public Vagon m_wagon;

	public GameObject m_destroyedLootPrefab;

	public float m_hoverOffset;

	public PlayerStatType m_discoverStat = PlayerStatType.None;

	private Inventory m_inventory;

	private ZNetView m_nview;

	private Piece m_piece;

	private bool m_inUse;

	private bool m_loading;

	private uint m_lastRevision = uint.MaxValue;

	private void Awake()
	{
		m_nview = (m_rootObjectOverride ? m_rootObjectOverride.GetComponent<ZNetView>() : GetComponent<ZNetView>());
		if (m_nview.GetZDO() != null)
		{
			m_inventory = new Inventory(m_name, m_bkg, m_width, m_height);
			Inventory inventory = m_inventory;
			inventory.m_onChanged = (Action)Delegate.Combine(inventory.m_onChanged, new Action(OnContainerChanged));
			m_piece = GetComponent<Piece>();
			if ((bool)m_nview)
			{
				m_nview.Register<long>("RPC_RequestOpen", RPC_RequestOpen);
				m_nview.Register<bool>("RPC_OpenResponse", RPC_OpenResponse);
				m_nview.Register<long>("RPC_RequestStack", RPC_RequestStack);
				m_nview.Register<bool>("RPC_StackResponse", RPC_StackResponse);
				m_nview.Register<long>("RPC_RequestTakeAll", RPC_RequestTakeAll);
				m_nview.Register<bool>("RPC_TakeAllResponse", RPC_TakeAllResponse);
				m_nview.Register<int>("RPC_Discovered", RPC_Discovered);
			}
			WearNTear wearNTear = (m_rootObjectOverride ? m_rootObjectOverride.GetComponent<WearNTear>() : GetComponent<WearNTear>());
			if ((bool)wearNTear)
			{
				wearNTear.m_onDestroyed = (Action)Delegate.Combine(wearNTear.m_onDestroyed, new Action(OnDestroyed));
			}
			Destructible destructible = (m_rootObjectOverride ? m_rootObjectOverride.GetComponent<Destructible>() : GetComponent<Destructible>());
			if ((bool)destructible)
			{
				destructible.m_onDestroyed = (Action)Delegate.Combine(destructible.m_onDestroyed, new Action(OnDestroyed));
			}
			if (m_nview.IsOwner() && !m_nview.GetZDO().GetBool(ZDOVars.s_addedDefaultItems))
			{
				AddDefaultItems();
				m_nview.GetZDO().Set(ZDOVars.s_addedDefaultItems, value: true);
			}
			InvokeRepeating("CheckForChanges", 0f, 1f);
		}
	}

	private void AddDefaultItems()
	{
		foreach (ItemDrop.ItemData dropListItem in m_defaultItems.GetDropListItems())
		{
			if (m_nview.GetZDO().IsCheated() && !PlayerProfile.s_bypassCheatChecks)
			{
				dropListItem.m_cheated = true;
			}
			m_inventory.AddItem(dropListItem);
		}
	}

	private void DropAllItems(GameObject lootContainerPrefab)
	{
		while (m_inventory.NrOfItems() > 0)
		{
			Vector3 position = base.transform.position + UnityEngine.Random.insideUnitSphere * 1f;
			UnityEngine.Object.Instantiate(lootContainerPrefab, position, UnityEngine.Random.rotation).GetComponent<Container>().GetInventory()
				.MoveAll(m_inventory);
		}
	}

	private void DropAllItems()
	{
		List<ItemDrop.ItemData> allItems = m_inventory.GetAllItems();
		int num = 1;
		foreach (ItemDrop.ItemData item in allItems)
		{
			Vector3 position = base.transform.position + Vector3.up * 0.5f + UnityEngine.Random.insideUnitSphere * 0.3f;
			Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f);
			ItemDrop.DropItem(item, 0, position, rotation);
			num++;
		}
		m_inventory.RemoveAll();
		Save();
	}

	private void OnDestroyed()
	{
		if (m_nview.IsOwner())
		{
			if ((bool)m_destroyedLootPrefab)
			{
				DropAllItems(m_destroyedLootPrefab);
			}
			else
			{
				DropAllItems();
			}
		}
	}

	private void CheckForChanges()
	{
		if (m_nview.IsValid() && Load())
		{
			UpdateUseVisual();
			if (m_autoDestroyEmpty && m_nview.IsOwner() && !IsInUse() && m_inventory.NrOfItems() == 0)
			{
				m_nview.Destroy();
			}
		}
	}

	private void UpdateUseVisual()
	{
		bool flag;
		if (m_nview.IsOwner())
		{
			flag = m_inUse;
			m_nview.GetZDO().Set(ZDOVars.s_inUse, m_inUse ? 1 : 0);
		}
		else
		{
			flag = m_nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
		}
		if ((bool)m_open)
		{
			m_open.SetActive(flag);
		}
		if ((bool)m_closed)
		{
			m_closed.SetActive(!flag);
		}
	}

	private void UpdateRows()
	{
		int num = m_height;
		foreach (ItemDrop.ItemData allItem in m_inventory.GetAllItems())
		{
			int num2 = allItem.m_gridPos.y + 1;
			if (num2 > num)
			{
				num = num2;
			}
		}
		m_inventory.SetHeight(num);
	}

	public string GetHoverText()
	{
		if (m_checkGuardStone && !PrivateArea.CheckAccess(base.transform.position, 0f, flash: false))
		{
			return Localization.instance.Localize(m_name + "\n$piece_noaccess");
		}
		string text = ((m_inventory.NrOfItems() != 0) ? m_name : (m_name + " ( $piece_container_empty )"));
		text += "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open $msg_stackall_hover";
		return Localization.instance.Localize(text);
	}

	public string GetHoverName()
	{
		return m_name;
	}

	public bool Interact(Humanoid character, bool hold, bool alt)
	{
		if (hold)
		{
			return false;
		}
		if (m_checkGuardStone && !PrivateArea.CheckAccess(base.transform.position))
		{
			return true;
		}
		long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
		if (!CheckAccess(playerID))
		{
			character.Message(MessageHud.MessageType.Center, "$msg_cantopen");
			return true;
		}
		m_nview.InvokeRPC("RPC_RequestOpen", playerID);
		if (m_discoverStat != PlayerStatType.None)
		{
			string str = "Discovered_" + Player.m_localPlayer.GetPlayerName();
			if (!m_nview.GetZDO().GetBool(str))
			{
				Game.instance.IncrementPlayerStat(m_discoverStat);
				m_nview.InvokeRPC("discovered", str.GetStableHashCode());
			}
		}
		return true;
	}

	private void RPC_Discovered(long sender, int keyHash)
	{
		m_nview.GetZDO().Set(keyHash, value: true);
	}

	public bool UseItem(Humanoid user, ItemDrop.ItemData item)
	{
		return false;
	}

	public bool CanBeRemoved()
	{
		if (m_privacy == PrivacySetting.Private && GetInventory().NrOfItems() > 0)
		{
			return false;
		}
		return true;
	}

	private bool CheckAccess(long playerID)
	{
		return m_privacy switch
		{
			PrivacySetting.Public => true, 
			PrivacySetting.Private => m_piece.GetCreator() == playerID, 
			PrivacySetting.Group => false, 
			_ => false, 
		};
	}

	public bool IsOwner()
	{
		return m_nview.IsOwner();
	}

	public bool IsInUse()
	{
		return m_inUse;
	}

	public void SetInUse(bool inUse)
	{
		if (m_nview.IsOwner() && m_inUse != inUse)
		{
			m_inUse = inUse;
			UpdateUseVisual();
			ZDOID gamepadEffectsExclusiveToPlayer = (IsOwner() ? Player.m_localPlayer.GetZDOID() : ZDOID.None);
			if (inUse)
			{
				m_openEffects.Create(base.transform.position, base.transform.rotation, null, 1f, -1, gamepadEffectsExclusiveToPlayer);
			}
			else
			{
				m_closeEffects.Create(base.transform.position, base.transform.rotation, null, 1f, -1, gamepadEffectsExclusiveToPlayer);
			}
		}
	}

	public Inventory GetInventory()
	{
		return m_inventory;
	}

	private void RPC_RequestOpen(long uid, long playerID)
	{
		ZLog.Log($"Player {uid} wants to open {base.gameObject.name}   im: {ZDOMan.GetSessionID()}");
		if (!m_nview.IsOwner())
		{
			ZLog.Log("  but im not the owner");
		}
		else if ((IsInUse() || ((bool)m_wagon && m_wagon.InUse())) && uid != ZNet.GetUID())
		{
			ZLog.Log("  in use");
			m_nview.InvokeRPC(uid, "RPC_OpenResponse", false);
		}
		else if (!CheckAccess(playerID))
		{
			ZLog.Log("  not yours");
			m_nview.InvokeRPC(uid, "RPC_OpenResponse", false);
		}
		else
		{
			ZDOMan.instance.ForceSendZDO(uid, m_nview.GetZDO().m_uid);
			m_nview.GetZDO().SetOwner(uid);
			m_nview.InvokeRPC(uid, "RPC_OpenResponse", true);
		}
	}

	private void RPC_OpenResponse(long uid, bool granted)
	{
		if ((bool)Player.m_localPlayer)
		{
			if (granted)
			{
				InventoryGui.instance.Show(this);
			}
			else
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse");
			}
		}
	}

	public void StackAll()
	{
		long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
		m_nview.InvokeRPC("RPC_RequestStack", playerID);
	}

	private void RPC_RequestStack(long uid, long playerID)
	{
		ZLog.Log("Player " + uid + " wants to stack all in " + base.gameObject.name + "   im: " + ZDOMan.GetSessionID());
		if (!m_nview.IsOwner())
		{
			ZLog.Log("  but im not the owner");
		}
		else if ((IsInUse() || ((bool)m_wagon && m_wagon.InUse())) && uid != ZNet.GetUID())
		{
			ZLog.Log("  in use");
			m_nview.InvokeRPC(uid, "RPC_StackResponse", false);
		}
		else if (!CheckAccess(playerID))
		{
			ZLog.Log("  not yours");
			m_nview.InvokeRPC(uid, "RPC_StackResponse", false);
		}
		else
		{
			ZDOMan.instance.ForceSendZDO(uid, m_nview.GetZDO().m_uid);
			m_nview.GetZDO().SetOwner(uid);
			m_nview.InvokeRPC(uid, "RPC_StackResponse", true);
		}
	}

	private void RPC_StackResponse(long uid, bool granted)
	{
		if (!Player.m_localPlayer)
		{
			return;
		}
		if (granted)
		{
			if (m_inventory.StackAll(Player.m_localPlayer.GetInventory(), message: true) > 0)
			{
				InventoryGui.instance.m_moveItemEffects.Create(base.transform.position, Quaternion.identity);
			}
		}
		else
		{
			Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse");
		}
	}

	public bool TakeAll(Humanoid character)
	{
		if (m_checkGuardStone && !PrivateArea.CheckAccess(base.transform.position))
		{
			return false;
		}
		long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
		if (!CheckAccess(playerID))
		{
			character.Message(MessageHud.MessageType.Center, "$msg_cantopen");
			return false;
		}
		m_nview.InvokeRPC("RPC_RequestTakeAll", playerID);
		return true;
	}

	private void RPC_RequestTakeAll(long uid, long playerID)
	{
		ZLog.Log($"Player {uid} wants to takeall from {base.gameObject.name}   im: {ZDOMan.GetSessionID()}");
		if (!m_nview.IsOwner())
		{
			ZLog.Log("  but im not the owner");
		}
		else if ((IsInUse() || ((bool)m_wagon && m_wagon.InUse())) && uid != ZNet.GetUID())
		{
			ZLog.Log("  in use");
			m_nview.InvokeRPC(uid, "RPC_TakeAllResponse", false);
		}
		else if (!CheckAccess(playerID))
		{
			ZLog.Log("  not yours");
			m_nview.InvokeRPC(uid, "RPC_TakeAllResponse", false);
		}
		else if (!(Time.time - m_lastTakeAllTime < 2f))
		{
			m_lastTakeAllTime = Time.time;
			m_nview.InvokeRPC(uid, "RPC_TakeAllResponse", true);
		}
	}

	private void RPC_TakeAllResponse(long uid, bool granted)
	{
		if (!Player.m_localPlayer)
		{
			return;
		}
		if (granted)
		{
			m_nview.ClaimOwnership();
			ZDOMan.instance.ForceSendZDO(uid, m_nview.GetZDO().m_uid);
			Player.m_localPlayer.GetInventory().MoveAll(m_inventory);
			if (m_onTakeAllSuccess != null)
			{
				m_onTakeAllSuccess();
			}
		}
		else
		{
			Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse");
		}
	}

	private void OnContainerChanged()
	{
		if (!m_loading && IsOwner())
		{
			Save();
		}
	}

	private void Save()
	{
		ZPackage zPackage = new ZPackage();
		m_inventory.Save(zPackage);
		byte[] array = zPackage.GetArray();
		m_nview.GetZDO().Set(ZDOVars.s_items, array);
		m_lastRevision = m_nview.GetZDO().DataRevision;
	}

	private bool Load()
	{
		if (m_nview.GetZDO().DataRevision == m_lastRevision)
		{
			return false;
		}
		if (m_inUse)
		{
			return false;
		}
		m_lastRevision = m_nview.GetZDO().DataRevision;
		byte[] byteArray = m_nview.GetZDO().GetByteArray(ZDOVars.s_items);
		if (byteArray == null)
		{
			return true;
		}
		ZPackage pkg = new ZPackage(byteArray);
		m_loading = true;
		m_inventory.Load(pkg);
		m_loading = false;
		UpdateRows();
		return true;
	}

	public float GetHoverOffset()
	{
		return m_hoverOffset;
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.2.0.10050' (yours is '9.1.0.7988')
