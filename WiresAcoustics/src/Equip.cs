using HarmonyLib;

namespace WiresAcoustics
{
    // Equipping: the game places the sound at the hand with no owner given, so it stays there while
    // you move (heard trailing behind). It follows the character equipping instead (Follow).
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
