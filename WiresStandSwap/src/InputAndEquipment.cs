using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresStandSwap
{
    // Input capture only: two key-down events plus two active holds.
    // Never scan scenes, inventory contents or armor-stand collections in Update.
    internal static class InputAndEquipment
    {
        private sealed class PendingHold
        {
            internal Player Player;
            internal GameObject Target;
            internal float Started;
            internal bool Activated;
            internal bool Alt;
        }

        private static readonly MethodInfo VanillaInteract = AccessTools.Method(
            typeof(Player), "Interact",
            new[] { typeof(GameObject), typeof(bool), typeof(bool) });
        private static readonly MethodInfo ArmorUseItem = AccessTools.Method(
            typeof(ArmorStand), "UseItem",
            new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) });
        private static readonly FieldInfo RightItemField =
            AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo LeftItemField =
            AccessTools.Field(typeof(Humanoid), "m_leftItem");
        private static readonly MethodInfo ShowHandItemsMethod =
            AccessTools.Method(typeof(Humanoid), "ShowHandItems");

        private static ItemDrop.ItemData RightItem(Player player) =>
            player != null && RightItemField != null
                ? RightItemField.GetValue(player) as ItemDrop.ItemData : null;
        private static ItemDrop.ItemData LeftItem(Player player) =>
            player != null && LeftItemField != null
                ? LeftItemField.GetValue(player) as ItemDrop.ItemData : null;

        private static readonly MethodInfo ItemCanAttach = AccessTools.Method(
            typeof(ItemStand), "CanAttach", new[] { typeof(ItemDrop.ItemData) });

        private static PendingHold _main;
        private static bool _focusConsumed;
        private static bool _allowVanillaTap;

        private static KeyCode MainKey => Plugin.MainHoldKey?.Value ?? KeyCode.E;
        private static KeyCode FocusKey => Plugin.FocusModifier?.Value ?? KeyCode.LeftShift;
        private static bool FocusDown
        {
            get
            {
                KeyCode key = FocusKey;
                if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
                    return ZInput.GetKey(KeyCode.LeftShift) ||
                        ZInput.GetKey(KeyCode.RightShift);
                return key != KeyCode.None && ZInput.GetKey(key);
            }
        }
        private static float Delay => Plugin.HoldSeconds != null
            ? Mathf.Clamp(Plugin.HoldSeconds.Value, 0.12f, 0.65f) : 0.22f;

        private static bool CanTakeInput(Player player)
        {
            return player != null && player == Player.m_localPlayer &&
                !player.IsDead() && !player.IsStaggering() &&
                !player.InAttack() && !player.InDodge() &&
                !InventoryGui.IsVisible() &&
                !Console.IsVisible() && !Menu.IsVisible() && !Hud.InRadial() &&
                (Chat.instance == null || !Chat.instance.HasFocus());
        }

        private static bool IsNormalStand(GameObject target)
        {
            if (target == null) return false;
            ItemStand itemStand = target.GetComponentInParent<ItemStand>();
            if (itemStand != null)
                return itemStand.m_canBeRemoved &&
                    itemStand.m_guardianPower == null;
            return StandRegistry.Resolve(target) != null;
        }

        private static bool SameStand(GameObject first, GameObject second)
        {
            if (first == null || second == null) return false;
            if (first == second) return true;
            ItemStand a = first.GetComponentInParent<ItemStand>();
            if (a != null) return a == second.GetComponentInParent<ItemStand>();
            ArmorStand armor = StandRegistry.Resolve(first);
            return armor != null && armor == StandRegistry.Resolve(second);
        }

        private static int SlotIndex(ArmorStand stand, GameObject hovered)
        {
            if (stand == null || hovered == null) return -1;
            Switch sw = hovered.GetComponentInParent<Switch>();
            return StandRegistry.ForSwitch(sw) == stand
                ? StandRegistry.IndexForSwitch(sw) : -1;
        }

        private static ItemDrop.ItemData RightOrLeft(
            Player player, Func<ItemDrop.ItemData, bool> predicate)
        {
            if (player == null) return null;
            ItemDrop.ItemData right = RightItem(player);
            if (IsHeld(player, right) && (predicate == null || predicate(right)))
                return right;
            ItemDrop.ItemData left = LeftItem(player);
            if (IsHeld(player, left) && (predicate == null || predicate(left)))
                return left;
            return null;
        }

        private static bool IsHeld(Player player, ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null &&
                player.GetInventory() != null &&
                player.GetInventory().ContainsItem(item) &&
                player.IsItemEquiped(item) &&
                StandSwaps.IsEquipmentItem(item.m_dropPrefab.name.GetStableHashCode());
        }

        private static ItemDrop.ItemData AnyHeld(Player player)
        {
            return RightOrLeft(player, null);
        }

        private static bool ItemFits(ItemStand stand, ItemDrop.ItemData item)
        {
            if (stand == null || item == null || ItemCanAttach == null)
                return false;
            try { return (bool)ItemCanAttach.Invoke(stand, new object[] { item }); }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Item-stand support check failed: " + ex.Message);
                return false;
            }
        }

        private static void Info(Player player, string text)
        {
            player?.Message(MessageHud.MessageType.Center, text);
        }

        private static void BeginMain(Player player, GameObject target, bool alt)
        {
            if (!CanTakeInput(player) || !IsNormalStand(target)) return;
            _main = new PendingHold
            {
                Player = player, Target = target, Alt = alt,
                Started = Time.unscaledTime
            };
        }

        private static void TriggerMain(PendingHold pending)
        {
            Player player = pending.Player;
            if (!CanTakeInput(player) ||
                !SameStand(pending.Target, player.GetHoverObject())) return;

            ItemStand itemStand = pending.Target.GetComponentInParent<ItemStand>();
            if (itemStand != null)
            {
                ItemDrop.ItemData anyHeld = AnyHeld(player);
                ItemDrop.ItemData held = RightOrLeft(player,
                    item => ItemFits(itemStand, item));
                if (anyHeld != null && held == null)
                {
                    Info(player, "Stand cannot hold held item");
                    return;
                }
                if (!itemStand.HaveAttachment())
                {
                    if (held == null)
                    {
                        Info(player, "Nothing to swap");
                        return;
                    }

                    if (!ItemFits(itemStand, held))
                    {
                        Info(player, "Stand cannot hold held item");
                        return;
                    }
                    itemStand.UseItem(player, held);
                    return;
                }

                if (!StandSwaps.IsEquipmentItem(itemStand.GetAttachedItem()))
                {
                    Info(player, "Cannot equip displayed item");
                    return;
                }
                // Explicit held equipment takes priority. Without it,
                // use the original exact-family/equipped item fallback.
                StandSwaps.TryItemStand(itemStand, player, held, held == null);
                return;
            }

            ArmorStand armor = StandRegistry.Resolve(pending.Target);
            if (armor == null) return;
            int requestedSlot = SlotIndex(armor, player.GetHoverObject());
            ItemDrop.ItemData heldItem = AnyHeld(player);

            if (heldItem != null)
            {
                // Prefer the exact looked-at slot. If looking at the
                // stand body, first fill an empty compatible slot.
                int targetSlot = -1;
                ItemDrop.ItemData chosenHeld = null;
                if (requestedSlot >= 0)
                {
                    chosenHeld = RightOrLeft(player,
                        item => ArmorLoadouts.CanAttach(armor, requestedSlot, item));
                    if (chosenHeld != null) targetSlot = requestedSlot;
                }
                if (targetSlot < 0)
                {
                    // Hold E is SMART, not a forced selection of the
                    // hovered slot. Shift+E below is the exact-slot action.
                    for (int i = 0; i < armor.m_slots.Count; ++i)
                    {
                        if (armor.GetAttachedItem(i) != 0) continue;
                        ItemDrop.ItemData candidate = RightOrLeft(player,
                            item => ArmorLoadouts.CanAttach(armor, i, item));
                        if (candidate == null) continue;
                        targetSlot = i;
                        chosenHeld = candidate;
                        break;
                    }
                    if (targetSlot < 0)
                        for (int i = 0; i < armor.m_slots.Count; ++i)
                        {
                            ItemDrop.ItemData candidate = RightOrLeft(player,
                                item => ArmorLoadouts.CanAttach(armor, i, item));
                            if (candidate == null) continue;
                            targetSlot = i;
                            chosenHeld = candidate;
                            break;
                        }
                }
                if (targetSlot < 0)
                {
                    Info(player, "Stand cannot hold held item");
                    return;
                }

                if (armor.GetAttachedItem(targetSlot) == 0)
                {
                    if (ArmorUseItem == null)
                    {
                        Info(player, "Cannot attach held item");
                        return;
                    }
                    try
                    {
                        ArmorUseItem.Invoke(armor, new object[]
                            { armor.m_slots[targetSlot].m_switch, player, chosenHeld });
                    }
                    catch (Exception e)
                    {
                        Plugin.Log?.LogWarning("Armor attachment failed: " + e);
                        Info(player, "Cannot attach held item");
                    }
                }
                else
                {
                    StandSwaps.TryArmorStand(armor, targetSlot, player, chosenHeld);
                }
                return;
            }

            // No item in hand: equip the whole set if the stand holds
            // armor. This supports an entirely unequipped character.
            int single = -1;
            bool hasArmor = false;
            for (int i = 0; i < armor.m_slots.Count; ++i)
            {
                int hash = armor.GetAttachedItem(i);
                if (!StandSwaps.IsEquipmentItem(hash)) continue;
                if (single < 0 || i == requestedSlot) single = i;
                var prefab = ObjectDB.instance.GetItemPrefab(hash);
                var item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (item == null) continue;
                var type = item.m_itemData.m_shared.m_itemType;
                if (type == ItemDrop.ItemData.ItemType.Helmet ||
                    type == ItemDrop.ItemData.ItemType.Chest ||
                    type == ItemDrop.ItemData.ItemType.Legs ||
                    type == ItemDrop.ItemData.ItemType.Shoulder ||
                    type == ItemDrop.ItemData.ItemType.Utility ||
                    type == ItemDrop.ItemData.ItemType.Trinket)
                    hasArmor = true;
            }

            if (hasArmor)
                ArmorLoadouts.TryStart(armor, player);
            else if (single >= 0)
                StandSwaps.TryArmorStand(armor, single, player, null, true);
            else
                Info(player, "Nothing to swap");
        }

        private static void TriggerFocused(Player player, GameObject target)
        {
            if (!CanTakeInput(player) || target == null) return;
            Switch sw = target.GetComponentInParent<Switch>();
            ArmorStand stand = StandRegistry.ForSwitch(sw);
            int index = StandRegistry.IndexForSwitch(sw);
            if (stand == null || index < 0 || index >= stand.m_slots.Count)
            {
                Info(player, "Aim at an armor stand piece");
                return;
            }

            int hash = stand.GetAttachedItem(index);
            ItemDrop.ItemData held = RightOrLeft(player,
                item => ArmorLoadouts.CanAttach(stand, index, item));
            // Body armor is worn, not held in the hands. A sword or
            // shield being drawn must not prevent swapping the helmet,
            // chest, legs or other currently worn armor piece.
            bool bodyPiece = false;
            if (hash != 0 && ObjectDB.instance != null)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null)
                {
                    switch (drop.m_itemData.m_shared.m_itemType)
                    {
                        case ItemDrop.ItemData.ItemType.Helmet:
                        case ItemDrop.ItemData.ItemType.Chest:
                        case ItemDrop.ItemData.ItemType.Legs:
                        case ItemDrop.ItemData.ItemType.Shoulder:
                        case ItemDrop.ItemData.ItemType.Utility:
                        case ItemDrop.ItemData.ItemType.Trinket:
                            bodyPiece = true;
                            break;
                    }
                }
            }
            if (bodyPiece)
                held = null;
            else if (AnyHeld(player) != null && held == null)
            {
                Info(player, "Stand cannot hold held item");
                return;
            }
            if (hash == 0)
            {
                if (held == null)
                {
                    Info(player, "Nothing to swap");
                    return;
                }
                if (ArmorUseItem == null)
                {
                    Info(player, "Cannot attach held item");
                    return;
                }
                try
                {
                    ArmorUseItem.Invoke(stand, new object[]
                        { stand.m_slots[index].m_switch, player, held });
                }
                catch (Exception error)
                {
                    Plugin.Log?.LogWarning("Focused attachment failed: " + error);
                    Info(player, "Cannot attach held item");
                }
                return;
            }
            if (!StandSwaps.IsEquipmentItem(hash))
            {
                Info(player, "Cannot equip displayed item");
                return;
            }

            // With empty hands, helmets/chest/etc. exchange actual WORN
            // armor, but a weapon doesn't exchange a stale backpack
            // item merely because it was used sometime in the past.
            bool takeOnly = held == null;
            if (held == null && ObjectDB.instance != null)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null)
                {
                    var type = drop.m_itemData.m_shared.m_itemType;
                    switch (type)
                    {
                        case ItemDrop.ItemData.ItemType.Helmet:
                        case ItemDrop.ItemData.ItemType.Chest:
                        case ItemDrop.ItemData.ItemType.Legs:
                        case ItemDrop.ItemData.ItemType.Shoulder:
                        case ItemDrop.ItemData.ItemType.Utility:
                        case ItemDrop.ItemData.ItemType.Trinket:
                            takeOnly = false;
                            break;
                    }
                }
            }
            Plugin.Log?.LogInfo("Focused swap requested for slot " + index +
                ", held=" + (held != null ? held.m_shared.m_name : "<none>"));
            StandSwaps.TryArmorStand(stand, index, player, held, takeOnly);
        }
        private static void RestoreNormalTap(PendingHold pending)
        {
            if (pending == null || pending.Activated ||
                VanillaInteract == null || pending.Player == null ||
                !CanTakeInput(pending.Player) ||
                !SameStand(pending.Target, pending.Player.GetHoverObject()))
                return;
            try
            {
                _allowVanillaTap = true;
                VanillaInteract.Invoke(pending.Player,
                    new object[] { pending.Target, false, pending.Alt });
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning("Normal tap fallback failed: " + error);
            }
            finally { _allowVanillaTap = false; }
        }

        internal static void Clear()
        {
            _main = null;
            _focusConsumed = false;
            _allowVanillaTap = false;
        }

        [HarmonyPatch(typeof(Player), "Interact")]
        private static class MainInteractPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
            {
                if (_allowVanillaTap || __instance != Player.m_localPlayer ||
                    MainKey == KeyCode.None)
                    return true;

                // Once a Shift+E action is handled, none of Valheim's
                // repeated hold calls should take or drop the same item.
                if (_focusConsumed)
                    return false;
                if (hold && _main != null && _main.Player == __instance)
                    return false;

                if (go == null || !ZInput.GetKey(MainKey) || !IsNormalStand(go))
                    return true;

                if (FocusDown)
                {
                    // The keydown arrives from Player.Interact itself,
                    // with the ACTUAL hovered Switch object. This fixes
                    // the old R handler failing to identify an armor slot.
                    if (!hold)
                    {
                        _focusConsumed = true;
                        Switch sw = go.GetComponentInParent<Switch>();
                        ArmorStand armor = StandRegistry.Resolve(go);
                        if (armor != null)
                        {
                            if (StandRegistry.ForSwitch(sw) == armor &&
                                StandRegistry.IndexForSwitch(sw) >= 0)
                                TriggerFocused(__instance, go);
                            else
                                Info(__instance, "Aim at a specific armor piece");
                        }
                        else
                            TriggerMain(new PendingHold {
                                Player = __instance, Target = go, Alt = alt,
                                Started = Time.unscaledTime
                            });
                    }
                    return false;
                }

                // Alt + Use may rotate item stand displays. Do not steal
                // that vanilla alternate interaction.
                if (alt)
                    return true;

                if (!hold)
                    BeginMain(__instance, go, alt);
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "Update")]
        private static class InputEventPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                // Only cheap input state and the two active hold records.
                // Never scan stand geometry or inventory in Update.
                KeyCode key = MainKey;
                if (_focusConsumed && !ZInput.GetKey(key))
                    _focusConsumed = false;

                if (key != KeyCode.None && key != KeyCode.E &&
                    ZInput.GetKeyDown(key))
                {
                    GameObject target = __instance.GetHoverObject();
                    if (FocusDown && IsNormalStand(target))
                    {
                        _focusConsumed = true;
                        ArmorStand armor = StandRegistry.Resolve(target);
                        if (armor != null)
                            TriggerFocused(__instance, target);
                        else
                            TriggerMain(new PendingHold {
                                Player = __instance, Target = target,
                                Started = Time.unscaledTime
                            });
                    }
                    else if (!FocusDown)
                        BeginMain(__instance, target, false);
                }

                if (_main == null) return;

                if (!ZInput.GetKey(key))
                {
                    PendingHold pending = _main;
                    _main = null;
                    RestoreNormalTap(pending);
                }
                else if (!_main.Activated &&
                         Time.unscaledTime - _main.Started >= Delay)
                {
                    _main.Activated = true;
                    TriggerMain(_main);
                }
            }
        }
        [HarmonyPatch(typeof(ArmorStand), "Awake")]
        private static class RegisterArmorStandPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ArmorStand __instance)
            {
                StandRegistry.Register(__instance);
            }
        }

        private static string Localize(string text)
        {
            return Localization.instance != null ?
                Localization.instance.Localize(text ?? "") : (text ?? "");
        }

        private static string ActionLabel(string key, string action)
        {
            return "\n[<color=yellow><b>" + key + "</b></color>] " + action;
        }

        private static string FocusHint =>
            FocusKey == KeyCode.LeftShift || FocusKey == KeyCode.RightShift
                ? "Shift+" + MainKey
                : FocusKey.ToString() + "+" + MainKey;

        [HarmonyPatch(typeof(ItemStand), "GetHoverText")]
        private static class ItemHoverPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ItemStand __instance, ref string __result)
            {
                if (!__instance.m_canBeRemoved ||
                    __instance.m_guardianPower != null ||
                    !PrivateArea.CheckAccess(__instance.transform.position, 0f, false))
                    return;

                bool attached = __instance.HaveAttachment();
                bool equippable = attached &&
                    StandSwaps.IsEquipmentItem(__instance.GetAttachedItem());

                Player player = Player.m_localPlayer;
                bool hasHeld = player != null &&
                    (RightItem(player) != null || LeftItem(player) != null);
                string name = Localize(__instance.m_name);
                if (attached)
                {
                    // Preserve vanilla's optional Alt+E orientation line.
                    // Its ordinary "hold E to drop" line conflicts with
                    // our own action and is intentionally replaced.
                    string secondaryAction = "";
                    int firstLine = __result.IndexOf('\n');
                    int nextLine = firstLine >= 0
                        ? __result.IndexOf('\n', firstLine + 1) : -1;
                    if (nextLine >= 0)
                        secondaryAction = __result.Substring(nextLine);

                    string displayed = Localize(__instance.m_currentItemName);
                    __result = name + (string.IsNullOrEmpty(displayed)
                        ? "" : " (" + displayed + ")");
                    if (equippable)
                    {
                        __result += ActionLabel("Hold " + MainKey,
                            hasHeld ? "Swap held equipment" : "Take and equip");
                        __result += ActionLabel(FocusHint,
                            hasHeld ? "Quick swap held equipment" :
                                      "Quick equip item");
                    }
                    else
                    {
                        // This is a decorative/non-equippable item.
                        // Do not advertise an impossible swap.
                    }
                    __result += secondaryAction;
                }
                else
                {
                    __result = name;
                    if (hasHeld)
                    {
                        __result += ActionLabel("Hold " + MainKey,
                            "Attach held item");
                        __result += ActionLabel(FocusHint,
                            "Quick attach held item");
                    }
                    else
                    {
                        // No additional swap prompt when there is nothing
                        // to swap; hotbar attachment remains available.
                        __result += ActionLabel("1-8",
                            "Attach item from hotbar");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Switch), "GetHoverText")]
        private static class ArmorHoverPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Switch __instance, ref string __result)
            {
                ArmorStand stand = StandRegistry.ForSwitch(__instance);
                int index = StandRegistry.IndexForSwitch(__instance);
                if (stand == null || index < 0) return;
                if (!PrivateArea.CheckAccess(stand.transform.position,
                    0f, false)) return;

                // Use the exact Switch registered by the armor stand;
                // never use one of the other visual slots as a guess.
                bool occupied = StandSwaps.IsEquipmentItem(
                    stand.GetAttachedItem(index));
                Player player = Player.m_localPlayer;
                bool hasHeld = player != null &&
                    (RightItem(player) != null || LeftItem(player) != null);

                string label = Localize(__instance.m_hoverText);
                if (string.IsNullOrWhiteSpace(label))
                    label = Localize(stand.m_name);
                __result = label;

                if (occupied)
                    __result += ActionLabel("Tap " + MainKey,
                        "Take item (vanilla)");

                if (occupied || hasHeld)
                {
                    __result += ActionLabel("Hold " + MainKey,
                        hasHeld ? "Swap / attach held item" : "Equip whole loadout");
                    __result += ActionLabel(FocusHint,
                        occupied ? "Swap / equip THIS piece" :
                                   "Attach held item to THIS slot");
                }
                else
                {
                    __result += ActionLabel("1-8",
                        "Attach item from hotbar");
                }
            }
        }
        [HarmonyPatch(typeof(Humanoid), "SetupEquipment")]
        private static class BatchRefreshPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Humanoid __instance)
            {
                return __instance != Player.m_localPlayer ||
                    !ArmorLoadouts.SuppressEquipmentRefresh;
            }
        }

        [HarmonyPatch(typeof(EffectList), "Create")]
        private static class BatchEffectsPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(EffectList __instance, ref GameObject[] __result)
            {
                if (!ArmorLoadouts.ShouldSuppressEffect(__instance)) return true;
                __result = new GameObject[0];
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "EquipItem")]
        private static class TrackEquipmentPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData item,
                bool __result)
            {
                if (__result && __instance == Player.m_localPlayer)
                    UsedItems.Record(item);
            }
        }
    }
}
