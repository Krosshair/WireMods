using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // A character's own item sounds: the game plays them at the hand or at the character with no owner
    // given (equipping, unequipping, using or eating an item, picking up, dropping), so they would stay
    // where they started while you move. Around those calls the character is the sound's owner, and it
    // follows them (Follow). The ones the game makes at the character's feet are moved to where they
    // happen (Spots): holstering at the back slot, dropping at the hand, eating at the head, picking up
    // where the item was. Nested calls (picking up and equipping) keep the outer owner.
    internal static class Equip
    {
        private static bool Take(Humanoid h, Transform bone = null, Vector3? point = null)
        {
            if (!Plugin.Enabled.Value || h == null || Follow.Owner != null) return false;
            Follow.Owner = h.transform;
            Spots.Owner = h.transform;
            Spots.Bone = bone;
            Spots.HasPoint = point.HasValue;
            Spots.Point = point ?? default;
            return true;
        }

        private static void Release(bool taken)
        {
            if (!taken) return;
            Follow.Owner = null;
            Spots.Clear();
        }

        private static VisEquipment Vis(Humanoid h) => h != null ? h.GetComponent<VisEquipment>() : null;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Equipping
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        private static class Unequipping
        {
            private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out bool __state)
            {
                Transform slot = Spots.BackSlot(Vis(__instance), item);
                __state = slot != null ? Take(__instance, slot) : Take(__instance, null, __instance != null ? __instance.transform.position + Vector3.up : (Vector3?)null);
            }
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class Using
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance, Vis(__instance)?.m_helmet);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
        private static class PickingUp
        {
            private static void Prefix(Humanoid __instance, GameObject go, out bool __state) =>
                __state = Take(__instance, null, go != null ? go.transform.position : (Vector3?)null);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        private static class Dropping
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance, Vis(__instance)?.m_rightHand);
            private static void Finalizer(bool __state) => Release(__state);
        }
    }
}
