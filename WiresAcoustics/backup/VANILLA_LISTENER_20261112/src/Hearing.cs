using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Where you hear from. In a third-person game the camera floats behind your character, and the
    // two do different jobs (sound design for third person; Wwise calls the second a "distance
    // probe"): which way a sound is (left, right, behind, above: the HRTF and panning) goes by the
    // camera, as you see it, so the Unity listener stays there (Listener); how far a sound is,
    // what's in the way, which room you're in and whether you're under water go by your character's
    // head, so zooming the camera out doesn't make your own steps quieter, a wall the camera slips
    // behind doesn't muffle the world, and a room you stand in is the room you hear. Dead, flying
    // the free camera or with no character: the camera for both.
    internal static class Hearing
    {
        internal static Vector3 At;
        private static readonly AccessTools.FieldRef<GameCamera, bool> FreeFly = AccessTools.FieldRefAccess<GameCamera, bool>("m_freeFly");

        // Every frame, from the game's audio update.
        internal static void Update(Transform camera)
        {
            global::Player p = global::Player.m_localPlayer;
            bool own = p != null && !p.IsDead() && p.m_eye != null &&
                !(GameCamera.instance != null && FreeFly(GameCamera.instance)) &&
                (p.m_eye.position - camera.position).sqrMagnitude < 30f * 30f;
            At = own ? p.m_eye.position : camera.position;
        }
    }
}
