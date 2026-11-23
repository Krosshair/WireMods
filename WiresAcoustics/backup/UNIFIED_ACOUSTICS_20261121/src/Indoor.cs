using HarmonyLib;

namespace WiresAcoustics
{
    // The game's mixer presets (snapshots) on top of everything.
    //  - Indoor: under any roof ("in shelter") it switches the whole mix to one fixed sound, a lean-to
    //    and a stone hall alike. With Room reverb on, the measured space does that job, so the preset
    //    is only used inside a shield dome (that's the dome, not a room). The game's reverb zones are
    //    taken over in Room.
    //  - Menu: pausing in game (the Esc menu) switched the world to the menu's muffled mix, so the
    //    room, the walls and the openings you were just hearing went away until you closed it. The
    //    world keeps sounding as it is; the main menu itself still gets its own mix.
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

    [HarmonyPatch(typeof(AudioMan), "UpdateSnapshots")]
    internal static class Paused
    {
        private static void Prefix(ref bool inMenu)
        {
            if (inMenu && Plugin.Enabled.Value && FejdStartup.instance == null && global::Player.m_localPlayer != null && global::Menu.IsVisible())
                inMenu = false;
        }
    }
}
