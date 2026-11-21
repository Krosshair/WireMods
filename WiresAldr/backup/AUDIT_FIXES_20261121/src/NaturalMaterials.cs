using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // The vanilla drop table has already resolved source-specific
    // weights, stacks and world resource-rate modifiers before here.
    // Extend the existing result with occasional copies of only the
    // resource prefab that was already awarded.
    [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList),
        new System.Type[0])]
    internal static class NaturalMaterialYields
    {
        [HarmonyPostfix]
        private static void Postfix(ref List<GameObject> __result)
        {
            if (!NaturalDropSourceScope.IsNaturalSource ||
                __result == null || __result.Count == 0 ||
                Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.Materials.Value || !WorldProgress.Ready)
                return;

            bool[] defeated = WorldProgress.ReadKeys();
            GlobalYieldSettings settings = GlobalMaterialProgress.Settings();
            List<GameObject> extraDrops = null;
            int original = __result.Count;

            for (int index = 0; index < original; ++index)
            {
                GameObject prefab = __result[index];
                if (prefab == null) continue;

                double percent;
                GlobalResource shared = BiomeCatalog.SharedMaterial(prefab);
                if (shared != GlobalResource.None)
                {
                    percent = GlobalYieldRules.Total(
                        GlobalYieldRules.Parts(shared, defeated, settings));
                }
                else
                {
                    int tier = BiomeCatalog.RegionalMaterialTier(prefab);
                    if (tier < 0) continue;
                    percent = GlobalYieldRules.Total(BonusBreakdown.Veteran(
                        tier, defeated, Plugin.MaterialBonus.Value,
                        Plugin.VeteranMaterials.Value,
                        Plugin.SkippedFactor.Value / 100.0, Plugin.LaterFactor.Value / 100.0));
                }

                int extra = Mathf.Min(Mathf.RoundToInt(Plugin.MaterialMaxExtra.Value), ProgressionMath.ExtraCount(1,
                    percent, UnityEngine.Random.value));
                if (extra > 0)
                {
                    if (extraDrops == null) extraDrops = new List<GameObject>(extra);
                    for (int i = 0; i < extra; ++i) extraDrops.Add(prefab);
                }
            }
            if (AldrDiagnostics.Active) AldrDiagnostics.Sample("NATURAL_DROPS",
                "original_drop_count=" + original + " extra_native_copies=" +
                (extraDrops == null ? 0 : extraDrops.Count) + " [only recognized natural loot sources]");
            if (extraDrops != null && extraDrops.Count > 0)
                __result.AddRange(extraDrops);
        }
    }
}
