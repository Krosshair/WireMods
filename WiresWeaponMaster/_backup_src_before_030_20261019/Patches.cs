using System;
using HarmonyLib;

namespace WiresWeaponMaster
{
    internal static class Patches
    {
        [HarmonyPatch(typeof(Player), "Start")]
        private static class PlayerStartPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                WeaponMasterController.Reset();
                WeaponMasterController.SeedCurrentState(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), "OnDestroy")]
        private static class PlayerDestroyPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                MultiplayerSync.ClearLocal();
                HoldRController.ResetTracking();
                WeaponVisuals.Cleanup();
            }
        }
        [HarmonyPatch(typeof(Player), "Update")]
        private static class PlayerUpdatePatch
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance)
            {
                HoldRController.BeforePlayerUpdate(__instance);
            }

            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                HoldRController.AfterPlayerUpdate(__instance);
            }
        }

        [HarmonyPatch(
            typeof(Humanoid),
            nameof(Humanoid.EquipItem),
            new[] { typeof(ItemDrop.ItemData), typeof(bool) })]
        private static class EquipItemPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                Humanoid __instance,
                ItemDrop.ItemData item,
                bool __result)
            {
                if (!__result || __instance != Player.m_localPlayer)
                    return;
                WeaponMasterController.NoteUsed(__instance, item);
                WeaponVisuals.RefreshLocal();
            }
        }

        [HarmonyPatch(
            typeof(Humanoid),
            nameof(Humanoid.HideHandItems),
            new[] { typeof(bool), typeof(bool) })]
        private static class HideHandItemsPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance)
            {
                if (__instance == Player.m_localPlayer)
                    WeaponVisuals.RefreshLocal();
            }
        }

        [HarmonyPatch(
            typeof(Humanoid),
            "ShowHandItems",
            new[] { typeof(bool), typeof(bool) })]
        private static class ShowHandItemsPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance)
            {
                if (__instance == Player.m_localPlayer)
                    WeaponVisuals.RefreshLocal();
            }
        }
        [HarmonyPatch(
            typeof(Inventory),
            "Changed",
            new[] { typeof(bool), typeof(bool) })]
        private static class InventoryChangedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Inventory __instance)
            {
                Player player = Player.m_localPlayer;
                if (player != null &&
                    ReferenceEquals(player.GetInventory(), __instance))
                {
                    WeaponVisuals.RefreshLocal();
                }
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        private static class VisEquipmentUpdatePatch
        {
            [HarmonyPostfix]
            private static void Postfix(VisEquipment __instance)
            {
                WeaponVisuals.SyncNetwork(__instance);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "OnDisable")]
        private static class VisEquipmentDisablePatch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance)
            {
                WeaponVisuals.Unregister(__instance);
            }
        }
    }
}
