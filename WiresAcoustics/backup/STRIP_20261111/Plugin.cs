using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace WiresAcoustics
{
    // Sound shaped by the world around you. Vanilla reverb only depends on how far a sound is from the
    // camera, and indoors is one on/off mixer preset. Here rays from the listener measure the space
    // (how enclosed, how big, what it's made of) and set the reverb from that, and sounds behind walls
    // or hills are muffled and quieter.
    [BepInPlugin(Guid, "Wire's Acoustics", Version)]
    [BepInDependency("Azumatt.ProximityVoiceChat", BepInDependency.DependencyFlags.SoftDependency)] // loaded first, so its voice streams can be patched
    [BepInDependency("kajetan.valheim.stackify", BepInDependency.DependencyFlags.SoftDependency)]  // its chest sounds (Stackify)
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.acoustics";
        public const string Version = "0.3.0";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled, RoomReverb, Occlusion, Hrtf, RealDistance, DebugLog, Declicking, CleanMix, OwnReverb;
        internal static ConfigEntry<float> ReverbStrength, OcclusionStrength, HrtfStrength;
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
                "Reverb from the space you're in: rays measure how enclosed it is, how big and what it's made of (stone rings, wood is warmer, soil and open sky swallow sound). Replaces the game's distance-only reverb indoors; outdoors it blends back to the game's own.");
            RealDistance = Setting("General", "Realistic distance", true,
                "Sounds get quieter with distance the way real sound does (6 dB each time the distance doubles, from the distance the game plays them at full volume), instead of the game's own curves, which cut many sounds off within a few metres (a hearth is silent 10 m away). The air still dulls them on top.");
            Occlusion = Setting("General", "Muffling", true, "Sounds behind walls, buildings or hills are muffled and quieter, more so through several layers.");
            Hrtf = Setting("General", "HRTF (headphones)", true,
                "Where a sound is around your head: a tiny delay and muffling for the far ear, duller sounds from behind, and the outer ear's cue for above and below, instead of plain left/right panning. Made for headphones; turn off on speakers.");
            ReverbStrength = Setting("Tuning", "Reverb strength", 1f, "How loud the room's reverb is (1 = as measured).", new AcceptableValueRange<float>(0f, 2f));
            HrtfStrength = Setting("Tuning", "HRTF strength", 1f, "How strong the direction cues are: the far ear's dulling and level drop, the shading from behind, the delay between the ears (1 = as modelled, 2 = exaggerated).", new AcceptableValueRange<float>(0f, 2f));
            OcclusionStrength = Setting("Tuning", "Muffling strength", 1f, "How much walls muffle and quieten sounds (1 = as designed, 0 = none).", new AcceptableValueRange<float>(0f, 1.5f));
            DebugLog = Setting("Debug", "Debug log", false, "Logs the measured space every few seconds and each muffled sound.", null, true);
            Declicking = Setting("Debug", "Fix sound clicks", true,
                "Repairs the clicks baked into the game's own sound files (wood and stone footsteps most of all). Off: the game's clips are reloaded as they ship, for comparing.", null, true);
            Declicking.SettingChanged += (_, __) => { if (!Declicking.Value) Declick.Restore(); };
            OwnReverb = Setting("Debug", "Physical reverb", true,
                "The room's sound made by the mod from what the rays measured: an early reflection off each wall, floor and ceiling from its direction, and a reverb tail that dies away as fast as the room's size, materials and the air make it, per frequency band. Off: Unity's reverb zone set from the same measurement, for comparing.", null, true);
            CleanMix = Setting("Debug", "Skip the game mixer colouring", true,
                "Sound effects and ambience go round the game's own mixer EQ, compressors and fixed reverbs (the boomy, smeared sound), keeping your volume settings and the final clipping limit. Off: as the game mixes them, for comparing.", null, true);
            m_harmony = new Harmony(Guid);
            m_harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Wire's Acoustics " + Version);
        }

        private void OnDestroy()
        {
            Mixer.UnrouteAll();
            Reverb.Shutdown();
            AcousticFilter.ReleaseAll();
            PlayerSounds.RestoreAll();
            Ambience.RestorePlaced();
            Room.Shutdown();
            Batch.Shutdown();
            Bounces.Shutdown();
            Ocean.Dispose();
            Directions.Shutdown();
            m_harmony?.UnpatchSelf();
        }
    }
}
