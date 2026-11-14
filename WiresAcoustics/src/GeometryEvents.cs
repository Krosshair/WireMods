using HarmonyLib;
using System.Collections.Generic;
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

        private static void Changed(Vector3 at, bool door)
        {
            if (!Plugin.Enabled.Value) return;
            if (!s_held && AreaLoaded()) { Apply(at, false, door); return; }
            if (!s_held) { s_held = true; s_heldSince = Time.time; }
        }

        // Near enough to matter: the room is measured out to Room.MaxDistance, so a piece further
        // off than that can't change it. Sounds are checked against where it was (Occlusion).
        private static void Apply(Vector3 at, bool everywhere, bool door)
        {
            if (everywhere || (at - Hearing.At).sqrMagnitude <= Room.MaxDistance * Room.MaxDistance) Room.Invalidate();
            Occlusion.WorldChanged(at, everywhere, door);
        }

        // At most every 0.25 s: pieces appear up to 10 a frame while walking into new zones.
        private static bool s_loaded = true;
        private static float s_loadedAt = -1f;
        private static bool AreaLoaded()
        {
            float now = Time.time;
            if (now - s_loadedAt < Recheck) return s_loaded;
            s_loadedAt = now;
            s_loaded = AreaReady(Hearing.At);
            return s_loaded;
        }

        // The game's ZNetScene.IsAreaReady, with a list of its own. The game's fills ZNetScene's own
        // working list (m_tempCurrentObjects), the one its streaming is in the middle of using when a
        // piece appears (CreateDestroyObjects: create from it, then remove whatever isn't on it), and
        // this is asked from inside a piece appearing. Its list emptied and refilled with a smaller
        // area there, the game removed everything that had dropped off it and streamed it back in
        // the next frame: pieces vanishing and coming back.
        private static readonly List<ZDO> AreaObjects = new List<ZDO>();
        private static readonly System.Func<ZNetScene, ZDO, bool> PrefabValid =
            HarmonyLib.AccessTools.MethodDelegate<System.Func<ZNetScene, ZDO, bool>>(HarmonyLib.AccessTools.Method(typeof(ZNetScene), "IsPrefabZDOValid"));
        internal static bool AreaReady(Vector3 point)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || ZoneSystem.instance == null || ZDOMan.instance == null) return true;
            Vector2s zone = ZoneSystem.GetZone(point);
            if (!ZoneSystem.instance.IsZoneLoaded(zone)) return false;
            AreaObjects.Clear();
            ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(1, 0), AreaObjects);
            bool ready = true;
            foreach (ZDO zdo in AreaObjects)
                if (PrefabValid(scene, zdo) && scene.FindInstance(zdo) == null) { ready = false; break; }
            AreaObjects.Clear();
            return ready;
        }

        // From Room's tick: the held changes, once the area is in.
        internal static void Step(float now)
        {
            if (!s_held || now < s_nextCheck) return;
            s_nextCheck = now + Recheck;
            if (!AreaLoaded() && now - s_heldSince < GiveUp) return;
            s_held = false;
            Apply(Vector3.zero, true, true);
        }

        [HarmonyPatch(typeof(Piece), "Awake")]
        private static class Built
        {
            private static void Postfix(Piece __instance) => Changed(__instance.transform.position, false);
        }

        [HarmonyPatch(typeof(Piece), "OnDestroy")]
        private static class Removed
        {
            private static void Prefix(Piece __instance) => Changed(__instance.transform.position, false);
        }

        [HarmonyPatch(typeof(Door), "SetState")]
        private static class DoorChanged
        {
            private static void Postfix(Door __instance) => Changed(__instance.transform.position, true);
        }
    }
}
