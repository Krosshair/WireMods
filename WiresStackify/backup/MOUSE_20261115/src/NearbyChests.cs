using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresStackify
{
    // Every chest that's loaded, collected as they wake up; searched only
    // when you quick stack or restock.
    internal static class NearbyChests
    {
        private static readonly List<Container> All = new List<Container>();

        internal static readonly AccessTools.FieldRef<Container, ZNetView> NView =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Container, Piece> PieceOf =
            AccessTools.FieldRefAccess<Container, Piece>("m_piece");
        private static readonly AccessTools.FieldRef<Container, bool> InUseFlag =
            AccessTools.FieldRefAccess<Container, bool>("m_inUse");
        private static readonly AccessTools.FieldRef<bool> PublicServer =
            AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(ZNet), "m_publicServer"));
        private static readonly System.Func<Container, long, bool> CheckAccess =
            AccessTools.MethodDelegate<System.Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        private static readonly int InUseKey = "InUse".GetStableHashCode();

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class AwakePatch
        {
            private static void Postfix(Container __instance)
            {
                if (NView(__instance) != null) All.Add(__instance);
            }
        }

        private static readonly System.Predicate<Container> IsGone = c => c == null;

        internal static List<Container> InRange(Vector3 point, float range)
        {
            All.RemoveAll(IsGone);
            List<Container> list = new List<Container>();
            float r2 = range * range;
            foreach (Container c in All)
            {
                if ((point - c.transform.position).sqrMagnitude >= r2) continue;
                ZNetView nv = NView(c);
                if (nv != null && nv.HasOwner()) list.Add(c);
            }
            return list;
        }

        // The chests a pouch works with: the open one first, then (for your
        // own pouch) every usable chest in range.
        internal static List<Container> ForPouch(Player player, bool onlyThisChest, Container current, float range)
        {
            List<Container> list = new List<Container>();
            if (current != null) list.Add(current);
            if (onlyThisChest || range <= 0f || !AreaAllowed()) return list;
            bool single = IsTrueSingleplayer();
            long id = player.GetPlayerID();
            foreach (Container c in InRange(player.transform.position, range))
                if (c != current && CanUse(c, id, single)) list.Add(c);
            return list;
        }

        internal static bool IsTrueSingleplayer()
        {
            return !ZNet.IsOpenServer() && !PublicServer() && ZNet.instance != null &&
                ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() &&
                ZNet.instance.GetConnectedPeers().Count == 0;
        }

        internal static bool AreaAllowed()
        {
            return IsTrueSingleplayer() || StackifyPlugin.HasMuc || StackifyPlugin.MultiplayerWithoutMuc.Value;
        }

        internal static bool CanUse(Container c, long playerId, bool singleplayer)
        {
            ZNetView nv = NView(c);
            Player owner = nv.GetComponent<Player>();
            if (owner != null && owner != Player.m_localPlayer) return false;
            if (!CheckAccess(c, playerId)) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, true, false)) return false;
            Piece piece = PieceOf(c);
            if (piece != null)
            {
                if (!piece.IsPlacedByPlayer() && !StackifyPlugin.NonPlayerBuilt.Value) return false;
            }
            else if (c.m_name != "piece_chest_drawer" && !StackifyPlugin.NonPhysicalContainers.Value) return false;

            if (StackifyPlugin.HasMuc) return true;
            // Ships move and sink: never someone else's in multiplayer.
            if (!singleplayer && c.transform.root.GetComponentInChildren<Ship>() != null) return false;
            return !InUse(c);
        }

        private static bool InUse(Container c)
        {
            ZDO zdo = NView(c).GetZDO();
            return (zdo != null && zdo.GetInt(InUseKey) == 1) || c.IsInUse() ||
                (c.m_wagon != null && c.m_wagon.InUse());
        }

        // Without Multi User Chest: take the chest over and mark it in use
        // while items move, so nobody else touches it at the same time.
        internal static void Claim(Container c, bool inUse)
        {
            ZNetView nv = NView(c);
            if (inUse) nv.ClaimOwnership();
            InUseFlag(c) = inUse;
            if (StackifyPlugin.QuietChests.Value) nv.GetZDO().Set(InUseKey, inUse ? 1 : 0);
            else c.SetInUse(inUse);
            ZDOMan.instance.ForceSendZDO(ZNet.GetUID(), nv.GetZDO().m_uid);
        }
    }
}
