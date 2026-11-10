using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("shudnal.MyLittleUI", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.balancedcraftfromnearbychests";
        public const string Name = "Wire's Balanced Craft From Nearby Chests";
        public const string Version = "0.1.13";

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
            // One class at a time: a patch broken by a game update only
            // switches off its own part.
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (System.Exception e) { Logger.LogError("Patch failed for " + type.FullName + ": " + e.Message); }
            }

            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        // Hover/context events come from vanilla Player.UpdateHover, with
        // no independent 80ms hover polling. One known chest is reconciled
        // every 0.25s for remote-player title changes that have no local event.
        private void Start()
        {
            InvokeRepeating(nameof(ReconcileRemoteChestNames), 0.25f, 0.25f);
        }

        private void ReconcileRemoteChestNames()
        {
            Prof.Begin(Prof.Poll);
            try { ContainerRegistry.TickReconcile(); }
            finally { Prof.End(Prof.Poll); }
        }

        internal static void Wake()
        {
            if (Instance != null && !Instance.enabled)
                Instance.enabled = true;
        }

        private static bool Busy =>
            MultiUserChestCompat.NeedsUpdate ||
            ConnectionVisuals.Busy ||
            VanillaConnectionFade.Busy ||
            ContainerRegistry.Busy;

        private void Update()
        {
            Prof.Begin(Prof.Update);
            try
            {
                MultiUserChestCompat.Update();
                ContainerRegistry.TickPending();
                ConnectionVisuals.Update();
                VanillaConnectionFade.Tick();
                if (!Busy)
                    enabled = false;
            }
            finally { Prof.End(Prof.Update); }
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
