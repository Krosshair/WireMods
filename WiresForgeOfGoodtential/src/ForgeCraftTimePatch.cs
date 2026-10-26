using HarmonyLib;

namespace WiresForgeOfGoodtential
{
    // Halve the Forge of Potential's refining time, including the
    // increase per quality level. These two values are only used for
    // upgrader stations, so they are halved once when the inventory UI
    // is created; normal crafting is untouched. Nothing runs per frame.
    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class ForgeCraftTimePatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            if (__instance == null)
                return;

            __instance.m_upgraderDuration *= 0.5f;
            __instance.m_upgraderDurationPerLevel *= 0.5f;
        }
    }
}
