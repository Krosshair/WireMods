using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Sleeping on it: waking up from a night's sleep adds a share (20% by
    // default) of the XP each skill earned in the last day, then starts over.
    // XP from longer ago counts for quickly less. Measured on the final XP
    // that reaches the skill (every Aldr bonus included), and granted through
    // the skill itself, so mods that build on skill XP, like a fortified
    // skill, get their share too.
    // Everything happens on XP gains, sleep and saving; nothing per frame.
    internal static class SleepBonus
    {
        private const string SaveKey = "Wire.Aldr.SleepXp";

        // Per skill: game hour index -> XP factor earned in that hour.
        private static readonly Dictionary<Skills.SkillType, Dictionary<long, float>> earned =
            new Dictionary<Skills.SkillType, Dictionary<long, float>>();
        private static readonly List<long> scratch = new List<long>();
        private static Player owner;
        private static long worldUid;
        private static double sleptAt = -1;
        // True while the sleep gift is handed out: no Aldr bonuses on top,
        // and it isn't recorded as new XP.
        private static bool granting;
        internal static bool Granting => granting;

        private static readonly AccessTools.FieldRef<Skills, Dictionary<Skills.SkillType, Skills.Skill>> SkillData =
            AccessTools.FieldRefAccess<Skills, Dictionary<Skills.SkillType, Skills.Skill>>("m_skillData");

        internal static bool Enabled =>
            Plugin.Enabled != null && Plugin.Enabled.Value && Plugin.XpEnabled.Value &&
            Plugin.SleepXpShare != null && Plugin.SleepXpShare.Value > 0f;

        private static double HourSeconds =>
            (EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0 ? EnvMan.instance.m_dayLengthSec : 1800) / 24.0;
        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;
        private static long WorldUid => ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0;

        // Full value within the window, then halving every half-life.
        private static double Weight(double ageHours)
        {
            double window = Plugin.SleepWindow.Value;
            if (ageHours <= window) return 1;
            double half = Mathf.Max(0.1f, Plugin.SleepHalfLife.Value);
            return System.Math.Pow(0.5, (ageHours - window) / half);
        }
        // Older than this is worth under 0.5%: forgotten.
        private static double ForgetHours => Plugin.SleepWindow.Value + 8.0 * Mathf.Max(0.1f, Plugin.SleepHalfLife.Value);

        // ---- Recording ----

        internal static void Record(Skills skills, Skills.SkillType skill, float factor)
        {
            if (granting || !Enabled || factor <= 0f || skill == Skills.SkillType.None ||
                skill == Skills.SkillType.All || ZNet.instance == null) return;
            Player p = Player.m_localPlayer;
            if (!p || p.GetSkills() != skills) return;
            Ensure(p);
            long hour = (long)System.Math.Floor(Now / HourSeconds);
            Dictionary<long, float> hours;
            if (!earned.TryGetValue(skill, out hours))
                earned[skill] = hours = new Dictionary<long, float>();
            float sum;
            hours.TryGetValue(hour, out sum);
            // A new hour: forget what has faded away for this skill.
            if (sum == 0f && hours.Count > 0) Forget(hours, hour);
            hours[hour] = sum + factor;
        }

        private static void Forget(Dictionary<long, float> hours, long currentHour)
        {
            double limit = ForgetHours;
            scratch.Clear();
            foreach (var kv in hours)
                if (currentHour - kv.Key > limit) scratch.Add(kv.Key);
            foreach (long key in scratch) hours.Remove(key);
        }

        // ---- Sleeping ----

        internal static void FellAsleep(Player p)
        {
            if (p != Player.m_localPlayer || ZNet.instance == null) return;
            sleptAt = Now;
        }

        internal static void WokeUp(Player p)
        {
            if (p != Player.m_localPlayer || ZNet.instance == null) return;
            double asleep = sleptAt >= 0 ? sleptAt : Now;
            sleptAt = -1;
            if (!Enabled) return;
            Ensure(p);
            Skills skills = p.GetSkills();
            if (skills == null || earned.Count == 0) return;

            double hourSeconds = HourSeconds;
            float share = Plugin.SleepXpShare.Value / 100f;
            var gifts = new List<KeyValuePair<Skills.SkillType, float>>();
            foreach (var skill in earned)
            {
                double total = 0;
                foreach (var hour in skill.Value)
                {
                    // Age from the middle of the hour to falling asleep.
                    double age = System.Math.Max(0, (asleep / hourSeconds) - (hour.Key + 0.5));
                    total += hour.Value * Weight(age);
                }
                if (total > 0.0001) gifts.Add(new KeyValuePair<Skills.SkillType, float>(skill.Key, (float)(total * share)));
            }
            // Slept on it: everything starts over.
            earned.Clear();
            p.m_customData?.Remove(SaveKey);

            foreach (var gift in gifts)
            {
                if (AldrDiagnostics.Active) AldrDiagnostics.Log("SLEEP_XP",
                    "skill=" + gift.Key + " factor=" + gift.Value.ToString("0.###"));
                Grant(skills, gift.Key, gift.Value);
            }
        }

        // Vanilla raises at most one level per gain and drops what's left
        // over, so a big gift is handed out level by level.
        private static void Grant(Skills skills, Skills.SkillType type, float factor)
        {
            Dictionary<Skills.SkillType, Skills.Skill> data = SkillData(skills);
            bool outer = granting;
            granting = true;
            try
            {
                float left = factor;
                float rate = 1f; // other mods' XP multiplier, learned as we go
                for (int guard = 0; left > 0.00001f && guard < 300; guard++)
                {
                    Skills.Skill skill;
                    if (data == null || !data.TryGetValue(type, out skill) || skill == null ||
                        skill.m_info == null || skill.m_level >= 100f)
                    {
                        skills.RaiseSkill(type, left);
                        return;
                    }
                    float unit = skill.m_info.m_increseStep * Game.m_skillGainRate;
                    if (unit <= 0f) { skills.RaiseSkill(type, left); return; }
                    float need = (Mathf.Pow(Mathf.Floor(skill.m_level + 1f), 1.5f) * 0.5f + 0.5f - skill.m_accumulator) / (unit * rate);
                    // The first step stops halfway, to learn the rate safely.
                    float piece = Mathf.Min(left, Mathf.Max(0.0001f, guard == 0 ? need * 0.5f : need));
                    float level = skill.m_level, before = skill.m_accumulator;
                    skills.RaiseSkill(type, piece);
                    left -= piece;
                    if (skill.m_level == level && skill.m_accumulator > before)
                        rate = Mathf.Max(0.01f, (skill.m_accumulator - before) / (unit * piece));
                }
            }
            finally { granting = outer; }
        }

        // ---- Saving ----

        private static void Ensure(Player p)
        {
            long uid = WorldUid;
            if (p == owner && uid == worldUid) return;
            owner = p;
            worldUid = uid;
            earned.Clear();
            string raw;
            if (p.m_customData == null || !p.m_customData.TryGetValue(SaveKey, out raw) || string.IsNullOrEmpty(raw)) return;
            // "world|skill:hour=xp,hour=xp|skill:..."; another world's clock
            // doesn't match this one, so its record doesn't carry over.
            string[] parts = raw.Split('|');
            long savedUid;
            if (parts.Length < 1 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out savedUid) ||
                savedUid != uid) return;
            for (int i = 1; i < parts.Length; i++)
            {
                int colon = parts[i].IndexOf(':');
                int skillId;
                if (colon <= 0 || !int.TryParse(parts[i].Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out skillId))
                    continue;
                var hours = new Dictionary<long, float>();
                foreach (string entry in parts[i].Substring(colon + 1).Split(','))
                {
                    int eq = entry.IndexOf('=');
                    long hour; float xp;
                    if (eq > 0 &&
                        long.TryParse(entry.Substring(0, eq), NumberStyles.Integer, CultureInfo.InvariantCulture, out hour) &&
                        float.TryParse(entry.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out xp) &&
                        xp > 0f && xp < 1e6f)
                        hours[hour] = xp;
                }
                if (hours.Count > 0) earned[(Skills.SkillType)skillId] = hours;
            }
        }

        internal static void Save(Player p)
        {
            if (!p || p != owner || p.m_customData == null) return;
            long now = (long)System.Math.Floor(Now / HourSeconds);
            var sb = new StringBuilder();
            foreach (var skill in earned)
            {
                Forget(skill.Value, now);
                if (skill.Value.Count == 0) continue;
                sb.Append('|').Append((int)skill.Key).Append(':');
                bool first = true;
                foreach (var hour in skill.Value)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(hour.Key.ToString(CultureInfo.InvariantCulture)).Append('=')
                      .Append(hour.Value.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            if (sb.Length == 0) p.m_customData.Remove(SaveKey);
            else p.m_customData[SaveKey] = worldUid.ToString(CultureInfo.InvariantCulture) + sb;
        }

        internal static void Clear() { owner = null; earned.Clear(); sleptAt = -1; }
    }

    // After every other XP change (Aldr's own and other mods'): the XP that
    // actually reaches the skill.
    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class SleepBonusRecord
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Skills __instance, Skills.SkillType skillType, float factor) =>
            SleepBonus.Record(__instance, skillType, factor);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetSleeping))]
    internal static class SleepBonusWake
    {
        private static void Prefix(Player __instance, bool sleep, out bool __state) =>
            __state = __instance.IsSleeping() != sleep;
        private static void Postfix(Player __instance, bool sleep, bool __state)
        {
            if (!__state) return;
            if (sleep) SleepBonus.FellAsleep(__instance);
            else SleepBonus.WokeUp(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class SleepBonusSave
    {
        private static void Prefix(Player __instance) => SleepBonus.Save(__instance);
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class SleepBonusWorldStart
    {
        private static void Postfix() => SleepBonus.Clear();
    }
}
