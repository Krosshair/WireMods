using System;
using HarmonyLib;
using UnityEngine;

namespace WiresShadows
{
    // Runs inside Valheim's existing IMonoUpdater.CustomUpdate call.
    // Does NOT add Unity Update / LateUpdate, coroutines or polling.
    [HarmonyPatch(typeof(LightFlicker), nameof(LightFlicker.CustomUpdate))]
    internal static class DistantLightFlicker
    {
        private static readonly AccessTools.FieldRef<LightFlicker, Light> SourceLight =
            AccessTools.FieldRefAccess<LightFlicker, Light>("m_light");
        private static readonly AccessTools.FieldRef<LightFlicker, float> Lifetime =
            AccessTools.FieldRefAccess<LightFlicker, float>("m_time");
        private static readonly AccessTools.FieldRef<LightFlicker, float> BaseIntensity =
            AccessTools.FieldRefAccess<LightFlicker, float>("m_baseIntensity");
        private static readonly AccessTools.FieldRef<LightFlicker, float> Multiplier =
            AccessTools.FieldRefAccess<LightFlicker, float>("m_multiplier");
        private static readonly AccessTools.FieldRef<LightFlicker, float> Smoothed =
            AccessTools.FieldRefAccess<LightFlicker, float>("m_smoothedIntensity");

        private static readonly Plane[] ViewPlanes = new Plane[6];
        private static int _planesFrame = -1;
        private static int _cameraId;
        private static Vector3 _cameraPosition;

        [HarmonyPrefix, HarmonyPriority(Priority.High)]
        private static bool SkipInvisibleOrDistantFlicker(LightFlicker __instance)
        {
            if (Plugin.UseShader == null || !Plugin.UseShader.Value ||
                Plugin.FlickerCulling == null || !Plugin.FlickerCulling.Value)
                return true;

            Light source = SourceLight(__instance);
            if (source == null || !source.enabled || !source.isActiveAndEnabled)
                return true;

            // These run their actual vanilla time/fade/expiry logic.
            // Never pause a TTL, fade-in, fade-out, or smooth-flicker light.
            if (__instance.m_ttl > 0f ||
                __instance.m_fadeInDuration > 0f &&
                Lifetime(__instance) <= __instance.m_fadeInDuration ||
                Settings.ReduceFlashingLights &&
                __instance.m_flashingLightsSetting ==
                    LightFlicker.LightFlashSettings.SmoothedFlicker)
                return true;

            Camera view = Utils.GetMainCamera();
            if (view == null) return true;

            Vector3 sourcePosition = source.transform.position;
            Vector3 eye = view.transform.position;
            // Distance is from the PLAYER, not the third-person camera;
            // camera frustum separately decides whether any light reaches the view.
            Vector3 observer = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position : eye;
            float distance = Mathf.Max(0f, Plugin.FlickerDistance.Value);
            bool far = (sourcePosition - observer).sqrMagnitude > distance * distance;

            if (!far)
            {
                int frame = Time.frameCount;
                int id = view.GetInstanceID();
                if (_planesFrame != frame || _cameraId != id ||
                    _cameraPosition != eye)
                {
                    GeometryUtility.CalculateFrustumPlanes(view, ViewPlanes);
                    _planesFrame = frame;
                    _cameraId = id;
                    _cameraPosition = eye;
                }
                // Same complete-light bounds that Wire's Shadows already
                // uses to reject offscreen moving shadow maps.
                float radius = Mathf.Max(0.01f, source.range);
                bool visible = GeometryUtility.TestPlanesAABB(ViewPlanes,
                    new Bounds(sourcePosition, Vector3.one * (radius * 2f)));
                if (visible) return true;
            }

            // No visual freeze at an arbitrary part of the sine cycle.
            // Hold steady authored brightness and preserve accessibility.
            float intensity;
            if (Settings.ReduceFlashingLights &&
                __instance.m_flashingLightsSetting == LightFlicker.LightFlashSettings.Off)
                intensity = 0f;
            else if (Settings.ReduceFlashingLights &&
                __instance.m_flashingLightsSetting == LightFlicker.LightFlashSettings.AlwaysOn)
                intensity = 1f;
            else
                intensity = BaseIntensity(__instance) * Multiplier(__instance);
            if (source.intensity != intensity)
                source.intensity = intensity;
            Smoothed(__instance) = 1f;
            return false;
        }
    }
}
