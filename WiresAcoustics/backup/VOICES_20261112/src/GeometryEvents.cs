using HarmonyLib;

namespace WiresAcoustics
{
    // Piggyback on Valheim's own build/door lifetime events rather than
    // polling the world looking for changes. Room resamples immediately
    // when something changed; it still has a safety refresh for terrain
    // and other mods that bypass these hooks.
    internal static class GeometryEvents
    {
        [HarmonyPatch(typeof(Piece), "Awake")]
        private static class Built
        {
            private static void Postfix() { if (Plugin.Enabled.Value) Room.Invalidate(); }
        }

        [HarmonyPatch(typeof(Piece), "OnDestroy")]
        private static class Removed
        {
            private static void Prefix() { if (Plugin.Enabled.Value) Room.Invalidate(); }
        }

        [HarmonyPatch(typeof(Door), "SetState")]
        private static class DoorChanged
        {
            private static void Postfix() { if (Plugin.Enabled.Value) Room.Invalidate(); }
        }
    }
}
