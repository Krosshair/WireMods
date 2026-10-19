using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WiresWeaponMaster
{
    internal static class WeaponMasterController
    {
        private const int LaneCount = 7;

        private static readonly List<ItemDrop.ItemData>[] History =
        {
            null,
            new List<ItemDrop.ItemData>(8),
            new List<ItemDrop.ItemData>(8),
            new List<ItemDrop.ItemData>(8),
            new List<ItemDrop.ItemData>(8),
            new List<ItemDrop.ItemData>(8),
            new List<ItemDrop.ItemData>(8)
        };

        private static readonly FieldInfo LeftItemField =
            AccessTools.Field(typeof(Humanoid), "m_leftItem");
        private static readonly FieldInfo RightItemField =
            AccessTools.Field(typeof(Humanoid), "m_rightItem");

        private static readonly FieldInfo HiddenLeftItemField =
            AccessTools.Field(typeof(Humanoid), "m_hiddenLeftItem");

        private static readonly FieldInfo HiddenRightItemField =
            AccessTools.Field(typeof(Humanoid), "m_hiddenRightItem");

        private static readonly MethodInfo TriggerEquipEffectMethod =
            AccessTools.Method(typeof(Humanoid), "TriggerEquipEffect");

        internal static void Reset()
        {
            for (int i = 1; i < LaneCount; i++)
                History[i].Clear();

            WeaponVisuals.RefreshLocal();
        }

        internal static List<ItemDrop.ItemData> GetHistory(HolsterLane lane)
        {
            int index = (int)lane;
            return index > 0 && index < LaneCount ? History[index] : null;
        }

        // Adds a remembered item at the end of its lane, keeping saved order.
        internal static void RestoreToLane(ItemDrop.ItemData item, HolsterLane lane)
        {
            int index = (int)lane;
            if (item == null || index <= 0 || index >= LaneCount)
                return;

            for (int i = 1; i < LaneCount; i++)
            {
                if (History[i].Contains(item))
                    return;
            }

            History[index].Add(item);
        }

        internal static void SeedCurrentState(Player player)
        {
            if (player == null)
                return;

            NoteUsed(player, GetLeft(player));
            NoteUsed(player, GetRight(player));
            ItemDrop.ItemData hiddenLeft = GetHiddenLeft(player);
            ItemDrop.ItemData hiddenRight = GetHiddenRight(player);

            Promote(hiddenLeft, ItemClassifier.ResolveLane(hiddenLeft, false));
            Promote(hiddenRight, ItemClassifier.ResolveLane(hiddenRight, true));

            WeaponVisuals.RefreshLocal();
        }

        internal static void NoteUsed(
            Humanoid humanoid,
            ItemDrop.ItemData item)
        {
            if (humanoid == null ||
                humanoid != Player.m_localPlayer ||
                item == null)
                return;

            bool rightHand = ReferenceEquals(GetRight(humanoid), item);
            HolsterLane lane = ItemClassifier.ResolveLane(item, rightHand);

            Promote(item, lane);
        }

        private static void Promote(
            ItemDrop.ItemData item,
            HolsterLane lane)
        {
            if (item == null || lane == HolsterLane.None)
                return;
            for (int i = 1; i < LaneCount; i++)
            {
                List<ItemDrop.ItemData> list = History[i];
                for (int j = list.Count - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(list[j], item))
                        list.RemoveAt(j);
                }
            }

            History[(int)lane].Insert(0, item);
        }

        internal static ItemDrop.ItemData GetVisibleForLane(
            HolsterLane lane)
        {
            Player player = Player.m_localPlayer;
            if (player == null || lane == HolsterLane.None)
                return null;

            Inventory inventory = player.GetInventory();
            if (inventory == null)
                return null;

            PruneMissing(inventory);

            ItemDrop.ItemData left = GetLeft(player);
            ItemDrop.ItemData right = GetRight(player);
            ItemDrop.ItemData hiddenLeft = GetHiddenLeft(player);
            ItemDrop.ItemData hiddenRight = GetHiddenRight(player);
            if (OccupiesLane(hiddenLeft, false, lane) ||
                OccupiesLane(hiddenRight, true, lane))
                return null;

            List<ItemDrop.ItemData> history = History[(int)lane];
            for (int i = 0; i < history.Count; i++)
            {
                ItemDrop.ItemData item = history[i];

                if (ReferenceEquals(item, left) ||
                    ReferenceEquals(item, right) ||
                    ReferenceEquals(item, hiddenLeft) ||
                    ReferenceEquals(item, hiddenRight))
                    continue;

                if (ItemClassifier.HasHolsterVisual(item))
                    return item;
            }

            return null;
        }

        private static bool OccupiesLane(
            ItemDrop.ItemData item,
            bool rightHand,
            HolsterLane lane)
        {
            return item != null &&
                   ItemClassifier.ResolveLane(item, rightHand) == lane;
        }
        private static void PruneMissing(Inventory inventory)
        {
            for (int lane = 1; lane < LaneCount; lane++)
            {
                List<ItemDrop.ItemData> history = History[lane];
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData item = history[i];
                    if (item == null || !inventory.ContainsItem(item))
                        history.RemoveAt(i);
                }
            }
        }

        internal static void ClearHistoryPreserveCurrentHands(Player player)
        {
            if (player == null)
                return;

            ItemDrop.ItemData left = GetLeft(player);
            ItemDrop.ItemData right = GetRight(player);

            for (int lane = 1; lane < LaneCount; lane++)
                History[lane].Clear();

            NoteUsed(player, left);
            NoteUsed(player, right);

            WeaponVisuals.RefreshLocal();
        }
        internal static void ClearHiddenHandItems(Player player)
        {
            if (player == null)
                return;

            ItemDrop.ItemData hiddenLeft = GetHiddenLeft(player);
            ItemDrop.ItemData hiddenRight = GetHiddenRight(player);

            if (hiddenLeft != null)
                player.UnequipItem(hiddenLeft, false);

            if (hiddenRight != null && !ReferenceEquals(hiddenRight, hiddenLeft))
                player.UnequipItem(hiddenRight, false);
        }

        internal static bool HasHandItems(Humanoid humanoid)
        {
            return humanoid != null &&
                   (GetLeft(humanoid) != null || GetRight(humanoid) != null);
        }

        internal static bool HasHiddenHandItems(Humanoid humanoid)
        {
            return humanoid != null &&
                   (GetHiddenLeft(humanoid) != null ||
                    GetHiddenRight(humanoid) != null);
        }

        internal static void PlayClearFeedback(Player player)
        {
            if (player == null || TriggerEquipEffectMethod == null)
                return;
            try
            {
                TriggerEquipEffectMethod.Invoke(player, new object[] { null });
            }
            catch (System.Exception ex)
            {
                Plugin.LogWarning("Clear feedback failed: " + ex.GetType().Name);
            }
        }

        internal static ItemDrop.ItemData GetLeft(Humanoid humanoid)
        {
            return humanoid == null
                ? null
                : LeftItemField.GetValue(humanoid) as ItemDrop.ItemData;
        }

        internal static ItemDrop.ItemData GetRight(Humanoid humanoid)
        {
            return humanoid == null
                ? null
                : RightItemField.GetValue(humanoid) as ItemDrop.ItemData;
        }

        internal static ItemDrop.ItemData GetHiddenLeft(Humanoid humanoid)
        {
            return humanoid == null
                ? null
                : HiddenLeftItemField.GetValue(humanoid) as ItemDrop.ItemData;
        }
        internal static ItemDrop.ItemData GetHiddenRight(Humanoid humanoid)
        {
            return humanoid == null
                ? null
                : HiddenRightItemField.GetValue(humanoid) as ItemDrop.ItemData;
        }
    }
}
