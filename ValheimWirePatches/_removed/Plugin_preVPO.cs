using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ValheimWirePatches
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheimwirepatches";
        public const string Name = "Valheim Wire Patches";
        public const string Version = "0.1.4";

        internal static Plugin Instance;

        internal static ConfigEntry<bool> DoubleClickSound;
        internal static ConfigEntry<bool> TeleportLighting;
        internal static ConfigEntry<bool> LightningLighting;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;

            DoubleClickSound = Config.Bind("Fixes", "Double click sound", false,
                "Inventory tabs and buttons (Craft, Upgrade, Skills, Trophies, Achievements...) play the click sound twice. Only one plays.");
            TeleportLighting = Config.Bind("Fixes", "Lighting after teleporting", true,
                "After a portal, a dungeon entrance or loading in, the lighting breaks for a few seconds and comes back in a wave spreading out from you. It stays right.");
            LightningLighting = Config.Bind("Fixes", "Lighting stuck on lightning flashes", true,
                "The lighting can get stuck with a lightning flash in it for a few seconds. It waits until the flash is over.");

            Config.Remove(Config.Bind("Performance", "Shadow maps sized to the light", true, "Removed.").Definition);
            Config.Remove(Config.Bind("Fixes", "Shadows for building lights", false, "Removed.").Definition);

            TeleportLighting.SettingChanged += (_, __) => ReflectionSnapshots.SettingChanged();

            _harmony = new Harmony(Guid);
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception e) { Logger.LogError("Couldn't patch " + type.Name + ": " + e.Message); }
            }
        }

        // Invoke() targets for ReflectionSnapshots
        internal void ReflectionGiveUp() => ReflectionSnapshots.GiveUp();
        internal void ReflectionTerrainTimeout() => ReflectionSnapshots.TerrainTimeout();
        internal void ReflectionFadeBack() => ReflectionSnapshots.RestoreFade();

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
