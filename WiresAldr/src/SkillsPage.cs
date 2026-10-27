using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WiresAldr
{
    // The skills page shows the XP bonus you'd get right now: the part every
    // skill gets (location, Forsaken share, Expedition) next to the page
    // title, and each skill's own extra (catch-up or weapon transfer) next
    // to its level. Bonuses from the moment itself (stars, combos, raids...)
    // aren't standing bonuses, so they aren't shown. Filled in when the page
    // opens, nothing while it's open. In the orange this page already uses
    // for the total skill count.
    [HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
    internal static class SkillsPage
    {
        private const string BonusName = "WireAldrXpBonus";
        private const string GlobalName = "WireAldrXpGlobal";

        private static readonly AccessTools.FieldRef<SkillsDialog, List<GameObject>> Elements =
            AccessTools.FieldRefAccess<SkillsDialog, List<GameObject>>("m_elements");

        private static bool Active =>
            Plugin.ShowSkillBonuses != null && Plugin.ShowSkillBonuses.Value &&
            Plugin.Enabled.Value && Plugin.XpEnabled.Value;

        // Location, Forsaken share and Expedition, in percent.
        internal static double GlobalPercent(Player p)
        {
            double location, history;
            DynamicExperience.BiomeXpHere(p, out location, out history);
            return (location + history) * 100.0 + Expedition.ActivePercent;
        }

        // Catch-up or weapon transfer, whichever is larger, in percent.
        internal static double SkillPercent(Player p, Skills.SkillType skill)
        {
            float catchUp = 1f;
            int tier = DynamicExperience.CatchUpTierOf(p);
            if (Plugin.XpCatchUpBonus.Value > 0f && tier > 1)
                catchUp = (float)ExperienceMath.CatchUp(tier, DynamicExperience.RawLevel(p.GetSkills(), skill), Plugin.XpCatchUpBonus.Value,
                    Plugin.XpCatchUpBase.Value, Plugin.XpCatchUpPerTier.Value);
            float transfer = DynamicExperience.WeaponTransfer(p.GetSkills(), skill);
            return (Mathf.Max(catchUp, transfer) - 1f) * 100.0;
        }

        private static string Percent(double v) => "<color=orange>+" + Math.Round(v).ToString("0") + "%</color>";

        [HarmonyPostfix]
        private static void Postfix(SkillsDialog __instance, Player player)
        {
            try { Fill(__instance, player); }
            catch (Exception e) { Debug.LogWarning("[Aldr] Skills page bonus unavailable: " + e.Message); }
        }

        private static void Fill(SkillsDialog dialog, Player player)
        {
            if (dialog == null || player == null) return;
            bool active = Active;
            List<Skills.Skill> skills = player.GetSkills().GetSkillList();
            List<GameObject> elements = Elements(dialog);

            // Per skill: a copy of the skill's own name text, right-aligned
            // in the space between the name and the level bar.
            for (int i = 0; elements != null && i < elements.Count; i++)
            {
                GameObject element = elements[i];
                if (element == null) continue;
                Transform existing = element.transform.Find(BonusName);
                TMP_Text label = existing != null ? existing.GetComponent<TMP_Text>() : null;
                double value = active && i < skills.Count && element.activeSelf
                    ? SkillPercent(player, skills[i].m_info.m_skill) : 0;
                bool show = Math.Round(value) >= 1;
                if (!show)
                {
                    if (label != null) label.gameObject.SetActive(false);
                    continue;
                }
                if (label == null) label = MakeSkillLabel(element);
                if (label == null) continue;
                label.text = Percent(value);
                label.gameObject.SetActive(true);
            }

            // Every skill: next to the page title.
            TMP_Text global = GlobalLabel(dialog);
            double all = active ? GlobalPercent(player) : 0;
            if (global != null)
            {
                bool show = Math.Round(all) >= 1;
                global.gameObject.SetActive(show);
                if (show) global.text = Percent(all) + " XP";
            }
        }

        private static TMP_Text MakeSkillLabel(GameObject element)
        {
            Transform name = Utils.FindChild(element.transform, "name");
            if (name == null) return null;
            GameObject copy = UnityEngine.Object.Instantiate(name.gameObject, name.parent, false);
            copy.name = BonusName;
            TMP_Text text = copy.GetComponent<TMP_Text>();
            if (text == null) { UnityEngine.Object.Destroy(copy); return null; }
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.raycastTarget = false;
            // Small, like the bonus next to the page title: the name's own
            // box, text pushed to its right end, just before the level bar.
            text.enableAutoSizing = false;
            text.fontSize = Mathf.Max(10f, text.fontSize * 0.75f);
            return text;
        }

        private static TMP_Text GlobalLabel(SkillsDialog dialog)
        {
            Transform frame = dialog.transform;
            Transform topic = Utils.FindChild(frame, "topic");
            if (topic == null) return null;
            Transform existing = topic.parent.Find(GlobalName);
            if (existing != null) return existing.GetComponent<TMP_Text>();
            GameObject copy = UnityEngine.Object.Instantiate(topic.gameObject, topic.parent, false);
            copy.name = GlobalName;
            TMP_Text text = copy.GetComponent<TMP_Text>();
            if (text == null) { UnityEngine.Object.Destroy(copy); return null; }
            foreach (var l in copy.GetComponents<Component>())
                if (l != null && l.GetType().Name == "Localize") UnityEngine.Object.Destroy(l);
            // Smaller, at the title's right, on the same line.
            RectTransform r = (RectTransform)copy.transform;
            RectTransform tr = (RectTransform)topic;
            text.fontSize = Mathf.Max(12f, text.fontSize * 0.5f);
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.raycastTarget = false;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 0.5f);
            r.sizeDelta = new Vector2(160f, tr.sizeDelta.y);
            r.anchoredPosition = new Vector2(-24f, tr.anchoredPosition.y);
            return text;
        }
    }
}
