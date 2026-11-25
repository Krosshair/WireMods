using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace WiresAldr
{
    // All settings. Section 01 is the simple part: a switch and a strength per
    // feature. Every section after it holds the numbers behind one feature and
    // is marked advanced, so config menus hide it until asked.
    public sealed partial class Plugin
    {
        // Switches
        internal static ConfigEntry<bool> Enabled, Lock, XpEnabled, ShowSkillBonuses, LocationXp, CatchUp,
            ExpeditionEnabled, XpChallenge, HitXp, Rewards, SleepBonusOn,
            Stars, StarLimits, NightStar, GroupLeaders, StarLooksOn,
            Forage, Materials, FishYields, DurabilityOn, KnockbackOn,
            FishExtraCatch, FishSpawnBoost, FishQualityBoost,
            KnockbackPlayers, KnockbackCreatures, Diagnostics;

        // Strengths of section 01 that are used as they are
        internal static ConfigEntry<float> ChallengeStrength, HitXpStrength, RewardsStrength,
            StarLimitStrength, StarLooksStrength, DurabilityStrength, KnockbackStrength;

        // Numbers scaled by their feature's strength (0 while it's off)
        internal static Scaled
            XpProgressionStrength, XpCatchUpBonus, XpWeaponTransfer, VenturePerKm, ExpeditionCurve,
            XpStarBonus, RaidBonus, SleepXpShare,
            StarBonus, ForageBonus, VeteranForage,
            MushroomBlackForestPremium, ThistleSwampElderPremium, ThistleSwampBonemassPremium,
            MaterialBonus, VeteranMaterials, StoneBonus, StoneLater,
            WoodEikthyr, WoodElder, WoodLater, CoreWoodElder, FineWoodElder, FineWoodYagluth, AshwoodFader,
            FishYieldBonus;

        internal static ConfigEntry<float>
            // World
            ForageCap, LaterFactor, SkippedFactor,
            StarCap, ForageMaxExtra, MaterialMaxExtra, FishQualityFactor, FishBonusCap,
            // Location and catch-up
            XpProgressShare, XpRewardLocationShare, XpCatchUpBase, XpCatchUpPerTier,
            XpTransferShare, XpTransferMinimum,
            // Expedition
            ExpeditionCredit, ExpeditionRoamRadius, ExpeditionRoamMinutes,
            ExpeditionSailShare, ExpeditionBaseDecay, ExpeditionBaseDoubling, ExpeditionIdleDecay, ExpeditionIdleGrace,
            // Challenge
            XpExtrasCap, XpExtrasCapRanged, XpCombatStrength,
            XpComboStep, XpCounterBonus, XpBlockStagger, XpSneakBonus, XpCookingFood,
            XpTravelBonus, XpRecipeIngredient,
            // Rewards
            KillXp, KillXpPerBiome, SneakKillXp, SummonKillShare, TreeXp, DepositXp,
            FishXp, FishXpPerQuality, XpParryStrength, XpDodgeStrength, BarrierXp,
            FirstDishXp, CropXp,
            // Sleep
            SleepWindow, SleepHalfLife,
            // Durability and knockback
            ArmorWear, ArmorWearCap, CapeShare, BlockWear, BlockWearCap, KnockbackVanillaPoint;

        // The questions nearly every feature asks first.
        internal static bool On => Enabled.Value;
        internal static bool XpOn => Enabled.Value && XpEnabled.Value;
        internal static bool RewardsOn => XpOn && Rewards.Value;
        internal static bool ChallengeOn => XpOn && XpChallenge.Value;

        private const string General = "01 - General";

        private void BindSettings()
        {
            MoveSavedValues();

            Enabled = Toggle(General, "Enable Aldr", true, "Master switch.");
            Lock = Toggle(General, "Lock server configuration", true, "Clients use the server's settings.");
            Sync.AddLockingConfigEntry(Lock);

            XpEnabled = Toggle(General, "Experience", true, "Everything that changes XP: location, catch-up, Expedition, challenge, hit XP, rewards and sleep.");
            ShowSkillBonuses = Config.Bind(General, "XP bonuses on the skills page", true,
                new ConfigDescription("Shows your current XP bonus on the skills page: the bonus every skill gets at the top, and each skill's extra next to its level. Local, not synced.",
                    null, new ConfigurationManagerAttributes { Order = NextOrder(General) }));
            LocationXp = Toggle(General, "Location XP", true, "More XP in harder biomes. Details in section 02.");
            var locationStrength = Strength("Location XP", "100 = +10% in the Black Forest up to +135% in the Ashlands.");
            CatchUp = Toggle(General, "Catch-up", true, "Skills that lag behind your progress or your best weapon level faster. Details in section 03.");
            var catchUpStrength = Strength("Catch-up", "Scales both bonuses.");
            ExpeditionEnabled = Toggle(General, "Expedition", true, "An XP bonus that grows while you adventure away from bases. Details in section 04.");
            var expeditionStrength = Strength("Expedition", "Scales how fast the bonus grows.");
            XpChallenge = Toggle(General, "Challenge XP", true, "Star, raid and action bonuses. Never against bosses. Details in section 05.");
            ChallengeStrength = Strength("Challenge XP", "Scales all of them.");
            HitXp = Toggle(General, "Hit XP", true, "Every enemy a swing hits gives XP, and a critical hit or a hit on a weakness gives more, by how much more it hurt.");
            HitXpStrength = Strength("Hit XP", "Scales what critical hits and weaknesses add.");
            Rewards = Toggle(General, "Rewards", true, "Flat XP for accomplishments. Never for bosses. Details in section 06.");
            RewardsStrength = Strength("Rewards", "Scales all of them.");
            SleepBonusOn = Toggle(General, "Sleep bonus", true, "Waking up from sleep adds a share of the XP each skill earned in the last day. Details in section 13.");
            SleepXpShare = new Scaled(Number(General, "Sleep bonus (%)", 20,
                "The share, then the day starts over. 0 turns it off.", 0, 100), SleepBonusOn);

            Stars = Toggle(General, "Creature stars", true, "Conquered biomes have more starred creatures. Details in section 07.");
            var starStrength = Strength("Creature stars", "Scales the added star chance.");
            StarLimits = Toggle(General, "Higher star limits", true, "A biome's creatures can roll one more star for its Forsaken and every later one you defeat. Needs Creature stars.");
            StarLimitStrength = Strength("Higher star limits", "Stars per Forsaken: 100 = one, 50 = one for every second.");
            NightStar = Toggle(General, "Night star", true, "One more star can roll at night. Needs Creature stars.");
            GroupLeaders = Toggle(General, "Group leaders", true, "The leader of a group of two or more has one star more. Needs Creature stars.");
            StarLooksOn = Toggle(General, "Star looks", true, "Creatures with more stars than Valheim has looks for grow and change colour further with each one.");
            StarLooksStrength = Strength("Star looks", "Scales how much each of those stars changes.");
            Forage = Toggle(General, "Wild foraging", true, "Wild plants of conquered biomes give more. Details in section 08.");
            var forageStrength = Strength("Wild foraging", "Scales the harvest chance and the yield.");
            Materials = Toggle(General, "Natural materials", true, "Trees, rocks and deposits give more as you defeat Forsaken. Details in section 09.");
            var materialStrength = Strength("Natural materials", "Scales all yields.");
            FishYields = Toggle(General, "Wild fishing", true, "More and better fish in conquered biomes. Details in section 10.");
            var fishStrength = Strength("Wild fishing", "Scales the fish bonus.");

            DurabilityOn = Toggle(General, "Durability", true, "Armor and shields wear by how hard you were hit. Details in section 14.");
            DurabilityStrength = Strength("Durability", "Scales armor and block wear.");
            KnockbackOn = Toggle(General, "Knockback", true, "A hit knocks back by how much of the target's maximum health it takes: 50% as in vanilla, less below that, up to twice as far. Details in section 15.");
            KnockbackStrength = Strength("Knockback", "Scales the push.");

            const string Location = "02 - Location XP";
            XpProgressionStrength = new Scaled(locationStrength, LocationXp);
            XpProgressShare = Number(Location, "Progress biome share (%)", 20,
                "Share of your progress biome's bonus (the biome after your last Forsaken) you get everywhere.", 0, 100);
            XpRewardLocationShare = Number(Location, "Reward location share (%)", 50,
                "Share of the location bonus that flat rewards get.", 0, 100);

            const string CatchUpSection = "03 - Catch-up";
            XpCatchUpBonus = Number(CatchUpSection, "Catch-up bonus (%)", 15,
                "Skills below the threshold: bonus x (threshold - skill) / threshold.", 0, 100, CatchUp, catchUpStrength);
            XpCatchUpBase = Number(CatchUpSection, "Threshold base", 8,
                "Threshold = base + per tier x tier (Meadows 1, Black Forest 2, ...).", 0, 100);
            XpCatchUpPerTier = Number(CatchUpSection, "Threshold per tier", 6, "See threshold base.", 0, 20);
            XpWeaponTransfer = Number(CatchUpSection, "Combat transfer bonus (%)", 100,
                "Combat skills below the transfer threshold: bonus x (threshold - skill) / threshold. The larger of this and catch-up applies.", 0, 300, CatchUp, catchUpStrength);
            XpTransferShare = Number(CatchUpSection, "Transfer threshold (% of best)", 75,
                "Threshold = this share of your best combat skill.", 0, 100);
            XpTransferMinimum = Number(CatchUpSection, "Transfer minimum best skill", 20,
                "Transfer starts once your best combat skill reaches this.", 0, 100);

            const string Expedition = "04 - Expedition";
            VenturePerKm = Number(Expedition, "Linear growth (% per minute)", 0.3f,
                "Bonus = linear x T + curve x T^1.5, T = expedition minutes.", 0, 5, ExpeditionEnabled, expeditionStrength);
            ExpeditionCurve = Number(Expedition, "Curve growth (% per minute^1.5)", 0.067f,
                "See linear growth.", 0, 1, ExpeditionEnabled, expeditionStrength);
            ExpeditionCredit = Number(Expedition, "Credit per XP gain (s)", 30,
                "Each XP gain adds the time since the previous one, up to this.", 1, 120);
            ExpeditionRoamRadius = Number(Expedition, "Roam distance (m)", 20,
                "Grows only while you move this far within the roam time.", 0, 200);
            ExpeditionRoamMinutes = Number(Expedition, "Roam time (min)", 6, "See roam distance.", 1, 30);
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
            XpStarBonus = Number(Challenge, "Star bonus (% per star)", 25, "Combat and dodge XP per star.", 0, 200, XpChallenge, ChallengeStrength);
            RaidBonus = Number(Challenge, "Raid bonus (%)", 50, "Combat XP multiplier against raiders while you're inside the raid.", 0, 300, XpChallenge, ChallengeStrength);
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
                "Added to the star chance of that biome's creatures, scaled with world difficulty: chance + this x chance / 10%.", 0, 40, Stars, starStrength);
            StarCap = Number(StarsSection, "Star chance cap (%)", 95, "Highest star chance after the bonus.", 0, 100);

            const string ForageSection = "08 - Foraging";
            ForageBonus = Number(ForageSection, "Harvest chance after biome Forsaken (pp)", 25,
                "Added to the bonus harvest chance of that biome's wild plants.", 0, 100, Forage, forageStrength);
            ForageCap = Number(ForageSection, "Bonus harvest chance cap (%)", 80,
                "Highest total bonus harvest chance, Farming skill included.", 0, 100);
            VeteranForage = Number(ForageSection, "Yield after later Forsaken (%)", 20,
                "Extra items per pick from the first Forsaken killed after the plant's own; each further one adds the later share of the previous (20, 10, 5...: max about 40).", 0, 200, Forage, forageStrength);
            MushroomBlackForestPremium = Number(ForageSection, "Black Forest mushrooms (pp)", 8,
                "Extra harvest chance after the Elder.", 0, 50, Forage, forageStrength);
            ThistleSwampElderPremium = Number(ForageSection, "Swamp thistle after the Elder (pp)", 10,
                "Extra harvest chance for thistle picked in the Swamp.", 0, 50, Forage, forageStrength);
            ThistleSwampBonemassPremium = Number(ForageSection, "Swamp thistle after Bonemass (pp)", 7.5f,
                "Further Swamp thistle harvest chance.", 0, 50, Forage, forageStrength);
            ForageMaxExtra = Number(ForageSection, "Max extra items per pick", 12, "Cap for the later-Forsaken yield.", 0, 50);

            const string MaterialSection = "09 - Natural Materials";
            MaterialBonus = Material(MaterialSection, "Regional yield after biome Forsaken (%)", 10,
                "Copper, tin, iron, silver, obsidian, tar and other regional materials of that biome.", materialStrength);
            VeteranMaterials = Material(MaterialSection, "Regional yield after later Forsaken (%)", 25,
                "From the first Forsaken killed after their biome's own; each further one adds the later share of the previous (25, 12.5...: max about 50).", materialStrength, 200);
            WoodEikthyr = Material(MaterialSection, "Wood after Eikthyr (%)", 10, "Extra wood.", materialStrength);
            WoodElder = Material(MaterialSection, "Wood after the Elder (%)", 10, "Extra wood.", materialStrength);
            WoodLater = Material(MaterialSection, "Wood per later Forsaken (%)", 5, "For each Forsaken after the Elder.", materialStrength);
            StoneBonus = Material(MaterialSection, "Stone after the Elder (%)", 20, "Extra stone.", materialStrength);
            StoneLater = Material(MaterialSection, "Stone per later Forsaken (%)", 5, "For each Forsaken after the Elder.", materialStrength);
            CoreWoodElder = Material(MaterialSection, "Core wood after the Elder (%)", 20, "Extra core wood.", materialStrength);
            FineWoodElder = Material(MaterialSection, "Fine wood after the Elder (%)", 10, "Extra fine wood.", materialStrength);
            FineWoodYagluth = Material(MaterialSection, "Fine wood after Yagluth (%)", 20, "On top of the Elder's.", materialStrength);
            AshwoodFader = Material(MaterialSection, "Ashwood after Fader (%)", 10, "Extra ashwood.", materialStrength);
            MaterialMaxExtra = Number(MaterialSection, "Max extra drops per item", 4,
                "Each dropped item rolls bonus % / 100 extra copies (the fraction as a chance), up to this.", 0, 20);

            const string FishSection = "10 - Fishing";
            FishYieldBonus = Number(FishSection, "Fish bonus after biome Forsaken (%)", 15,
                "Bonus for that biome's native fish. Later Forsaken add their share (section 11).", 0, 100, FishYields, fishStrength);
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

            const string DiagnosticsSection = "12 - Diagnostics";
            Diagnostics = Config.Bind(DiagnosticsSection, "Enable diagnostics", false,
                new ConfigDescription("Writes BepInEx/Aldr-Diagnostics.log. Local, not synced.", null,
                    new ConfigurationManagerAttributes { Order = NextOrder(DiagnosticsSection), IsAdvanced = true }));

            const string SleepSection = "13 - Sleep";
            SleepWindow = Number(SleepSection, "Full value (game hours)", 24,
                "XP earned this recently before falling asleep counts fully. A game day is 24 hours.", 1, 72);
            SleepHalfLife = Number(SleepSection, "Fading, half-life (game hours)", 2,
                "Older XP counts half as much for every this many hours beyond.", 0.5f, 24);

            const string DurabilitySection = "14 - Durability";
            ArmorWear = Number(DurabilitySection, "Armor wear (points)", 100,
                "Durability a hit for your whole maximum health would cost, shared between the armor pieces. Half the health, half the wear.", 0, 500);
            ArmorWearCap = Number(DurabilitySection, "Armor wear cap (points)", 65, "Most a single hit can cost.", 0, 500);
            CapeShare = Number(DurabilitySection, "Cape share (%)", 2, "How much of it the cape takes while you wear other armor.", 0, 100);
            BlockWear = Number(DurabilitySection, "Block wear (points)", 45,
                "Durability that blocking a hit of your whole maximum health would cost the shield or weapon.", 0, 500);
            BlockWearCap = Number(DurabilitySection, "Block wear cap (points)", 35, "Most a single block can cost.", 0, 500);

            const string KnockbackSection = "15 - Knockback";
            KnockbackPlayers = Toggle(KnockbackSection, "Players", true, "Hits on players.");
            KnockbackCreatures = Toggle(KnockbackSection, "Creatures", true, "Hits on creatures.");
            KnockbackVanillaPoint = Number(KnockbackSection, "Vanilla point (% of max health)", 50,
                "A hit that takes this much pushes as in vanilla. Below and above it changes in proportion.", 10, 100);
        }

        // Switches and strengths that used to sit in the sections behind section 01.
        // Moved before anything binds, so saved values are picked up as if they'd
        // always been there.
        private void MoveSavedValues()
        {
            var saved = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                ?.GetValue(Config, null) as Dictionary<ConfigDefinition, string>;
            if (saved == null) return;
            Move(saved, "02 - Location XP", "Location bonus strength (%)", "Location XP strength (%)");
            Move(saved, "04 - Expedition", "Expedition", "Expedition");
            Move(saved, "05 - Challenge XP", "Challenge XP", "Challenge XP");
            Move(saved, "06 - Rewards", "Rewards", "Rewards");
            Move(saved, "13 - Sleep", "Sleep bonus (%)", "Sleep bonus (%)");
        }

        private static void Move(Dictionary<ConfigDefinition, string> saved, string section, string key, string newKey)
        {
            var from = new ConfigDefinition(section, key);
            if (!saved.TryGetValue(from, out string value)) return;
            saved.Remove(from);
            // A value already saved in the new place is newer: it stays.
            var to = new ConfigDefinition(General, newKey);
            if (!saved.ContainsKey(to)) saved[to] = value;
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

        private ConfigurationManagerAttributes Attributes(string section) =>
            new ConfigurationManagerAttributes { Order = NextOrder(section), IsAdvanced = section != General };

        private ConfigEntry<bool> Toggle(string section, string label, bool value, string description)
        {
            var entry = Config.Bind(section, label, value, new ConfigDescription(description, null, Attributes(section)));
            Sync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        private ConfigEntry<float> Number(string section, string label, float value, string description, float min, float max)
        {
            var entry = Config.Bind(section, label, value, new ConfigDescription(description,
                new AcceptableValueRange<float>(min, max), Attributes(section)));
            Sync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        private Scaled Number(string section, string label, float value, string description, float min, float max,
            ConfigEntry<bool> on, ConfigEntry<float> strength) =>
            new Scaled(Number(section, label, value, description, min, max), on, strength);

        private Scaled Material(string section, string label, float value, string description,
            ConfigEntry<float> strength, float max = 100) =>
            Number(section, label, value, description, 0, max, Materials, strength);

        private ConfigEntry<float> Strength(string feature, string description) =>
            Number(General, feature + " strength (%)", 100, description, 0, 300);
    }

    // A number of a feature: nothing while the feature is off, otherwise scaled by its strength.
    internal sealed class Scaled
    {
        private readonly ConfigEntry<float> number, strength;
        private readonly ConfigEntry<bool> on;

        internal Scaled(ConfigEntry<float> number, ConfigEntry<bool> on, ConfigEntry<float> strength = null)
        {
            this.number = number;
            this.on = on;
            this.strength = strength;
        }

        internal float Value => !on.Value ? 0f : strength == null ? number.Value : number.Value * strength.Value / 100f;
    }

    // Compatible with BepInEx ConfigurationManager; no dependency on it.
    #pragma warning disable CS0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public string DispName;
        public int? Order;
        public string Category;
        public bool? IsAdvanced;
    }
}
