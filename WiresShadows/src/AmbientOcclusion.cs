using System.Reflection;
using AmplifyOcclusion;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // The game's ambient occlusion (Amplify Occlusion, GTAO) coloured by the surface it lands on:
    // GTAO multi-bounce (see WireAOApply.shader). Amplify's apply shader is swapped for ours,
    // which is the same with that one step added; everything else (method, reach, strength,
    // tint) stays the game's. Swapped when Amplify makes its materials and when the mod is
    // switched on or off. Per frame only two values added to what Amplify records anyway.
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

        // The same sample directions every frame. The game's AO rotates them each frame and lets its
        // temporal filter average the frames; grass and trees swaying in the wind write no motion, so the
        // filter throws their history away and the raw, differently rotated frames show: shimmer. Held at
        // 0, every frame samples alike: the filter still averages away the flicker of the half-resolution
        // AO on moving leaves, and where it throws history away the frame looks the same anyway.
        // Amplify sets them once per frame while it records its parameters; ours go in right after.
        private static readonly int DirectionsId = Shader.PropertyToID("_AO_TemporalDirections");
        private static readonly int OffsetsId = Shader.PropertyToID("_AO_TemporalOffsets");

        [HarmonyPatch(typeof(AmplifyOcclusionEffect), "UpdateGlobalShaderConstants_AmbientOcclusion")]
        private static class FixedDirections
        {
            private static void Postfix(CommandBuffer cb)
            {
                if (!On || cb == null) return;
                cb.SetGlobalFloat(DirectionsId, 0f);
                cb.SetGlobalFloat(OffsetsId, 0f);
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
