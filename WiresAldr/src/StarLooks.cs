using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Stars past the game's own look. Vanilla gives each creature a look per level in its prefab
    // (LevelEffects.m_levelSetups: size, hue, saturation, brightness, extra parts), usually for
    // 1 and 2 stars, and a creature with more stars than that showed none of it (it looked like a
    // 0-star one). Here every star past the last authored one carries on the creature's own step:
    // as much bigger per star as its last authored star was over the one before (over the plain
    // creature when it has only one), with the last star's extra parts. Its colour comes from
    // StarColours; creatures not listed there keep the last star's saturation and brightness and
    // go round the colour wheel. Same per-prefab-and-level material cache as vanilla.
    [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
    internal static class StarLooks
    {
        private static readonly AccessTools.FieldRef<LevelEffects, Character> CharacterOf =
            AccessTools.FieldRefAccess<LevelEffects, Character>("m_character");
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        internal static bool Past(LevelEffects fx, int level) =>
            fx != null && level > 1 && fx.m_levelSetups != null && fx.m_levelSetups.Count > 0 && fx.m_levelSetups.Count < level - 1;

        // The look for a level past the authored ones, from the creature's own last step.
        internal static LevelEffects.LevelSetup Extend(LevelEffects fx, int level, string prefab)
        {
            List<LevelEffects.LevelSetup> s = fx.m_levelSetups;
            LevelEffects.LevelSetup last = s[s.Count - 1];
            LevelEffects.LevelSetup prev = s.Count > 1 ? s[s.Count - 2] : new LevelEffects.LevelSetup { m_scale = 1f };
            float extra = (level - 1 - s.Count) * Plugin.StarLooksStrength.Value / 100f;
            Vector3 colour = Colour(last, prefab, level - 1, level - 1 - s.Count);
            return new LevelEffects.LevelSetup
            {
                m_scale = last.m_scale + (last.m_scale - prev.m_scale) * extra,
                m_hue = colour.x,
                m_saturation = colour.y,
                m_value = colour.z,
                m_setEmissiveColor = last.m_setEmissiveColor,
                m_emissiveColor = last.m_emissiveColor,
                m_enableObject = last.m_enableObject,
            };
        }

        // The strength fades the colour in from the last authored one, the size has no such end.
        private static Vector3 Colour(LevelEffects.LevelSetup last, string prefab, int stars, int past)
        {
            Vector3 from = new Vector3(last.m_hue, last.m_saturation, last.m_value);
            // Nine steps to a full turn: with two authored stars, 3 to 10 all differ.
            if (!StarColours.TryGet(prefab, stars, out Vector3 to)) to = new Vector3(last.m_hue + past / 9f, from.y, from.z);
            float strength = Mathf.Clamp01(Plugin.StarLooksStrength.Value / 100f);
            float turn = Mathf.Repeat(to.x - from.x + .5f, 1f) - .5f;
            return new Vector3(Mathf.Repeat(from.x + turn * strength + .5f, 1f) - .5f,
                Mathf.Lerp(from.y, to.y, strength), Mathf.Lerp(from.z, to.z, strength));
        }

        private static string PrefabOf(Character c) => c != null ? Utils.GetPrefabName(c.gameObject) : "";

        private static void Postfix(LevelEffects __instance, int level)
        {
            if (!Plugin.On || !Plugin.StarLooksOn.Value || !Past(__instance, level)) return;
            Character c = CharacterOf(__instance);
            LevelEffects.LevelSetup look = Extend(__instance, level, PrefabOf(c));
            __instance.transform.localScale = Vector3.one * look.m_scale;
            Renderer main = __instance.m_mainRender;
            if (main != null && c != null)
            {
                string key = PrefabOf(c) + level + "|" + Plugin.StarLooksStrength.Value;
                Material[] mats = main.sharedMaterials;
                if (!Materials.TryGetValue(key, out Material m) || m == null)
                {
                    m = new Material(mats[0]);
                    m.SetFloat("_Hue", look.m_hue);
                    m.SetFloat("_Saturation", look.m_saturation);
                    m.SetFloat("_Value", look.m_value);
                    if (look.m_setEmissiveColor) m.SetColor("_EmissionColor", look.m_emissiveColor);
                    Materials[key] = m;
                }
                mats[0] = m;
                main.sharedMaterials = mats;
            }
            if (__instance.m_baseEnableObject != null) __instance.m_baseEnableObject.SetActive(false);
            if (look.m_enableObject != null) look.m_enableObject.SetActive(true);
        }
    }

    // The same colour where the game asks for it (GetColorChanges: what a creature's ragdoll and
    // effects are tinted with).
    [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.GetColorChanges))]
    internal static class StarLooksColour
    {
        private static readonly AccessTools.FieldRef<LevelEffects, Character> CharacterOf =
            AccessTools.FieldRefAccess<LevelEffects, Character>("m_character");

        private static void Postfix(LevelEffects __instance, ref float hue, ref float saturation, ref float value)
        {
            if (!Plugin.On || !Plugin.StarLooksOn.Value) return;
            Character c = CharacterOf(__instance);
            if (c == null || !StarLooks.Past(__instance, c.GetLevel())) return;
            LevelEffects.LevelSetup look = StarLooks.Extend(__instance, c.GetLevel(), Utils.GetPrefabName(c.gameObject));
            hue = look.m_hue; saturation = look.m_saturation; value = look.m_value;
        }
    }
}
