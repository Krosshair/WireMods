using System.Reflection;
using AmplifyOcclusion;
using HarmonyLib;
using UnityEngine;

namespace WiresShadows
{
    // The game's ambient occlusion (Amplify Occlusion, GTAO) coloured by the surface it lands on:
    // GTAO multi-bounce (see WireAOApply.shader). Amplify's apply shader is swapped for ours,
    // which is the same with that one step added; everything else (method, reach, strength,
    // tint) stays the game's. Swapped when Amplify makes its materials and when the mod is
    // switched on or off; nothing per frame.
    internal static class AmbientOcclusion
    {
        internal static Shader BounceShader;
        // the mod's AO: Enabled and its own setting (to compare it alone)
        private static bool On => Plugin.UseShader.Value && (Plugin.ModAO == null || Plugin.ModAO.Value);
        private static Shader s_vanillaApply;
        private static readonly FieldInfo ApplyMat = AccessTools.Field(typeof(AmplifyOcclusionEffect), "m_applyOcclusionMat");

        internal static void Apply()
        {
            Material mat = ApplyMat != null ? ApplyMat.GetValue(null) as Material : null;
            if (mat == null || BounceShader == null || !BounceShader.isSupported) return;
            if (s_vanillaApply == null && mat.shader != BounceShader) s_vanillaApply = mat.shader;
            // Off with the mod (the benchmark's vanilla phase too): the game's own.
            Shader want = On || s_vanillaApply == null ? BounceShader : s_vanillaApply;
            if (mat.shader != want) mat.shader = want;
        }

        // Full resolution. The game runs its AO at half resolution on every setting (CameraEffects.SetSSAO,
        // Downsample), and far away it reaches 50 m (FadeToRadius, _GameMain): distant grass and trees
        // moving in the wind got coarse, sparse occlusion that the temporal filter couldn't hold, so it
        // shimmered (more so at a lower render scale). Same reach, fade and strength; only the resolution.
        private static readonly AccessTools.FieldRef<CameraEffects, AmplifyOcclusionEffect> CameraAO =
            AccessTools.FieldRefAccess<CameraEffects, AmplifyOcclusionEffect>("m_amplifyOcclusion");

        // And the same sample directions every frame. The game's AO rotates them each frame and averages
        // the frames (Amplify's temporal filter); grass and trees swaying in the wind write no motion,
        // so the filter throws their history away and the raw, differently rotated frames show: shimmer.
        // Without the filter the rotation stays at 0 (AmplifyOcclusionEffect: _AO_TemporalDirections),
        // so AO only changes when the scene does. One sample level more and a second blur pass (picked)
        // make up for the averaging. Off with the mod: the game's own settings.
        private static SampleCountLevel s_gameSamples = SampleCountLevel.Medium;

        internal static void Resolution()
        {
            AmplifyOcclusionEffect ao = CameraEffects.instance != null ? CameraAO(CameraEffects.instance) : null;
            if (ao == null) return;
            bool mod = On;
            ao.Downsample = !mod;
            ao.FilterEnabled = !mod;
            ao.BlurPasses = mod ? 2 : 1;
            ao.SampleCount = mod && s_gameSamples < SampleCountLevel.VeryHigh ? s_gameSamples + 1 : s_gameSamples;
        }

        [HarmonyPatch(typeof(CameraEffects), "SetSSAO")]
        private static class OnSSAO
        {
            private static void Postfix(AmplifyOcclusionEffect ___m_amplifyOcclusion)
            {
                if (___m_amplifyOcclusion != null) s_gameSamples = ___m_amplifyOcclusion.SampleCount; // what the game just chose
                Resolution();
            }
        }

        // Amplify makes its materials as it's enabled.
        [HarmonyPatch(typeof(AmplifyOcclusionEffect), "OnEnable")]
        private static class OnAmplify
        {
            private static void Postfix() => Apply();
        }
    }
}
