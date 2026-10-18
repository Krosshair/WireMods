using UnityEngine;

namespace WiresAldr
{
    // Flat bonus XP for a real accomplishment, in vanilla XP units (a weapon
    // hit on a creature is 1.5, one craft is 1, one second of sprinting is 1).
    // It is a normal XP gain: Aldr's biome, catch-up and Expedition scaling
    // apply, so the same accomplishment is worth more in a dangerous biome.
    internal static class SkillReward
    {
        // True while a flat reward is being paid (weapon transfer skips it).
        [System.ThreadStatic] internal static bool Granting;

        internal static void Grant(Player player, Skills.SkillType type,
            float xp, string reason)
        {
            if (player == null || player != Player.m_localPlayer ||
                Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.XpEnabled.Value || !Plugin.Rewards.Value ||
                type == Skills.SkillType.None || xp <= 0f) return;
            if (AldrDiagnostics.Active)
                AldrDiagnostics.Sample("REWARD_XP", "skill=" + type +
                    " reason=" + reason + " base_xp=" + xp.ToString("0.##"));
            bool outer = Granting;
            Granting = true;
            try { player.RaiseSkill(type, xp); }
            finally { Granting = outer; }
        }
    }
}
