using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // "Can I just overpower this one?" From real game numbers, worked out on the
    // existing events only (target re-pick, damage), never per frame:
    //  - hits each side needs to kill the other (its hit vs your health, yours vs its health)
    //  - body size vs yours
    //  - whether its hit staggers you through your block anyway
    // +1: it outmatches you and stops dancing around; -1: you outmatch it, so it plays careful.
    public sealed partial class WiresAIPlugin
    {
        private static readonly MethodInfo CurrentBlocker = AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker");

        // One swing of its current weapon, scaled like Attack does (stars: 1 + 0.5 per star)
        // and by its individual damage trait.
        private static bool HitDamage(Character c, out HitData.DamageTypes dmg)
        {
            dmg = default;
            if (!(c is Humanoid h)) return false;
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            if (w == null) return false;
            dmg = w.GetDamage();
            float mult = Mathf.Max(.1f, w.m_shared.m_attack.m_damageMultiplier) *
                (1f + .5f * Mathf.Max(0, c.GetLevel() - 1));
            if (Phys.TryGetValue(c, out Physical p) && p.Individual != null) mult *= p.Individual.Axis[1];
            dmg.Modify(mult);
            return dmg.GetTotalDamage() > 0f;
        }

        // Same as Humanoid.GetCurrentBlocker + GetBlockPower(blocking skill), no parry bonus.
        private static float BlockPower(Character c)
        {
            if (!(c is Humanoid h) || CurrentBlocker == null) return 0f;
            ItemDrop.ItemData blocker = CurrentBlocker.Invoke(h, null) as ItemDrop.ItemData;
            if (blocker == null) return 0f;
            return blocker.GetBlockPower(c.GetSkillFactor(Skills.SkillType.Blocking));
        }

        // Character.GetRadius multiplies non-players by localScale.magnitude (sqrt 3 at scale 1).
        private static float BodyRadius(Character c) =>
            c is Player || c.m_useAltStatusEffectScaling ? c.GetRadius() : c.GetRadius() / 1.7320508f;

        private static void UpdateOverpower(Behavior b, Character self, Character target)
        {
            float op = 0f;
            b.BreaksBlock = false;
            bool haveMine = HitDamage(self, out HitData.DamageTypes mine);
            if (haveMine && HitDamage(target, out HitData.DamageTypes theirs))
            {
                float hitsToKillThem = Mathf.Max(1f, target.GetHealth()) / mine.GetTotalDamage();
                float hitsToKillMe = Mathf.Max(1f, self.GetHealth()) / theirs.GetTotalDamage();
                // They can't know how strong you are: a bad matchup never makes them doubt, a good one
                // still makes them bolder.
                op += Mathf.Max(0f, .55f * (float)System.Math.Tanh(Mathf.Log(hitsToKillMe / hitsToKillThem) / 1.5f));
            }
            op += .25f * Mathf.Clamp(Mathf.Log(Mathf.Max(.05f, BodyRadius(self)) /
                Mathf.Max(.05f, BodyRadius(target)), 2f) / 2f, -1f, 1f);
            // As Humanoid.BlockAttack: block power works like armour, and what's left of
            // the stagger damage staggers the blocker once it reaches max HP x stagger factor.
            float block = BlockPower(target);
            if (haveMine && block > 0f && target.m_staggerDamageFactor > 0f)
            {
                HitData.DamageTypes through = mine;
                through.ApplyArmor(block);
                b.BreaksBlock = through.GetTotalStaggerDamage() >= target.GetMaxHealth() * target.m_staggerDamageFactor;
            }
            if (b.BreaksBlock) op += .2f;
            b.Overpower = Mathf.Clamp(op, -1f, 1f);
        }
    }
}
