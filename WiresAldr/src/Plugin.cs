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
        public const string Version = "1.0.4";
        private static readonly ConfigSync Sync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version,
            ModRequired = true
        };
        internal static ConfigEntry<bool> ShowSkillBonuses;
        internal static ConfigEntry<bool> Enabled, Lock, Stars, Forage, Materials, FishYields,
            XpEnabled, ExpeditionEnabled, XpChallenge, Rewards, Diagnostics,
            FishExtraCatch, FishSpawnBoost, FishQualityBoost;
        internal static ConfigEntry<float>
            // World
            StarBonus, ForageBonus, ForageCap, VeteranForage,
            MushroomBlackForestPremium, ThistleSwampElderPremium, ThistleSwampBonemassPremium,
            MaterialBonus, VeteranMaterials, StoneBonus, StoneLater,
            WoodEikthyr, WoodElder, WoodLater, CoreWoodElder, FineWoodElder, FineWoodYagluth, AshwoodFader,
            FishYieldBonus, LaterFactor, SkippedFactor,
            StarCap, ForageMaxExtra, MaterialMaxExtra, FishQualityFactor, FishBonusCap,
            // Location and catch-up
            XpProgressionStrength, XpProgressShare, XpRewardLocationShare,
            XpCatchUpBonus, XpCatchUpBase, XpCatchUpPerTier,
            XpWeaponTransfer, XpTransferShare, XpTransferMinimum,
            // Expedition
            VenturePerKm, ExpeditionCurve, ExpeditionCredit, ExpeditionRoamRadius, ExpeditionRoamMinutes,
            ExpeditionSailShare, ExpeditionBaseDecay, ExpeditionBaseDoubling, ExpeditionIdleDecay, ExpeditionIdleGrace,
            // Challenge
            XpStarBonus, RaidBonus, XpExtrasCap, XpExtrasCapRanged, XpCombatStrength,
            XpComboStep, XpCounterBonus, XpBlockStagger, XpSneakBonus, XpCookingFood,
            XpTravelBonus, XpRecipeIngredient,
            // Rewards
            KillXp, KillXpPerBiome, SneakKillXp, SummonKillShare, TreeXp, DepositXp,
            FishXp, FishXpPerQuality, XpParryStrength, XpDodgeStrength, BarrierXp,
            FirstDishXp, CropXp;
        private Harmony _harmony;

        private void Awake()
        {
            Config.SaveOnConfigSet = false;

            const string General = "01 - General";
            Enabled = Toggle(General, "Enable Aldr", true, "Master switch.");
            Lock = Toggle(General, "Lock server configuration", true, "Clients use the server's settings.");
            Sync.AddLockingConfigEntry(Lock);
            XpEnabled = Toggle(General, "Experience", true, "All experience changes (sections 02 to 06).");
            Stars = Toggle(General, "Creature stars", true, "Section 07.");
            Forage = Toggle(General, "Wild foraging", true, "Section 08.");
            Materials = Toggle(General, "Natural materials", true, "Section 09.");
            FishYields = Toggle(General, "Wild fishing", true, "Section 10.");
            ShowSkillBonuses = Config.Bind(General, "XP bonuses on the skills page", true,
                new ConfigDescription("Shows your current XP bonus on the skills page: the bonus every skill gets at the top, and each skill's own extra next to its level. Local, not synced.",
                    null, new ConfigurationManagerAttributes { Order = NextOrder(General) }));

            const string Location = "02 - Location XP";
            XpProgressionStrength = Number(Location, "Location bonus strength (%)", 100,
                "Scales the biome bonus: Meadows +0%, Black Forest +10%, Swamp/Ocean +25%, Mountains +45%, Plains +70%, Mistlands +100%, Ashlands +135%.", 0, 300);
            XpProgressShare = Number(Location, "Progress biome share (%)", 20,
                "Share of your progress biome's bonus (the biome after your last Forsaken) you get everywhere.", 0, 100);
            XpRewardLocationShare = Number(Location, "Reward location share (%)", 50,
                "Share of the location bonus that flat rewards get.", 0, 100);

            const string CatchUp = "03 - Catch-up";
            XpCatchUpBonus = Number(CatchUp, "Catch-up bonus (%)", 15,
                "Skills below the threshold: bonus x (threshold - skill) / threshold.", 0, 100);
            XpCatchUpBase = Number(CatchUp, "Threshold base", 8,
                "Threshold = base + per tier x tier (Meadows 1, Black Forest 2, ...).", 0, 100);
            XpCatchUpPerTier = Number(CatchUp, "Threshold per tier", 6,
                "See threshold base.", 0, 20);
            XpWeaponTransfer = Number(CatchUp, "Combat transfer bonus (%)", 100,
                "Combat skills below the transfer threshold: bonus x (threshold - skill) / threshold. The larger of this and catch-up applies.", 0, 300);
            XpTransferShare = Number(CatchUp, "Transfer threshold (% of best)", 75,
                "Threshold = this share of your best combat skill.", 0, 100);
            XpTransferMinimum = Number(CatchUp, "Transfer minimum best skill", 20,
                "Transfer starts once your best combat skill reaches this.", 0, 100);

            const string Expedition = "04 - Expedition";
            ExpeditionEnabled = Toggle(Expedition, "Expedition", true, "Bonus that grows while you adventure away from bases.");
            VenturePerKm = Number(Expedition, "Linear growth (% per minute)", 0.3f,
                "Bonus = linear x T + curve x T^1.5, T = expedition minutes.", 0, 5);
            ExpeditionCurve = Number(Expedition, "Curve growth (% per minute^1.5)", 0.067f,
                "See linear growth.", 0, 1);
            ExpeditionCredit = Number(Expedition, "Credit per XP gain (s)", 30,
                "Each XP gain adds the time since the previous one, up to this.", 1, 120);
            ExpeditionRoamRadius = Number(Expedition, "Roam distance (m)", 20,
                "Grows only while you move this far within the roam time.", 0, 200);
            ExpeditionRoamMinutes = Number(Expedition, "Roam time (min)", 6,
                "See roam distance.", 1, 30);
            ExpeditionSailShare = Number(Expedition, "Sailing pace (%)", 20,
                "Growth while sailing at 2 m/s or more, relative to normal.", 0, 100);
            ExpeditionBaseDecay = Number(Expedition, "Decay at a base, start (min per min)", 0.1f,
                "Expedition minutes lost per minute when you arrive at a base. It speeds up the longer you stay: quick visits home cost almost nothing, long stays use it up. Resting doubles it.", 0, 20);
            ExpeditionBaseDoubling = Number(Expedition, "Decay at a base, doubling time (s)", 45,
                "How often the decay at a base doubles while you stay.", 5, 600);
            ExpeditionIdleDecay = Number(Expedition, "Idle decay (min per min)", 0.5f,
                "Expedition minutes lost per minute away from bases without XP. Resting doubles it.", 0, 20);
            ExpeditionIdleGrace = Number(Expedition, "Idle grace (s)", 90,
                "Time without XP before idle decay starts. Halved while resting.", 0, 600);

            const string Challenge = "05 - Challenge XP";
            XpChallenge = Toggle(Challenge, "Challenge XP", true, "Star, raid and action bonuses. Never against bosses.");
            XpStarBonus = Number(Challenge, "Star bonus (% per star)", 25, "Combat and dodge XP per star.", 0, 200);
            RaidBonus = Number(Challenge, "Raid bonus (%)", 50, "Combat XP multiplier against raiders while you're inside the raid.", 0, 300);
            XpExtrasCap = Number(Challenge, "Extras cap (%)", 60, "Cap for all action extras together.", 0, 300);
            XpExtrasCapRanged = Number(Challenge, "Extras cap, ranged (%)", 75, "Cap for bows and crossbows.", 0, 300);
            XpCombatStrength = Number(Challenge, "Danger and aim strength (%)", 100,
                "Scales danger (+10% same biome as your last conquered, +4% per biome beyond, max 18%) and aim bonuses.", 0, 300);
            XpComboStep = Number(Challenge, "Combo bonus (% per step)", 12, "Per landed chain step, up to 3 steps.", 0, 50);
            XpCounterBonus = Number(Challenge, "Counter bonus (%)", 6, "First hit within 2.25 s of a parry or perfect dodge.", 0, 50);
            XpBlockStagger = Number(Challenge, "Block stagger bonus (%)", 40, "Times the share of your stagger bar the blocked hit filled.", 0, 100);
            XpSneakBonus = Number(Challenge, "Sneak bonus (%)", 35, "Sneaking near unaware enemies.", 0, 100);
            XpCookingFood = Number(Challenge, "Cooking food bonus (%)", 50,
                "Times (health + stamina + eitr - 40) / 120 of the dish, max 1.", 0, 100);
            XpTravelBonus = Number(Challenge, "Travel bonus (%)", 40, "Run, Swim and Ride at steady straight-line progress.", 0, 100);
            XpRecipeIngredient = Number(Challenge, "Recipe bonus (% per ingredient type)", 2.5f,
                "Crafting, cooking and placing pieces, max 12%. Upgrades +3.5%.", 0, 10);

            const string RewardsSection = "06 - Rewards";
            Rewards = Toggle(RewardsSection, "Rewards", true, "Flat XP for accomplishments. Never for bosses.");
            KillXp = Number(RewardsSection, "Kill XP", 2.5f,
                "Creature of your last conquered biome. Halved per biome older; +1 per biome beyond (max +3). +50% per star.", 0, 20);
            KillXpPerBiome = Number(RewardsSection, "Kill XP per biome beyond", 1, "See kill XP.", 0, 10);
            SneakKillXp = Number(RewardsSection, "Sneak attack kill XP", 3, "Sneak XP, crouched against an unaware target. Halved per biome older.", 0, 20);
            SummonKillShare = Number(RewardsSection, "Summon kill share (%)", 60, "Of a kill; full if you fought in the last 20 s.", 0, 100);
            TreeXp = Number(RewardsSection, "Tree felled XP", 2, "Plus the tool tier the tree needs.", 0, 20);
            DepositXp = Number(RewardsSection, "Deposit XP", 4,
                "Regional deposits: plus tool tier. Per chunk: this / (2 x sqrt(chunks)); mined out: this again.", 0, 20);
            FishXp = Number(RewardsSection, "Fish caught XP", 3, "Rod catches.", 0, 20);
            FishXpPerQuality = Number(RewardsSection, "Fish XP per quality", 1.5f, "Per quality level above 1.", 0, 10);
            XpParryStrength = Number(RewardsSection, "Parry XP strength (%)", 100,
                "Parry XP = 0.15 x (1 + 9 x stagger^4) x (0.2 + 0.8 x hit size) x this.", 0, 300);
            XpDodgeStrength = Number(RewardsSection, "Perfect dodge XP strength (%)", 100,
                "Streak XP = 0.05 per dodge in the streak (max 0.3) x this.", 0, 300);
            BarrierXp = Number(RewardsSection, "Barrier XP", 4, "Blood Magic, per unskilled barrier's worth of damage absorbed.", 0, 20);
            FirstDishXp = Number(RewardsSection, "First station dish XP", 2, "Cooking.", 0, 20);
            CropXp = Number(RewardsSection, "Crop harvested XP", 0.5f, "Farming.", 0, 10);

            const string StarsSection = "07 - Creature Stars";
            StarBonus = Number(StarsSection, "Star chance after biome Forsaken (pp)", 8,
                "Added to the star chance of that biome's creatures, scaled with world difficulty: chance + this x chance / 10%.", 0, 40);
            StarCap = Number(StarsSection, "Star chance cap (%)", 95, "Highest star chance after the bonus.", 0, 100);

            const string ForageSection = "08 - Foraging";
            ForageBonus = Number(ForageSection, "Harvest chance after biome Forsaken (pp)", 25,
                "Added to the bonus harvest chance of that biome's wild plants.", 0, 100);
            ForageCap = Number(ForageSection, "Bonus harvest chance cap (%)", 80,
                "Highest total bonus harvest chance, Farming skill included.", 0, 100);
            VeteranForage = Number(ForageSection, "Yield after later Forsaken (%)", 20,
                "Extra items per pick from the first Forsaken killed after the plant's own; each further one adds the later share of the previous (20, 10, 5...: max about 40).", 0, 200);
            MushroomBlackForestPremium = Number(ForageSection, "Black Forest mushrooms (pp)", 8,
                "Extra harvest chance after the Elder.", 0, 50);
            ThistleSwampElderPremium = Number(ForageSection, "Swamp thistle after the Elder (pp)", 10,
                "Extra harvest chance for thistle picked in the Swamp.", 0, 50);
            ThistleSwampBonemassPremium = Number(ForageSection, "Swamp thistle after Bonemass (pp)", 7.5f,
                "Further Swamp thistle harvest chance.", 0, 50);
            ForageMaxExtra = Number(ForageSection, "Max extra items per pick", 12, "Cap for the later-Forsaken yield.", 0, 50);

            const string MaterialSection = "09 - Natural Materials";
            MaterialBonus = Number(MaterialSection, "Regional yield after biome Forsaken (%)", 10,
                "Copper, tin, iron, silver, obsidian, tar and other regional materials of that biome.", 0, 100);
            VeteranMaterials = Number(MaterialSection, "Regional yield after later Forsaken (%)", 25,
                "From the first Forsaken killed after their biome's own; each further one adds the later share of the previous (25, 12.5...: max about 50).", 0, 200);
            WoodEikthyr = Number(MaterialSection, "Wood after Eikthyr (%)", 10, "Extra wood.", 0, 100);
            WoodElder = Number(MaterialSection, "Wood after the Elder (%)", 10, "Extra wood.", 0, 100);
            WoodLater = Number(MaterialSection, "Wood per later Forsaken (%)", 5, "For each Forsaken after the Elder.", 0, 100);
            StoneBonus = Number(MaterialSection, "Stone after the Elder (%)", 20, "Extra stone.", 0, 100);
            StoneLater = Number(MaterialSection, "Stone per later Forsaken (%)", 5, "For each Forsaken after the Elder.", 0, 100);
            CoreWoodElder = Number(MaterialSection, "Core wood after the Elder (%)", 20, "Extra core wood.", 0, 100);
            FineWoodElder = Number(MaterialSection, "Fine wood after the Elder (%)", 10, "Extra fine wood.", 0, 100);
            FineWoodYagluth = Number(MaterialSection, "Fine wood after Yagluth (%)", 20, "On top of the Elder's.", 0, 100);
            AshwoodFader = Number(MaterialSection, "Ashwood after Fader (%)", 10, "Extra ashwood.", 0, 100);
            MaterialMaxExtra = Number(MaterialSection, "Max extra drops per item", 4,
                "Each dropped item rolls bonus % / 100 extra copies (the fraction as a chance), up to this.", 0, 20);

            const string FishSection = "10 - Fishing";
            FishYieldBonus = Number(FishSection, "Fish bonus after biome Forsaken (%)", 15,
                "Bonus for that biome's native fish. Later Forsaken add their share (section 11).", 0, 100);
            FishBonusCap = Number(FishSection, "Fish bonus cap (%)", 80, "Highest total fish bonus.", 0, 200);
            FishExtraCatch = Toggle(FishSection, "Extra fish per catch", true, "Rod catch: bonus % chance of one more fish of the same quality.");
            FishSpawnBoost = Toggle(FishSection, "More fish", true, "Spawn chance and spawn rate x (1 + bonus); population cap + bonus % (at least +1).");
            FishQualityBoost = Toggle(FishSection, "Better fish", true, "Higher-quality roll chance + quality factor x bonus (percentage points).");
            FishQualityFactor = Number(FishSection, "Quality factor (%)", 35, "See Better fish.", 0, 200);

            const string ProgressSection = "11 - Forsaken Shares";
            LaterFactor = Number(ProgressSection, "Later Forsaken share (%)", 50,
                "Stars, harvest chance, fishing and later-Forsaken yields: each later Forsaken adds this share of the previous one.", 0, 100);
            SkippedFactor = Number(ProgressSection, "Unconquered biome share (%)", 15,
                "Share of later Forsaken bonuses a biome gets while its own Forsaken lives.", 0, 100);

            Diagnostics = Config.Bind("12 - Diagnostics", "Enable diagnostics", false,
                "Writes BepInEx/Aldr-Diagnostics.log. Local, not synced.");
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

        // ConfigurationManager lists higher Order first: settings appear
        // in the order they are bound, section by section.
        private string orderSection;
        private int order;
        private int NextOrder(string section)
        {
            if (section != orderSection) { orderSection = section; order = 1000; }
            return order--;
        }
        private ConfigEntry<bool> Toggle(string section, string label, bool value, string description)
        {
            var entry = Config.Bind(section, label, value, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { Order = NextOrder(section) }));
            Sync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }
        private ConfigEntry<float> Number(string section, string label,
            float value, string description, float min, float max)
        {
            var entry = Config.Bind(section, label, value, new ConfigDescription(description,
                new AcceptableValueRange<float>(min, max),
                new ConfigurationManagerAttributes { Order = NextOrder(section) }));
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
