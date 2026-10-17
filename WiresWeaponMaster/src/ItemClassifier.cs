namespace WiresWeaponMaster
{
    internal enum HolsterLane
    {
        None = 0,
        Shield = 1,
        Tool = 2,
        Melee = 3,
        TwoHandedMelee = 4,
        Bow = 5,
        Atgeir = 6
    }

    internal static class ItemClassifier
    {
        internal static HolsterLane ResolveLane(
            ItemDrop.ItemData item,
            bool rightHand)
        {
            if (item == null || item.m_shared == null || item.m_dropPrefab == null)
                return HolsterLane.None;

            ItemDrop.ItemData.ItemType effective =
                item.m_shared.m_attachOverride != ItemDrop.ItemData.ItemType.None
                    ? item.m_shared.m_attachOverride
                    : item.m_shared.m_itemType;

            switch (effective)
            {
                case ItemDrop.ItemData.ItemType.Shield:
                    return HolsterLane.Shield;

                case ItemDrop.ItemData.ItemType.Tool:
                    return HolsterLane.Tool;

                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                    return HolsterLane.Melee;

                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    return HolsterLane.TwoHandedMelee;

                case ItemDrop.ItemData.ItemType.Bow:
                    return HolsterLane.Bow;

                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return HolsterLane.Atgeir;

                case ItemDrop.ItemData.ItemType.Torch:
                    return rightHand ? HolsterLane.Melee : HolsterLane.Tool;

                default:
                    return HolsterLane.None;
            }
        }

        internal static bool HasHolsterVisual(ItemDrop.ItemData item)
        {
            if (item == null || item.m_dropPrefab == null)
                return false;

            UnityEngine.Transform root = item.m_dropPrefab.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                string childName = root.GetChild(i).name;
                if (childName == "attach_back" || childName == "attach")
                    return true;
            }

            return false;
        }

        internal static bool UsesRightHandForBackAttach(HolsterLane lane)
        {
            return lane == HolsterLane.Melee;
        }
    }
}
