using System;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Blocks, parries and perfect dodges.
    internal static partial class DynamicExperience
    {
        // Character.AddStaggerDamage computes the decisive block failure
        // result. BlockAttack's own bool return is NOT that result: it also
        // returns true for attacks that staggered the player.
        private static Character observedBlockPlayer;
        private static bool observedBlockStaggered;
        private static bool observedBlockStaggerChecked;

        [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
        private static class ObserveBlockStagger
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance, bool __result)
            {
                // Only one synchronous, local player's block in progress;
                // never capture ordinary stagger events or other entities.
                if (__instance != null && __instance == observedBlockPlayer)
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
                    current = new ActionScope
                    {
                        Player = p, Target = attacker, TargetTier = GetEntityTier(attacker),
                        Stars = Mathf.Clamp(attacker.GetLevel(), 1, 4), Boss = attacker.IsBoss(),
                        Dangerous = ai != null && ai.IsEnemy(p) && !attacker.IsTamed()
                    };
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
                        !__state.Attacker || __state.Attacker.IsBoss() || current == null || !current.Dangerous ||
                        !Plugin.RewardsOn || !observedBlockStaggerChecked || addingParryXp) return;
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
                finally { addingParryXp = false; }
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
                s.Threatening = source != null && GetEntityTier(source) >= ChallengeTier(__instance) - 1;
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
                if (__instance != Player.m_localPlayer || !Plugin.RewardsOn ||
                    Plugin.XpDodgeStrength.Value <= 0f || __state.Before) return;
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
                    "confirmed_streak=" + s.Streak + " added_skill_gain=" + bonus.ToString("0.###"));
                SkillReward.Grant(__instance, Skills.SkillType.Dodge, bonus, "dodge_streak_" + s.Streak);
            }
        }
    }
}

