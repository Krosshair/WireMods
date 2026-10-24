using System.Collections.Generic;
using UnityEngine;

namespace WiresNativeCursorSupport
{
    // For other mods: mark a UI object (and everything under it) as
    // something you click, so the pointer shows the hand over it, like it
    // does over an item. Call it once for each object; it stays until the
    // object is gone or Clear is called.
    //
    //   WiresNativeCursorSupport.NativeCursorApi.SetHand(gameObject);
    //
    // From a mod that doesn't reference this one, by reflection:
    //   Type.GetType("WiresNativeCursorSupport.NativeCursorApi, WiresNativeCursorSupport")
    //       ?.GetMethod("SetHand")?.Invoke(null, new object[] { gameObject });
    public static class NativeCursorApi
    {
        private const int MaxDepth = 12;
        private static readonly HashSet<int> Hands = new HashSet<int>();

        public static void SetHand(GameObject go)
        {
            if (go != null) Hands.Add(go.GetInstanceID());
        }

        public static void Clear(GameObject go)
        {
            if (go != null) Hands.Remove(go.GetInstanceID());
        }

        internal static bool IsHand(GameObject hit)
        {
            if (Hands.Count == 0 || hit == null) return false;
            Transform t = hit.transform;
            for (int i = 0; i < MaxDepth && t != null; i++, t = t.parent)
                if (Hands.Contains(t.gameObject.GetInstanceID())) return true;
            return false;
        }
    }
}
