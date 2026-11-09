using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    // Native Player.UpdateHover performs Valheim's own hover raycast and
    // updates m_hovering before this postfix runs. Compare only the cached
    // target/tool/build state; expensive graph discovery happens on changes.
    // No second raycast, Unity Update or independent 80ms hover poll.
    [HarmonyPatch(typeof(Player), "UpdateHover")]
    internal static class NativeHoverEvents
    {
        private static void Postfix(Player __instance)
        {
            if (Plugin.Instance == null) return;
            ConnectionVisuals.HoverUpdated(__instance);
        }
    }

    // The original timer also noticed that the player had disappeared.
    // Without it, a world unload must explicitly tear down any active beams.
    [HarmonyPatch(typeof(Player), "OnDestroy")]
    internal static class LocalPlayerHoverCleanup
    {
        private static void Prefix(Player __instance)
        {
            if (Plugin.Instance != null && __instance == Player.m_localPlayer)
                ConnectionVisuals.Cleanup();
        }
    }

    [HarmonyPatch(typeof(Player), "SetLocalPlayer")]
    internal static class LocalPlayerHoverRebind
    {
        private static void Postfix(Player __instance)
        {
            if (Plugin.Instance != null && __instance == Player.m_localPlayer)
                ConnectionVisuals.ForceNextContextRefresh();
        }
    }
}
