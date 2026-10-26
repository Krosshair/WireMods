using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Gamepad and keyboard list navigation recenters the list. Glide there
    // instead of jumping, the same way wheel scrolling does.
    [HarmonyPatch(typeof(ScrollRectEnsureVisible), "CenterOnItem")]
    internal static class SmoothScrollCenterPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ScrollRectEnsureVisible __instance, out SmoothScrollSystem.Capture __state)
        {
            __state = SmoothScrollSystem.Begin(__instance.GetComponent<ScrollRect>());
        }

        [HarmonyPostfix]
        private static void Postfix(SmoothScrollSystem.Capture __state)
        {
            SmoothScrollSystem.End(__state);
        }
    }
}
