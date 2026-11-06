using HarmonyLib;

namespace WiresAcoustics
{
    // Equipping: the sound is spawned at the hand with no owner given; it's placed like the player's
    // other sounds (PlayerSounds) and follows the character equipping (Follow).
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class Equip
    {
        private static void Prefix(Humanoid __instance)
        {
            if (Plugin.Enabled.Value) Follow.Owner = __instance.transform;
        }

        private static void Finalizer() => Follow.Owner = null;
    }
}
