using BepInEx;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace WiresStandSwap
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.standswap";
        public const string Name = "Wire's Stand Swap";
        public const string Version = "0.2.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Instance = null;
            Log = null;
            StandSwaps.Clear();
            ArmorLoadouts.Clear();
            StandRegistry.Clear();
            UsedItems.Clear();
        }
    }

    internal static class InputAndEquipment
    {
        internal static bool Pressed(Humanoid user, bool hold)
        {
            return !hold &&
                user != null &&
                user == Player.m_localPlayer &&
                (ZInput.GetKey(KeyCode.LeftShift) ||
                 ZInput.GetKey(KeyCode.RightShift)) &&
                ZInput.GetKey(KeyCode.E);
        }

        [HarmonyPatch(typeof(ItemStand), "Interact")]
        private static class ItemStandPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                ItemStand __instance, Humanoid user,
                bool hold, ref bool __result)
            {
                if (!Pressed(user, hold) ||
                    !__instance.m_canBeRemoved ||
                    __instance.m_guardianPower != null ||
                    !__instance.HaveAttachment())
                    return true;

                if (!StandSwaps.IsEquipmentItem(
                    __instance.GetAttachedItem()))
                    return true;

                __result = StandSwaps.TryItemStand(
                    __instance, (Player)user);
                return false;
            }
        }

        // Physical Shift + E on ANY clickable portion of the
        // armor stand invokes one full-loadout transaction.
        // Plain E never reaches this branch and stays vanilla.
        [HarmonyPatch(typeof(Player), "Interact")]
        private static class FullLoadoutPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                Player __instance, GameObject go, bool hold)
            {
                if (go == null ||
                    !(ZInput.GetKey(KeyCode.LeftShift) ||
                      ZInput.GetKey(KeyCode.RightShift)) ||
                    !ZInput.GetKey(KeyCode.E))
                    return true;

                ArmorStand stand = StandRegistry.Resolve(go);
                if (stand == null)
                    return true;
                if (!hold)
                    ArmorLoadouts.TryStart(stand, __instance);
                return false;
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
        [HarmonyPatch(typeof(ItemStand), "GetHoverText")]
        private static class ItemStandHoverPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                ItemStand __instance, ref string __result)
            {
                if (!__instance.m_canBeRemoved ||
                    __instance.m_guardianPower != null ||
                    !__instance.HaveAttachment() ||
                    !StandSwaps.IsEquipmentItem(
                        __instance.GetAttachedItem()) ||
                    !PrivateArea.CheckAccess(
                        __instance.transform.position,
                        0f, flash: false))
                    return;
                __result +=
                    "\n[<color=yellow><b>Shift + E</b></color>] " +
                    StandSwaps.DescribeAction(
                        __instance.GetAttachedItem(),
                        __instance);
            }
        }

        [HarmonyPatch(typeof(Switch), "GetHoverText")]
        private static class ArmorStandHoverPatch
        {
            private const string Shortcut =
                "\n[<color=yellow><b>Shift + E</b></color>] Swap full loadout";

            [HarmonyPostfix]
            private static void Postfix(
                Switch __instance, ref string __result)
            {
                // Only a weak dictionary lookup. Never scan the
                // scene or inventory every hovering frame.
                if (StandRegistry.ForSwitch(__instance) != null)
                    __result += Shortcut;
            }
        }
        [HarmonyPatch(typeof(Humanoid), "SetupEquipment")]
        private static class BatchEquipmentRefreshPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Humanoid __instance)
            {
                return __instance != Player.m_localPlayer ||
                       !ArmorLoadouts.SuppressEquipmentRefresh;
            }
        }

        [HarmonyPatch(typeof(EffectList), "Create")]
        private static class BatchEquipmentEffectsPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                EffectList __instance,
                ref GameObject[] __result)
            {
                if (!ArmorLoadouts.ShouldSuppressEffect(__instance))
                    return true;

                __result = new GameObject[0];
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "EquipItem")]
        private static class TrackEquipmentPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                Humanoid __instance,
                ItemDrop.ItemData item,
                bool __result)
            {
                if (__result &&
                    __instance == Player.m_localPlayer)
                    UsedItems.Record(item);
            }
        }
    }
}