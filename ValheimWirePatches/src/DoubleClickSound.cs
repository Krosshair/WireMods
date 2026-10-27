using HarmonyLib;
using UnityEngine;

namespace ValheimWirePatches
{
    // SetActiveGroup plays m_setActiveGroupEffects (meant for gamepad bumper
    // switching) on top of the button's own ButtonSfx, so mouse clicks on
    // tabs/buttons that call it get two clicks. Drop the ButtonSfx one.
    internal static class DoubleClickSound
    {
        private static int _groupSoundFrame = -1;

        [HarmonyPatch(typeof(InventoryGui), "SetActiveGroup", new[] { typeof(int), typeof(bool) })]
        private static class SetActiveGroup
        {
            private static void Postfix(bool playSound)
            {
                if (playSound && Player.m_localPlayer != null)
                    _groupSoundFrame = Time.frameCount;
            }
        }

        [HarmonyPatch(typeof(ButtonSfx), "PlaySfx")]
        private static class PlaySfx
        {
            private static void Prefix(ref GameObject soundPrefab, ref GameObject vibrationPrefab)
            {
                if (!Plugin.DoubleClickSound.Value || _groupSoundFrame != Time.frameCount || soundPrefab == null)
                    return;
                soundPrefab = null;
                vibrationPrefab = null;
            }
        }
    }
}
