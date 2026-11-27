using HarmonyLib;
using UnityEngine;

namespace WiresShakeItUp
{
    // Opt-in wide lens: a wider field of view with barrel distortion, which
    // magnifies the centre a little and keeps the edges where they were.
    internal static class WideLens
    {
        internal static bool On => ModConfig.WideLensEnabled.Value && LensEffect.Ready;
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class WideLensFovPatch
    {
        private static readonly AccessTools.FieldRef<GameCamera, Camera> CameraRef =
            AccessTools.FieldRefAccess<GameCamera, Camera>("m_camera");
        private static readonly AccessTools.FieldRef<GameCamera, bool> FreeFlyRef =
            AccessTools.FieldRefAccess<GameCamera, bool>("m_freeFly");

        [HarmonyPostfix]
        private static void Postfix(GameCamera __instance)
        {
            if (!WideLens.On || FreeFlyRef(__instance)) return;
            // UpdateCamera sets both cameras to m_fov every frame; widen on top of that.
            float fov = __instance.m_fov + ModConfig.WideLensFov.Value;
            Camera camera = CameraRef(__instance);
            if (camera != null) camera.fieldOfView = fov;
            if (__instance.m_skyCamera != null) __instance.m_skyCamera.fieldOfView = fov;
        }
    }
}
