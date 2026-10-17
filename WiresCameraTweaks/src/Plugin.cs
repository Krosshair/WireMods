using BepInEx;
using HarmonyLib;

namespace WiresShakeItUp
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        // Keep the original GUID so existing Shake It Up / Camera Overhaul
        // configuration carries over into Camera Tweaks.
        public const string Guid = "wire.valheim.shakeitup";
        public const string Name = "Wire's Camera Tweaks";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            ModConfig.Bind(Config);
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            ShakeEngine.Reset();
            ZoomEngine.Reset();
            CollisionAutoZoomEngine.Reset();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Instance = null;
        }
    }
}
