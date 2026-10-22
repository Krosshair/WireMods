using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    internal static class DynamicExperience
    {
        [ThreadStatic] private static ActionScope current;
        [ThreadStatic] private static FishingScope currentFish;
        [ThreadStatic] private static Character dodgeAttackOwner;
        private sealed class FishingScope { internal Player Player; internal int Tier; internal int Quality; internal bool Completion; }
        private static bool addingParryXp;
        [ThreadStatic] private static int xpDepth;
        [ThreadStatic] private static int summonXpDepth;

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
            private static void Postfix(bool __state) { if (__state && summonXpDepth > 0) --summonXpDepth; }
            private static System.Exception Finalizer(System.Exception __exception, bool __state)
            {
                if (__exception != null && __state && summonXpDepth > 0) --summonXpDepth;
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
        internal static float WeaponTransfer(Skills skills, Skills.SkillType skill)
        {
            float strength = Plugin.XpWeaponTransfer != null ? Plugin.XpWeaponTransfer.Value : 0f;
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
            internal int HitCount;
            internal int LastTarget;
            internal bool Raid;
            internal float StaggerBefore; // your stagger bar before this block, 0..1
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
                danger *= 1f + 0.4f * Mathf.Clamp01(
                    (player.GetSkillLevel(skill) - 40f) / 50f);
            bool ranged = skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows;
            float precision = Mathf.Clamp(c.Precision, 0f, ranged ? 0.25f : 0.12f);
            // One attack hitting several enemies (cleaves, sweeps, piercing shots, AoE).
            precision += 0.03f * Mathf.Max(0, c.HitCount - 1);
            return danger + precision * (Plugin.XpCombatStrength.Value / 100f);
        }
        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class SkillXpPatch
        {
            private static void Prefix(Skills __instance, Skills.SkillType skillType, ref float factor, out bool __state)
            {
                __state = true;
                ++xpDepth;
                if (!(Plugin.Enabled.Value && Plugin.XpEnabled.Value) || factor <= 0f || skillType == Skills.SkillType.None || skillType == Skills.SkillType.All) return;
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
                    ? (float)ExperienceMath.CatchUp(tier, p.GetSkillLevel(skillType), Plugin.XpCatchUpBonus.Value,
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
                growth += Mathf.Clamp(extras, 0f, (ranged ? Plugin.XpExtrasCapRanged.Value : Plugin.XpExtrasCap.Value) / 100f);
                if (c != null && c.Player == p && c.Raid && !c.Boss && IsCombatSkill(skillType) &&
                    Plugin.RaidBonus.Value > 0f)
                {
                    growth *= 1f + Plugin.RaidBonus.Value / 100f;
                    if (AldrDiagnostics.Active) AldrDiagnostics.Sample("RAID_XP",
                        "skill=" + skillType + " target=" + c.Target.name +
                        " bonus_percent=" + Plugin.RaidBonus.Value.ToString("0"));
                }
                factor *= growth;
                AldrDiagnostics.Skill(skillType, xpBefore, factor, p);
            }
            private static void Postfix(bool __state) { if (__state && xpDepth > 0) --xpDepth; }
            private static System.Exception Finalizer(System.Exception __exception, bool __state)
            {
                if (__exception != null && __state && xpDepth > 0) --xpDepth;
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

        // A scoped context lives only during the actual melee/area hit operation.
        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        private static class MeleePatch
        {
            private static void Prefix(Attack __instance, ref ActionScope __state)
            {
                __state = current;
                Player p = AttackOwner(__instance) as Player;
                current = p && p == Player.m_localPlayer ? new ActionScope { Player = p } : null;
            }
            private static void Postfix(ActionScope __state) { current = __state; }
            private static Exception Finalizer(Exception __exception, ActionScope __state) { current = __state; return __exception; }
        }
        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        private static class AreaPatch
        {
            private static void Prefix(Attack __instance, ref ActionScope __state)
            {
                __state = current;
                Player p = AttackOwner(__instance) as Player;
                current = p && p == Player.m_localPlayer ? new ActionScope { Player = p } : null;
            }
            private static void Postfix(ActionScope __state) { current = __state; }
            private static Exception Finalizer(Exception __exception, ActionScope __state) { current = __state; return __exception; }
        }
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class ProjectilePatch
        {
            private static void Prefix(Projectile __instance, ref ActionScope __state)
            {
                __state = current;
                Player p = ProjectileOwner(__instance) as Player;
                current = p && p == Player.m_localPlayer ? new ActionScope { Player = p } : null;
            }
            private static void Postfix(ActionScope __state) { current = __state; }
            private static Exception Finalizer(Exception __exception, ActionScope __state) { current = __state; return __exception; }
        }
        // Area effects (staff explosions, bombs, AoE spells): same combat
        // context as a projectile, and the owner counts as a dodge source.
        private static readonly AccessTools.FieldRef<Aoe, Character> AoeOwner =
            AccessTools.FieldRefAccess<Aoe, Character>("m_owner");
        private struct AoeState { internal ActionScope Scope; internal Character DodgeOwner; }
        [HarmonyPatch(typeof(Aoe), "OnHit")]
        private static class AoePatch
        {
            private static void Prefix(Aoe __instance, ref AoeState __state)
            {
                __state = new AoeState { Scope = current, DodgeOwner = dodgeAttackOwner };
                Character owner = AoeOwner(__instance);
                Player p = owner as Player;
                current = p && p == Player.m_localPlayer ? new ActionScope { Player = p } : null;
                dodgeAttackOwner = owner;
            }
            private static void Postfix(AoeState __state) { current = __state.Scope; dodgeAttackOwner = __state.DodgeOwner; }
            private static Exception Finalizer(Exception __exception, AoeState __state)
            { current = __state.Scope; dodgeAttackOwner = __state.DodgeOwner; return __exception; }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class DamageContextPatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                ActionScope c = current;
                if (c == null || !__instance || __instance.IsPlayer() || hit == null) return;
                if (hit.GetAttacker() != c.Player) return;
                int tier = GetEntityTier(__instance);
                if (c.Target != null && c.TargetTier > tier) return;
                int targetId = __instance.GetInstanceID();
                if (c.LastTarget != targetId) { c.LastTarget = targetId; c.HitCount = Mathf.Min(4, c.HitCount + 1); }
                c.Target = __instance;
                c.TargetTier = tier;
                c.Stars = Mathf.Clamp(__instance.GetLevel(), 1, 4);
                c.Boss = __instance.IsBoss();
                BaseAI ai = __instance.GetBaseAI();
                c.Dangerous = ai != null && ai.IsEnemy(c.Player) && !__instance.IsTamed();
                c.Raid = c.Dangerous && IsRaidCreature(__instance);
                // Leading a moving target at range requires more accuracy than hitting a stationary target.
                float distance = Vector3.Distance(c.Player.transform.position, __instance.transform.position);
                bool rangedHit = hit.m_skill == Skills.SkillType.Bows || hit.m_skill == Skills.SkillType.Crossbows;
                if (rangedHit)
                    // Any moving target is hard to hit; distance adds more.
                    c.Precision = (IsMoving(__instance) ? 0.08f : 0.03f) + Mathf.Min(0.12f, distance * 0.003f);
                else
                    c.Precision = distance > 12f && IsMoving(__instance) ? Mathf.Min(0.07f, (distance - 12f) * 0.002f) : 0f;
                bool unaware = ai != null && !ai.IsAlerted() && hit.m_backstabBonus > 1.01f;
                if (unaware) c.Precision += 0.07f;
                if (c.Dangerous) SkillSpecific.NoteConnectedHit();
                if (c.Dangerous && !c.Boss)
                    Accomplishments.RecordHit(__instance, c.Player, hit, c.TargetTier, c.Stars, unaware);
            }
        }
        private static bool IsMoving(Character c)
        {
            try { return c.GetVelocity().sqrMagnitude > 2.25f; } catch { return false; }
        }

        // Character.AddStaggerDamage computes the decisive block failure
        // result. BlockAttack's own bool return is NOT that result: it also
        // returns true for attacks that staggered the player.
        [ThreadStatic] private static Character observedBlockPlayer;
        [ThreadStatic] private static bool observedBlockStaggered;
        [ThreadStatic] private static bool observedBlockStaggerChecked;

        [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
        private static class ObserveBlockStagger
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance, bool __result)
            {
                // Only one synchronous, local player's block in progress;
                // never capture ordinary stagger events or other entities.
                if (__instance != null &&
                    __instance == observedBlockPlayer)
                {
                    observedBlockStaggerChecked = true;
                    if (__result)
                        observedBlockStaggered = true;
                }
            }
        }

        // BlockAttack raises its vanilla skill XP internally. Only EXTRA parry XP is awarded after confirmed success.
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class BlockPatch
        {
            private struct BlockInfo
            {
                internal ActionScope Previous;
                internal bool Timed;
                internal Character Attacker;
                internal float AttackRisk;
                internal Character PriorObservedPlayer;
                internal bool PriorStaggered;
                internal bool PriorStaggerChecked;
            }
            private static void Prefix(Humanoid __instance, Character attacker, HitData hit, ref BlockInfo __state)
            {
                __state = new BlockInfo
                {
                    Previous = current,
                    Attacker = attacker,
                    PriorObservedPlayer = observedBlockPlayer,
                    PriorStaggered = observedBlockStaggered,
                    PriorStaggerChecked = observedBlockStaggerChecked
                };
                Player p = __instance as Player;
                if (!p || p != Player.m_localPlayer) return;
                observedBlockPlayer = p;
                observedBlockStaggered = false;
                observedBlockStaggerChecked = false;
                float timer = BlockTimer(__instance);
                ItemDrop.ItemData blocker = CurrentBlocker(__instance);
                __state.Timed = blocker != null && blocker.m_shared.m_timedBlockBonus > 1f && timer >= 0f && timer < 0.25f;
                float incoming = hit == null ? 0f : hit.GetTotalBlockableDamage();
                __state.AttackRisk = Mathf.Clamp01(incoming / Mathf.Max(1f, p.GetMaxHealth() * 0.35f));
                // Never reuse or modify an outer scope: a block without a
                // known attacker is judged as vanilla.
                current = null;
                if (attacker)
                {
                    BaseAI ai = attacker.GetBaseAI();
                    current = new ActionScope { Player = p, Target = attacker, TargetTier = GetEntityTier(attacker), Stars = Mathf.Clamp(attacker.GetLevel(), 1, 4), Boss = attacker.IsBoss(), Dangerous = ai != null && ai.IsEnemy(p) && !attacker.IsTamed() };
                    current.Raid = current.Dangerous && IsRaidCreature(attacker);
                    current.StaggerBefore = p.GetStaggerPercentage();
                }
            }
            private static void Postfix(Humanoid __instance, bool __result, BlockInfo __state)
            {
                try
                {
                    Player p = __instance as Player;
                    if (!p || p != Player.m_localPlayer || !__result || !__state.Timed ||
                        !__state.Attacker || __state.Attacker.IsBoss() || current == null || !current.Dangerous || !(Plugin.Enabled.Value && Plugin.XpEnabled.Value) || !Plugin.Rewards.Value ||
                        !observedBlockStaggerChecked ||
                        addingParryXp) return;
                    if (GetEntityTier(__state.Attacker) < ChallengeTier(p) - 1)
                        return; // no challenge reward for trivial threats
                    // A parry that still staggers you (or empties your stamina)
                    // was the riskiest kind: it counts as a full stagger bar,
                    // like a guard break does for normal blocks.
                    bool broken = observedBlockStaggered || p.IsStaggering() || !p.HaveStamina();
                    float r = broken ? 1f : Mathf.Clamp01(p.GetStaggerPercentage());
                    float bonus = (float)ExperienceMath.Parry(r, Plugin.XpParryStrength.Value) * (0.2f + 0.8f * __state.AttackRisk);
                    if (bonus <= 0f) return;
                    if (!broken) SkillSpecific.DefenseSucceeded(p); // no counter-attack bonus while staggered
                    if (AldrDiagnostics.Active) AldrDiagnostics.Sample("PARRY_BONUS",
                        "stagger_fraction=" + r.ToString("0.###") +
                        " additional_bonus_factor=" + bonus.ToString("0.###"));
                    // 10x applies only to this extra bonus, never to all Blocking XP.
                    addingParryXp = true;
                    SkillReward.Grant(p, Skills.SkillType.Blocking, 3f * bonus, "risky_parry");
                }
                finally
                {
                    addingParryXp = false;
                    current = __state.Previous;
                    observedBlockPlayer = __state.PriorObservedPlayer;
                    observedBlockStaggered = __state.PriorStaggered;
                    observedBlockStaggerChecked = __state.PriorStaggerChecked;
                }
            }
            private static Exception Finalizer(Exception __exception, BlockInfo __state)
            {
                current = __state.Previous;
                addingParryXp = false;
                observedBlockPlayer = __state.PriorObservedPlayer;
                observedBlockStaggered = __state.PriorStaggered;
                observedBlockStaggerChecked = __state.PriorStaggerChecked;
                return __exception;
            }
        }

        // Track the attacker behind a confirmed perfect-dodge event. Boss
        // attacks must never grant Challenge XP, including projectile hits.
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class DodgeAttackerScope
        {
            private static void Prefix(Projectile __instance, out Character __state)
            {
                __state = dodgeAttackOwner;
                dodgeAttackOwner = ProjectileOwner(__instance);
            }
            private static void Postfix(Character __state) { dodgeAttackOwner = __state; }
            private static Exception Finalizer(Exception __exception, Character __state)
            {
                dodgeAttackOwner = __state;
                return __exception;
            }
        }
        [HarmonyPatch(typeof(Player), nameof(Player.HitWhileDodging))]
        private static class DodgeHitSource
        {
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                long id = __instance.GetPlayerID();
                DodgeState s;
                dodges.TryGetValue(id, out s);
                // Unknown source receives vanilla XP, never invented challenge XP.
                Character source = dodgeAttackOwner != null ? dodgeAttackOwner : SkillSpecific.DodgeAttacker;
                s.BossHit = source == null || source.IsBoss() || source.GetBaseAI() == null || !source.GetBaseAI().IsEnemy(__instance);
                s.Threatening = source != null &&
                    GetEntityTier(source) >= ChallengeTier(__instance) - 1;
                s.SourceKnown = source != null;
                s.Source = source;
                dodges[id] = s;
            }
        }

        // Vanilla RPC_HitWhileDodging confirms a real attack evaded while invulnerable.
        [HarmonyPatch(typeof(Player), "Dodge")]
        private static class DodgeStartPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                long id = __instance.GetPlayerID();
                DodgeState s;
                dodges.TryGetValue(id, out s);
                if (s.Pending && !s.Succeeded) s.Streak = 0;
                if (Time.time - s.Last > 8f) s.Streak = 0;
                s.Pending = true; s.Succeeded = false; s.BossHit = false;
                s.Threatening = false; s.SourceKnown = false; s.Last = Time.time;
                dodges[id] = s;
            }
        }
        // Only runs on a confirmed dodge whose attacker is unknown (rare).
        private static void InferDodgeSource(Player p, ref DodgeState s)
        {
            Character nearest = null;
            float nearestSq = 10f * 10f;
            bool boss = false;
            Vector3 at = p.transform.position;
            foreach (Character ch in Character.GetAllCharacters())
            {
                if (!ch || ch == p || ch.IsPlayer() || ch.IsDead() || ch.IsTamed()) continue;
                float d = (ch.transform.position - at).sqrMagnitude;
                if (ch.IsBoss() && d < 30f * 30f) { boss = true; break; }
                if (d >= nearestSq) continue;
                BaseAI ai = ch.GetBaseAI();
                if (ai == null || !ai.IsEnemy(p)) continue;
                nearest = ch; nearestSq = d;
            }
            s.BossHit = boss || nearest == null;
            s.Threatening = !s.BossHit && GetEntityTier(nearest) >= ChallengeTier(p) - 1;
            s.Source = s.BossHit ? null : nearest;
            s.SourceKnown = true;
        }

        [HarmonyPatch(typeof(Player), "RPC_HitWhileDodging")]
        private static class DodgeSuccessPatch
        {
            private struct State { internal bool Before; internal ActionScope Previous; }
            // The vanilla perfect-dodge XP is raised inside this RPC: give it
            // the dodged creature as context (tier, stars, size).
            private static void Prefix(Player __instance, ref State __state)
            {
                __state = new State { Before = BeenHitWhileDodging(__instance), Previous = current };
                if (__state.Before || __instance != Player.m_localPlayer) return;
                long id = __instance.GetPlayerID();
                DodgeState s; dodges.TryGetValue(id, out s);
                if (!s.SourceKnown) InferDodgeSource(__instance, ref s);
                dodges[id] = s;
                Character src = s.Source;
                if (!s.BossHit && src)
                    current = new ActionScope { Player = __instance, Target = src, TargetTier = GetEntityTier(src),
                        Stars = Mathf.Clamp(src.GetLevel(), 1, 4), Dangerous = true };
            }
            private static Exception Finalizer(Exception __exception, State __state)
            {
                current = __state.Previous;
                return __exception;
            }
            private static void Postfix(Player __instance, State __state)
            {
                current = __state.Previous;
                if (__instance != Player.m_localPlayer || !(Plugin.Enabled.Value && Plugin.XpEnabled.Value) || !Plugin.Rewards.Value || Plugin.XpDodgeStrength.Value <= 0f || __state.Before) return;
                bool now = BeenHitWhileDodging(__instance);
                if (!now) return;
                long id = __instance.GetPlayerID();
                DodgeState s; dodges.TryGetValue(id, out s);
                if (s.Succeeded) return;
                // In multiplayer the attack runs on whoever controls the
                // creature, so the source may be unknown here: judge it from
                // the hostile creatures right around you instead.
                if (!s.SourceKnown) InferDodgeSource(__instance, ref s);
                if (s.BossHit || !s.Threatening)
                {
                    s.Pending = false;
                    s.Streak = 0;
                    dodges[id] = s;
                    return;
                }
                s.Streak = Mathf.Min(s.Streak + 1, 6);
                s.Succeeded = true; s.Pending = false; s.Last = Time.time;
                dodges[id] = s;
                // Only successful dodges, not roll inputs, receive this additional XP.
                float bonus = (float)ExperienceMath.Dodge(s.Streak, Plugin.XpDodgeStrength.Value);
                SkillSpecific.DefenseSucceeded(__instance);
                if (AldrDiagnostics.Active) AldrDiagnostics.Sample("DODGE_BONUS",
                    "confirmed_streak=" + s.Streak + " added_skill_gain=" +
                    bonus.ToString("0.###"));
                SkillReward.Grant(__instance, Skills.SkillType.Dodge, bonus,
                    "dodge_streak_" + s.Streak);
            }
        }
    }
}

