using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // What you're using is heard clearly: the last thing you used (a chest, a door, a station) or
    // the piece you just placed (any sound it makes while being placed, wherever the game plays it). Its sounds get no muffling while you're within the game's own reach
    // for using things (Player.m_maxInteractDistance). You're right at it, so nothing stands between
    // you and it. Told by the game when you use something; nothing per frame.
    internal static class Interaction
    {
        private static Transform s_used;  // what you last used (its networked object), null for a placement
        private static Bounds s_box;      // its colliders and its pivot, in the world when used
        private static bool s_any;
        private static bool s_placing; // inside PlacePiece: its sounds are the placing, wherever they play

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
            if (s_placing) return true;
            if (!s_any) return false;
            if (s_used != null && sound != null && sound.IsChildOf(s_used)) return true;
            return s_box.Contains(at);
        }

        // While it plays: clear as long as you're within reach of it.
        internal static bool InReach(Vector3 at)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return false;
            float reach = Mathf.Max(p.m_maxInteractDistance, p.m_maxPlaceDistance);
            // measured to what you used (its nearest point), as the game measures your reach to it
            Vector3 eye = p.GetEyePoint();
            Vector3 to = s_any ? s_box.ClosestPoint(eye) : at;
            return (to - eye).sqrMagnitude <= reach * reach;
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
                if (__instance != Player.m_localPlayer) return;
                Used(null, pos); // before its sound starts
                s_placing = true;
            }

            // After: the piece now standing there is what you're using (its sounds start next frame).
            private static void Postfix(Player __instance, Vector3 pos)
            {
                if (__instance != Player.m_localPlayer) return;
                int n = Physics.OverlapSphereNonAlloc(pos, .25f, Near, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    ZNetView nv = Near[i] != null ? Near[i].GetComponentInParent<ZNetView>() : null;
                    if (nv != null && (nv.transform.position - pos).sqrMagnitude < 1e-4f) { Used(nv.transform, pos); break; }
                }
            }

            private static void Finalizer() => s_placing = false;
            private static readonly Collider[] Near = new Collider[32];
        }
    }
}
