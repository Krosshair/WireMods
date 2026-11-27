using HarmonyLib;
using UnityEngine;

namespace WiresShakeItUp
{
    [HarmonyPatch(typeof(GameCamera), "Awake")]
    internal static class GameCameraAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            ShakeEngine.ResetTransient();
        }
    }

    [HarmonyPatch(
        typeof(GameCamera),
        nameof(GameCamera.GetAttenuatedShakeIntensity))]
    internal static class ShakeAmplitudePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            ref float __result)
        {
            __result =
                ShakeEngine.ShapeRumble(
                    __instance,
                    __result);
        }
    }

    [HarmonyPatch(
        typeof(GameCamera),
        nameof(GameCamera.AddShake))]
    internal static class AddShakePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            GameCamera __instance,
            out float __state)
        {
            __state =
                ShakeEngine.CurrentIntensity(
                    __instance);
        }

        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            Vector3 point,
            float range,
            float strength,
            float __state)
        {
            ShakeEngine.ObserveShake(
                __instance,
                point,
                range,
                strength,
                __state);
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCameraShake")]
    internal static class UpdateCameraShakePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            GameCamera __instance,
            out float __state)
        {
            __state = __instance.m_shakeFreq;

            if (ShakeEngine.Enabled(__instance))
            {
                __instance.m_shakeFreq =
                    __state *
                    ModConfig.FrequencyMultiplier.Value;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            float dt,
            float __state)
        {
            try
            {
                ShakeEngine.AfterCameraShake(
                    __instance,
                    dt);
            }
            finally
            {
                __instance.m_shakeFreq = __state;
            }
        }
    }
}
