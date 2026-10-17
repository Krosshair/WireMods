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
        private static readonly AccessTools.FieldRef<MonsterAI,Character> MonsterTarget = AccessTools.FieldRefAccess<MonsterAI,Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<StatusEffect,Character> ShieldCharacter = AccessTools.FieldRefAccess<StatusEffect,Character>("m_character");
        private static readonly Dictionary<int, float> escaped = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> shieldHit = new Dictionary<int, float>();

        // A targeted creature losing its alert state can reward a legitimate
        // nearby crouching player. Each creature has a 90-second cooldown.
        [HarmonyPatch(typeof(BaseAI), "SetAlerted")]
        private static class LostDetection
        {
            private static void Prefix(BaseAI __instance, bool alert)
            {
                if (alert || __instance == null || !__instance.IsAlerted() ||
                    Plugin.Enabled == null || !Plugin.Enabled.Value ||
                    !Plugin.XpEnabled.Value || !Plugin.XpChallenge.Value ||
                    !(__instance is MonsterAI monster)) return;
                Character target = MonsterTarget(monster);
                Player p = target as Player;
                if (!p || p != Player.m_localPlayer || !p.IsCrouching() || p.IsDead()) return;
                Character mob = __instance.GetComponent<Character>();
                if (!mob || mob.IsBoss() || mob.IsTamed() ||
                    !__instance.IsEnemy(p) ||
                    Vector3.Distance(mob.transform.position, p.transform.position) > 28f) return;
                int id = __instance.GetInstanceID();
                float last;
                if (escaped.TryGetValue(id, out last) && Time.time - last < 90f) return;
                if (escaped.Count > 2048) escaped.Clear();
                escaped[id] = Time.time;
                SkillReward.Grant(p, Skills.SkillType.Sneak, 2f, "escaped_detection");
            }
        }

        // A blood-magic barrier on the local player absorbing a real hostile
        // blow is useful practice. Don't attribute a teammate's shield to an
        // unknown caster, or allow cheap hits to farm bonus XP.
        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
        private static class BarrierProtect
        {
            private static void Prefix(SE_Shield __instance, HitData hit, Character attacker)
            {
                if (__instance == null || hit == null || attacker == null ||
                    __instance.m_levelUpSkillOnBreak != Skills.SkillType.BloodMagic ||
                    Plugin.Enabled == null || !Plugin.Enabled.Value ||
                    !Plugin.XpEnabled.Value || !Plugin.XpChallenge.Value ||
                    attacker.IsBoss() || attacker.IsTamed()) return;
                BaseAI ai = attacker.GetBaseAI();
                if (ai == null) return;
                Player p = ShieldCharacter(__instance) as Player;
                if (!p || p != Player.m_localPlayer || !ai.IsEnemy(p)) return;
                float damage = hit.GetTotalDamage();
                float ratio = damage / Mathf.Max(1f, p.GetMaxHealth());
                if (ratio < 0.08f) return;
                int id = __instance.GetInstanceID();
                float previous;
                if (shieldHit.TryGetValue(id, out previous) && Time.time - previous < 10f) return;
                if (shieldHit.Count > 1024) shieldHit.Clear();
                shieldHit[id] = Time.time;
                SkillReward.Grant(p, Skills.SkillType.BloodMagic,
                    Mathf.Clamp(0.5f + ratio * 3f, 0.5f, 2f), "shield_absorbed_hit");
            }
        }
    }
}
