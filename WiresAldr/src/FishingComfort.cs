using HarmonyLib;

namespace WiresAldr
{
    // Valheim already has skill-dependent reeling speed and stamina
    // costs. Gently improve those same properties once per new float;
    // do not add new fishing actions, bait or fish data.
    [HarmonyPatch(typeof(FishingFloat), "Awake")]
    internal static class FishingComfort
    {
        [HarmonyPostfix]
        private static void Postfix(FishingFloat __instance)
        {
            if (__instance == null || Plugin.Enabled == null ||
                !Plugin.Enabled.Value || !Plugin.FishYields.Value) return;

            __instance.m_pullLineSpeed *= 1.25f;
            __instance.m_pullLineSpeedMaxSkill *= 1.25f;
            __instance.m_pullStaminaUse *= 0.8f;
            __instance.m_hookedStaminaPerSec *= 0.82f;
            __instance.m_hookedStaminaPerSecMaxSkill *= 0.82f;
        }
    }
}
