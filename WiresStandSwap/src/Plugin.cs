using System.Collections;
using BepInEx;
using BepInEx.Configuration;
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
        public const string Version = "0.2.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        private Harmony _harmony;

        // What Shift + E swaps. Off types are left to the game: they stay
        // on the stand and nothing of yours is taken for them.
        internal static ConfigEntry<bool> SwapArmor;
        internal static ConfigEntry<bool> SwapUtility;
        internal static ConfigEntry<bool> SwapWeapons;
        internal static ConfigEntry<bool> TapToRotate;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            SwapArmor = Config.Bind("1 - Swapping", "Armor", true,
                "Helmets, chest pieces, legs and capes are swapped.");
            SwapUtility = Config.Bind("1 - Swapping", "Utility items", true,
                "Belts, trinkets and other utility items are swapped.");
            SwapWeapons = Config.Bind("1 - Swapping", "Weapons, shields and tools", true,
                "Weapons, shields and tools are swapped.");
            TapToRotate = Config.Bind("2 - Item stands", "Tap E to rotate", true,
                "Shift + E swaps gear on item stands, so turning the item there moves to a quick tap of E. Holding E still takes the item, as in the game. Off is the game's Shift + E to rotate, on item stands holding something this mod doesn't swap.");
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
        internal static bool Allowed(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return Plugin.SwapArmor.Value;
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return Plugin.SwapUtility.Value;
                default:
                    return Plugin.SwapWeapons.Value;
            }
        }

        // An item stand's item that this mod swaps, with the current settings.
        internal static bool Swapped(int prefabHash)
        {
            if (!StandSwaps.IsEquipmentItem(prefabHash)) return false;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && Allowed(drop.m_itemData.m_shared.m_itemType);
        }

        // After a Shift + E swap, the same E press can't go on to a hold
        // interaction (taking the item off the stand) until E comes back up.
        internal static bool HoldBlocked;

        private static bool ShiftHeld()
        {
            return ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
        }
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

                if (!Swapped(__instance.GetAttachedItem()))
                    return true;

                __result = StandSwaps.TryItemStand(
                    __instance, (Player)user);
                HoldBlocked = true;
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
                // Holds only come while E stays down; a new press clears it.
                if (!hold)
                    HoldBlocked = false;
                else if (HoldBlocked)
                    return false;
                if (go == null ||
                    !(ZInput.GetKey(KeyCode.LeftShift) ||
                      ZInput.GetKey(KeyCode.RightShift)) ||
                    !ZInput.GetKey(KeyCode.E))
                    return true;

                ArmorStand stand = StandRegistry.Resolve(go);
                if (stand == null)
                    return true;
                if (!hold)
                {
                    ArmorLoadouts.TryStart(stand, __instance);
                    HoldBlocked = true;
                }
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
                    !Swapped(__instance.GetAttachedItem()) ||
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
        // Tap E turns the item on an item stand. The game takes the item
        // after holding E for 0.2 s, so the turn waits for the key to come
        // back up before then; a hold is left to the game. Only runs for
        // that fraction of a second after a press.
        [HarmonyPatch(typeof(ItemStand), "Interact")]
        private static class TapRotatePatch
        {
            [HarmonyPrefix]
            private static void Prefix(ItemStand __instance, Humanoid user, bool hold, bool alt)
            {
                if (hold || alt || !Plugin.TapToRotate.Value || Plugin.Instance == null ||
                    user == null || user != Player.m_localPlayer || ShiftHeld() ||
                    ZInput.IsGamepadActive() || !__instance.HaveAttachment() ||
                    __instance.m_guardianPower != null)
                    return;
                Plugin.Instance.StartCoroutine(RotateOnRelease(__instance, (Player)user));
            }

            private static IEnumerator RotateOnRelease(ItemStand stand, Player player)
            {
                float start = Time.time;
                while (Time.time - start < 0.2f)
                {
                    yield return null;
                    if (ZInput.GetButton("Use")) continue;
                    if (stand != null && player != null && stand.HaveAttachment() &&
                        player.GetHoverObject() != null &&
                        player.GetHoverObject().GetComponentInParent<ItemStand>() == stand)
                        stand.Interact(player, false, true); // the game's own turn
                    yield break;
                }
            }
        }

        // The game's "Shift + E: change orientation" line becomes "E".
        [HarmonyPatch(typeof(ItemStand), "GetHoverText")]
        private static class RotateHoverPatch
        {
            private static string _from, _to, _language;

            [HarmonyPostfix]
            private static void Postfix(ref string __result)
            {
                if (!Plugin.TapToRotate.Value || ZInput.IsGamepadActive() ||
                    Localization.instance == null || string.IsNullOrEmpty(__result))
                    return;
                string language = Localization.instance.GetSelectedLanguage();
                if (_from == null || _language != language)
                {
                    _language = language;
                    _from = Localization.instance.Localize("[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_changeorientation");
                    _to = Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] $hud_changeorientation");
                }
                if (__result.Contains(_from)) __result = __result.Replace(_from, _to);
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