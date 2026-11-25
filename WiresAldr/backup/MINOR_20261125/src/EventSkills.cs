using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Bonus events that cannot be inferred reliably from skill name alone.
    // All hooks run only when the vanilla event happens, never each frame.
    internal static class EventSkills
    {
        private static readonly AccessTools.FieldRef<StatusEffect,Character> ShieldCharacter = AccessTools.FieldRefAccess<StatusEffect,Character>("m_character");

        // A Blood Magic barrier on the local player absorbing real hostile
        // blows trains Blood Magic, like blocking trains Blocking: 4 XP per
        // baseline barrier (the unskilled one) worth of damage absorbed, paid
        // per hit. Measured against the baseline, so a stronger barrier earns
        // the same per hit and more in total, never less. Many small hits can't
        // earn more than one big one. Bosses excluded (no
        // boss bonuses). A teammate's barrier isn't credited to an unknown caster.
        private static readonly AccessTools.FieldRef<SE_Shield, float> ShieldDamage =
            AccessTools.FieldRefAccess<SE_Shield, float>("m_damage");
        private static readonly AccessTools.FieldRef<SE_Shield, float> ShieldTotal =
            AccessTools.FieldRefAccess<SE_Shield, float>("m_totalAbsorbDamage");

        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
        private static class BarrierProtect
        {
            private static void Prefix(SE_Shield __instance, HitData hit, Character attacker)
            {
                if (__instance == null || hit == null || attacker == null ||
                    __instance.m_levelUpSkillOnBreak != Skills.SkillType.BloodMagic ||
                    Plugin.Enabled == null || !Plugin.Enabled.Value ||
                    !Plugin.XpEnabled.Value || !Plugin.Rewards.Value ||
                    attacker.IsBoss() || attacker.IsTamed() || attacker.IsPlayer()) return;
                BaseAI ai = attacker.GetBaseAI();
                if (ai == null) return;
                Player p = ShieldCharacter(__instance) as Player;
                if (!p || p != Player.m_localPlayer || !ai.IsEnemy(p)) return;
                // Baseline: the barrier's own capacity without skill (vanilla
                // SE_Shield.SetLevel), world level included as enemies scale with it.
                float baseline = __instance.m_absorbDamage +
                    (Game.m_worldLevel > 0 ? __instance.m_absorbDamageWorldLevel * Game.m_worldLevel : 0f);
                if (baseline <= 0f) return;
                // Read before vanilla zeroes the hit and adds it to the total.
                float absorbed = Mathf.Min(hit.GetTotalDamage(),
                    Mathf.Max(0f, ShieldTotal(__instance) - ShieldDamage(__instance)));
                if (absorbed <= 0f) return;
                SkillReward.Grant(p, Skills.SkillType.BloodMagic,
                    Plugin.BarrierXp.Value * absorbed / baseline, "barrier_absorbed");
            }
        }
    }
}
