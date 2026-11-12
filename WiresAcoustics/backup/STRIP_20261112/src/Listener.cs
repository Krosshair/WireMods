using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // You hear from where you see: the game puts its listener at your character's eyes while the
    // camera floats behind, so what's left and right, near and far, and which room you're in were
    // the character's, not the view's. With the mod on the listener stays on the camera.
    [HarmonyPatch(typeof(GameCamera), "UpdateListner")]
    internal static class Listener
    {
        private static bool Prefix(AudioListener ___m_listner)
        {
            if (!Plugin.Enabled.Value || ___m_listner == null) return true;
            ___m_listner.transform.localPosition = Vector3.zero;
            return false;
        }
    }
}
