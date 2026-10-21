using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Diagnostics only (off unless "Enable diagnostics" is on): records a
    // creature's level as every part of the game sees it, to catch stars
    // that don't match health or drops.
    internal static class CreatureCheck
    {
        private static readonly HashSet<int> seen = new HashSet<int>();

        private static string Describe(Character c)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return "creature=" + c.gameObject.name.Replace("(Clone)", "") +
                " level_field=" + c.GetLevel() +
                " level_zdo=" + (zdo != null ? zdo.GetInt(ZDOVars.s_level, 1).ToString() : "?") +
                " health=" + c.GetHealth().ToString("0") + "/" + c.GetMaxHealth().ToString("0") +
                " base_health=" + c.m_health.ToString("0") +
                " owner_here=" + (view != null && view.IsOwner()) +
                " owner_id=" + (zdo != null ? zdo.GetOwner().ToString() : "?");
        }

        // First hit you land on each creature.
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class FirstHit
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!AldrDiagnostics.Active || !__instance || __instance.IsPlayer() || hit == null ||
                    hit.GetAttacker() != Player.m_localPlayer) return;
                if (seen.Count > 256) seen.Clear();
                if (!seen.Add(__instance.GetInstanceID())) return;
                AldrDiagnostics.Log("CREATURE_HIT", Describe(__instance) +
                    " hit_damage=" + hit.GetTotalDamage().ToString("0"));
            }
        }

        // Drops are rolled on the game that controls the creature.
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class Drops
        {
            private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
            {
                if (!AldrDiagnostics.Active || __instance == null) return;
                Character c = __instance.GetComponent<Character>();
                if (!c) return;
                int items = 0;
                if (__result != null) foreach (var kv in __result) items += kv.Value;
                AldrDiagnostics.Log("CREATURE_DROPS", Describe(c) + " drop_items=" + items);
            }
        }

        // Spawns rolled on this game, with the level vanilla assigned.
        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        private static class Level
        {
            private static void Postfix(Character __instance, int level)
            {
                if (!AldrDiagnostics.Active || !__instance || __instance.IsPlayer()) return;
                AldrDiagnostics.Log("CREATURE_LEVEL_SET", "set=" + level + " " + Describe(__instance));
            }
        }
    }
}
