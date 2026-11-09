using HarmonyLib;

namespace WiresAcoustics
{
    // A character's own item sounds: the game plays them at the hand or at the character with no owner
    // given (equipping, unequipping, using or eating an item, picking up, dropping), so they would stay
    // where they started while you move. Around those calls the character is the sound's owner, and it
    // follows them (Follow). Nested calls (picking up and equipping) keep the outer owner.
    internal static class Equip
    {
        private static bool Take(Humanoid h)
        {
            if (!Plugin.Enabled.Value || h == null || Follow.Owner != null) return false;
            Follow.Owner = h.transform;
            return true;
        }

        private static void Release(bool taken) { if (taken) Follow.Owner = null; }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Equipping
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        private static class Unequipping
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class Using
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
        private static class PickingUp
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance);
            private static void Finalizer(bool __state) => Release(__state);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        private static class Dropping
        {
            private static void Prefix(Humanoid __instance, out bool __state) => __state = Take(__instance);
            private static void Finalizer(bool __state) => Release(__state);
        }
    }
}
