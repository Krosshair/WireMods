using HarmonyLib;

namespace WiresAcoustics
{
    // The game's other built-in reverb: under any roof ("in shelter") it switches the whole mix to its
    // Indoor preset, one fixed sound for a lean-to and a stone hall alike. With Room reverb on, the
    // measured space does that job, so the preset is only used inside a shield dome (that's the dome,
    // not a room). The game's reverb zones are taken over in Room.
    [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.SetIndoor))]
    internal static class Indoor
    {
        private static void Prefix(ref bool indoor)
        {
            if (!indoor || !Plugin.Enabled.Value || !Plugin.RoomReverb.Value) return;
            global::Player p = global::Player.m_localPlayer;
            indoor = p != null && ShieldGenerator.IsInsideShield(p.transform.position);
        }
    }
}
