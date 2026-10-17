using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresHoodToggle
{
    // In some menu / character-preview startup orders, a renderer
    // is registered before MaterialMan.Start() creates its shared
    // MaterialPropertyBlock. Vanilla PropertyContainer captures the
    // null block in its constructor and later throws every frame in
    // PropertyContainer.UpdateBlock -> m_propertyBlock.Clear().
    //
    // Ensure the shared block exists immediately before the ORIGINAL
    // RegisterRenderers runs. We do not skip registration, change
    // shader data, or touch the saved headpiece state.
    [HarmonyPatch(typeof(MaterialMan), "RegisterRenderers")]
    internal static class EarlyMaterialBlockPatch
    {
        private static readonly FieldInfo SharedBlock =
            AccessTools.Field(typeof(MaterialMan), "m_propertyBlock");

        private static bool _reportedMissingField;

        [HarmonyPrefix]
        private static void Prefix(MaterialMan __instance)
        {
            if (__instance == null)
                return;

            if (SharedBlock == null)
            {
                if (!_reportedMissingField)
                {
                    _reportedMissingField = true;
                    Plugin.Log?.LogWarning(
                        "MaterialMan private property-block field " +
                        "could not be found. Startup-order guard " +
                        "was not applied.");
                }
                return;
            }

            if (SharedBlock.GetValue(__instance) != null)
                return;

            SharedBlock.SetValue(__instance,
                new MaterialPropertyBlock());
            Plugin.Log?.LogInfo(
                "MaterialMan property block initialized before " +
                "early renderer registration.");
        }
    }
}
