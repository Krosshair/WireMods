using HarmonyLib;

namespace WiresSmoothUI
{
    // Floating damage numbers off (setting): the game's one place that puts any of them on screen.
    [HarmonyPatch(typeof(DamageText), "AddInworldText")]
    internal static class DamageNumbers
    {
        private static bool Prefix() => WiresSmoothUIPlugin.HideDamageNumbers == null || !WiresSmoothUIPlugin.HideDamageNumbers.Value;
    }
}