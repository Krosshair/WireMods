using BepInEx;
using HarmonyLib;

namespace WiresWeaponMaster
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.weaponmaster";
        public const string Name = "Wire's Weapon Master";
        public const string Version = "0.2.1";

        internal static Plugin Instance;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            WeaponMasterConfig.Bind(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        internal static void LogInfo(string message)
        {
            if (Instance != null)
                Instance.Logger.LogInfo(message);
        }

        internal static void LogWarning(string message)
        {
            if (Instance != null)
                Instance.Logger.LogWarning(message);
        }

        private void OnDestroy()
        {
            MultiplayerSync.ClearLocal();
            WeaponMasterConfigSync.Reset();
            WeaponVisuals.Cleanup();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Instance = null;
        }
    }
}
