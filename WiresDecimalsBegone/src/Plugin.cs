using BepInEx;
using HarmonyLib;

namespace WiresDecimalsBegone
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.decimalsbegone";
        public const string Name = "Wire's Decimals Begone";
        public const string Version = "0.1.1";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
