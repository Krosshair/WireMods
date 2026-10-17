using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class SmoothUIHudAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (__instance == null)
                return;

            if (__instance.GetComponent<DynamicReticleSystem>() == null)
                __instance.gameObject.AddComponent<DynamicReticleSystem>();

            if (__instance.GetComponent<SmoothUIHudVisibilityReworkDriver>() == null)
            {
                SmoothUIHudVisibilityReworkDriver driver =
                    __instance.gameObject.AddComponent<SmoothUIHudVisibilityReworkDriver>();
                driver.Initialize(__instance);
            }
        }
    }
}
