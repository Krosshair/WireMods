using System.Reflection;
using HarmonyLib;

namespace WiresWeaponMaster
{
    internal readonly struct HolsterSelection
    {
        internal readonly int Hash;
        internal readonly int Variant;
        internal readonly int Quality;

        internal HolsterSelection(int hash, int variant, int quality)
        {
            Hash = hash;
            Variant = variant;
            Quality = quality;
        }

        internal int PackedMeta
        {
            get { return (Quality << 16) | (Variant & 0xFFFF); }
        }

        internal static HolsterSelection FromPacked(int hash, int meta)
        {
            if (hash == 0)
                return default;

            int variant = meta & 0xFFFF;
            int quality = (meta >> 16) & 0xFFFF;
            return new HolsterSelection(hash, variant, quality);
        }
    }

    internal static class MultiplayerSync
    {
        private const int LaneCount = 7;

        private static readonly FieldInfo NViewField =
            AccessTools.Field(typeof(VisEquipment), "m_nview");

        private static readonly int[] HashKeys = BuildKeys("hash");
        private static readonly int[] MetaKeys = BuildKeys("meta");

        private static int[] BuildKeys(string suffix)
        {
            int[] keys = new int[LaneCount];
            for (int i = 1; i < LaneCount; i++)
            {
                keys[i] =
                    ("wire.weaponmaster.v2.lane" + i + "." + suffix)
                    .GetStableHashCode();
            }

            return keys;
        }

        internal static ZNetView GetNetView(VisEquipment vis)
        {
            return vis == null || NViewField == null
                ? null
                : NViewField.GetValue(vis) as ZNetView;
        }

        internal static ZDO GetZdo(VisEquipment vis)
        {
            ZNetView nview = GetNetView(vis);
            return nview != null && nview.IsValid()
                ? nview.GetZDO()
                : null;
        }

        internal static void Publish(
            VisEquipment vis,
            HolsterSelection[] selections)
        {
            if (vis == null || selections == null)
                return;

            ZNetView nview = GetNetView(vis);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return;

            for (int lane = 1; lane < LaneCount; lane++)
            {
                HolsterSelection selection = selections[lane];
                int hash = selection.Hash;
                int meta = hash != 0 ? selection.PackedMeta : 0;

                if (zdo.GetInt(HashKeys[lane]) != hash)
                    zdo.Set(HashKeys[lane], hash);

                if (zdo.GetInt(MetaKeys[lane]) != meta)
                    zdo.Set(MetaKeys[lane], meta);
            }
        }

        internal static HolsterSelection Read(
            VisEquipment vis,
            HolsterLane lane)
        {
            ZDO zdo = GetZdo(vis);
            if (zdo == null || lane == HolsterLane.None)
                return default;

            int index = (int)lane;
            int hash = zdo.GetInt(HashKeys[index]);
            int meta = zdo.GetInt(MetaKeys[index]);

            return HolsterSelection.FromPacked(hash, meta);
        }

        internal static void ClearLocal()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            VisEquipment vis = WeaponVisuals.GetVisEquipment(player);
            if (vis == null)
                return;

            Publish(vis, new HolsterSelection[LaneCount]);
        }
    }
}
