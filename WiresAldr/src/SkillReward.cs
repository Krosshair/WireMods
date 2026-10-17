using System;
using UnityEngine;

namespace WiresAldr
{
    // Convert one verified gameplay accomplishment to a fraction of the
    // vanilla next-level requirement. The vanilla Skills.RaiseSkill path
    // still applies skill-gain rate, biome progression and skill caps.
    internal static class SkillReward
    {
        internal static void Grant(Player player, Skills.SkillType type,
            float nextLevelFraction, string reason)
        {
            if (player == null || player != Player.m_localPlayer ||
                Plugin.Enabled == null || !Plugin.Enabled.Value ||
                !Plugin.XpEnabled.Value || !Plugin.XpChallenge.Value ||
                nextLevelFraction <= 0f) return;

            Skills skills = player.GetSkills();
            if (skills == null || Game.m_skillGainRate <= 0f) return;

            // Use the underlying vanilla skill level, not an effective
            // level potentially raised by FortifiedSkillsRedux.
            Skills.Skill trained = null;
            foreach (var entry in skills.GetSkillList())
                if (entry != null && entry.m_info != null &&
                    entry.m_info.m_skill == type)
                {
                    trained = entry;
                    break;
                }
            if (trained == null)
            {
                // Let vanilla initialize a never-used skill if necessary.
                skills.GetSkillLevel(type);
                foreach (var entry in skills.GetSkillList())
                    if (entry != null && entry.m_info != null &&
                        entry.m_info.m_skill == type)
                    {
                        trained = entry;
                        break;
                    }
            }
            if (trained == null || trained.m_info.m_increseStep <= 0f)
                return;
            float step = trained.m_info.m_increseStep;
            float level = trained.m_level;
            if (level >= 100f) return;

            // Mirrors vanilla Skill.GetNextLevelRequirement exactly.
            float required = Mathf.Pow(Mathf.Floor(level + 1f), 1.5f)
                * 0.5f + 0.5f;
            float gain = required * Mathf.Clamp(nextLevelFraction, 0f, 0.04f);
            float factor = gain / (step * Game.m_skillGainRate);
            if (float.IsNaN(factor) || float.IsInfinity(factor) ||
                factor <= 0f) return;

            if (AldrDiagnostics.Active)
                AldrDiagnostics.Sample("COMPLETION_XP",
                    "skill=" + type + " reason=" + reason +
                    " next_level_fraction=" + nextLevelFraction.ToString("0.####") +
                    " level=" + level.ToString("0.#"));
            player.RaiseSkill(type, factor);
        }
    }
}
