using HarmonyLib;
using UnityEngine;

namespace WiresShadows
{
    // The game's ambient occlusion (Amplify Occlusion) is laid over the finished image, so it
    // darkens faces a fire or the sun lights directly as much as faces in the shade. Its
    // Deferred method puts it into the G-buffer before lighting instead: only the ambient light
    // is darkened (its tint too: Amplify multiplies the ambient by the tinted occlusion). Amplify
    // rebuilds its command buffers on its own when the method changes.
    //
    // With that on, it's also tuned (vanilla in brackets): a 1 m reach near you (3 m), which
    // darkens corners and where things meet instead of throwing wide halos; far away still
    // treated as thin (vanilla thickens it with distance, haloing whatever stands in front of
    // something farther); and stronger, since it now only has the ambient light to work on:
    // exponent 4 (3) and a quarter more intensity, at most full (day 0.8, night 1). No source for
    // the numbers: judgement values.
    internal static class AmbientOcclusion
    {
        private const float Radius = 1f, Power = 4f, FarThickness = 0f, IntensityScale = 1.25f;

        private static readonly AccessTools.FieldRef<CameraEffects, AmplifyOcclusionEffect> Effect =
            AccessTools.FieldRefAccess<CameraEffects, AmplifyOcclusionEffect>("m_amplifyOcclusion");

        // Vanilla's values, kept to put back when it's switched off.
        private static AmplifyOcclusionEffect s_for;
        private static float s_radius, s_power, s_farThickness;
        private static float s_envIntensity = -1f;

        private static bool On => Plugin.UseShader.Value && Plugin.AmbientOcclusionAmbientOnly.Value;

        internal static void Apply()
        {
            CameraEffects effects = CameraEffects.instance;
            if (effects == null) return;
            AmplifyOcclusionEffect ao = Effect(effects);
            if (ao == null) return;
            if (s_for != ao)
            {
                s_for = ao;
                s_radius = ao.Radius;
                s_power = ao.PowerExponent;
                s_farThickness = ao.FadeToThickness;
            }
            bool on = On;
            // Off with the mod too (the benchmark's vanilla phase): vanilla's overlay.
            ao.ApplyMethod = on ? AmplifyOcclusionEffect.ApplicationMethod.Deferred
                                : AmplifyOcclusionEffect.ApplicationMethod.PostEffect;
            ao.Radius = on ? Radius : s_radius;
            ao.PowerExponent = on ? Power : s_power;
            ao.FadeToThickness = on ? FarThickness : s_farThickness;
            if (s_envIntensity >= 0f)
                ao.Intensity = Intensity(s_envIntensity, on);
        }

        private static float Intensity(float env, bool on) => on ? Mathf.Min(1f, env * IntensityScale) : env;

        [HarmonyPatch(typeof(CameraEffects), "Awake")]
        private static class OnCamera
        {
            private static void Postfix() => Apply();
        }

        // The weather sets the strength as it changes; scale it on the way in.
        [HarmonyPatch(typeof(CameraEffects), nameof(CameraEffects.SetEnvironmentAOParams))]
        private static class OnWeather
        {
            private static void Prefix(ref float intensity)
            {
                s_envIntensity = intensity;
                intensity = Intensity(intensity, On);
            }
        }
    }
}
