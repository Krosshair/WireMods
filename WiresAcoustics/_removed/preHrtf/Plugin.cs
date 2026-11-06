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
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.acoustics";
        public const string Version = "0.1.0";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled, RoomReverb, Occlusion, DebugLog;
        internal static ConfigEntry<float> ReverbStrength, OcclusionStrength;
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
            Occlusion = Setting("General", "Muffling", true, "Sounds behind walls, buildings or hills are muffled and quieter, more so through several layers.");
            ReverbStrength = Setting("Tuning", "Reverb strength", 1f, "How loud the room's reverb is (1 = as measured).", new AcceptableValueRange<float>(0f, 2f));
            OcclusionStrength = Setting("Tuning", "Muffling strength", 1f, "How much walls muffle and quieten sounds (1 = as designed, 0 = none).", new AcceptableValueRange<float>(0f, 1.5f));
            DebugLog = Setting("Debug", "Debug log", false, "Logs the measured space every few seconds and each muffled sound.", null, true);
            m_harmony = new Harmony(Guid);
            m_harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Wire's Acoustics " + Version);
        }

        private void OnDestroy()
        {
            Room.Shutdown();
            m_harmony?.UnpatchSelf();
        }
    }
}
