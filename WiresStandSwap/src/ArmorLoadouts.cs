using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresStandSwap
{
    // A loadout is one transaction, never N independent single-slot swaps.
    // Empty display slots are untouched. Every occupied equipment slot
    // participates or the operation is cancelled without changing anything.
    internal static class ArmorLoadouts
    {
        private static readonly HashSet<int> Pending = new HashSet<int>();
        private static readonly MethodInfo QueueEquip =
            AccessTools.Method(typeof(Player), "QueueEquipAction");
        private static readonly FieldInfo ActionQueue =
            AccessTools.Field(typeof(Player), "m_actionQueue");

        private static readonly FieldInfo InventoryItems =
            AccessTools.Field(typeof(Inventory), "m_inventory");
        private static readonly MethodInfo InventoryChanged =
            AccessTools.Method(typeof(Inventory), "Changed");
        private static readonly MethodInfo SetupEquipment =
            AccessTools.Method(typeof(Humanoid), "SetupEquipment");

        private static int _equipmentBatchDepth;

        internal static bool SuppressEquipmentRefresh =>
            _equipmentBatchDepth > 0;

        internal static bool ShouldSuppressEffect(EffectList effects)
        {
            // During the tiny synchronous equipment batch, vanilla
            // would spawn an equip/unequip effect for every item.
            // One loadout swap effect is emitted after the batch.
            return SuppressEquipmentRefresh;
        }

        private sealed class Entry
        {
            internal int Index, Hash, Variant;
            internal byte[] Data;
            internal ItemDrop.ItemData Displayed;
            internal ItemDrop.ItemData Outgoing;
            internal ItemDrop.ItemData StandCopy;
            internal Vector2i Destination;
            internal bool WasEquipped, Removed, Added, Queued;
        }

        private sealed class Transaction
        {
            internal Player Player;
            internal ArmorStand Stand;
            internal ZNetView View;
            internal readonly List<Entry> Slots = new List<Entry>();
            // Stand slots holding gear the settings leave alone.
            internal readonly HashSet<int> Skipped = new HashSet<int>();
        }

        internal static bool CanAttach(ArmorStand stand, int index,
            ItemDrop.ItemData item)
        {
            if (stand == null || index < 0 ||
                index >= stand.m_slots.Count || item == null ||
                item.m_dropPrefab == null)
                return false;
            var slot = stand.m_slots[index];
            var attachType = item.m_shared.m_attachOverride ==
                ItemDrop.ItemData.ItemType.None
                ? item.m_shared.m_itemType
                : item.m_shared.m_attachOverride;
            if (slot.m_supportedTypes.Count > 0 &&
                !slot.m_supportedTypes.Contains(attachType))
                return false;

            if (item.m_shared.m_itemType ==
                    ItemDrop.ItemData.ItemType.Chest ||
                item.m_shared.m_itemType ==
                    ItemDrop.ItemData.ItemType.Legs)
                return true;

            Transform prefab = item.m_dropPrefab.transform;
            return prefab.Find("attach") != null ||
                   prefab.Find("attach_skin") != null;
        }

        private static bool IsBody(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return true;
                default:
                    return false;
            }
        }

        private static bool FitsDisplayed(ArmorStand stand, int index,
            ItemDrop.ItemData displayed, ItemDrop.ItemData candidate)
        {
            if (candidate == null || !CanAttach(stand,index,candidate))
                return false;
            if (IsBody(displayed.m_shared.m_itemType))
                return displayed.m_shared.m_itemType ==
                       candidate.m_shared.m_itemType;
            string family = UsedItems.Family(displayed);
            return family != null && family == UsedItems.Family(candidate);
        }

        private static List<ItemDrop.ItemData> RawItems(
            Inventory inventory)
        {
            return inventory == null || InventoryItems == null
                ? null
                : InventoryItems.GetValue(inventory)
                    as List<ItemDrop.ItemData>;
        }

        private static void NotifyInventoryChanged(
            Inventory inventory)
        {
            if (inventory == null || InventoryChanged == null)
                throw new InvalidOperationException(
                    "Inventory refresh unavailable.");

            InventoryChanged.Invoke(
                inventory,
                new object[] { false, false });
        }

        private static void BeginEquipmentBatch(Transaction tx)
        {
            _equipmentBatchDepth++;
        }

        private static void EndEquipmentBatch(Player player)
        {
            if (_equipmentBatchDepth <= 0)
                return;

            _equipmentBatchDepth--;
            if (_equipmentBatchDepth > 0)
                return;

            if (player != null && SetupEquipment != null)
                SetupEquipment.Invoke(player, null);
        }

        private static bool ValidateSnapshot(Transaction tx)
        {
            Player player = tx.Player;
            ZNetView view = tx.View;
            if (player == null || view == null ||
                !view.IsValid() || ActionsBusy(player))
                return false;

            ZDO zdo = view.GetZDO();
            if (zdo == null)
                return false;

            int occupied = 0;
            for (int i = 0; i < tx.Stand.m_slots.Count; i++)
            {
                if (tx.Skipped.Contains(i))
                    continue;
                if (zdo.GetInt(
                        (i + "_item").GetStableHashCode()) != 0)
                    occupied++;
            }

            if (occupied != tx.Slots.Count)
                return false;

            foreach (Entry entry in tx.Slots)
            {
                int hash = zdo.GetInt(
                    (entry.Index + "_item").GetStableHashCode());
                int variant = zdo.GetInt(
                    (entry.Index + "_variant").GetStableHashCode());
                byte[] data = zdo.GetByteArray(
                    (entry.Index + "_itemData").GetStableHashCode());

                if (hash != entry.Hash ||
                    variant != entry.Variant ||
                    !StandSwaps.EqualBytes(data, entry.Data))
                    return false;
            }

            return true;
        }

        private static bool ActionsBusy(Player player)
        {
            if (ActionQueue == null)
                return true; // Cannot safely ignore the game's queue.
            var queue = ActionQueue.GetValue(player) as ICollection;
            return queue == null || queue.Count > 0;
        }

        private static void Message(Player player, string text)
        {
            player?.Message(MessageHud.MessageType.Center,text);
        }

        internal static bool TryStart(ArmorStand stand, Player player)
        {
            ZNetView view = stand == null ? null :
                (stand.m_netViewOverride ? stand.m_netViewOverride :
                 stand.GetComponent<ZNetView>());
            if (view == null || !view.IsValid() ||
                player == null || Plugin.Instance == null ||
                player.InAttack() || player.InDodge() ||
                player.IsStaggering() || player.IsDead() ||
                (player.IsSwimming() && !player.IsOnGround()) ||
                !PrivateArea.CheckAccess(view.transform.position,
                    0f,flash:false))
                return false;
            int id = view.GetInstanceID();
            if (Pending.Contains(id))
                return false;

            var tx = new Transaction {
                Player=player, Stand=stand, View=view
            };
            if (!Prepare(tx,true))
                return false;
            Pending.Add(id);
            Plugin.Instance.StartCoroutine(WaitAndCommit(tx,id));
            return true;
        }

        private static bool Prepare(Transaction tx, bool first)
        {
            Player player = tx.Player;
            if (player == null || ActionsBusy(player))
            {
                Message(player,"Finish changing equipment first");
                return false;
            }
            Inventory inv = player.GetInventory();
            ZDO zdo = tx.View.GetZDO();
            if (inv == null || zdo == null)
                return false;
            var usedItems = new HashSet<ItemDrop.ItemData>();
            var occupiedDestinations = new HashSet<Vector2i>();
            var entries = new List<Entry>();
            // Reserve one unique outgoing item for every stand slot.
            for (int i=0;i<tx.Stand.m_slots.Count;i++)
            {
                int key = (i+"_item").GetStableHashCode();
                int hash = zdo.GetInt(key);
                if (hash == 0)
                    continue;

                if (!StandSwaps.IsEquipmentItem(hash))
                {
                    Message(player,"Loadout contains unsupported equipment");
                    return false;
                }
                ItemDrop.ItemData displayed =
                    StandSwaps.ReadDisplayed(zdo,hash,i);
                if (displayed != null &&
                    !InputAndEquipment.Allowed(displayed.m_shared.m_itemType))
                {
                    if (first)
                        tx.Skipped.Add(i);
                    else if (!tx.Skipped.Contains(i))
                        return false;
                    continue;
                }
                if (displayed == null || !displayed.IsEquipable() ||
                    displayed.m_stack != 1 ||
                    (displayed.m_shared.m_useDurability &&
                     displayed.m_durability <= 0f) ||
                    (displayed.m_shared.m_dlc.Length > 0 &&
                     !DLCMan.instance.IsDLCInstalled(
                         displayed.m_shared.m_dlc)) ||
                    (Game.m_worldLevel > 0 &&
                     displayed.m_worldLevel < Game.m_worldLevel &&
                     (displayed.m_shared.m_itemType ==
                          ItemDrop.ItemData.ItemType.Utility ||
                      displayed.m_shared.m_itemType ==
                          ItemDrop.ItemData.ItemType.Trinket)))
                {
                    Message(player,"Cannot equip part of this loadout");
                    return false;
                }

                Func<ItemDrop.ItemData,bool> acceptable = candidate =>
                    candidate != null &&
                    !usedItems.Contains(candidate) &&
                    !player.IsEquipActionQueued(candidate) &&
                    inv.ContainsItem(candidate) &&
                    candidate.m_stack == 1 &&
                    FitsDisplayed(tx.Stand,i,displayed,candidate);

                // For complete outfits prefer what is actually worn.
                // For weapons/tools fall back to unified usage history,
                // then compatible equipment in inventory.
                ItemDrop.ItemData outgoing = null;
                if (!IsBody(displayed.m_shared.m_itemType))
                {
                    foreach(var candidate in inv.GetAllItems())
                        if (player.IsItemEquiped(candidate) &&
                            UsedItems.Family(candidate) ==
                                UsedItems.Family(displayed) &&
                            acceptable(candidate))
                        {
                            outgoing=candidate;
                            break;
                        }
                }
                if (outgoing == null)
                    outgoing=UsedItems.Find(player,displayed,acceptable);
                if (outgoing == null &&
                    UsedItems.Find(player,displayed,
                        item => !usedItems.Contains(item)) != null)
                {
                    Message(player,"Stand cannot hold matching equipment");
                    return false;
                }
                var entry = new Entry {
                    Index=i,Hash=hash,Displayed=displayed,
                    Outgoing=outgoing,
                    Variant=zdo.GetInt(
                        (i+"_variant").GetStableHashCode()),
                    Data=zdo.GetByteArray(
                        (i+"_itemData").GetStableHashCode())
                };
                if(entry.Data != null)
                    entry.Data=(byte[])entry.Data.Clone();
                if (outgoing != null)
                {
                    if (!usedItems.Add(outgoing))
                        return false;
                    entry.Destination=outgoing.m_gridPos;
                    entry.WasEquipped=player.IsItemEquiped(outgoing);
                    occupiedDestinations.Add(entry.Destination);
                }
                entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                Message(player,first && tx.Skipped.Count > 0
                    ? "Nothing on the armor stand to swap with your settings"
                    : "Nothing on the armor stand");
                return false;
            }
            // Give incoming items without outgoing counterparts new cells.
            // Reserve each position only once before touching inventory.
            foreach (Entry e in entries)
            {
                if (e.Outgoing != null)
                    continue;
                Vector2i destination;
                if (!FindFreeCell(player,e.Displayed,occupiedDestinations,
                    out destination))
                {
                    Message(player,"Not enough free equipment space");
                    return false;
                }
                e.Destination=destination;
                occupiedDestinations.Add(destination);
            }
            if (first)
            {
                tx.Slots.AddRange(entries);
                return true;
            }
            // Network ownership may take time. Require the EXACT
            // same stand contents, outgoing items, and destinations.
            if (tx.Slots.Count != entries.Count)
                return false;
            for (int i=0;i<entries.Count;i++)
            {
                Entry old=tx.Slots[i], now=entries[i];
                if (old.Index != now.Index ||
                    old.Hash != now.Hash ||
                    old.Variant != now.Variant ||
                    old.Outgoing != now.Outgoing ||
                    old.Destination != now.Destination ||
                    !StandSwaps.EqualBytes(old.Data,now.Data))
                    return false;
            }
            return true;
        }

        private static bool FindFreeCell(Player player,
            ItemDrop.ItemData item, HashSet<Vector2i> reserved,
            out Vector2i destination)
        {
            destination=new Vector2i(-1,-1);
            Inventory inv=player.GetInventory();
            // Dedicated EAQS cells are preferred for armor.
            if (IsBody(item.m_shared.m_itemType))
            {
                Type t=AccessTools.TypeByName(
                    "EquipmentAndQuickSlots.Slots");
                MethodInfo method=t==null ? null :
                    AccessTools.Method(t,"GetEquipmentSlots");
                if(method != null)
                {
                    try
                    {
                        var slots=method.Invoke(null,
                            new object[]{true}) as IEnumerable;
                        if(slots==null)
                            return false;
                        foreach(object slot in slots)
                        {
                            Type st=slot.GetType();
                            var fits=AccessTools.Method(st,"ItemFits");
                            var allowed=AccessTools.Method(
                                t,"WouldFitEquipmentSlot");
                            var grid=AccessTools.Property(
                                st,"GridPosition");
                            if (fits == null || grid == null ||
                                !(bool)fits.Invoke(slot,new object[]{item}) ||
                                (allowed != null &&
                                 !(bool)allowed.Invoke(null,
                                     new object[]{slot,item})))
                                continue;
                            var pos=(Vector2i)grid.GetValue(slot,null);
                            if(inv.GetItemAt(pos.x,pos.y)==null &&
                               !reserved.Contains(pos))
                            {
                                destination=pos;
                                return true;
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Plugin.Log.LogWarning(
                            "EAQS loadout slot lookup: "+ex);
                        return false;
                    }
                }
            }

            int rows=inv.GetHeight();
            Type slotsType=AccessTools.TypeByName(
                "EquipmentAndQuickSlots.Slots");
            if(slotsType!=null)
            {
                var prop=AccessTools.Property(slotsType,"VisibleRows");
                if(prop==null)
                    return false;
                rows=(int)prop.GetValue(null,null);
            }
            for(int y=0;y<rows;y++)
                for(int x=0;x<inv.GetWidth();x++)
                {
                    var pos=new Vector2i(x,y);
                    if(inv.GetItemAt(x,y)==null &&
                       !reserved.Contains(pos))
                    {
                        destination=pos;
                        return true;
                    }
                }
            return false;
        }

        private static IEnumerator WaitAndCommit(Transaction tx,int id)
        {
            try
            {
                ZNetView view=tx.View;
                if(view==null || !view.IsValid())
                    yield break;
                if(!view.IsOwner())
                    view.InvokeRPC("RPC_RequestOwn");
                float until=Time.time+1.8f;
                while(view!=null && view.IsValid() &&
                      !view.IsOwner() && Time.time<until)
                    yield return new WaitForSeconds(0.08f);
                if(view==null || !view.IsValid() || !view.IsOwner())
                {
                    Message(tx.Player,"Stand busy; try again");
                    yield break;
                }

                if(Commit(tx))
                    yield return FlushVisuals(tx);
            }
            finally
            {
                Pending.Remove(id);
            }
        }

        private static IEnumerator FlushVisuals(Transaction tx)
        {
            ZNetView view=tx.View;
            foreach(Entry e in tx.Slots)
            {
                if(view==null || !view.IsValid())
                    yield break;

                int hash=e.StandCopy==null ? 0 :
                    e.StandCopy.m_dropPrefab.name.GetStableHashCode();
                int variant=e.StandCopy==null ? 0 :
                    e.StandCopy.m_variant;

                // Same prefab+variant already looks correct. Only
                // metadata changed, so rebuilding the visual is wasted.
                if(hash==e.Hash && variant==e.Variant)
                    continue;

                // ArmorStand.SetVisualItem rebuilds supports and scans
                // child cloth components. Pace changed slots across
                // frames instead of doing every rebuild in one frame.
                yield return null;

                view.InvokeRPC(
                    ZNetView.Everybody,
                    "RPC_SetVisualItem",
                    e.Index,
                    hash,
                    variant);
            }
        }

        private static bool Commit(Transaction tx)
        {
            Player player=tx.Player;
            ZNetView view=tx.View;
            if(player==null || player!=Player.m_localPlayer ||
               view==null || !view.IsValid() || !view.IsOwner() ||
               player.InAttack() || player.InDodge() ||
               player.IsStaggering() || player.IsDead() ||
               (player.IsSwimming() && !player.IsOnGround()) ||
               Vector3.Distance(player.transform.position,
                   view.transform.position)>5f ||
               !PrivateArea.CheckAccess(view.transform.position,
                   0f,flash:false))
                return false;

            if(!ValidateSnapshot(tx))
            {
                Message(player,"Loadout changed; try again");
                return false;
            }

            Inventory inv=player.GetInventory();
            ZDO zdo=view.GetZDO();
            List<ItemDrop.ItemData> raw=RawItems(inv);
            if(inv==null || zdo==null || raw==null)
                return false;

            var originalWorn=new List<ItemDrop.ItemData>();
            foreach(var item in inv.GetAllItems())
                if(player.IsItemEquiped(item))
                    originalWorn.Add(item);

            foreach(Entry e in tx.Slots)
            {
                if(e.Outgoing!=null &&
                   (inv.GetItemAt(e.Destination.x,
                      e.Destination.y)!=e.Outgoing ||
                    e.WasEquipped!=player.IsItemEquiped(e.Outgoing)))
                {
                    Message(player,"Equipped items changed; try again");
                    return false;
                }
                if(e.Outgoing==null && inv.GetItemAt(
                    e.Destination.x,e.Destination.y)!=null)
                {
                    Message(player,"Inventory changed; try again");
                    return false;
                }
            }

            bool started=false;
            bool equipmentBatch=false;
            try
            {
                started=true;

                foreach(Entry e in tx.Slots)
                {
                    if(e.Outgoing==null)
                        continue;

                    e.StandCopy=e.Outgoing.Clone();
                    e.StandCopy.m_stack=1;
                    e.StandCopy.m_equipped=false;
                    StandSwaps.StripEquipmentCellTags(e.StandCopy);
                }

                // Unequipping a full set normally calls
                // Humanoid.SetupEquipment once per piece. Suppress
                // those intermediate rebuilds and do one refresh after
                // every inventory/equipment field is in its final state.
                BeginEquipmentBatch(tx);
                equipmentBatch=true;

                foreach(Entry e in tx.Slots)
                {
                    if(e.WasEquipped)
                        player.UnequipItem(
                            e.Outgoing,
                            triggerEquipEffects:false);
                }

                // Inventory.RemoveOneItem/AddItem each fires
                // Inventory.Changed, which rebuilds known recipes and
                // available pieces. Mutate the already-validated list
                // directly, then notify exactly once.
                foreach(Entry e in tx.Slots)
                {
                    if(e.Outgoing==null)
                        continue;

                    if(!raw.Remove(e.Outgoing))
                        throw new InvalidOperationException(
                            "Could not remove outgoing item.");

                    e.Removed=true;
                }

                foreach(Entry e in tx.Slots)
                {
                    e.Displayed.m_equipped=false;
                    e.Displayed.m_stack=1;
                    e.Displayed.m_gridPos=e.Destination;
                    StandSwaps.StripEquipmentCellTags(e.Displayed);

                    if(raw.Contains(e.Displayed) ||
                       inv.GetItemAt(
                           e.Destination.x,
                           e.Destination.y)!=null)
                    {
                        throw new InvalidOperationException(
                            "Loadout destination became occupied.");
                    }

                    raw.Add(e.Displayed);
                    e.Added=true;

                    if(e.Displayed.m_shared.m_equipDuration>0f)
                    {
                        if(QueueEquip==null)
                            throw new InvalidOperationException(
                                "Vanilla equip queue unavailable");

                        QueueEquip.Invoke(
                            player,
                            new object[]{e.Displayed});
                        e.Queued=true;
                    }
                    else if(!player.EquipItem(
                                e.Displayed,
                                triggerEquipEffects:false))
                    {
                        throw new InvalidOperationException(
                            "Could not equip loadout equipment.");
                    }
                }

                equipmentBatch=false;
                EndEquipmentBatch(player);

                NotifyInventoryChanged(inv);

                foreach(Entry e in tx.Slots)
                {
                    int dataKey=(e.Index+"_itemData")
                        .GetStableHashCode();
                    int itemKey=(e.Index+"_item")
                        .GetStableHashCode();
                    int variantKey=(e.Index+"_variant")
                        .GetStableHashCode();

                    if(e.StandCopy!=null)
                    {
                        ItemDrop.SaveToZDO(
                            e.StandCopy,
                            zdo,
                            e.Index);
                        zdo.Set(
                            itemKey,
                            e.StandCopy.m_dropPrefab.name
                                .GetStableHashCode());
                        zdo.Set(
                            variantKey,
                            e.StandCopy.m_variant);
                    }
                    else
                    {
                        zdo.RemoveByteArray(dataKey);
                        zdo.Set(itemKey,0);
                        zdo.Set(variantKey,0);
                    }
                }

                // One feedback effect for the whole loadout.
                tx.Stand.m_effects.Create(
                    tx.Stand.transform.position,
                    Quaternion.identity);
                Game.instance.IncrementPlayerStat(
                    PlayerStatType.ArmorStandUses);

                Plugin.Log.LogInfo(
                    "Shift+E full armor-stand loadout: "+
                    tx.Slots.Count+" pieces exchanged.");

                return true;
            }
            catch(Exception error)
            {
                Plugin.Log.LogError(
                    "Full loadout swap aborted: "+error);

                if(started)
                {
                    try
                    {
                        if(!equipmentBatch)
                        {
                            BeginEquipmentBatch(tx);
                            equipmentBatch=true;
                        }

                        foreach(Entry e in tx.Slots)
                        {
                            if(e.Queued)
                                player.RemoveEquipAction(e.Displayed);

                            if(e.Added && raw.Contains(e.Displayed))
                            {
                                if(player.IsItemEquiped(e.Displayed))
                                    player.UnequipItem(
                                        e.Displayed,
                                        triggerEquipEffects:false);

                                raw.Remove(e.Displayed);
                            }
                        }

                        foreach(Entry e in tx.Slots)
                        {
                            if(!e.Removed ||
                               raw.Contains(e.Outgoing))
                                continue;

                            e.Outgoing.m_gridPos=e.Destination;
                            raw.Add(e.Outgoing);
                        }

                        foreach(var item in inv.GetAllItems())
                        {
                            if(player.IsItemEquiped(item) &&
                               !originalWorn.Contains(item))
                            {
                                player.UnequipItem(
                                    item,
                                    triggerEquipEffects:false);
                            }
                        }

                        foreach(var item in originalWorn)
                        {
                            if(raw.Contains(item) &&
                               !player.IsItemEquiped(item))
                            {
                                player.EquipItem(
                                    item,
                                    triggerEquipEffects:false);
                            }
                        }

                        if(equipmentBatch)
                        {
                            equipmentBatch=false;
                            EndEquipmentBatch(player);
                        }

                        NotifyInventoryChanged(inv);

                        foreach(Entry e in tx.Slots)
                        {
                            int dataKey=(e.Index+"_itemData")
                                .GetStableHashCode();
                            int key=(e.Index+"_item")
                                .GetStableHashCode();

                            if(e.Data!=null)
                                zdo.Set(dataKey,e.Data);
                            else
                                zdo.RemoveByteArray(dataKey);

                            zdo.Set(key,e.Hash);
                            zdo.Set(
                                (e.Index+"_variant")
                                    .GetStableHashCode(),
                                e.Variant);
                        }

                        foreach(Entry e in tx.Slots)
                        {
                            view.InvokeRPC(
                                ZNetView.Everybody,
                                "RPC_SetVisualItem",
                                e.Index,
                                e.Hash,
                                e.Variant);
                        }
                    }
                    catch(Exception rollback)
                    {
                        if(equipmentBatch)
                        {
                            try
                            {
                                equipmentBatch=false;
                                EndEquipmentBatch(player);
                            }
                            catch(Exception refreshError)
                            {
                                Plugin.Log.LogError(
                                    "Equipment refresh failed: "+
                                    refreshError);
                            }
                            equipmentBatch=false;
                        }

                        Plugin.Log.LogError(
                            "CRITICAL loadout rollback failed: "+
                            rollback);
                        Message(
                            player,
                            "Loadout rollback error: check log");
                        return false;
                    }
                }

                if(equipmentBatch)
                {
                    try
                    {
                        equipmentBatch=false;
                        EndEquipmentBatch(player);
                    }
                    catch(Exception refreshError)
                    {
                        Plugin.Log.LogError(
                            "Equipment refresh failed: "+
                            refreshError);
                    }
                }

                Message(
                    player,
                    "Loadout cancelled; nothing changed");
                return false;
            }
        }
        internal static void Clear()
        {
            Pending.Clear();
            _equipmentBatchDepth=0;
        }
    }
}
