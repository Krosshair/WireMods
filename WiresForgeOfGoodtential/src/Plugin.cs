using System;
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
        public const string Version = "0.6.1";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(Guid);
            // Class by class: a patch a game update breaks only loses that part.
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception e) { Logger.LogError("Couldn't patch " + type.Name + ": " + e.Message); }
            }
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
