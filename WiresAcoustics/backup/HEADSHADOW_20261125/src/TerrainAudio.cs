using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Valheim applies a TerrainOp before calling its OnPlaced effect at the
    // ORIGINAL build position. A raised surface can then cover that point.
    // Move the one-shot origin to the newly exposed upper surface before
    // EffectList.Create instantiates/plays the sound; no audio polling.
    [HarmonyPatch(typeof(TerrainOp), "OnPlaced")]
    internal static class TerrainAudio
    {
        private static TerrainOp s_current;

        private static void Prefix(TerrainOp __instance, out TerrainOp __state)
        {
            __state = s_current;
            if (Plugin.Enabled.Value) s_current = __instance;
        }

        private static void Finalizer(TerrainOp __state)
        {
            s_current = __state;
        }

        internal static float RaisedSurfaceHeight(float originalY,
            float raiseDelta, bool hasCurrentHeight, float currentHeight)
        {
            // Heightmap can still contain the pre-operation vertex data if
            // its mesh is queued for rebuild. Use the authored raise delta
            // then, but prefer the real changed surface when it is already
            // available. A small 16 cm air clearance avoids a sound exactly
            // on the ground being interpreted as inside the ground collider.
            float y = originalY + Mathf.Max(0f, raiseDelta);
            if (hasCurrentHeight)
                y = Mathf.Max(y, currentHeight);
            return y + .16f;
        }

        internal static void Place(EffectList list, ref Vector3 at)
        {
            TerrainOp op = s_current;
            if (op == null || op.m_settings == null ||
                !ReferenceEquals(list, op.m_onPlacedEffect) ||
                !op.m_settings.m_raise ||
                op.m_settings.m_raiseDelta <= 0f) return;
            Vector3 atOperation = op.transform.position;
            bool measured = Heightmap.GetHeight(atOperation, out float ground);
            at.y = Mathf.Max(at.y, RaisedSurfaceHeight(atOperation.y,
                op.m_settings.m_raiseDelta, measured, ground));
        }
    }
}