using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Per-skill accomplishment rewards. Each one is a real vanilla event,
    // paid as a share of the skill's next level (SkillReward), so it stays
    // worthwhile at high levels without inflating every single hit.
    internal static class Accomplishments
    {
        // ---- Killing blows -------------------------------------------------
        private struct LocalHit
        {
            internal float Time;
            internal Skills.SkillType Skill;
            internal int Tier;
            internal int Stars;
            internal bool Sneak;
        }
        private static readonly Dictionary<int, LocalHit> lastHits = new Dictionary<int, LocalHit>();

        // Recorded on the attacking client (Character.Damage runs there), so
        // kills are credited even when another player owns the creature.
        internal static void RecordHit(Character target, Player player, HitData hit,
            int targetTier, int stars, bool unaware)
        {
            if (!target || !player || hit == null || hit.m_skill == Skills.SkillType.None) return;
            if (lastHits.Count > 256) lastHits.Clear();
            lastHits[target.GetInstanceID()] = new LocalHit
            {
                Time = UnityEngine.Time.time, Skill = hit.m_skill, Tier = targetTier,
                Stars = Mathf.Clamp(stars, 1, 4),
                Sneak = unaware && player.IsCrouching()
            };
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class KillingBlow
        {
            private static void Prefix(Character __instance)
            {
                if (!__instance || __instance.IsPlayer() || __instance.IsBoss() || __instance.IsTamed()) return;
                int id = __instance.GetInstanceID();
                LocalHit h;
                if (!lastHits.TryGetValue(id, out h)) return;
                lastHits.Remove(id);
                Player p = Player.m_localPlayer;
                if (!p || UnityEngine.Time.time - h.Time > 4f) return;
                BaseAI ai = __instance.GetBaseAI();
                if (ai == null || !ai.IsEnemy(p)) return;

                int diff = h.Tier - DynamicExperience.PlayerTierOf(p);
                float fraction = diff >= 0 ? 0.006f + 0.002f * Mathf.Min(diff, 3)
                    : diff == -1 ? 0.003f : 0f;
                if (fraction <= 0f) return; // trivial old-tier foes: vanilla XP only
                fraction *= 1f + 0.5f * (h.Stars - 1);
                SkillReward.Grant(p, h.Skill, fraction, "killing_blow_tier_diff_" + diff);
                if (h.Sneak)
                    SkillReward.Grant(p, Skills.SkillType.Sneak, 0.004f + 0.002f * Mathf.Max(0, diff),
                        "sneak_attack_kill");
            }
        }

        // ---- Woodcutting: felling a whole tree ------------------------------
        [ThreadStatic] private static HitData treeHit;

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        private static class TreeDamageScope
        {
            private static void Prefix(HitData hit, out HitData __state) { __state = treeHit; treeHit = hit; }
            private static void Postfix(HitData __state) { treeHit = __state; }
            private static Exception Finalizer(Exception __exception, HitData __state) { treeHit = __state; return __exception; }
        }

        [HarmonyPatch(typeof(TreeBase), "SpawnLog")]
        private static class TreeFelled
        {
            private static void Prefix(TreeBase __instance)
            {
                Player p = Player.m_localPlayer;
                if (treeHit == null || !p || treeHit.GetAttacker() != p) return;
                int biome = Mathf.Max(0, BiomeCatalog.FromBiome(Heightmap.FindBiome(__instance.transform.position)));
                float fraction = 0.003f + biome * 0.0012f + Mathf.Max(0, __instance.m_minToolTier) * 0.0015f;
                SkillReward.Grant(p, Skills.SkillType.WoodCutting, fraction, "tree_felled");
            }
        }

        // ---- Mining: emptying a regional deposit ----------------------------
        private static bool RegionalDeposit(DropTable table)
        {
            if (table == null || table.m_drops == null) return false;
            foreach (var d in table.m_drops)
                if (d.m_item != null && BiomeCatalog.RegionalMaterialTier(d.m_item) >= 0) return true;
            return false;
        }

        private static float DepositFraction(Component source, int toolTier)
        {
            int biome = Mathf.Max(0, BiomeCatalog.FromBiome(Heightmap.FindBiome(source.transform.position)));
            return 0.006f + biome * 0.0015f + Mathf.Max(0, toolTier) * 0.001f;
        }

        private static readonly AccessTools.FieldRef<MineRock5, bool> AllDestroyed5 =
            AccessTools.FieldRefAccess<MineRock5, bool>("m_allDestroyed");

        [HarmonyPatch(typeof(MineRock5), "DamageArea")]
        private static class DepositEmptied5
        {
            private static void Prefix(MineRock5 __instance, out bool __state)
            { __state = __instance != null && AllDestroyed5(__instance); }
            private static void Postfix(MineRock5 __instance, HitData hit, bool __state)
            {
                Player p = Player.m_localPlayer;
                if (__state || __instance == null || !AllDestroyed5(__instance) || hit == null ||
                    !p || hit.GetAttacker() != p || !RegionalDeposit(__instance.m_dropItems)) return;
                SkillReward.Grant(p, Skills.SkillType.Pickaxes,
                    DepositFraction(__instance, __instance.m_minToolTier), "deposit_emptied");
            }
        }

        private static readonly AccessTools.FieldRef<MineRock, ZNetView> RockView =
            AccessTools.FieldRefAccess<MineRock, ZNetView>("m_nview");
        // Read only when an area actually breaks (rare), so plain reflection is fine.
        private static readonly System.Reflection.FieldInfo RockAreas =
            AccessTools.Field(typeof(MineRock), "m_hitAreas");

        private static float AreaHealth(MineRock rock, int index)
        {
            ZNetView nview = RockView(rock);
            if (nview == null || !nview.IsValid()) return 0f;
            return nview.GetZDO().GetFloat("Health" + index, rock.m_health);
        }

        // Smaller deposits: each broken area pays its share, so emptying
        // the whole deposit pays the same as a large one.
        [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
        private static class DepositAreaBroken
        {
            private static void Prefix(MineRock __instance, int hitAreaIndex, out bool __state)
            {
                __state = false;
                ZNetView nview = __instance != null ? RockView(__instance) : null;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                __state = AreaHealth(__instance, hitAreaIndex) > 0f;
            }
            private static void Postfix(MineRock __instance, HitData hit, int hitAreaIndex, bool __state)
            {
                Player p = Player.m_localPlayer;
                if (!__state || __instance == null || hit == null || !p || hit.GetAttacker() != p) return;
                if (AreaHealth(__instance, hitAreaIndex) > 0f) return;
                if (!RegionalDeposit(__instance.m_dropItems)) return;
                Array areas = RockAreas != null ? RockAreas.GetValue(__instance) as Array : null;
                int count = areas != null && areas.Length > 0 ? areas.Length : 1;
                SkillReward.Grant(p, Skills.SkillType.Pickaxes,
                    DepositFraction(__instance, __instance.m_minToolTier) / count, "deposit_area_broken");
            }
        }

        // ---- Farming: harvesting grown crops -------------------------------
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class CropHarvest
        {
            private static readonly AccessTools.FieldRef<Pickable, bool> Picked =
                AccessTools.FieldRefAccess<Pickable, bool>("m_picked");
            private static readonly AccessTools.FieldRef<Pickable, bool> PickedLocal =
                AccessTools.FieldRefAccess<Pickable, bool>("m_pickedLocal");
            private static void Prefix(Pickable __instance, Humanoid character, out bool __state)
            {
                __state = __instance != null && character == Player.m_localPlayer &&
                    __instance.m_pickRaiseSkill == Skills.SkillType.Farming &&
                    __instance.GetComponent<Piece>() != null &&
                    !Picked(__instance) && !PickedLocal(__instance);
            }
            private static void Postfix(Pickable __instance, bool __state)
            {
                if (!__state || !PickedLocal(__instance)) return;
                SkillReward.Grant(Player.m_localPlayer, Skills.SkillType.Farming, 0.002f, "crop_harvested");
            }
        }

        // ---- Cooking: first time producing a station/fermenter dish ---------
        private static HashSet<string> stationProducts;

        private static bool IsStationProduct(string sharedName)
        {
            if (stationProducts == null)
            {
                if (ZNetScene.instance == null) return false;
                var set = new HashSet<string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null) continue;
                    CookingStation cs = prefab.GetComponent<CookingStation>();
                    if (cs != null && cs.m_conversion != null)
                        foreach (var c in cs.m_conversion)
                            if (c != null && c.m_to != null) set.Add(c.m_to.m_itemData.m_shared.m_name);
                    Fermenter f = prefab.GetComponent<Fermenter>();
                    if (f != null && f.m_conversion != null)
                        foreach (var c in f.m_conversion)
                            if (c != null && c.m_to != null) set.Add(c.m_to.m_itemData.m_shared.m_name);
                }
                stationProducts = set;
            }
            return stationProducts.Contains(sharedName);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        private static class FirstDish
        {
            private static void Prefix(Player __instance, ItemDrop.ItemData item)
            {
                if (__instance != Player.m_localPlayer || item == null || item.m_shared == null ||
                    __instance.IsMaterialKnown(item.m_shared.m_name) ||
                    !IsStationProduct(item.m_shared.m_name)) return;
                SkillReward.Grant(__instance, Skills.SkillType.Cooking, 0.012f, "first_station_dish");
            }
        }

        internal static void Reset() { lastHits.Clear(); stationProducts = null; }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class AccomplishmentsWorldStart
    {
        private static void Postfix() => Accomplishments.Reset();
    }
}
