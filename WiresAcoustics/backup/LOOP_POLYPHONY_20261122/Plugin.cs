using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sound shaped by the world around you. Vanilla reverb only depends on how far a sound is from the
    // listener, and indoors is one on/off mixer preset. Here rays from the listener measure the space
    // (how enclosed, how big, what it's made of) and set the reverb from that, and sounds behind walls
    // or hills are muffled and quieter.
    [BepInPlugin(Guid, "Wire's Acoustics", Version)]
    [BepInDependency("Azumatt.ProximityVoiceChat", BepInDependency.DependencyFlags.SoftDependency)] // loaded first, so its voice streams can be patched
    [BepInDependency("kajetan.valheim.stackify", BepInDependency.DependencyFlags.SoftDependency)]  // its chest sounds (Stackify)
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.acoustics";
        public const string Version = "0.7.0";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled, RoomReverb, Occlusion, Hrtf, RealDistance, DebugLog;
        internal static ConfigEntry<bool> TravelTime, DopplerOn, UnderwaterOn, Air, Wind, Placement, VoiceChatOn;
        internal static ConfigEntry<float> ReverbStrength, OcclusionStrength, HrtfStrength;
        internal static ConfigEntry<int> VolumetricRays;
        internal static ConfigEntry<float> SourceSphereRadius;
        private Harmony m_harmony;

        // ConfigurationManager reads these by name (no dependency on its DLL).
        public sealed class ConfigurationManagerAttributes
        {
            public bool? IsAdvanced { get; set; }
            public int? Order { get; set; }
        }

        private int m_order = 1000;
        private ConfigEntry<T> Setting<T>(string section, string key, T value, string description, AcceptableValueBase range = null, bool advanced = false) =>
            Config.Bind(section, key, value, new ConfigDescription(description, range,
                new ConfigurationManagerAttributes { Order = m_order--, IsAdvanced = advanced ? true : (bool?)null }));

        private void Awake()
        {
            Log = Logger;
            Enabled = Setting("General", "Enabled", true, "Master switch. Off: the game's own reverb and no muffling.");
            RoomReverb = Setting("General", "Room reverb", true,
                "Reverb from the space you're in: rays measure how enclosed it is, how big and what it's made of (stone rings, wood is warmer, soil and open sky swallow sound). Replaces the game's own reverb everywhere; out in the open it goes dry.");
            RealDistance = Setting("General", "Realistic distance", true,
                "Sounds get quieter with distance the way real sound does (6 dB each time the distance doubles, from the distance the game plays them at full volume), instead of the game's own curves, which cut many sounds off within a few metres (a hearth is silent 10 m away). The air still dulls them on top.");
            Occlusion = Setting("General", "Muffling", true, "Sounds behind walls, buildings or hills are muffled and quieter, more so through several layers.");
            Hrtf = Setting("General", "HRTF (headphones)", true,
                "Where a sound is around your head: a tiny delay and muffling for the far ear, duller sounds from behind, and the outer ear's cue for above and below, instead of plain left/right panning. Made for headphones; turn off on speakers. On surround (5.1, 7.1) output the game's own panning is used.");
            TravelTime = Setting("General", "Travel time", true,
                "Sound takes time to reach you (350 m/s from your character's head): a sound far away starts as late as its distance makes it, up to half a second.");
            DopplerOn = Setting("General", "Doppler", true,
                "Sounds coming toward you are higher, going away lower. Needs Realistic distance (off: the game's own).");
            UnderwaterOn = Setting("General", "Under water", true, "With your head under water everything is muffled, and sounds under water are muffled when you're above it.");
            Air = Setting("General", "Air", true, "The further a sound travels, the more of its highs the air takes away (by the weather).");
            Wind = Setting("General", "Wind", true, "Wind carries sound: downwind of a sound it reaches further, upwind less.");
            Placement = Setting("General", "Sound placement", true,
                "Sounds come from where they're made: a weapon's hand, a door's hinge, a chest's lid, the part of a station you used, a bird up in the open air, your own unplaced sounds at you. Off: where the game plays them.");
            VoiceChatOn = Setting("General", "Voice chat", true, "Proximity voice chat (if installed) is placed, muffled by walls and dulled by distance like any sound.");
            ReverbStrength = Setting("Tuning", "Reverb strength", 1f, "How loud the room's reverb is (1 = as designed, 0 = none).", new AcceptableValueRange<float>(0f, 2f));
            HrtfStrength = Setting("Tuning", "HRTF strength", 1f, "How strong the direction cues are: the far ear's dulling and level drop, the shading from behind, the delay between the ears (1 = as modelled, 2 = exaggerated).", new AcceptableValueRange<float>(0f, 2f));
            OcclusionStrength = Setting("Tuning", "Muffling strength", 1f, "How much walls muffle and quieten sounds (1 = as designed, 0 = none).", new AcceptableValueRange<float>(0f, 1.5f));
            VolumetricRays = Setting("Tuning", "Source-volume rays", 16,
                "Maximum additional sampled rays per emitting source: 0 (legacy three-ray approximation), 4, 8, 16, or 32. The actual number adapts to projected collider/source size and distance. Increasing this improves partial occlusion near edges but increases physics-job cost.",
                new AcceptableValueList<int>(0, 4, 8, 16, 32), true);
            SourceSphereRadius = Setting("Tuning", "Unshaped source sphere radius", .30f,
                "Volumetric occlusion radius (metres) for sounds without a measured looping emitter. A Steam Audio source parameter; tune it to the physical emitter's extent.",
                new AcceptableValueRange<float>(.05f, 3f), true);
            DebugLog = Setting("Debug", "Debug log", false, "Every three seconds, logs room geometry, DSP cost, and the 24 most potentially audible sources with their geometric coverage and source-volume ray count, path, target and actual gain/filter, localization, reverb, water and path age. These measurements do not capture the final mixed sound.", null, true);
            MoreVoices();
            m_harmony = new Harmony(Guid);
            m_harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Wire's Acoustics " + Version);
        }

        // Unity mixes 32 sounds at once in Valheim (its project setting); the rest play silently
        // ("virtual") until they're among the loudest again. A base with fires, stations and
        // creatures plays about 60, so quieter far ones (a mead ketill across the hall) dropped out
        // as you walked away, and when there are more sounds than voices Unity either cuts an old
        // one off mid-sound or doesn't start a new one. 128 real voices, so every sound plays to its
        // end; ones too quiet to hear are the first to give way if even that runs out (Budget).
        // Set once at start, before the game plays anything (changing it restarts Unity's audio, so
        // it stays when the mod is switched off in game).
        private const int Voices = 128;
        private static void MoreVoices()
        {
            AudioConfiguration c = UnityEngine.AudioSettings.GetConfiguration();
            if (c.numRealVoices >= Voices) return;
            int was = c.numRealVoices;
            c.numRealVoices = Voices;
            if (c.numVirtualVoices < Voices) c.numVirtualVoices = Voices;
            Log.LogInfo(UnityEngine.AudioSettings.Reset(c) ? "Real voices " + was + " -> " + Voices : "Couldn't raise real voices from " + was);
        }

        private void OnDestroy()
        {
            AcousticFilter.ReleaseAll();
            PlayerSounds.RestoreAll();
            Ambience.RestorePlaced();
            Room.Shutdown();
            Batch.Shutdown(); Starts.Shutdown();
            Ocean.Dispose();
            Directions.Shutdown();
            m_harmony?.UnpatchSelf();
        }
    }
}
