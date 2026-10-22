using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresStandSwap
{
    internal static class StandSwaps
    {
        private static readonly HashSet<int> Pending =
            new HashSet<int>();

        private static readonly MethodInfo ItemCanAttach =
            AccessTools.Method(typeof(ItemStand),
                "CanAttach");

        private static readonly MethodInfo ArmorCanAttach =
            AccessTools.Method(typeof(ArmorStand),
                "CanAttach");

        private static readonly MethodInfo QueueEquip =
            AccessTools.Method(typeof(Player), "QueueEquipAction");

        private static readonly MethodInfo UpdateArmorSupports =
            AccessTools.Method(typeof(ArmorStand), "UpdateSupports");

        private static readonly FieldInfo ArmorCloths =
            AccessTools.Field(typeof(ArmorStand), "m_cloths");

        private sealed class Request
        {
            internal Player Player;
            internal ZNetView View;
            internal ItemStand ItemStand;
            internal ArmorStand ArmorStand;
            internal int Index;
            internal int ExpectedHash;
            internal byte[] ExpectedData;
            internal int OldQuality;
            internal int OldVariant;
            internal int OldSlotVariant;
            internal ItemDrop.ItemData ExpectedOutgoing;
            internal bool WillReplace;
            internal ItemDrop.ItemData.ItemType
                DisplayedType;
            internal string DisplayedFamily;
            internal bool UseEmptyEquipmentCell;
            internal Vector2i EmptyEquipmentCell;
        }

        internal static bool IsEquipmentItem(int prefabHash)
        {
            if (prefabHash == 0 || ObjectDB.instance == null)
                return false;

            GameObject prefab =
                ObjectDB.instance.GetItemPrefab(prefabHash);

            if (prefab == null)
                return false;

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            return drop != null && IsExchangeable(
                drop.m_itemData.m_shared.m_itemType);
        }

        private static bool IsExchangeable(
            ItemDrop.ItemData.ItemType t)
        {
            if (UsedItems.IsWeaponOrTool(t))
                return true;

            switch (t)
            {
                case ItemDrop.ItemData.ItemType.Shield:
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

        internal static bool TryItemStand(
            ItemStand stand, Player player)
        {
            if (stand == null || !stand.m_canBeRemoved ||
                stand.m_guardianPower != null)
                return false;

            ZNetView view = stand.m_netViewOverride
                ? stand.m_netViewOverride
                : stand.GetComponent<ZNetView>();

            return TryStart(new Request
            {
                Player = player,
                ItemStand = stand,
                View = view,
                Index = -1
            });
        }

        internal static bool TryArmorStand(
            ArmorStand stand, int index, Player player)
        {
            if (stand == null || index < 0 ||
                index >= stand.m_slots.Count)
                return false;

            ZNetView view = stand.m_netViewOverride
                ? stand.m_netViewOverride
                : stand.GetComponent<ZNetView>();

            return TryStart(new Request
            {
                Player = player,
                ArmorStand = stand,
                View = view,
                Index = index
            });
        }

        internal static string DescribeAction(
            int displayedHash,
            ItemStand itemStand = null,
            ArmorStand armorStand = null,
            int index = -1)
        {
            if (ObjectDB.instance == null)
                return "Swap / equip";

            GameObject prefab =
                ObjectDB.instance.GetItemPrefab(
                    displayedHash);
            ItemDrop drop = prefab != null
                ? prefab.GetComponent<ItemDrop>()
                : null;
            if (drop == null)
                return "Swap / equip";

            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            string name = Localization.instance != null
                ? Localization.instance.Localize(
                    item.m_shared.m_name)
                : item.m_shared.m_name;

            Player player = Player.m_localPlayer;
            Request request = new Request
            {
                Player = player,
                ItemStand = itemStand,
                ArmorStand = armorStand,
                Index = index,
                DisplayedType = item.m_shared.m_itemType,
                DisplayedFamily = UsedItems.Family(item)
            };
            bool hasMatching =
                player != null &&
                UsedItems.Find(player, item,
                    candidate => IsValidReplacement(
                        request, candidate)) != null;

            return (hasMatching ? "Swap " : "Equip ") +
                name;
        }

        private static int ItemKey(int index)
        {
            return index < 0
                ? ZDOVars.s_item
                : (index + "_item").GetStableHashCode();
        }

        private static int ItemDataKey(int index)
        {
            return index < 0
                ? ZDOVars.s_itemData
                : (index + "_itemData").GetStableHashCode();
        }

        internal static bool EqualBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null)
                return a == b;

            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;

            return true;
        }

        internal static void StripEquipmentCellTags(
            ItemDrop.ItemData item)
        {
            // Player/slot markers are EAQS bookkeeping, not
            // intrinsic item properties. A piece displayed on
            // someone else's stand must never carry them over.
            if (item?.m_customData == null)
                return;
            item.m_customData.Remove("eaqs_player");
            item.m_customData.Remove("eaqs_slot");
            item.m_customData.Remove("eaqs_parked");
        }

        private static void Notify(
            Player player, string message)
        {
            if (player != null)
                player.Message(
                    MessageHud.MessageType.Center, message);
        }

        private static bool TryStart(Request request)
        {
            Player player = request.Player;
            ZNetView view = request.View;
            if (player == null || view == null ||
                !view.IsValid() ||
                !PrivateArea.CheckAccess(
                    view.transform.position,
                    0f, flash: false))
                return false;

            if (player.InAttack() || player.InDodge() ||
                player.IsStaggering() || player.IsDead())
                return false;

            ZDO zdo = view.GetZDO();
            int hash = zdo.GetInt(ItemKey(request.Index));
            if (!IsEquipmentItem(hash))
                return false;

            ItemDrop.ItemData displayed =
                ReadDisplayed(zdo, hash, request.Index);
            if (displayed != null)
            {
                request.DisplayedType =
                    displayed.m_shared.m_itemType;
                request.DisplayedFamily =
                    UsedItems.Family(displayed);
            }

            // Start with the most recent matching equipment that
            // this exact stand can accept, then try the next
            // compatible family member instead of cancelling
            // because the first candidate cannot be attached.
            ItemDrop.ItemData outgoing =
                UsedItems.Find(player, displayed,
                    candidate => IsValidReplacement(
                        request, candidate));

            // If matching gear exists but the stand can't hold
            // any of it, cancel. Never silently turn a requested
            // swap into a one-way take in that case.
            if (outgoing == null &&
                UsedItems.Find(player, displayed) != null)
            {
                Notify(player,
                    "Stand cannot hold matching equipment");
                return false;
            }

            if (displayed == null ||
                !displayed.IsEquipable())
            {
                Notify(player, "Cannot equip displayed item");
                return false;
            }
            if (displayed.m_shared.m_useDurability &&
                displayed.m_durability <= 0f)
            {
                Notify(player, "Displayed item is broken");
                return false;
            }
            if (player.IsSwimming() &&
                !player.IsOnGround())
            {
                Notify(player,
                    "Cannot equip while swimming");
                return false;
            }

            // This is an exchange of the currently equipped item,
            // not "take the chestplate" when lookup/attachment fails.
            // Never silently leave someone's equipped gear behind.
            if (outgoing != null &&
                !IsValidReplacement(request, outgoing))
            {
                Notify(player,
                    "Stand cannot hold your current equipment");
                return false;
            }
            request.ExpectedOutgoing = outgoing;
            request.WillReplace = outgoing != null;
            if (outgoing == null)
            {
                bool hasMatchingCells;
                bool lookupFailed;
                request.UseEmptyEquipmentCell =
                    EquipmentSlotsCompat.FindFreeSlot(
                        player, displayed,
                        out request.EmptyEquipmentCell,
                        out hasMatchingCells,
                        out lookupFailed);

                if (lookupFailed)
                {
                    Notify(player,
                        "Equipment slots unavailable; try again");
                    return false;
                }

                if (!request.UseEmptyEquipmentCell &&
                    hasMatchingCells)
                {
                    Notify(player,
                        "Equipment slot occupied; clear it first");
                    return false;
                }

                if (!request.UseEmptyEquipmentCell &&
                    !player.GetInventory().CanAddItem(displayed))
                {
                    Notify(player,
                        "Inventory full; cannot take equipment");
                    return false;
                }
            }

            int id = view.GetInstanceID();
            if (Pending.Contains(id) ||
                Plugin.Instance == null)
                return false;

            byte[] data =
                zdo.GetByteArray(ItemDataKey(request.Index));
            // Older stands may have only the item prefab hash.
            // Vanilla LoadFromZDO uses prefab defaults when there is
            // no serialized item blob. Mirror that behavior here.
            request.ExpectedHash = hash;
            request.ExpectedData =
                data != null ? (byte[])data.Clone() : null;
            request.OldQuality = zdo.GetInt(
                ZDOVars.s_quality, 1);
            request.OldVariant = zdo.GetInt(
                ZDOVars.s_variant);
            request.OldSlotVariant = request.Index >= 0
                ? zdo.GetInt(
                    (request.Index + "_variant")
                        .GetStableHashCode())
                : 0;

            Plugin.Log.LogInfo(
                "Shift+E " +
                (request.WillReplace
                    ? "swap "
                    : "equip into " +
                      (request.UseEmptyEquipmentCell
                          ? "empty EAQS cell "
                          : "inventory ")) +
                displayed.m_shared.m_itemType +
                " standSlot=" + request.Index +
                " displayed=" + displayed.m_shared.m_name +
                ", outgoing=" +
                (outgoing != null
                    ? outgoing.m_shared.m_name +
                      " (" + outgoing.m_shared.m_itemType + ")"
                    : "<none>"));

            Pending.Add(id);
            Plugin.Instance.StartCoroutine(
                WaitForOwnership(request, id));

            return true;
        }

        internal static ItemDrop.ItemData ReadDisplayed(
            ZDO zdo, int hash, int index)
        {
            if (!IsEquipmentItem(hash))
                return null;

            GameObject prefab =
                ObjectDB.instance.GetItemPrefab(hash);
            ItemDrop drop = prefab != null
                ? prefab.GetComponent<ItemDrop>()
                : null;

            if (drop == null)
                return null;

            ItemDrop.ItemData item =
                drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;

            ItemDrop.LoadFromZDO(item, zdo, index);
            // Older stands keep visual variant/quality without an
            // item-data blob. Don't silently discard those values.
            if (zdo.GetByteArray(ItemDataKey(index)) == null)
            {
                item.m_variant = zdo.GetInt(
                    index < 0 ? ZDOVars.s_variant :
                    (index + "_variant").GetStableHashCode());
                if (index < 0)
                    item.m_quality = zdo.GetInt(
                        ZDOVars.s_quality, item.m_quality);
            }
            // An equipped flag packed into the stand must never
            // be interpreted as equipment already worn by us.
            item.m_equipped = false;
            return item;
        }

        private static bool IsValidReplacement(
            Request request, ItemDrop.ItemData item)
        {
            if (item == null ||
                item.m_stack != 1 ||
                item.m_dropPrefab == null ||
                !request.Player.GetInventory()
                    .ContainsItem(item))
                return false;

            ItemDrop.ItemData.ItemType type =
                item.m_shared.m_itemType;

            if (!IsExchangeable(type))
                return false;

            try
            {
                if (request.ItemStand != null)
                {
                    if (ItemCanAttach == null)
                        return false;

                    return (bool)ItemCanAttach.Invoke(
                        request.ItemStand,
                        new object[] { item });
                }

                if (request.ArmorStand != null)
                {
                    // Body armor must match its exact equipped
                    // slot. Weapons, shields, and tools use the
                    // SAME skill/kind family rules as item stands,
                    // even if the item's broad weapon type differs.
                    // Never match unrelated gear simply because
                    // a visual slot supports several types.
                    bool handEquipment =
                        request.DisplayedType ==
                            ItemDrop.ItemData.ItemType.Shield ||
                        UsedItems.IsWeaponOrTool(
                            request.DisplayedType);

                    if (handEquipment)
                    {
                        if (request.DisplayedFamily == null ||
                            UsedItems.Family(item) !=
                                request.DisplayedFamily)
                            return false;
                    }
                    else if (type != request.DisplayedType)
                        return false;
                    if (ArmorCanAttach == null)
                        return false;

                    ArmorStand.ArmorStandSlot slot =
                        request.ArmorStand.m_slots[
                            request.Index];

                    bool valid = (bool)ArmorCanAttach.Invoke(
                        request.ArmorStand,
                        new object[] { slot, item });

                    if (!valid)
                        return false;

                    // The vanilla armor stand also requires
                    // an attach or attach_skin except for
                    // legs and chest armor.
                    if (type ==
                            ItemDrop.ItemData.ItemType.Legs ||
                        type ==
                            ItemDrop.ItemData.ItemType.Chest)
                        return true;

                    Transform prefab =
                        item.m_dropPrefab.transform;
                    return prefab.Find("attach") != null ||
                           prefab.Find("attach_skin") != null;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static IEnumerator WaitForOwnership(
            Request request, int pendingId)
        {
            try
            {
                ZNetView view = request.View;
                if (view == null || !view.IsValid())
                    yield break;

                if (!view.IsOwner())
                    view.InvokeRPC("RPC_RequestOwn");

                float timeout = Time.time + 1.8f;
                while (view != null && view.IsValid() &&
                       !view.IsOwner() &&
                       Time.time < timeout)
                {
                    yield return new WaitForSeconds(0.08f);
                }

                if (view == null || !view.IsValid() ||
                    !view.IsOwner())
                {
                    Notify(request.Player,
                        "Stand busy; try again");
                    yield break;
                }

                Commit(request);
            }
            finally
            {
                Pending.Remove(pendingId);
            }
        }

        private static bool Commit(Request request)
        {
            Player player = request.Player;
            ZNetView view = request.View;

            if (player == null ||
                player != Player.m_localPlayer ||
                view == null || !view.IsValid() ||
                !view.IsOwner() ||
                player.InAttack() || player.InDodge() ||
                player.IsStaggering() || player.IsDead() ||
                Vector3.Distance(
                    player.transform.position,
                    view.transform.position) > 5f ||
                !PrivateArea.CheckAccess(
                    view.transform.position,
                    0f, flash: false))
                return false;

            ZDO zdo = view.GetZDO();
            int itemKey = ItemKey(request.Index);
            int dataKey = ItemDataKey(request.Index);

            if (zdo.GetInt(itemKey) !=
                    request.ExpectedHash ||
                !EqualBytes(
                    zdo.GetByteArray(dataKey),
                    request.ExpectedData))
            {
                Notify(player,
                    "Stand changed; try again");
                return false;
            }

            ItemDrop.ItemData displayed =
                ReadDisplayed(zdo, request.ExpectedHash,
                    request.Index);
            ItemDrop.ItemData outgoing =
                UsedItems.Find(player, displayed,
                    candidate => IsValidReplacement(
                        request, candidate));
            if (outgoing != request.ExpectedOutgoing)
            {
                // Ownership may take a moment. The user could have
                // switched weapons in that time: do not silently
                // exchange a different item than they intended.
                Notify(player,
                    "Equipment changed; try again");
                return false;
            }

            if (displayed == null ||
                displayed.m_stack != 1 ||
                !displayed.IsEquipable() ||
                displayed.m_shared.m_itemType !=
                    request.DisplayedType ||
                (request.WillReplace &&
                 !IsValidReplacement(request, outgoing)))
            {
                Notify(player, "Cannot swap this equipment");
                return false;
            }

            if (!request.WillReplace)
            {
                Inventory invBefore = player.GetInventory();
                if (request.UseEmptyEquipmentCell)
                {
                    Vector2i cell = request.EmptyEquipmentCell;
                    if (invBefore.GetItemAt(
                            cell.x, cell.y) != null)
                    {
                        Notify(player,
                            "Equipment slot changed; try again");
                        return false;
                    }
                }
                else if (!invBefore.CanAddItem(displayed))
                {
                    Notify(player,
                        "Inventory full; cannot take equipment");
                    return false;
                }
            }

            if (player.IsSwimming() &&
                !player.IsOnGround())
            {
                Notify(player,
                    "Cannot equip while swimming");
                return false;
            }

            if (displayed.m_shared.m_useDurability &&
                displayed.m_durability <= 0f)
            {
                Notify(player, "Displayed item is broken");
                return false;
            }
            if (displayed.m_shared.m_equipDuration > 0f &&
                QueueEquip == null)
            {
                Notify(player, "Cannot start equipping");
                return false;
            }

            // Do not remove an item that Valheim already has in
            // its equip/unequip action queue. That would leave a
            // stale action pointing at an item no longer owned.
            if (request.WillReplace &&
                player.IsEquipActionQueued(outgoing))
            {
                Notify(player,
                    "Finish changing equipment first");
                return false;
            }

            Inventory inv = player.GetInventory();
            Vector2i originalPos = request.WillReplace
                ? outgoing.m_gridPos : Vector2i.zero;
            bool wasEquipped = request.WillReplace &&
                player.IsItemEquiped(outgoing);

            // Vanilla equipment saves include durability,
            // quality, variants, crafting, world level and
            // custom item data. Preserve everything.
            ItemDrop.ItemData forStand =
                request.WillReplace ? outgoing.Clone() : null;
            if (forStand != null)
            {
                forStand.m_equipped = false;
                forStand.m_stack = 1;
                StripEquipmentCellTags(forStand);
            }
            displayed.m_stack = 1;
            displayed.m_equipped = false;
            StripEquipmentCellTags(displayed);

            bool didRemove = false;
            bool didInsert = false;
            bool zdoWritten = false;
            bool queuedEquip = false;

            try
            {
                if (wasEquipped)
                    player.UnequipItem(outgoing,
                        triggerEquipEffects: false);

                if (request.WillReplace)
                {
                    if (!inv.RemoveOneItem(outgoing))
                        throw new InvalidOperationException(
                            "Could not remove outgoing item.");
                    didRemove = true;
                }

                // EAQS recognizes an occupied equipment cell when
                // the item is equipped OR waiting to be equipped.
                // Queue before insertion, otherwise its validation
                // can move the item out of the equipment cell.
                if (displayed.m_shared.m_equipDuration > 0f)
                {
                    QueueEquip.Invoke(
                        player, new object[] { displayed });
                    queuedEquip = true;
                }

                bool putInExactCell =
                    request.WillReplace ||
                    request.UseEmptyEquipmentCell;
                Vector2i destination =
                    request.WillReplace
                        ? originalPos
                        : request.EmptyEquipmentCell;
                bool added = putInExactCell
                    ? inv.AddItem(displayed, destination)
                    : inv.AddItem(displayed);
                if (!added)
                    throw new InvalidOperationException(
                        "No inventory room for incoming item.");
                didInsert = true;

                // Never silently leave a replacement in the
                // backpack when the user asked for an actual
                // equipment-slot swap.
                if (putInExactCell &&
                    displayed.m_gridPos != destination)
                    throw new InvalidOperationException(
                        "Item did not occupy expected slot.");

                // Transfer is immediate; equip uses vanilla's
                // ordinary animation/wait (already queued).
                zdoWritten = true;
                if (forStand != null)
                {
                    ItemDrop.SaveToZDO(
                        forStand, zdo, request.Index);
                    if (request.Index < 0)
                    {
                        zdo.Set(ZDOVars.s_quality,
                            forStand.m_quality);
                        zdo.Set(ZDOVars.s_variant,
                            forStand.m_variant);
                    }
                    zdo.Set(itemKey, forStand.m_dropPrefab
                        .name.GetStableHashCode());
                }
                else
                {
                    // Nothing worn / last used: take the displayed
                    // item into inventory, leaving this slot empty.
                    zdo.RemoveByteArray(dataKey);
                    zdo.Set(itemKey, 0);
                    // Global keys belong to single item stands,
                    // not to individual armor stand slots.
                    if (request.Index < 0)
                    {
                        zdo.Set(ZDOVars.s_quality, 0);
                        zdo.Set(ZDOVars.s_variant, 0);
                    }
                }

                if (request.Index >= 0)
                {
                    zdo.Set(
                        (request.Index + "_variant")
                            .GetStableHashCode(),
                        forStand != null ? forStand.m_variant : 0);
                }

                if (!queuedEquip &&
                    !player.EquipItem(displayed,
                        triggerEquipEffects: false))
                {
                    throw new InvalidOperationException(
                        "Could not equip displayed item.");
                }

                // The vanilla RPC refreshes stand visuals; item
                // data has already been exchanged in the ZDO.
                int newHash = forStand != null
                    ? forStand.m_dropPrefab.name.GetStableHashCode()
                    : 0;
                int newVariant = forStand != null
                    ? forStand.m_variant : 0;
                int newQuality = forStand != null
                    ? forStand.m_quality : 0;
                bool sameVisual = newHash != 0 &&
                    request.ExpectedHash == newHash &&
                    displayed.m_variant == newVariant;
                if (request.ItemStand != null)
                {
                    int orientation =
                        zdo.GetInt(ZDOVars.s_type);
                    // Vanilla skips visual updates when prefab and
                    // variant are unchanged, even if quality differs.
                    // Reset only those cached visuals before updating.
                    if (sameVisual)
                        view.InvokeRPC(ZNetView.Everybody,
                            "SetVisualItem", 0, 0, 0, orientation);
                    view.InvokeRPC(ZNetView.Everybody,
                        "SetVisualItem",
                        newHash,
                        newVariant,
                        newQuality,
                        orientation);
                    request.ItemStand.m_effects.Create(
                        request.ItemStand.transform.position,
                        Quaternion.identity);
                    Game.instance.IncrementPlayerStat(
                        PlayerStatType.ItemStandUses);
                }
                else
                {
                    if (sameVisual)
                        view.InvokeRPC(ZNetView.Everybody,
                            "RPC_SetVisualItem",
                            request.Index, 0, 0);
                    view.InvokeRPC(ZNetView.Everybody,
                        "RPC_SetVisualItem",
                        request.Index,
                        newHash,
                        newVariant);
                    // Vanilla's take path also refreshes supports
                    // and cloth references after removing a slot.
                    if (newHash == 0)
                    {
                        UpdateArmorSupports?.Invoke(
                            request.ArmorStand, null);
                        ArmorCloths?.SetValue(
                            request.ArmorStand,
                            request.ArmorStand
                                .GetComponentsInChildren<Cloth>());
                    }
                    request.ArmorStand.m_effects.Create(
                        request.ArmorStand.transform.position,
                        Quaternion.identity);
                    Game.instance.IncrementPlayerStat(
                        PlayerStatType.ArmorStandUses);
                }

                // Last-used tracking stays tied to actual EquipItem,
                // not the start of a delayed equipment action.
                return true;
            }
            catch (Exception error)
            {
                // If anything failed before completion, put
                // the original inventory item back and restore
                // the original stand metadata.
                if (zdoWritten || didRemove || didInsert ||
                    queuedEquip)
                {
                    try
                    {
                        if (queuedEquip)
                            player.RemoveEquipAction(displayed);

                        if (didInsert)
                        {
                            player.UnequipItem(displayed,
                                triggerEquipEffects: false);
                            inv.RemoveOneItem(displayed);
                        }

                        if (didRemove)
                        {
                            inv.AddItem(outgoing, originalPos);
                            if (wasEquipped)
                                player.EquipItem(outgoing,
                                    triggerEquipEffects: false);
                        }

                        if (zdoWritten)
                        {
                            if (request.ExpectedData == null)
                                zdo.RemoveByteArray(dataKey);
                            else
                                zdo.Set(dataKey,
                                    request.ExpectedData);
                            zdo.Set(itemKey,
                                request.ExpectedHash);
                            if (request.Index < 0)
                            {
                                zdo.Set(ZDOVars.s_quality,
                                    request.OldQuality);
                                zdo.Set(ZDOVars.s_variant,
                                    request.OldVariant);
                            }
                            if (request.Index >= 0)
                                zdo.Set(
                                    (request.Index + "_variant")
                                        .GetStableHashCode(),
                                    request.OldSlotVariant);
                        }
                    }
                    catch (Exception rollbackError)
                    {
                        Plugin.Log.LogError(
                            rollbackError);
                    }
                }
                else if (wasEquipped)
                {
                    player.EquipItem(outgoing,
                        triggerEquipEffects: false);
                }

                Plugin.Log.LogWarning(
                    "Swap cancelled: " + error);
                Notify(player,
                    "Could not swap; equipment unchanged");
                return false;
            }
        }

        internal static void Clear()
        {
            Pending.Clear();
        }
    }
}
