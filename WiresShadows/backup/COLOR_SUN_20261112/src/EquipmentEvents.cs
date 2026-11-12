using HarmonyLib;

namespace WiresLightOcclusion
{
    // VisEquipment.UpdateEquipmentVisuals calls this ONLY when a visual
    // actually changed. Unlike CustomUpdate, this is not per-frame work.
    // The game destroys old equipment renderer objects and attaches new ones.
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    internal static class EquipmentShadowRefresh
    {
        private static void Postfix(VisEquipment __instance) =>
            ShadowCache.EquipmentVisualChanged(__instance);
    }
}
