using System;
using BepInEx;
using HarmonyLib;
using ServerSync;

namespace WiresAldr
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.heimvoxtr";
        public const string Name = "Wire's Aldr";
        public const string Version = "1.3.1";
        private static readonly ConfigSync Sync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version,
            ModRequired = true
        };
        private Harmony _harmony;

        private void Awake()
        {
            // One write for all of them; after that every change is saved.
            Config.SaveOnConfigSet = false;
            BindSettings();
            Config.Save();
            Config.SaveOnConfigSet = true;
            AldrDiagnostics.Start(Logger);
            _harmony = new Harmony(Guid);
            try
            {
                // One class at a time: a patch broken by a game update
                // only switches off its own feature, not the whole mod.
                foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
                {
                    try { _harmony.CreateClassProcessor(type).Patch(); }
                    catch (Exception e)
                    {
                        AldrDiagnostics.Log("PATCH_FAILED", type.FullName + ": " + e);
                        Logger.LogError("Patch failed for " + type.FullName + ": " + e.Message);
                    }
                }
                AldrPatchAudit.Check();
                AldrDiagnostics.Settings();
                AldrDiagnostics.Flush();
            }
            catch (Exception e)
            {
                AldrDiagnostics.Log("PATCH_FATAL", e.ToString());
                Logger.LogError(e);
            }
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy() { AldrDiagnostics.Close(); _harmony?.UnpatchSelf(); }
    }
}
