using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Piggyback on Valheim's own build/door lifetime events rather than
    // polling the world looking for changes. Room resamples immediately
    // when something changed; it still has a safety refresh for terrain
    // and other mods that bypass these hooks. Playing loops get their
    // wall check again too (Occlusion.WatchLoop).
    //
    // After a portal the game keeps loading a base in for a few seconds, 10 objects a frame
    // (ZNetScene.CreateObjects), and every piece appearing re-measured the room. While the area
    // round you is still coming in (ZNetScene.IsAreaReady, the check the portal itself waits on),
    // changes are held and done once when it's in. Looked at every 0.25 s; never held longer than
    // 15 s, the game's own give-up time for a portal (Player.UpdateTeleport).
    internal static class GeometryEvents
    {
        private const float Recheck = 0.25f, GiveUp = 15f;
        private static bool s_held;
        private static float s_heldSince, s_nextCheck;

        private static void Changed()
        {
            if (!Plugin.Enabled.Value) return;
            if (!s_held && AreaLoaded()) { Apply(); return; }
            if (!s_held) { s_held = true; s_heldSince = Time.time; }
        }

        private static void Apply()
        {
            Room.Invalidate();
            Occlusion.WorldChanged();
        }

        // At most every 0.25 s: pieces appear up to 10 a frame while walking into new zones.
        private static bool s_loaded = true;
        private static float s_loadedAt = -1f;
        private static bool AreaLoaded()
        {
            float now = Time.time;
            if (now - s_loadedAt < Recheck) return s_loaded;
            s_loadedAt = now;
            s_loaded = ZNetScene.instance == null || ZoneSystem.instance == null || ZNetScene.instance.IsAreaReady(Hearing.At);
            return s_loaded;
        }

        // From Room's tick: the held changes, once the area is in.
        internal static void Step(float now)
        {
            if (!s_held || now < s_nextCheck) return;
            s_nextCheck = now + Recheck;
            if (!AreaLoaded() && now - s_heldSince < GiveUp) return;
            s_held = false;
            Apply();
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
