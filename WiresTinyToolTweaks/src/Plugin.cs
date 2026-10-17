using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresTinyToolTweaks
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.tinytooltweaks";
        public const string Name = "Wire's Tiny Tool Tweaks";
        public const string Version = "1.0.2";

        internal static ConfigEntry<bool> RemoveEmptyVehicles;
        internal static ConfigEntry<bool> InteractWithBuildTool;

        private Harmony _harmony;

        private void Awake()
        {
            RemoveEmptyVehicles = Config.Bind(
                "Features",
                "Dismantle empty carts and ships",
                true,
                "Allow hammer removal of empty carts and ships, subject to vanilla ownership, access, station and in-use checks.");

            InteractWithBuildTool = Config.Bind(
                "Features",
                "Interact while holding build tool",
                true,
                "Allow normal interaction and held-tool interaction animation while the hammer or another build tool is equipped.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateHover")]
    internal static class InteractWhileToolEquippedPatch
    {
        private delegate void FindHoverObjectDelegate(
            Player player,
            out GameObject hovering,
            out Character hoveringCreature);

        private static readonly FindHoverObjectDelegate FindHoverObject =
            AccessTools.MethodDelegate<FindHoverObjectDelegate>(
                AccessTools.Method(
                    typeof(Player),
                    "FindHoverObject"),
                null,
                true);

        private static readonly AccessTools.FieldRef<Player, GameObject>
            Hovering =
                AccessTools.FieldRefAccess<Player, GameObject>(
                    "m_hovering");

        private static readonly AccessTools.FieldRef<Player, Character>
            HoveringCreature =
                AccessTools.FieldRefAccess<Player, Character>(
                    "m_hoveringCreature");

        private static readonly AccessTools.FieldRef<Player, IDoodadController>
            DoodadController =
                AccessTools.FieldRefAccess<Player, IDoodadController>(
                    "m_doodadController");

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (Plugin.InteractWithBuildTool == null ||
                !Plugin.InteractWithBuildTool.Value ||
                __instance == null ||
                !__instance.InPlaceMode() ||
                __instance.IsDead() ||
                DoodadController(__instance) != null)
                return;

            ItemDrop.ItemData tool = __instance.RightItem;
            if (tool == null ||
                tool.m_shared == null ||
                tool.m_shared.m_buildPieces == null)
                return;

            GameObject hovering;
            Character hoveringCreature;

            FindHoverObject(
                __instance,
                out hovering,
                out hoveringCreature);

            Hovering(__instance) = hovering;
            HoveringCreature(__instance) = hoveringCreature;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "DoInteractAnimation")]
    internal static class ToolInteractAnimationPatch
    {
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation>
            Zanim =
                AccessTools.FieldRefAccess<Character, ZSyncAnimation>(
                    "m_zanim");

        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance)
        {
            Player player = __instance as Player;
            if (Plugin.InteractWithBuildTool == null ||
                !Plugin.InteractWithBuildTool.Value ||
                player == null ||
                !player.InPlaceMode())
                return;

            ItemDrop.ItemData tool = player.RightItem;
            if (tool == null ||
                tool.m_shared == null ||
                tool.m_shared.m_buildPieces == null ||
                tool.m_shared.m_attack == null)
                return;

            ZSyncAnimation zanim = Zanim(player);
            if (zanim == null ||
                string.IsNullOrEmpty(
                    tool.m_shared.m_attack.m_attackAnimation))
                return;

            zanim.SetTrigger(
                tool.m_shared.m_attack.m_attackAnimation);
        }
    }

    [HarmonyPatch(typeof(Player), "RemovePiece")]
    internal static class RemoveEmptyVehicleWithHammerPatch
    {
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation>
            Zanim =
                AccessTools.FieldRefAccess<Character, ZSyncAnimation>(
                    "m_zanim");

        private static readonly int VehicleRemoveMask =
            LayerMask.GetMask(
                "Default",
                "static_solid",
                "Default_small",
                "piece",
                "piece_nonsolid",
                "terrain",
                "vehicle");

        [HarmonyPrefix]
        private static bool Prefix(
            Player __instance,
            ref bool __result)
        {
            if (Plugin.RemoveEmptyVehicles == null ||
                !Plugin.RemoveEmptyVehicles.Value ||
                __instance == null ||
                GameCamera.instance == null)
                return true;

            ItemDrop.ItemData tool = __instance.RightItem;
            if (tool == null ||
                tool.m_shared == null ||
                tool.m_shared.m_buildPieces == null ||
                !tool.m_shared.m_buildPieces.m_canRemovePieces)
                return true;

            RaycastHit hit;
            if (!Physics.Raycast(
                    GameCamera.instance.transform.position,
                    GameCamera.instance.transform.forward,
                    out hit,
                    50f,
                    VehicleRemoveMask))
                return true;

            if (Vector3.Distance(
                    hit.point,
                    __instance.GetEyePoint()) >=
                __instance.m_maxPlaceDistance)
                return true;

            Ship ship =
                hit.collider.GetComponentInParent<Ship>();
            Vagon cart =
                hit.collider.GetComponentInParent<Vagon>();

            if (ship == null && cart == null)
                return true;

            Component vehicle =
                ship != null
                    ? (Component)ship
                    : cart;

            Piece piece =
                vehicle.GetComponentInParent<Piece>() ??
                vehicle.GetComponentInChildren<Piece>(true);

            if (piece == null)
                return true;

            if (Location.IsInsideNoBuildLocation(
                    piece.transform.position))
            {
                __instance.Message(
                    MessageHud.MessageType.Center,
                    "$msg_nobuildzone");
                __result = false;
                return false;
            }

            if (!PrivateArea.CheckAccess(
                    piece.transform.position))
            {
                __instance.Message(
                    MessageHud.MessageType.Center,
                    "$msg_privatezone");
                __result = false;
                return false;
            }

            CraftingStation required =
                piece.m_craftingStation;

            string stationName =
                required != null
                    ? required.m_name
                    : "$piece_workbench";

            if (CraftingStation.HaveBuildStationInRange(
                    stationName,
                    __instance.transform.position) == null)
            {
                __instance.Message(
                    MessageHud.MessageType.Center,
                    "$msg_missingstation");
                __result = false;
                return false;
            }

            Container container =
                cart != null
                    ? cart.m_container
                    : piece.GetComponentInChildren<Container>(true);

            if (container != null &&
                container.GetInventory() != null &&
                container.GetInventory().NrOfItems() > 0)
            {
                __instance.Message(
                    MessageHud.MessageType.Center,
                    "$msg_cantremovenow");
                __result = false;
                return false;
            }

            if (ship != null && !ship.CanBeRemoved())
            {
                __instance.Message(
                    MessageHud.MessageType.Center,
                    "$msg_cantremovenow");
                __result = false;
                return false;
            }

            if (cart != null && cart.InUse())
            {
                __instance.Message(
                    MessageHud.MessageType.Center,
                    "$msg_cantremovenow");
                __result = false;
                return false;
            }

            piece.GetComponent<IRemoved>()?.OnRemoved();

            WearNTear wear =
                piece.GetComponent<WearNTear>();

            if (wear != null)
            {
                wear.Remove();
            }
            else
            {
                ZNetView view =
                    piece.GetComponent<ZNetView>();

                if (view == null)
                {
                    __result = false;
                    return false;
                }

                view.ClaimOwnership();
                piece.DropResources();
                ZNetScene.instance.Destroy(
                    piece.gameObject);
            }

            __instance.FaceLookDirection();

            ZSyncAnimation zanim = Zanim(__instance);
            if (zanim != null &&
                !string.IsNullOrEmpty(
                    tool.m_shared.m_attack.m_attackAnimation))
            {
                zanim.SetTrigger(
                    tool.m_shared.m_attack.m_attackAnimation);
            }

            __result = true;
            return false;
        }
    }
}
