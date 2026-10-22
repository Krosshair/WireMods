using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WiresStandSwap
{
    internal static class UsedItems
    {
        // One chronological history across weapons, tools and
        // shields. Choose the newest matching item, not the newest
        // item of an unrelated kind. Search exact weapon kind first,
        // then other variants within the same skill family.
        private static readonly List<ItemDrop.ItemData>
            RecentEquipment = new List<ItemDrop.ItemData>();

        private static readonly Dictionary<
            ItemDrop.ItemData.ItemType, FieldInfo>
            WornArmor = new Dictionary<
                ItemDrop.ItemData.ItemType, FieldInfo>
            {
                { ItemDrop.ItemData.ItemType.Helmet,
                    AccessTools.Field(typeof(Humanoid), "m_helmetItem") },
                { ItemDrop.ItemData.ItemType.Chest,
                    AccessTools.Field(typeof(Humanoid), "m_chestItem") },
                { ItemDrop.ItemData.ItemType.Legs,
                    AccessTools.Field(typeof(Humanoid), "m_legItem") },
                { ItemDrop.ItemData.ItemType.Shoulder,
                    AccessTools.Field(typeof(Humanoid), "m_shoulderItem") },
                { ItemDrop.ItemData.ItemType.Utility,
                    AccessTools.Field(typeof(Humanoid), "m_utilityItem") },
                { ItemDrop.ItemData.ItemType.Trinket,
                    AccessTools.Field(typeof(Humanoid), "m_trinketItem") }
            };

        internal static bool IsWeaponOrTool(
            ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                    return true;
                default:
                    return false;
            }
        }

        internal static string Family(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
                return null;

            var t = item.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Shield)
                return "shield";

            if (t == ItemDrop.ItemData.ItemType.Torch)
                return "torch";

            if (!IsWeaponOrTool(t))
                return null;

            Skills.SkillType skill = item.m_shared.m_skillType;

            // Axes and battleaxes/greataxes share Axes, maces
            // and two-handed clubs share Clubs, etc.
            if (t != ItemDrop.ItemData.ItemType.Tool)
            {
                if (skill == Skills.SkillType.None)
                    return item.m_dropPrefab != null
                        ? "prefab:" + item.m_dropPrefab.name
                        : null;
                return "weapon:" + skill;
            }

            // Several non-combat tools inherit a default Sword
            // skill value. Never treat a hammer as a sword.
            if (skill == Skills.SkillType.Pickaxes ||
                skill == Skills.SkillType.WoodCutting ||
                skill == Skills.SkillType.Fishing ||
                skill == Skills.SkillType.Farming ||
                skill == Skills.SkillType.Crafting)
                return "tool:" + skill;

            return item.m_dropPrefab != null
                ? "toolprefab:" + item.m_dropPrefab.name
                : "toolname:" + item.m_shared.m_name;
        }

        private static bool SameFamily(
            ItemDrop.ItemData a,
            ItemDrop.ItemData b)
        {
            string fa = Family(a);
            return fa != null &&
                   fa == Family(b);
        }

        internal static void Record(ItemDrop.ItemData item)
        {
            if (item == null || Family(item) == null)
                return;

            // The same item can be unequipped/re-equipped many
            // times. Refresh its position rather than duplicating
            // it in history.
            RecentEquipment.Remove(item);
            RecentEquipment.Insert(0, item);
            if (RecentEquipment.Count > 128)
                RecentEquipment.RemoveRange(
                    128, RecentEquipment.Count - 128);
        }

        private static ItemDrop.ItemData MatchingRecent(
            Player player, ItemDrop.ItemData displayed,
            bool exactKind,
            Func<ItemDrop.ItemData, bool> canSwap)
        {
            Inventory inv = player.GetInventory();
            foreach (ItemDrop.ItemData recent in
                RecentEquipment)
            {
                if (recent == null ||
                    !inv.ContainsItem(recent) ||
                    !SameFamily(recent, displayed) ||
                    player.IsEquipActionQueued(recent))
                    continue;

                if (exactKind &&
                    recent.m_shared.m_itemType !=
                        displayed.m_shared.m_itemType)
                    continue;
                if (canSwap != null && !canSwap(recent))
                    continue;

                return recent;
            }
            return null;
        }

        private static ItemDrop.ItemData FindSimilar(
            Player player, ItemDrop.ItemData displayed,
            Func<ItemDrop.ItemData, bool> canSwap)
        {
            Inventory inv = player.GetInventory();
            ItemDrop.ItemData best = null;
            int bestScore = int.MinValue;
            foreach (ItemDrop.ItemData candidate in
                inv.GetAllItems())
            {
                if (candidate == null ||
                    candidate == displayed ||
                    candidate.m_stack != 1 ||
                    !SameFamily(candidate, displayed) ||
                    player.IsEquipActionQueued(candidate) ||
                    (canSwap != null && !canSwap(candidate)))
                    continue;

                // Prefer the same exact weapon kind (one-hand axe
                // before greataxe), but greataxe still qualifies if
                // it's the only Axes-family item.
                int score = 0;
                if (candidate.m_shared.m_itemType ==
                    displayed.m_shared.m_itemType)
                    score += 100;
                if (candidate.m_dropPrefab != null &&
                    displayed.m_dropPrefab != null &&
                    candidate.m_dropPrefab.name ==
                    displayed.m_dropPrefab.name)
                    score += 50;
                if (player.IsItemEquiped(candidate))
                    score += 200;
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        internal static ItemDrop.ItemData Find(
            Player player, ItemDrop.ItemData displayed,
            Func<ItemDrop.ItemData, bool> canSwap = null)
        {
            if (player == null || displayed == null)
                return null;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return null;

            var type = displayed.m_shared.m_itemType;
            if (type == ItemDrop.ItemData.ItemType.Shield ||
                IsWeaponOrTool(type))
            {
                // Exact kind, then family variant, then inventory.
                // E.g. most recent axe over an older battleaxe;
                // if no axe was used, a battleaxe is still valid.
                ItemDrop.ItemData recent =
                    MatchingRecent(
                        player, displayed, true, canSwap);
                if (recent != null)
                    return recent;
                recent = MatchingRecent(
                    player, displayed, false, canSwap);
                return recent ??
                    FindSimilar(
                        player, displayed, canSwap);
            }

            // Each armor-stand piece selects the exact body slot:
            // helmet, chest, legs, shoulder/cape, utility, trinket.
            FieldInfo field;
            if (WornArmor.TryGetValue(type, out field) &&
                field != null)
            {
                ItemDrop.ItemData worn =
                    field.GetValue(player) as ItemDrop.ItemData;
                if (worn != null &&
                    worn.m_shared.m_itemType == type &&
                    inv.ContainsItem(worn) &&
                    player.IsItemEquiped(worn) &&
                    (canSwap == null || canSwap(worn)))
                    return worn;
            }

            foreach (ItemDrop.ItemData item in
                inv.GetAllItems())
            {
                if (item != null &&
                    item.m_shared.m_itemType == type &&
                    player.IsItemEquiped(item) &&
                    (canSwap == null || canSwap(item)))
                    return item;
            }

            // EAQS can hold an item in a dedicated equipment
            // cell even if no body-slot field currently references
            // it. Exchange that exact cell instead of always
            // falling back to Chest or grabbing the backpack.
            ItemDrop.ItemData occupied =
                EquipmentSlotsCompat.FindOccupiedSlot(
                    player, displayed);
            return occupied != null &&
                (canSwap == null || canSwap(occupied))
                    ? occupied : null;
        }

        internal static void Clear()
        {
            RecentEquipment.Clear();
        }
    }
}
