using HarmonyLib;
using UnityEngine;

namespace WiresDecimalsBegone
{
    // Raw display float, before Valheim's "0.#" formatting.
    [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText),
        new[] { typeof(DamageText.TextType), typeof(Vector3),
                typeof(float), typeof(bool) })]
    internal static class FloatNumberPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref float dmg)
        {
            dmg = WholeNumbers.Round(dmg);
        }
    }

    // Other mods may call the string overload directly.
    [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText),
        new[] { typeof(DamageText.TextType), typeof(Vector3),
                typeof(string), typeof(bool) })]
    internal static class StringNumberPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref string text)
        {
            text = WholeNumbers.RoundLabel(text);
        }
    }

    // Display-only receiver: rounds remote labels from players
    // without the mod and leaves all visual styling untouched.
    [HarmonyPatch(typeof(DamageText), "AddInworldText")]
    internal static class IncomingNumberPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref string text)
        {
            text = WholeNumbers.RoundLabel(text);
        }
    }
}
