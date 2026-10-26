using BepInEx;
using HarmonyLib;

namespace WiresForgeOfGoodtential
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.forgeofgoodtential";
        public const string Name = "Wire's Forge of Goodtential";
        public const string Version = "0.5.0";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            ForgeContext.Restore();
            ForgeSelection.ResetAll();
            ForgeIdolSound.Cleanup();

            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }
}
