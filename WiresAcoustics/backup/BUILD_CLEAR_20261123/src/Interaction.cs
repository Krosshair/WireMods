using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // What you're using is heard clearly: the last thing you used (a chest, a door, a station) or
    // the piece you just placed. Its sounds get no muffling while you're within the game's own reach
    // for using things (Player.m_maxInteractDistance). You're right at it, so nothing stands between
    // you and it. Told by the game when you use something; nothing per frame.
    internal static class Interaction
    {
        private static Transform s_used;  // what you last used (its networked object), null for a placement
        private static Bounds s_box;      // its colliders and its pivot, in the world when used
        private static bool s_any;

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

        // At a sound's start: whether it comes from what you used (on it, or made where it is).
        internal static bool From(Vector3 at, Transform sound)
        {
            if (!s_any) return false;
            if (s_used != null && sound != null && sound.IsChildOf(s_used)) return true;
            return s_box.Contains(at);
        }

        // While it plays: clear as long as you're within reach of it.
        internal static bool InReach(Vector3 at)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return false;
            float reach = p.m_maxInteractDistance;
            return (at - p.GetEyePoint()).sqrMagnitude <= reach * reach;
        }

        [HarmonyPatch(typeof(Player), "Interact")]
        private static class UsedSomething
        {
            // before: what it does starts its sound (a chest's lid)
            private static void Prefix(Player __instance, GameObject go)
            {
                if (__instance != Player.m_localPlayer || go == null) return;
                ZNetView nv = go.GetComponentInParent<ZNetView>();
                Transform owner = nv != null ? nv.transform : go.transform;
                Used(owner, go.transform.position);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        private static class Placed
        {
            private static void Prefix(Player __instance, Vector3 pos)
            {
                if (__instance == Player.m_localPlayer) Used(null, pos); // before its sound starts
            }
        }
    }
}
