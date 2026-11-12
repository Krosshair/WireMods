using UnityEngine;

namespace WiresAcoustics
{
    // Where you hear from. In a third-person game the camera floats behind your character, and the
    // two do different jobs (sound design for third person; Wwise calls the second a "distance
    // probe"). How far a sound is, what's in the way, which room you're in and whether you're under
    // water go by your character's head: that's where the game itself keeps its listener, so
    // Unity's own fall-off already measures from there (smoothly, every sample). Which way a sound
    // is (the HRTF) goes by the camera, as you see it on screen. Dead, flying the free camera or
    // with no character, the game puts its listener on the camera, and so both are the camera.
    internal static class Hearing
    {
        internal static Vector3 At;     // your character's head (the game's listener)
        internal static Transform Camera; // the camera the listener hangs from

        // Every frame, from the game's audio update.
        internal static void Update(Transform listener)
        {
            At = listener.position;
            Camera = listener.parent != null ? listener.parent : listener;
        }
    }
}
