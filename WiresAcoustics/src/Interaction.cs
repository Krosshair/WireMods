using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // What you act on (use, put an item on or in, place, remove, repair) is heard from the point you
    // actually touched: the game's own aim result (the hover ray for using, the placement marker for
    // building, the hammer's ray for removing and repairing). The game only let you reach that point
    // because nothing was between you and it, so these sounds trace their walls from your head to that
    // point instead of to their own spot (often inside or behind the thing: a chest's pivot, a floor's
    // middle below the surface you aimed at, a station's sound inside its model). Walls between you
    // and it still count, so nothing is heard through a wall it shouldn't be. Later sounds from the
    // same thing (a station crafting, a smelter running) trace to the part you last touched. Told by
    // the game when you act; nothing per frame, one ray per action.
    internal static class Interaction
    {
        private static Transform s_used;  // what you last acted on (its networked object)
        private static Bounds s_box;      // its colliders and its pivot, in the world then
        private static bool s_any;
        private static Vector3 s_touched; // the point you touched on it
        private static int s_acting;      // inside one of your actions: the sounds made now are yours
        private static bool s_building;   // the last action was building (place, remove, repair): the build reach

        internal static bool Acting => s_acting > 0;
        internal static Vector3 Touched => s_touched;

        private static readonly AccessTools.FieldRef<Player, int> InteractMask = AccessTools.FieldRefAccess<Player, int>("m_interactMask");
        private static readonly AccessTools.FieldRef<Player, int> RemoveMask = AccessTools.FieldRefAccess<Player, int>("m_removeRayMask");
        private static readonly AccessTools.FieldRef<Player, GameObject> Marker = AccessTools.FieldRefAccess<Player, GameObject>("m_placementMarkerInstance");
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private static void Used(Transform owner, Vector3 point, Vector3 touched)
        {
            s_used = owner;
            s_box = new Bounds(point, Vector3.zero);
            if (owner != null)
            {
                s_box.Encapsulate(owner.position);
                foreach (Collider c in owner.GetComponentsInChildren<Collider>(false))
                    if (c != null && !c.isTrigger) s_box.Encapsulate(c.bounds);
            }
            s_touched = touched;
            s_any = true;
        }

        // Where the game's aim ray (from the camera, as the game casts it) first meets something that
        // isn't you: the point you touched. Falls back to the thing's own position.
        private static Vector3 Aim(Player p, int mask, Vector3 fallback)
        {
            GameCamera cam = GameCamera.instance;
            if (cam == null) return fallback;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, Hits, 50f, mask);
            float best = float.MaxValue;
            Vector3 at = fallback;
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null || (c.attachedRigidbody != null && c.attachedRigidbody.gameObject == p.gameObject)) continue;
                if (Hits[i].distance < best) { best = Hits[i].distance; at = Hits[i].point; }
            }
            return at;
        }

        private static void Used(Player p, GameObject go, int mask)
        {
            if (go == null) return;
            ZNetView nv = go.GetComponentInParent<ZNetView>();
            Used(nv != null ? nv.transform : go.transform, go.transform.position, Aim(p, mask, go.transform.position));
        }

        // At a sound's start: whether it comes from what you acted on (on it, or made inside its box).
        internal static bool From(Vector3 at, Transform sound)
        {
            if (!s_any) return false;
            if (s_used != null && sound != null && sound.IsChildOf(s_used)) return true;
            return s_box.Contains(at);
        }

        // A sound made during your action counts only if it's made within your reach: the action can
        // set off sounds elsewhere too.
        internal static bool Near(Vector3 at)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return false;
            float reach = s_building ? p.m_maxPlaceDistance : p.m_maxInteractDistance;
            return (at - p.GetEyePoint()).sqrMagnitude <= reach * reach;
        }

        private static bool Mine(Humanoid h) => h != null && h == Player.m_localPlayer;

        // Using something (E).
        [HarmonyPatch(typeof(Player), "Interact")]
        private static class Use
        {
            private static void Prefix(Player __instance, GameObject go, out bool __state)
            {
                __state = Mine(__instance);
                if (!__state) return;
                s_building = false;
                Used(__instance, go, InteractMask(__instance));
                s_acting++;
            }
            private static void Finalizer(bool __state) { if (__state) s_acting--; }
        }

        // An item used on what you're looking at (ore into a smelter, a trophy onto a stand).
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class UseOn
        {
            private static void Prefix(Humanoid __instance, out bool __state)
            {
                __state = Mine(__instance);
                if (!__state) return;
                s_building = false;
                Player p = (Player)__instance;
                Used(p, p.GetHoverObject(), InteractMask(p));
                s_acting++;
            }
            private static void Finalizer(bool __state) { if (__state) s_acting--; }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        private static class Place
        {
            private static void Prefix(Player __instance, Vector3 pos, out bool __state)
            {
                __state = Mine(__instance);
                if (!__state) return;
                s_building = true;
                // the placement marker sits on the surface you aimed at
                GameObject marker = Marker(__instance);
                Vector3 touched = marker != null && marker.activeSelf ? marker.transform.position : pos;
                Used(null, pos, touched);
                s_acting++;
            }

            // After: the piece now standing there is what you acted on (its later sounds).
            private static void Postfix(Player __instance, Vector3 pos)
            {
                if (!Mine(__instance)) return;
                int n = Physics.OverlapSphereNonAlloc(pos, .25f, Near, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    ZNetView nv = Near[i] != null ? Near[i].GetComponentInParent<ZNetView>() : null;
                    if (nv != null && (nv.transform.position - pos).sqrMagnitude < 1e-4f) { Used(nv.transform, pos, s_touched); break; }
                }
            }

            private static void Finalizer(bool __state) { if (__state) s_acting--; }
            private static readonly Collider[] Near = new Collider[32];
        }

        // Removing a piece with the hammer: its sound is made where it stood.
        [HarmonyPatch(typeof(Player), "RemovePiece")]
        private static class Remove
        {
            private static void Prefix(Player __instance, out bool __state)
            {
                __state = Mine(__instance);
                if (!__state) return;
                s_building = true;
                Vector3 touched = Aim(__instance, RemoveMask(__instance), __instance.GetEyePoint());
                Used(null, touched, touched);
                s_acting++;
            }
            private static void Finalizer(bool __state) { if (__state) s_acting--; }
        }

        [HarmonyPatch(typeof(Player), "Repair")]
        private static class Fix
        {
            private static void Prefix(Player __instance, Piece repairPiece, out bool __state)
            {
                __state = Mine(__instance);
                if (!__state) return;
                s_building = true;
                if (repairPiece != null) Used(__instance, repairPiece.gameObject, RemoveMask(__instance));
                s_acting++;
            }
            private static void Finalizer(bool __state) { if (__state) s_acting--; }
        }
    }
}
