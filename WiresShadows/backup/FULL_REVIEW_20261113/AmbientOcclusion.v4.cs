using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // The game's ambient occlusion (Amplify Occlusion) is laid over the finished image, so it
    // darkens faces a fire or the sun lights directly as much as faces in the shade. Its
    // Deferred method puts it into the G-buffer before lighting instead: only the ambient light
    // is darkened (its tint too: Amplify multiplies the ambient by the tinted occlusion). Amplify
    // rebuilds its command buffers on its own when the method changes.
    //
    // With that on, it's also tuned (vanilla in brackets): far away still treated as thin
    // (vanilla thickens it with distance, haloing whatever stands in front of something
    // farther), and stronger, since it now only has the ambient light to work on: exponent 4 (3)
    // and a quarter more intensity, at most full (day 0.8, night 1). Its reach stays vanilla's
    // 3 m: at 1 m the shade under trees and roofs lost its darkening and looked lit up. No
    // source for the numbers: judgement values.
    //
    // And coloured by the surface (GTAO multi-bounce, see WireAOApply.shader): Amplify's own
    // apply shader is swapped for ours, which is the same with that one step added. It needs the
    // surface colour while it writes into the G-buffer, so the albedo is copied out first, once
    // a frame, by a step added to Amplify's command buffer when Amplify builds it (not by us
    // every frame).
    internal static class AmbientOcclusion
    {
        internal static Shader BounceShader;
        private static Shader s_vanillaApply;
        private static readonly FieldInfo ApplyMat = AccessTools.Field(typeof(AmplifyOcclusionEffect), "m_applyOcclusionMat");
        private static readonly int AlbedoId = Shader.PropertyToID("_WireAOAlbedo");

        private const float Power = 4f, FarThickness = 0f, IntensityScale = 1.25f;

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
            ao.Radius = s_radius;
            ao.PowerExponent = on ? Power : s_power;
            ao.FadeToThickness = on ? FarThickness : s_farThickness;
            if (s_envIntensity >= 0f)
                ao.Intensity = Intensity(s_envIntensity, on);
            SwapShader();
        }

        private static Material Mat => ApplyMat != null ? ApplyMat.GetValue(null) as Material : null;
        private static bool Bouncing => BounceShader != null && Mat is Material m && m.shader == BounceShader;

        private static void SwapShader()
        {
            Material mat = Mat;
            if (mat == null || BounceShader == null || !BounceShader.isSupported) return;
            if (s_vanillaApply == null && mat.shader != BounceShader) s_vanillaApply = mat.shader;
            Shader want = On || s_vanillaApply == null ? BounceShader : s_vanillaApply;
            if (mat.shader != want) mat.shader = want;
        }

        // Amplify makes its materials as it's enabled.
        [HarmonyPatch(typeof(AmplifyOcclusionEffect), "OnEnable")]
        private static class OnAmplify
        {
            private static void Postfix() => SwapShader();
        }

        // The albedo copy, ahead of the apply passes (the LDR path has Amplify's own copy).
        [HarmonyPatch(typeof(AmplifyOcclusionEffect), "commandBuffer_FillApplyDeferred")]
        private static class OnApplyDeferred
        {
            private static void Prefix(CommandBuffer cb, bool logTarget, out bool __state)
            {
                __state = !logTarget && Bouncing;
                if (!__state) return;
                cb.GetTemporaryRT(AlbedoId, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                cb.Blit(BuiltinRenderTextureType.GBuffer0, AlbedoId);
                cb.SetGlobalTexture(AlbedoId, AlbedoId);
            }

            private static void Postfix(CommandBuffer cb, bool __state)
            {
                if (__state) cb.ReleaseTemporaryRT(AlbedoId);
            }
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
