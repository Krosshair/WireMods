using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // What you're acting on is heard clearly: what you use (a chest, a door, a station), put an item
    // on or in (ore in a smelter, a trophy on a stand), place, remove or repair. Every sound the game
    // makes during that action is yours (marked when it's made, Follow), and so is any later sound
    // from the thing itself (a crafting station's crafting). They get no muffling while you're
    // within the game's own reach for that action (using: m_maxInteractDistance, building: m_maxPlaceDistance).
    // You're right at it, so nothing stands between you and it. Told by the game when you act; nothing
    // per frame.
    internal static class Interaction
    {
        private static Transform s_used;  // what you last acted on (its networked object)
        private static Bounds s_box;      // its colliders and its pivot, in the world then
        private static bool s_any;
        private static int s_acting;      // inside one of your actions: the sounds made now are yours
        private static bool s_building;   // the last action was building (place, remove, repair): the build reach

        internal static bool Acting => s_acting > 0;

        private static void Used(Transform owner, Vector3 point)
        {
            s_used = owner;
            s_box = new Bounds(point, Vector3.zero);
            if (owner != null)
            {
                s_box.Encapsulate(owner.position);
                foreach (Collider c in owner.GetComponentsInChildren<Collider>(false))
                    if (c != null && !c.isTrigger) s_box.Encapsulate(c.bounds);
            }
            s_any = true;
        }

        private static void Used(GameObject go)
        {
            if (go == null) return;
            ZNetView nv = go.GetComponentInParent<ZNetView>();
            Used(nv != null ? nv.transform : go.transform, go.transform.position);
        }

        // At a sound's start: whether it comes from what you acted on (on it, or made where it is).
        internal static bool From(Vector3 at, Transform sound)
        {
            if (!s_any) return false;
            if (s_used != null && sound != null && sound.IsChildOf(s_used)) return true;
            return s_box.Contains(at);
        }

        // While it plays: clear as long as it, or what you acted on, is within your reach, measured
        // as the game measures it (to its nearest point).
        internal static bool InReach(Vector3 at)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return false;
            float reach = Reach(p);
            Vector3 eye = p.GetEyePoint();
            float d = (at - eye).sqrMagnitude;
            if (s_any) d = Mathf.Min(d, (s_box.ClosestPoint(eye) - eye).sqrMagnitude);
            return d <= reach * reach;
        }

        // A sound made during your action counts only if it's made within your reach (to the sound
        // itself): the action can set off sounds elsewhere too.
        internal static bool Near(Vector3 at)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return false;
            float reach = Reach(p);
            return (at - p.GetEyePoint()).sqrMagnitude <= reach * reach;
        }

        // The game's own reach for what you did: building (8 m on the player prefab) or using (3.5 m).
        private static float Reach(Player p) => s_building ? p.m_maxPlaceDistance : p.m_maxInteractDistance;

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
                Used(go);
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
                Used(__instance.GetHoverObject());
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
                Used(null, pos);
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
                    if (nv != null && (nv.transform.position - pos).sqrMagnitude < 1e-4f) { Used(nv.transform, pos); break; }
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
                if (repairPiece != null) Used(repairPiece.gameObject);
                s_acting++;
            }
            private static void Finalizer(bool __state) { if (__state) s_acting--; }
        }
    }
}
