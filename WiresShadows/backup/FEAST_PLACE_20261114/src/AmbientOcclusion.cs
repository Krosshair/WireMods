using System.Reflection;
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
        private static Shader s_vanillaApply;
        private static readonly FieldInfo ApplyMat = AccessTools.Field(typeof(AmplifyOcclusionEffect), "m_applyOcclusionMat");

        internal static void Apply()
        {
            Material mat = ApplyMat != null ? ApplyMat.GetValue(null) as Material : null;
            if (mat == null || BounceShader == null || !BounceShader.isSupported) return;
            if (s_vanillaApply == null && mat.shader != BounceShader) s_vanillaApply = mat.shader;
            // Off with the mod (the benchmark's vanilla phase too): the game's own.
            Shader want = Plugin.UseShader.Value || s_vanillaApply == null ? BounceShader : s_vanillaApply;
            if (mat.shader != want) mat.shader = want;
        }

        // Amplify makes its materials as it's enabled.
        [HarmonyPatch(typeof(AmplifyOcclusionEffect), "OnEnable")]
        private static class OnAmplify
        {
            private static void Postfix() => Apply();
        }
    }
}
