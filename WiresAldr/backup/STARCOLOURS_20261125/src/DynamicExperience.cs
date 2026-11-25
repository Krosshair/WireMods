using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    internal static partial class DynamicExperience
    {
        private static ActionScope current;
        private static FishingScope currentFish;
        private static Character dodgeAttackOwner;
        private sealed class FishingScope { internal Player Player; internal int Tier; internal int Quality; internal bool Completion; }
        private static bool addingParryXp;
        private static int xpDepth;
        private static int summonXpDepth;

        // ---- Your own fighting (attacks, casts, blocks) ----
        // Fighting alongside your summons is rewarded (see summon kills).
        internal static float LastOwnAction = -999f;
        internal const float ActiveWindow = 20f;
        internal static bool RecentlyActive => Time.time - LastOwnAction <= ActiveWindow;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class OwnAttack
        {
            private static void Postfix(Humanoid __instance, bool __result)
            {
                if (__result && __instance == Player.m_localPlayer) LastOwnAction = Time.time;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class OwnBlock
        {
            private static void Postfix(Humanoid __instance, bool __result)
            {
                if (__result && __instance == Player.m_localPlayer) LastOwnAction = Time.time;
            }
        }

        // A tamed summon passing XP to its owner (vanilla Character.RaiseSkill).
        [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
        private static class SummonXp
        {
            private static void Prefix(Character __instance, out bool __state)
            {
                __state = __instance != null && !(__instance is Player) && __instance.IsTamed();
                if (__state) ++summonXpDepth;
            }
            private static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state && summonXpDepth > 0) --summonXpDepth;
                return __exception;
            }
        }

        // ---- Weapon experience transfer ----
        // An experienced fighter picks up a new weapon (or magic) faster: a
        // weapon skill below 75% of your best weapon skill learns up to x2 as
        // fast (default), tapering to normal as it closes that gap. Covers
        // late-unlocked skills (crossbows, magic) and switching weapons.
        private static readonly AccessTools.FieldRef<Skills, Dictionary<Skills.SkillType, Skills.Skill>> SkillData =
            AccessTools.FieldRefAccess<Skills, Dictionary<Skills.SkillType, Skills.Skill>>("m_skillData");
        private static bool IsTransferSkill(Skills.SkillType t)
        {
            switch (t)
            {
                case Skills.SkillType.Swords: case Skills.SkillType.Knives: case Skills.SkillType.Clubs:
                case Skills.SkillType.Polearms: case Skills.SkillType.Spears: case Skills.SkillType.Axes:
                case Skills.SkillType.Bows: case Skills.SkillType.Crossbows: case Skills.SkillType.Unarmed:
                case Skills.SkillType.ElementalMagic: case Skills.SkillType.BloodMagic:
                    return true;
                default: return false;
            }
        }
        // The skill's own level, without gear bonuses. Never creates an entry.
        internal static float RawLevel(Skills skills, Skills.SkillType skill)
        {
            Dictionary<Skills.SkillType, Skills.Skill> data = skills != null ? SkillData(skills) : null;
            Skills.Skill s;
            return data != null && data.TryGetValue(skill, out s) && s != null ? s.m_level : 0f;
        }
        internal static float WeaponTransfer(Skills skills, Skills.SkillType skill)
        {
            float strength = Plugin.XpWeaponTransfer.Value;
            if (strength <= 0f || !IsTransferSkill(skill) || skills == null) return 1f;
            // Raw levels from existing entries only: never creates a skill
            // entry (which would reveal a skill in the skills panel).
            Dictionary<Skills.SkillType, Skills.Skill> data = SkillData(skills);
            if (data == null) return 1f;
            float best = 0f, own = 0f;
            foreach (var kv in data)
            {
                if (!IsTransferSkill(kv.Key) || kv.Value == null) continue;
                if (kv.Key == skill) own = kv.Value.m_level;
                else if (kv.Value.m_level > best) best = kv.Value.m_level;
            }
            // Only once you are actually experienced (a weapon at 20+), so a
            // fresh character's first levels stay vanilla-paced.
            float target = best * Plugin.XpTransferShare.Value / 100f;
            if (best < Plugin.XpTransferMinimum.Value || target <= 0f || own >= target) return 1f;
            return 1f + strength / 100f * Mathf.Clamp01((target - own) / target);
        }
        // ---- For the Compendium ----
        internal static int ProgressTierOf(Player p) => p ? ProgressTier(p) : 1;
        internal static void BiomeXpHere(Player p, out double location, out double history)
        {
            location = history = 0;
            if (!p) return;
            ExperienceMath.BiomeParts(BiomeTier(p.GetCurrentBiome()), ProgressTier(p),
                Plugin.XpProgressionStrength.Value, Plugin.XpProgressShare.Value / 100.0, out location, out history);
        }
        internal static bool BestWeapon(Player p, out Skills.SkillType skill, out float level)
        {
            skill = Skills.SkillType.None; level = 0f;
            Dictionary<Skills.SkillType, Skills.Skill> data = p ? SkillData(p.GetSkills()) : null;
            if (data == null) return false;
            foreach (var kv in data)
                if (IsTransferSkill(kv.Key) && kv.Value != null && kv.Value.m_level > level)
                { skill = kv.Key; level = kv.Value.m_level; }
            return level >= Plugin.XpTransferMinimum.Value;
        }

        private static bool IsRaidCreature(Character c)
        {
            MonsterAI ai = c != null ? c.GetBaseAI() as MonsterAI : null;
            // Real vanilla event spawn, and the local player is inside the
            // active event area.
            return ai != null && ai.IsEventCreature() && RandEventSystem.InEvent();
        }
        // Compiled field accessors, resolved once rather than creating Traverse objects per action.
        private static readonly AccessTools.FieldRef<Attack, Humanoid> AttackOwner =
            AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<Projectile, Character> ProjectileOwner =
            AccessTools.FieldRefAccess<Projectile, Character>("m_owner");
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer =
            AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");
        private static readonly Func<Humanoid, ItemDrop.ItemData> CurrentBlocker =
            AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData>>(
                AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker"));
        private static readonly AccessTools.FieldRef<Player, bool> BeenHitWhileDodging =
            AccessTools.FieldRefAccess<Player, bool>("m_beenHitWhileDodging");
        private sealed class ActionScope
        {
            internal Player Player;
            internal Character Target;
            internal int TargetTier;
            internal int Stars = 1;
            internal bool Boss;
            internal bool Dangerous;
            internal float Precision;
            internal bool Raid;
            internal float StaggerBefore; // your stagger bar before this block, 0..1
            // XP of this attack, one per creature hit: that hit's share of a split swing
            // times its damage bonus (crit x2, weakness by how much more it did)
            internal float HitXp;
            internal float LastRandom, LastFactor; // the swing's skill roll, and the roll after the split
            internal bool HasSplit;
        }
        private struct DodgeState
        {
            internal bool Pending;
            internal bool Succeeded;
            internal bool BossHit;
            internal bool Threatening;
            internal int Streak;
            internal float Last;
            internal bool SourceKnown;
            internal Character Source;
        }
        private static readonly Dictionary<long, DodgeState> dodges = new Dictionary<long, DodgeState>();

        // Enum switch: called on every XP gain, so no string conversion.
        internal static int BiomeTier(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return 2;
                case Heightmap.Biome.Swamp: return 3;
                case Heightmap.Biome.Mountain: return 4;
                case Heightmap.Biome.Plains: return 5;
                case Heightmap.Biome.Mistlands: return 6;
                case Heightmap.Biome.AshLands: return 7;
                case Heightmap.Biome.DeepNorth: return 8;
                // Open ocean has no Forsaken of its own. Serpent waters are
                // treated like the Swamp step instead of falling back to Meadows.
                // Ashlands and Deep North waters already report their own biome.
                case Heightmap.Biome.Ocean: return 3;
                default: return 1;
            }
        }
        private static int cachedTier = 1;
        private static int cachedPlayerId = -1;
        private static float lastCheck = -999f;
        private static int ProgressTier(Player player)
        {
            if (player != null && Time.time - lastCheck < 2f && cachedPlayerId == player.GetInstanceID()) return cachedTier;
            bool[] keys = WorldProgress.ReadKeys();
            int tier = 1;
            for (int i=0; i<keys.Length; i++)
                if (keys[i]) tier = Math.Max(tier, i+2); // next biome after the last Forsaken = your tier
            cachedTier = tier; cachedPlayerId = player == null ? -1 : player.GetInstanceID(); lastCheck = Time.time;
            return tier;
        }
        private static int PlayerTier(Player player)
        {
            // Progression is the floor: returning to older areas still allows catching up a new skill.
            return Math.Min(9, Math.Max(ProgressTier(player), BiomeTier(player.GetCurrentBiome())));
        }
        // Creatures are judged against the biome you most recently conquered:
        // it is still worth your time. Your unconquered frontier counts as +1,
        // and biomes beyond it as +2 and more (high risk, higher reward). Based
        // on progression only, so visiting a harder biome doesn't make its
        // creatures look easier.
        private static int ChallengeTier(Player player) => Math.Max(1, ProgressTier(player) - 1);
        internal static int PlayerTierOf(Player player) => player ? ChallengeTier(player) : 1;
        internal static int CatchUpTierOf(Player player) => player ? PlayerTier(player) : 1;

        // A creature's tier never changes: cached per instance, so repeated
        // hits don't re-parse its name. Unknown (modded) creatures use the
        // biome they stand in, which isn't cached.
        private static readonly Dictionary<int, int> entityTiers = new Dictionary<int, int>();
        internal static void ResetWorld() { entityTiers.Clear(); dodges.Clear(); }
        internal static int GetEntityTier(Character target)
        {
            if (!target) return 1;
            int id = target.GetInstanceID();
            int tier;
            if (entityTiers.TryGetValue(id, out tier))
                return tier > 0 ? tier : BiomeTier(Heightmap.FindBiome(target.transform.position));
            string prefab = target.gameObject.name.Replace("(Clone)", "").Trim();
            // Native biome from Valheim's own spawn lists first (bosses never
            // appear there), then the curated list for dungeon/location/quest
            // creatures that are not spawned by the world spawn system.
            int native = target.IsBoss() ? -1 : CreatureTiers.Native(prefab);
            bool known = true;
            if (native >= 0) tier = native + 1;
            else if (!TierData.Enemies.TryGetValue(prefab, out tier)) known = false;
            // Unknown creatures are remembered as unknown (0), so later hits
            // skip the name lookup, but only once the spawn lists are loaded.
            if (!known && !CreatureTiers.Loaded)
                return BiomeTier(Heightmap.FindBiome(target.transform.position));
            if (entityTiers.Count > 512) entityTiers.Clear();
            entityTiers[id] = known ? tier : 0;
            return known ? tier : BiomeTier(Heightmap.FindBiome(target.transform.position));
        }
        private static bool IsCombatSkill(Skills.SkillType t)
        {
            return (t >= Skills.SkillType.Swords && t <= Skills.SkillType.Unarmed) || t == Skills.SkillType.Crossbows;
        }
        private static float CombatChallenge(ActionScope c, Player player, Skills.SkillType skill)
        {
            if (c == null || !c.Target || c.Boss || !c.Dangerous || !Plugin.XpChallenge.Value || !IsCombatSkill(skill)) return 0f;
            // World/player progress determines challenge, not the weapon's current skill level.
            float danger = (float)ExperienceMath.Threat(c.TargetTier, ChallengeTier(player), Plugin.XpCombatStrength.Value);
            // Meaningful combat keeps improving even at high skill levels.
            // No late-level boost against substantially weaker enemies.
            if (c.TargetTier >= ChallengeTier(player) - 1)
                danger *= 1f + 0.4f * Mathf.Clamp01((RawLevel(player.GetSkills(), skill) - 40f) / 50f);
            bool ranged = skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows;
            float precision = Mathf.Clamp(c.Precision, 0f, ranged ? 0.25f : 0.12f);
            return danger + precision * (Plugin.XpCombatStrength.Value / 100f);
        }
        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class SkillXpPatch
        {
            private static void Prefix(Skills __instance, Skills.SkillType skillType, ref float factor, out bool __state)
            {
                __state = true;
                ++xpDepth;
                if (!Plugin.XpOn || SleepBonus.Granting || factor <= 0f || skillType == Skills.SkillType.None || skillType == Skills.SkillType.All) return;
                Player p = Player.m_localPlayer;
                if (!p || p.GetSkills() != __instance) return; // avoid GetComponent on every XP gain
                int tier = PlayerTier(p);
                var c = current;
                if (c != null && c.Player == p && c.Target != null && IsCombatSkill(skillType))
                    tier = Math.Max(tier, c.TargetTier);
                var fishing = currentFish;
                if (skillType == Skills.SkillType.Fishing && fishing != null && fishing.Player == p)
                    tier = Math.Max(tier, fishing.Tier);
                int worldTier = ProgressTier(p);
                int currentBiomeTier = BiomeTier(p.GetCurrentBiome());
                // High risk, higher reward: fighting or parrying a genuinely
                // hostile, non-boss creature from a later biome earns that
                // creature's location XP, wherever the fight happens.
                if (c != null && c.Player == p && c.Target && c.Dangerous && !c.Boss &&
                    IsCombatSkill(skillType))
                    currentBiomeTier = Math.Max(currentBiomeTier, c.TargetTier);
                // A fish native to a later biome counts as that biome, which
                // also covers open-ocean fishing.
                if (skillType == Skills.SkillType.Fishing && fishing != null && fishing.Player == p)
                    currentBiomeTier = Math.Max(currentBiomeTier, fishing.Tier);

                float xpBefore = factor;
                // Momentum from real activity; nested bonus awards granted
                // inside another XP event don't count twice, and a summon
                // earning XP on its own isn't you adventuring.
                if (xpDepth == 1 && summonXpDepth == 0) Expedition.ObserveXp(currentBiomeTier);
                // Everything below ADDS to the biome step (Expedition, catch-up
                // or weapon transfer, stars, action extras), so stacked bonuses
                // can't multiply into absurd numbers. Only raids multiply.
                // Transfer is for the skill's own actions, not flat rewards.
                float catchUp = Plugin.XpCatchUpBonus.Value > 0f && tier > 1
                    ? (float)ExperienceMath.CatchUp(tier, RawLevel(__instance, skillType), Plugin.XpCatchUpBonus.Value,
                        Plugin.XpCatchUpBase.Value, Plugin.XpCatchUpPerTier.Value) : 1f;
                float transfer = SkillReward.Granting ? 1f : WeaponTransfer(__instance, skillType);
                float learning = Mathf.Max(catchUp, transfer) - 1f;
                float biome = (float)ExperienceMath.BiomeAndProgress(currentBiomeTier, worldTier, Plugin.XpProgressionStrength.Value,
                    Plugin.XpProgressShare.Value / 100.0);
                // Flat rewards already scale with how hard the target was
                // (kill tier, tool tier, fish quality): half the biome step.
                if (SkillReward.Granting) biome = 1f + (biome - 1f) * Plugin.XpRewardLocationShare.Value / 100f;
                float growth = biome + (Expedition.Multiplier() - 1f) + learning;
                // All action extras (threat, precision, multi-hit, combo, counter,
                // crafting, sneak...) share ONE additive pool with a cap.
                float extras = 0f;
                bool ranged = skillType == Skills.SkillType.Bows || skillType == Skills.SkillType.Crossbows;
                if (c != null && c.Player == p && c.Target && skillType == Skills.SkillType.Dodge &&
                    c.Dangerous && !c.Boss && Plugin.XpChallenge.Value)
                {
                    // A perfect dodge is worth more the more dangerous the
                    // attacker: stars, its tier, and how big it is (up to +25%
                    // for a creature with 4x your health, like a troll early on).
                    growth += Plugin.XpStarBonus.Value / 100f * (Mathf.Clamp(c.Stars, 1, 4) - 1);
                    extras += ((float)ExperienceMath.Threat(c.TargetTier, ChallengeTier(p), 100f) +
                        0.25f * Mathf.Clamp01(c.Target.GetMaxHealth() / Mathf.Max(1f, p.GetMaxHealth() * 4f))) *
                        (Plugin.XpCombatStrength.Value / 100f);
                }
                if (c != null && c.Player == p && c.Target && IsCombatSkill(skillType))
                {
                    // 0/1/2 stars: +0.25 / +0.5, added like everything else
                    if (Plugin.XpChallenge.Value) growth += Plugin.XpStarBonus.Value / 100f * (Mathf.Clamp(c.Stars, 1, 4) - 1);
                    if (skillType != Skills.SkillType.Blocking && !addingParryXp)
                        extras += CombatChallenge(c, p, skillType);
                    else if (skillType == Skills.SkillType.Blocking && !addingParryXp &&
                        c.Dangerous && !c.Boss && Plugin.XpChallenge.Value)
                        // A block is worth more the more dangerous it was: the
                        // creature's tier, plus how much of your stagger bar
                        // the blocked hit filled (up to +40% for the whole bar).
                        extras += ((float)ExperienceMath.Threat(c.TargetTier, ChallengeTier(p), 100f) +
                            Plugin.XpBlockStagger.Value / 100f * Mathf.Clamp01(p.GetStaggerPercentage() - c.StaggerBefore)) *
                            (Plugin.XpCombatStrength.Value / 100f);
                }
                if (skillType == Skills.SkillType.Fishing && fishing != null && fishing.Player == p &&
                    !fishing.Completion)
                    // Reeling a better fish is a little harder: +0.25 per
                    // quality level. Most quality XP comes at pickup.
                    growth += 0.25f * (Mathf.Clamp(fishing.Quality, 1, 5) - 1);
                if (Plugin.XpChallenge.Value)
                {
                    float special = SkillSpecific.Multiplier(p, skillType);
                    if (skillType == Skills.SkillType.Sneak) special += SkillSpecific.SneakBonus();
                    special += SkillSpecific.ConsumeBuildBonus(skillType);
                    if (c == null || !c.Boss) extras += Mathf.Clamp(special, 0f, 0.45f);
                }
                growth += Mathf.Clamp(extras, 0f, (ranged ? Plugin.XpExtrasCapRanged.Value : Plugin.XpExtrasCap.Value) / 100f) *
                    Plugin.ChallengeStrength.Value / 100f;
                if (c != null && c.Player == p && c.Raid && !c.Boss && IsCombatSkill(skillType) &&
                    Plugin.RaidBonus.Value > 0f)
                {
                    growth *= 1f + Plugin.RaidBonus.Value / 100f;
                    if (AldrDiagnostics.Active) AldrDiagnostics.Sample("RAID_XP",
                        "skill=" + skillType + " target=" + c.Target.name +
                        " bonus_percent=" + Plugin.RaidBonus.Value.ToString("0"));
                }
                factor *= growth;
                // a crit or weakness hit teaches as much more as it hurt more
                if (c != null && c.Player == p && c.Target && IsCombatSkill(skillType) &&
                    skillType != Skills.SkillType.Blocking && !addingParryXp && c.HitXp > 0f)
                    factor *= c.HitXp;
                AldrDiagnostics.Skill(skillType, xpBefore, factor, p);
            }
            private static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state && xpDepth > 0) --xpDepth;
                return __exception;
            }
        }

        // Transpiler redirects only the vanilla fishing skill award, not the
        // 50 Hz fishing physics loop. No work is done on idle fishing frames.
        [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
        private static class FishingPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
            {
                var original = AccessTools.Method(typeof(Character), nameof(Character.RaiseSkill),
                    new[] { typeof(Skills.SkillType), typeof(float) });
                var wrapper = AccessTools.Method(typeof(FishingPatch), nameof(RaiseWithFish));
                foreach (CodeInstruction code in input)
                {
                    if (!code.Calls(original)) { yield return code; continue; }
                    var loadFloat = new CodeInstruction(OpCodes.Ldarg_0);
                    loadFloat.labels.AddRange(code.labels);
                    loadFloat.blocks.AddRange(code.blocks);
                    yield return loadFloat;
                    yield return new CodeInstruction(OpCodes.Call, wrapper);
                }
            }

            private static void RaiseWithFish(Character owner, Skills.SkillType skill, float factor, FishingFloat ff)
            {
                FishingScope previous = currentFish;
                currentFish = null;
                try
                {
                    Player p = owner as Player;
                    if (p && p == Player.m_localPlayer)
                    {
                        Fish fish = ff.GetCatch();
                        if (fish)
                        {
                            ItemDrop item = fish.GetComponent<ItemDrop>();
                            currentFish = new FishingScope
                            {
                                Player = p,
                                Tier = GetFishTier(fish),
                                Quality = item != null ? item.m_itemData.m_quality : 1
                            };
                        }
                    }
                    owner.RaiseSkill(skill, factor);
                }
                finally { currentFish = previous; }
            }
        }
        internal static int GetFishTier(Fish fish)
        {
            int tier;
            string prefab = fish.gameObject.name.Replace("(Clone)", "").Trim();
            int native = CreatureTiers.Native(prefab);
            if (native >= 0) return native + 1;
            return TierData.Enemies.TryGetValue(prefab, out tier) ? tier : 1;
        }

        // Completion rewards granted at pickup use the same fish/biome
        // context as reeling, without repeating the reel quality multiplier
        // (completion XP is already quality-scaled).
        internal static void WithFishCompletion(Player player, int fishTier, Action award)
        {
            FishingScope previous = currentFish;
            currentFish = new FishingScope { Player = player, Tier = Math.Max(1, fishTier), Quality = 1, Completion = true };
            try { award(); }
            finally { currentFish = previous; }
        }
    }
}
