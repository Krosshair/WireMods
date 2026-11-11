using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace WiresAcoustics
{
    // The game's mixer colours every sound effect on its way out (MasterMixer, Default snapshot):
    //  - SFX: a compressor (threshold -9.8 dB, 10 ms attack, 154 ms release, +1.6 dB make-up), cuts of
    //    1.9 dB at 401 Hz and 1.8 dB at 1584 Hz, +1.2 dB at 12 kHz, and its own 0.5 s reverb;
    //  - SFX_LARGE (under SFX): another cut at 420 Hz, a lift at 8.4 kHz, a compressor and a 3.5 s reverb;
    //  - Ambient: a 2 dB cut at 223 Hz, low-passes at 11 and 15 kHz, a compressor (+3 dB make-up), -4 dB.
    // The mid cuts and the compressor are the boomy, smeared sound; the reverbs are the game's own
    // fixed rooms on top of the measured ones. Only the volumes can be changed from outside, so the
    // sounds go round those groups instead, straight into their parent, Effects: it holds the sound
    // effects volume setting (the game's SfxVol) and goes on to Master, whose high-pass (25 Hz) and
    // compressor at -1 dB (a safety limit against clipping) stay. The groups' own levels are the
    // filter's instead (RouteGain): ambience -4 dB, and in menus what the game's Menu snapshot gives
    // them (sound effects -15 dB, large ones -29.8 dB as they sit under sound effects, ambience
    // -15 dB), glided over the game's own snapshot transition time.
    internal static class Mixer
    {
        // the groups' levels in dB (MasterMixer: Default and Menu snapshots)
        private const float SfxMenu = -15f, LargeMenu = -15f - 14.816608f, AmbientDefault = -4f, AmbientMenu = -15f;
        private static float s_menu; // 0 out of menus, 1 in one
        private static float s_lastMenu = -1f;
        private static AudioMixerGroup s_effects, s_sfx, s_large, s_ambient;
        private static bool s_found, s_on;
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> AmbientLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_ambientLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> WindLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_windLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> OceanLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static readonly System.Func<AudioMan, bool> InMenu = AccessTools.MethodDelegate<System.Func<AudioMan, bool>>(AccessTools.Method(typeof(AudioMan), "InMenu"));

        private static bool Find(AudioMan man)
        {
            if (s_found) return s_effects != null;
            if (man == null || man.m_ambientMixer == null) return false;
            s_found = true;
            s_ambient = man.m_ambientMixer;
            foreach (AudioMixerGroup g in s_ambient.audioMixer.FindMatchingGroups(""))
            {
                if (g.name == "Effects") s_effects = g;
                else if (g.name == "SFX") s_sfx = g;
                else if (g.name == "SFX_LARGE") s_large = g;
            }
            Plugin.Log.LogInfo("[acoustics] mixer: " + (s_effects != null ? "sound effects go straight to Effects" : "no Effects group found, left as it is") +
                (s_sfx == null ? " (no SFX group)" : "") + (s_large == null ? " (no SFX_LARGE group)" : ""));
            return s_effects != null;
        }

        // Every frame (Room, from the game's audio update): on or off as the setting and the menus say.
        internal static void Step(AudioMan man)
        {
            bool want = Plugin.Enabled.Value && Plugin.CleanMix.Value && man != null && Find(man);
            if (want != s_on)
            {
                s_on = want;
                s_lastMenu = -1f;
                foreach (AcousticFilter f in AcousticFilter.Live)
                    if (f != null) { if (want) Route(f); else Unroute(f); }
            }
            if (!s_on) return;
            float glide = Mathf.Max(.01f, man.m_snapshotTransitionTime);
            s_menu = Mathf.MoveTowards(s_menu, InMenu(man) ? 1f : 0f, Time.unscaledDeltaTime / glide);
            if (s_menu != s_lastMenu)
            {
                s_lastMenu = s_menu;
                foreach (AcousticFilter f in AcousticFilter.Live)
                    if (f != null && f.OrigGroup != null) Level(f);
            }
            // the game's three loops (their filters are added here if they have none yet)
            Loop(AmbientLoop(man));
            Loop(WindLoop(man));
            Loop(OceanLoop(man));
        }

        private static void Loop(AudioSource src)
        {
            if (src == null || src.outputAudioMixerGroup == s_effects) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>() ?? src.gameObject.AddComponent<AcousticFilter>();
            Route(f);
        }

        // Round the game's colouring, if it's one of the groups that has it.
        internal static void Route(AcousticFilter f)
        {
            if (!s_on || f == null) return;
            AudioSource src = f.Source;
            if (src == null) return;
            AudioMixerGroup g = src.outputAudioMixerGroup;
            if (g == null || g == s_effects || (g != s_sfx && g != s_large && g != s_ambient)) return;
            f.OrigGroup = g;
            src.outputAudioMixerGroup = s_effects;
            Level(f);
        }

        // The group's level for this filter now (menu glide included).
        private static void Level(AcousticFilter f)
        {
            AudioMixerGroup g = f.OrigGroup;
            float db = g == s_ambient ? Mathf.Lerp(AmbientDefault, AmbientMenu, s_menu)
                     : g == s_large ? LargeMenu * s_menu
                     : SfxMenu * s_menu;
            f.RouteGain = db == 0f ? 1f : Mathf.Pow(10f, db / 20f);
            if (f.RouteGain < 1f)
            {
                if (!f.enabled) f.enabled = true;
                f.Active = true; // the level is the filter's
            }
        }

        internal static void Unroute(AcousticFilter f)
        {
            if (f == null || f.OrigGroup == null) return;
            if (f.Source != null) f.Source.outputAudioMixerGroup = f.OrigGroup;
            f.OrigGroup = null;
            f.RouteGain = 1f;
        }

        internal static void UnrouteAll()
        {
            s_on = false;
            foreach (AcousticFilter f in AcousticFilter.Live) Unroute(f);
        }
    }
}
