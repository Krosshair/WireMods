using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // What your attacks hit, and how much XP each hit is worth.
    internal static partial class DynamicExperience
    {
        // Lives only while one swing, area attack, projectile or area effect works out its hits.
        private static ActionScope ScopeFor(Character owner)
        {
            Player p = owner as Player;
            return p && p == Player.m_localPlayer ? new ActionScope { Player = p } : null;
        }

        [HarmonyPatch]
        private static class AttackPatch
        {
            private static IEnumerable<MethodBase> TargetMethods() => AttackMethods.Of("DoMeleeAttack", "DoAreaAttack");
            private static void Prefix(Attack __instance, out ActionScope __state)
            {
                __state = current;
                current = ScopeFor(AttackOwner(__instance));
            }
            private static Exception Finalizer(Exception __exception, ActionScope __state) { current = __state; return __exception; }
        }
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class ProjectilePatch
        {
            private static void Prefix(Projectile __instance, out ActionScope __state)
            {
                __state = current;
                current = ScopeFor(ProjectileOwner(__instance));
            }
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
            private static void Prefix(Aoe __instance, out AoeState __state)
            {
                __state = new AoeState { Scope = current, DodgeOwner = dodgeAttackOwner };
                Character owner = AoeOwner(__instance);
                current = ScopeFor(owner);
                dodgeAttackOwner = owner;
            }
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
                // Each creature hit counts. A swing that lowers damage per target
                // (Attack.DoMeleeAttack: 1 / (targets x 0.75)) gives that share of XP per hit.
                float share = c.HasSplit && c.LastRandom > 0f ? Mathf.Min(1f, c.LastFactor / c.LastRandom) : 1f;
                c.HasSplit = false;
                BaseAI ai = __instance.GetBaseAI();
                bool hostile = ai != null && ai.IsEnemy(c.Player) && !__instance.IsTamed();
                if (Plugin.HitXp.Value) c.HitXp += share * (hostile ? HitDamageBonus(__instance, hit) : 1f);
                int tier = GetEntityTier(__instance);
                if (c.Target != null && c.TargetTier > tier) return;
                c.Target = __instance;
                c.TargetTier = tier;
                c.Stars = Mathf.Clamp(__instance.GetLevel(), 1, 4);
                c.Boss = __instance.IsBoss();
                c.Dangerous = hostile;
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
        // The melee loop rolls the skill factor, divides it for a split swing, then
        // passes it to ModifyDamage: the two together give that hit's share.
        [HarmonyPatch(typeof(Player), nameof(Player.GetRandomSkillFactor))]
        private static class SwingRoll
        {
            private static void Postfix(Player __instance, float __result)
            {
                ActionScope c = current;
                if (c != null && c.Player == __instance) c.LastRandom = __result;
            }
        }
        [HarmonyPatch(typeof(Attack), "ModifyDamage")]
        private static class SwingShare
        {
            private static void Prefix(float damageFactor)
            {
                ActionScope c = current;
                if (c == null) return;
                c.LastFactor = damageFactor;
                c.HasSplit = true;
            }
        }

        // How much more this hit does than a plain one, the way the game works it out
        // in Character.RPC_Damage/ApplyDamage: x2 on a staggered creature (the crit),
        // times the resistance gain where it is weak (weak spot included). Never below 1.
        private static readonly MethodInfo FindWeakSpot =
            AccessTools.Method(typeof(Character), "FindWeakSpotIndex");
        private static readonly MethodInfo WeakSpotAt =
            AccessTools.Method(typeof(Character), "GetWeakSpot");
        private static readonly FieldInfo HitCollider =
            AccessTools.Field(typeof(HitData), "m_hitCollider");
        private static float HitDamageBonus(Character target, HitData hit)
        {
            float bonus = target.IsStaggering() ? 2f : 1f;
            try
            {
                WeakSpot spot = null;
                object collider = HitCollider?.GetValue(hit);
                if (FindWeakSpot != null && WeakSpotAt != null && collider != null)
                    spot = (WeakSpot)WeakSpotAt.Invoke(target, new object[] { FindWeakSpot.Invoke(target, new object[] { collider }) });
                HitData plain = hit.Clone();
                float before = plain.GetTotalDamage();
                plain.ApplyResistance(target.GetDamageModifiers(spot), out _);
                float after = plain.GetTotalDamage();
                if (before > 0f && after > before) bonus *= after / before;
            }
            catch { }
            return 1f + (bonus - 1f) * Plugin.HitXpStrength.Value / 100f;
        }

        private static bool IsMoving(Character c)
        {
            try { return c.GetVelocity().sqrMagnitude > 2.25f; } catch { return false; }
        }
    }
}
