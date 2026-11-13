using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // In-world XP popups: vanilla's own damage-number pipeline (same prefab,
    // font, size, fade and float), shown only on this game. Mods that
    // restyle damage numbers restyle these too.
    internal static class WorldXpPopups
    {
        private static readonly Action<DamageText, DamageText.TextType, Vector3, float, string, bool> AddInworldText =
            AccessTools.MethodDelegate<Action<DamageText, DamageText.TextType, Vector3, float, string, bool>>(
                AccessTools.Method(typeof(DamageText), "AddInworldText"));

        // Skills raised just by moving around, about once a second.
        internal static bool IsMovementSkill(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Run:
                case Skills.SkillType.Jump:
                case Skills.SkillType.Swim:
                case Skills.SkillType.Ride:
                case Skills.SkillType.Sneak:
                    return true;
                default:
                    return false;
            }
        }

        internal static void Show(Skills.SkillType skill, float gained)
        {
            if (WiresNotificationOverhaulPlugin.HideMovementXpPopups.Value == WiresNotificationOverhaulPlugin.Toggle.On &&
                IsMovementSkill(skill))
                return;
            DamageText damage = DamageText.instance;
            Player player = Player.m_localPlayer;
            Camera camera = Utils.GetMainCamera();
            if (damage == null || player == null || camera == null || AddInworldText == null ||
                Hud.IsUserHidden())
                return;

            Vector3 at = player.GetTopPoint();
            float distance = Vector3.Distance(camera.transform.position, at);
            if (distance > damage.m_maxTextDistance) return;

            string text = gained.ToString(gained < 0.95f ? "0.##" : "0.#", CultureInfo.InvariantCulture) + "xp";
            AddInworldText(damage, DamageText.TextType.Normal, at, distance, text, false);
        }
    }
}
