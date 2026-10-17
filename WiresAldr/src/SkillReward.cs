using UnityEngine;

namespace WiresAldr
{
    // Flat bonus XP for a real accomplishment, in vanilla XP units (a weapon
    // hit on a creature is 1.5, one craft is 1, one second of sprinting is 1).
    // It is a normal XP gain: Aldr's biome, catch-up and Expedition scaling
    // apply, so the same accomplishment is worth more in a dangerous biome.
    internal static class SkillReward
    {
        internal static void Grant(Player player, Skills.SkillType type,
            float xp, string reason)
        {
            if (player == null || player != Player.m_localPlayer ||
                Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.XpEnabled.Value || !Plugin.XpChallenge.Value ||
                type == Skills.SkillType.None || xp <= 0f) return;
            if (AldrDiagnostics.Active)
                AldrDiagnostics.Sample("REWARD_XP", "skill=" + type +
                    " reason=" + reason + " base_xp=" + xp.ToString("0.##"));
            player.RaiseSkill(type, xp);
        }
    }
}
