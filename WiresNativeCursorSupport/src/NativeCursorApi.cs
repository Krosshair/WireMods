using UnityEngine;

namespace WiresNativeCursorSupport
{
    // For other mods: mark a UI object as something you click, so the pointer shows the hand over it,
    // like it does over an item. Call it once for each object; it stays until the object is gone or
    // Clear is called. The object needs a raycast target (an Image or text) to get the pointer.
    //
    //   WiresNativeCursorSupport.NativeCursorApi.SetHand(gameObject);
    //
    // From a mod that doesn't reference this one, by reflection:
    //   Type.GetType("WiresNativeCursorSupport.NativeCursorApi, WiresNativeCursorSupport")
    //       ?.GetMethod("SetHand")?.Invoke(null, new object[] { gameObject });
    public static class NativeCursorApi
    {
        public static void SetHand(GameObject go)
        {
            if (go != null && go.GetComponent<PointerHand>() == null) go.AddComponent<PointerHand>();
        }

        public static void Clear(GameObject go)
        {
            PointerHand hand = go != null ? go.GetComponent<PointerHand>() : null;
            if (hand != null) Object.Destroy(hand);
        }
    }
}
