using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;

namespace WiresAldr
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.heimvoxtr";
        public const string Name = "Wire's Aldr";
        public const string Version = "0.4.1";
        private static readonly ConfigSync Sync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version,
            ModRequired = true
        };
        internal static ConfigEntry<bool> Enabled, Stars, Forage, Materials, Lock,
            FishYields, XpEnabled, XpAllSkills, XpChallenge, XpDodgeStreaks, Diagnostics;
        internal static ConfigEntry<float> StarBonus, ForageBonus, ForageCap,
                        MaterialBonus, StoneBonus, VeteranForage, VeteranMaterials, VentureCap, VenturePerKm, RaidBonus, LaterFactor, SkippedFactor,
            WoodEikthyr, WoodElder, WoodLater, StoneLater,
            CoreWoodElder, FineWoodElder, FineWoodYagluth, AshwoodFader,
            MushroomBlackForestPremium, ThistleSwampElderPremium,
            ThistleSwampBonemassPremium,
            FishYieldBonus, XpProgressionStrength, XpCatchUpBonus,
            XpCombatStrength, XpParryStrength, XpDodgeStrength;
        private Harmony _harmony;

        private void Awake()
        {
            Config.SaveOnConfigSet = false;
            Enabled = Bind("01 - Global Toggles", "Enable Heimvöxtr", true,
                "Master switch. Off restores vanilla progression.");
            Stars = Bind("01 - Global Toggles", "Creature difficulty", true,
                "Boost eligible native creature star rolls. Respects vanilla world difficulty and per-spawn star limits.");
            Forage = Bind("01 - Global Toggles", "Wild foraging", true,
                "Add extra-harvest probability to eligible wild pickables only.");
            Materials = Bind("01 - Global Toggles", "Natural material yields", true,
                "Slightly increase natural wood, stone, and ore yields through existing drop tables.");
            FishYields = Bind("01 - Global Toggles", "Wild fishing yields", true,
                "Progress native fish spawning, quality, native catch yields and reeling efficiency through existing Valheim systems.");
            XpEnabled = Bind("01 - Global Toggles", "Dynamic experience", true,
                "Strong progression XP for all vanilla skills with optional contextual combat bonuses.");
            Lock = Bind("01 - Global Toggles", "Lock server configuration", true,
                "When on, connected clients use the server's settings, editable by server administrators.");
            Sync.AddLockingConfigEntry(Lock);
            StarBonus = Percent("02 - Creature Difficulty",
                "Own-boss star roll bonus (pp)", 8,
                "Bonus percentage points at normal world difficulty. Scales with vanilla star-chance modifiers.", 0, 40);
            // Existing installations have 4 stored as the old default.
            // Migrate once to the more noticeable 8-point default,
            // while preserving custom values other than 4.
            var starMigration = Config.Bind("Internal",
                "Star bonus defaults migrated to 0.1.4", false,
                new ConfigDescription("Internal version migration flag.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));
            if (!starMigration.Value)
            {
                if (Math.Abs(StarBonus.Value - 4f) < 0.0001f)
                    StarBonus.Value = 8f;
                starMigration.Value = true;
            }

            ForageBonus = Percent("03 - Foraging",
                "Own-boss extra harvest chance (pp)", 25,
                "Additional chance to obtain one extra item, added to the vanilla Farming bonus.", 0, 100);
            ForageCap = Percent("03 - Foraging",
                "Total bonus harvest chance cap (%)", 80,
                "Upper cap on Farming + Heimvöxtr bonus-harvest probability.", 0, 100);
            VeteranForage = Percent("03 - Foraging",
                "Veteran yield per later Forsaken (%)", 40,
                "Each Forsaken defeated after a wild resource's own biome boss adds this much extra yield per pick, as extra native items. Makes quick gathering trips to earlier biomes worthwhile.", 0, 200);
            MushroomBlackForestPremium = Percent("03 - Foraging",
                "Forest mushroom difficulty bonus (%)", 8,
                "Additional mushroom harvest chance in Black Forest from defeating Elder, followed by diminishing later Forsaken contributions.", 0, 50);
            ThistleSwampElderPremium = Percent("03 - Foraging",
                "Swamp thistle early risk bonus (%)", 10,
                "Extra Swamp thistle harvest chance from Elder, even before Bonemass. Applies only when harvesting in Swamp.", 0, 50);
            ThistleSwampBonemassPremium = Percent("03 - Foraging",
                "Swamp thistle later risk bonus (%)", 7.5f,
                "Further Swamp thistle bonus from Bonemass and diminishing later Forsaken defeats.", 0, 50);

            MaterialBonus = Percent("04 - Natural Materials",
                "Own-boss yield improvement (%)", 10,
                "Average increase per natural resource. Uses extra probabilistic native item drops.", 0, 100);
            VeteranMaterials = Percent("04 - Natural Materials",
                "Regional veteran yield per later Forsaken (%)", 25,
                "Biome-exclusive materials (copper, tin, iron scrap, silver, tar etc.) gain this much extra yield for each Forsaken defeated after their own biome boss. Wood and stone are unaffected.", 0, 200);
            StoneBonus = Percent("04 - Natural Materials",
                "Stone base yield improvement (%)", 20,
                "Special stone progression starts after the Elder.", 0, 100);
            WoodEikthyr = Percent("04 - Natural Materials",
                "Normal wood - Eikthyr (%)", 10,
                "Additional normal wood yield when the first Forsaken is defeated.", 0, 100);
            WoodElder = Percent("04 - Natural Materials",
                "Normal wood - Elder (%)", 10,
                "Additional normal wood yield from defeating the Black Forest Forsaken.", 0, 100);
            WoodLater = Percent("04 - Natural Materials",
                "Normal wood - each subsequent boss (%)", 5,
                "Additional normal wood yield from each distinct boss after the Elder.", 0, 100);
            StoneLater = Percent("04 - Natural Materials",
                "Stone - each boss after Elder (%)", 5,
                "Additional stone yield from each distinct boss after the Elder.", 0, 100);
            CoreWoodElder = Percent("04 - Natural Materials",
                "Core wood - Elder (%)", 20,
                "Core wood receives this single bonus from the Elder, not later bosses.", 0, 100);
            FineWoodElder = Percent("04 - Natural Materials",
                "Fine wood - Elder (%)", 10,
                "Fine wood yield bonus unlocked after the Elder.", 0, 100);
            FineWoodYagluth = Percent("04 - Natural Materials",
                "Fine wood - Yagluth (%)", 20,
                "Additional fine wood yield bonus unlocked after Yagluth.", 0, 100);
            AshwoodFader = Percent("04 - Natural Materials",
                "Ashwood - Fader (%)", 10,
                "Ashwood yield bonus unlocked after Fader.", 0, 100);

            FishYieldBonus = Percent("06 - Fishing", "Native biome fish yield bonus (%)", 15,
                "Existing native rod-catch yield bonus; also drives fish spawn abundance and higher-quality spawn rolls after the native biome Forsaken. Does not affect unhooked fish pickups.", 0, 100);
            XpAllSkills = Bind("07 - Experience", "Progression for all vanilla skills", true,
                "Scale every vanilla skill via the current biome and existing Forsaken progression. Makes new weapon types viable late in a world.");
            XpProgressionStrength = Percent("07 - Experience", "Biome XP growth strength (%)", 100,
                "100% matches the familiar strong 1x to 4x tier curve; 0% means normal XP, without disabling action bonuses.", 0, 300);
            XpCatchUpBonus = Percent("07 - Experience", "Low skill catch-up bonus cap (%)", 15,
                "Small optional extra for skills below expected world progression, on top of the strong tier multiplier.", 0, 50);
            VenturePerKm = Percent("07 - Experience", "Expedition bonus per active minute (%)", 1f,
                "Expedition grows linearly with active time away from bases: this much extra XP for all skills per minute in which you earn any skill XP. Standing around builds nothing. Only applies away from bases. 0 disables Expedition.", 0, 10);
            VentureCap = Percent("07 - Experience", "Expedition bonus cap (%)", 200,
                "Upper limit for the Expedition bonus.", 0, 1000);
            XpChallenge = Bind("08 - Experience challenge", "Challenge XP", true,
                "Extra XP for genuine combat threats, good parries and confirmed perfect dodge streaks. No boss challenge bonuses.");
            XpCombatStrength = Percent("08 - Experience challenge", "Enemy challenge bonus strength (%)", 100,
                "100% = mild contextual threat and moving-target bonuses; 0% disables those extras.", 0, 300);
            XpParryStrength = Percent("08 - Experience challenge", "Parry risk bonus strength (%)", 100,
                "100% = the designed risk curve; near-stagger parries multiply only their small EXTRA bonus.", 0, 300);
            XpDodgeStrength = Percent("08 - Experience challenge", "Perfect dodge bonus strength (%)", 100,
                "100% = growing capped success streak bonuses.", 0, 300);
            RaidBonus = Percent("08 - Experience challenge", "Raid defense bonus (%)", 50,
                "Extra combat XP for fighting raid creatures while you are inside an active raid.", 0, 300);
            XpDodgeStreaks = Bind("08 - Experience challenge", "Perfect dodge streak bonus", true,
                "Only successful dodge-hit events earn the additional streak bonus.");

            LaterFactor = Percent("05 - Progression",
                "Later-boss contribution (%)", 50,
                "Each later biome contributes this proportion of the preceding biome-step bonus.", 0, 100);
            SkippedFactor = Percent("05 - Progression",
                "Unconquered biome contribution (%)", 15,
                "Contribution from later bosses when the biome's own boss is still undefeated.", 0, 100);
            Diagnostics = Config.Bind("09 - Diagnostics", "Enable diagnostics", false,
                "Write a bounded, event-driven session log to BepInEx/Aldr-Diagnostics.log. Disable after validation. Local setting, never server-synchronized.");
            Config.Save();
            AldrDiagnostics.Start(Logger);
            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll();
                AldrPatchAudit.Check();
                AldrDiagnostics.Settings();
                AldrDiagnostics.Flush();
            }
            catch (Exception e)
            {
                AldrDiagnostics.Log("PATCH_FATAL", e.ToString());
                Logger.LogError(e);
            }
            Logger.LogInfo(Name + " " + Version + " loaded. Diagnostic file in BepInEx.");
        }

        private ConfigEntry<T> Bind<T>(string section, string label,
            T value, string description)
        {
            var entry = Config.Bind(section, label, value,
                new ConfigDescription(description,
                    null, new ConfigurationManagerAttributes { Order = 100, DispName = label == "Enable Heimvöxtr" ? "Enable Aldr" : label }));
            Sync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }
        private ConfigEntry<float> Percent(string section, string label,
            float value, string description, float min, float max)
        {
            var entry = Config.Bind(section, label, value,
                new ConfigDescription(description,
                    new AcceptableValueRange<float>(min,max),
                    new ConfigurationManagerAttributes { Order = 10,
                        DispName = label.Replace("(pp)", "(%)") }));
            Sync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }
        private void OnDestroy() { AldrDiagnostics.Close(); _harmony?.UnpatchSelf(); }
    }

    // Compatible with BepInEx ConfigurationManager; no dependency on it.
    #pragma warning disable CS0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public string DispName;
        public int? Order;
        public string Category;
    }
}
