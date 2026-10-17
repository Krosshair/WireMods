using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.balancedcraftfromnearbychests";
        public const string Name = "Wire's Balanced Craft From Nearby Chests";
        public const string Version = "0.1.8";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Bind(Config);
            RouteRegistry.SeedKnownRoutes();
            SharedRouteRegistry.SeedKnownGroups();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void Update()
        {
            MultiUserChestCompat.Update();
            RefillTargetRegistry.Tick();
            ConnectionVisuals.Update();
            VanillaConnectionFade.Tick();
        }

        private void OnDestroy()
        {
            ConnectionVisuals.Cleanup();
            VanillaConnectionFade.Cleanup();
            RefillTargetRegistry.Clear();
            ContainerRegistry.Clear();
            KeywordSync.Reset();
            MultiUserChestCompat.Reset();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Log = null;
            Instance = null;
        }
    }
}
