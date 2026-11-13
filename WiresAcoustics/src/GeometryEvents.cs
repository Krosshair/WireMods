using HarmonyLib;

namespace WiresAcoustics
{
    // Piggyback on Valheim's own build/door lifetime events rather than
    // polling the world looking for changes. Room resamples immediately
    // when something changed; it still has a safety refresh for terrain
    // and other mods that bypass these hooks. Playing loops get their
    // wall check again too (Occlusion.WatchLoop).
    internal static class GeometryEvents
    {
        private static void Changed()
        {
            if (!Plugin.Enabled.Value) return;
            Room.Invalidate();
            Occlusion.WorldChanged();
        }

        [HarmonyPatch(typeof(Piece), "Awake")]
        private static class Built
        {
            private static void Postfix() => Changed();
        }

        [HarmonyPatch(typeof(Piece), "OnDestroy")]
        private static class Removed
        {
            private static void Prefix() => Changed();
        }

        [HarmonyPatch(typeof(Door), "SetState")]
        private static class DoorChanged
        {
            private static void Postfix() => Changed();
        }
    }
}
