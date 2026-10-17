using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Accomplishment rewards (flat bonus XP, see SkillReward).
    // Detected on the player's OWN game, so they work the same in
    // multiplayer no matter whose game controls the creature, tree or rock:
    // a hit is remembered for 6 s, and if the target dies or is destroyed in
    // that time while the player is still nearby, the reward is paid.
    // In co-op, everyone who hit it in those 6 s gets the reward.
    internal static class Accomplishments
    {
        private sealed class Pending
        {
            internal UnityEngine.Object Target;
            internal Character Creature;
            internal Vector3 Position;
            internal float Time;
            internal Skills.SkillType Skill;
            internal float Xp;
            internal float SneakXp;
            internal string Reason;
        }

        private const float Window = 6f;
        private const float Range = 40f;
        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static readonly List<int> scratch = new List<int>();

        private static void Remember(UnityEngine.Object target, Pending p)
        {
            if (pending.Count > 256) Prune(true);
            pending[target.GetInstanceID()] = p;
        }

        // ---- Kills -------------------------------------------------------
        // Same tier as your progression: 2.5 XP (about two hits' worth),
        // +1 per tier above (max +3), one tier below: 1, older: nothing.
        // +50% per star. Sneak-attack kills also give Sneak XP.
        internal static void RecordHit(Character target, Player player, HitData hit,
            int targetTier, int stars, bool unaware)
        {
            if (!target || !player || hit == null || hit.m_skill == Skills.SkillType.None ||
                target.IsBoss() || target.IsTamed() || target.IsPlayer()) return;
            int diff = targetTier - DynamicExperience.PlayerTierOf(player);
            float xp = diff >= 0 ? 2.5f + Mathf.Min(diff, 3) : diff == -1 ? 1f : 0f;
            if (xp <= 0f) { pending.Remove(target.GetInstanceID()); return; }
            xp *= 1f + 0.5f * (Mathf.Clamp(stars, 1, 4) - 1);
            float sneak = unaware && player.IsCrouching() ? (diff >= 0 ? 3f : 1.5f) : 0f;
            Remember(target, new Pending
            {
                Target = target, Creature = target, Position = target.transform.position,
                Time = UnityEngine.Time.time, Skill = hit.m_skill, Xp = xp, SneakXp = sneak,
                Reason = "kill_tier_diff_" + diff
            });
        }

        private static void Pay(Pending p)
        {
            Player player = Player.m_localPlayer;
            if (!player) return;
            SkillReward.Grant(player, p.Skill, p.Xp, p.Reason);
            if (p.SneakXp > 0f) SkillReward.Grant(player, Skills.SkillType.Sneak, p.SneakXp, "sneak_attack_kill");
        }

        // Immediate payout when this game controls the creature.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class KillOnOwner
        {
            private static void Prefix(Character __instance)
            {
                if (!__instance) return;
                int id = __instance.GetInstanceID();
                Pending p;
                if (!pending.TryGetValue(id, out p)) return;
                pending.Remove(id);
                if (UnityEngine.Time.time - p.Time <= Window) Pay(p);
            }
        }

        // ---- Trees and deposits (recorded on the hitting player's side) ----
        private static bool RegionalDeposit(DropTable table)
        {
            if (table == null || table.m_drops == null) return false;
            foreach (var d in table.m_drops)
                if (d.m_item != null && BiomeCatalog.RegionalMaterialTier(d.m_item) >= 0) return true;
            return false;
        }

        private static void RecordObject(Component target, HitData hit, Skills.SkillType skill, float xp, string reason)
        {
            Player player = Player.m_localPlayer;
            if (!target || hit == null || !player || hit.GetAttacker() != player ||
                hit.GetTotalDamage() <= 0f || xp <= 0f) return;
            Remember(target, new Pending
            {
                Target = target, Position = target.transform.position, Time = UnityEngine.Time.time,
                Skill = skill, Xp = xp, Reason = reason
            });
        }

        // Felling a whole standing tree: 2 XP, +1 per tool tier the tree needs.
        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
        private static class TreeHit
        {
            private static void Prefix(TreeBase __instance, HitData hit) =>
                RecordObject(__instance, hit, Skills.SkillType.WoodCutting,
                    2f + Mathf.Max(0, __instance.m_minToolTier), "tree_felled");
        }

        // Emptying a whole deposit of a biome-exclusive material: 4 XP, +1 per tool tier.
        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        private static class DepositHit5
        {
            private static void Prefix(MineRock5 __instance, HitData hit)
            {
                if (RegionalDeposit(__instance.m_dropItems))
                    RecordObject(__instance, hit, Skills.SkillType.Pickaxes,
                        4f + Mathf.Max(0, __instance.m_minToolTier), "deposit_emptied");
            }
        }

        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        private static class DepositHit
        {
            private static void Prefix(MineRock __instance, HitData hit)
            {
                if (__instance.m_removeWhenDestroyed && RegionalDeposit(__instance.m_dropItems))
                    RecordObject(__instance, hit, Skills.SkillType.Pickaxes,
                        4f + Mathf.Max(0, __instance.m_minToolTier), "deposit_emptied");
            }
        }

        // Called on vanilla's ~2 s player refresh. No per-frame work.
        internal static void Tick(Player player)
        {
            if (pending.Count == 0 || !player) return;
            float now = UnityEngine.Time.time;
            scratch.Clear();
            foreach (var kv in pending)
            {
                Pending p = kv.Value;
                bool gone = p.Target == null;
                bool dead = !gone && p.Creature != null && (p.Creature.IsDead() || p.Creature.GetHealth() <= 0f);
                if (gone || dead)
                {
                    if (now - p.Time <= Window && !player.IsDead() && !player.IsTeleporting() &&
                        Vector3.Distance(player.transform.position, p.Position) <= Range)
                        Pay(p);
                    scratch.Add(kv.Key);
                }
                else if (now - p.Time > Window) scratch.Add(kv.Key);
            }
            foreach (int id in scratch) pending.Remove(id);
        }

        private static void Prune(bool all)
        {
            scratch.Clear();
            float now = UnityEngine.Time.time;
            foreach (var kv in pending)
                if (all || now - kv.Value.Time > Window) scratch.Add(kv.Key);
            foreach (int id in scratch) pending.Remove(id);
        }

        // ---- Farming: harvesting grown crops: +0.5 XP -------------------
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
                SkillReward.Grant(Player.m_localPlayer, Skills.SkillType.Farming, 0.5f, "crop_harvested");
            }
        }

        // ---- Cooking: first time you get each station/fermenter product: 2 XP
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
                SkillReward.Grant(__instance, Skills.SkillType.Cooking, 2f, "first_station_dish");
            }
        }

        internal static void Reset() { pending.Clear(); stationProducts = null; }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class AccomplishmentsWorldStart
    {
        private static void Postfix() => Accomplishments.Reset();
    }
}
