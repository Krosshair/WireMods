using HarmonyLib;

namespace WiresShadows
{
    // The game's ambient occlusion (Amplify Occlusion) is laid over the finished image, so it
    // darkens faces a fire or the sun lights directly as much as faces in the shade. Its
    // Deferred method puts it into the G-buffer before lighting instead: only the ambient light
    // is darkened. Set once on the camera when it wakes and when a setting changes; Amplify
    // rebuilds its command buffers on its own when the method changes.
    internal static class AmbientOcclusion
    {
        private static readonly AccessTools.FieldRef<CameraEffects, AmplifyOcclusionEffect> Effect =
            AccessTools.FieldRefAccess<CameraEffects, AmplifyOcclusionEffect>("m_amplifyOcclusion");

        internal static void Apply()
        {
            CameraEffects effects = CameraEffects.instance;
            if (effects == null) return;
            AmplifyOcclusionEffect ao = Effect(effects);
            if (ao == null) return;
            // Off with the mod (the benchmark's vanilla phase too): vanilla's overlay.
            bool ambientOnly = Plugin.UseShader.Value && Plugin.AmbientOcclusionAmbientOnly.Value;
            ao.ApplyMethod = ambientOnly
                ? AmplifyOcclusionEffect.ApplicationMethod.Deferred
                : AmplifyOcclusionEffect.ApplicationMethod.PostEffect;
        }

        [HarmonyPatch(typeof(CameraEffects), "Awake")]
        private static class OnCamera
        {
            private static void Postfix() => Apply();
        }
    }
}
