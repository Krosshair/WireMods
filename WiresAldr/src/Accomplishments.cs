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
            internal bool IsKill;
            internal int Diff;
            internal float Window = 6f;
        }

        private const float Range = 40f;      // trees and deposits
        private const float KillRange = 120f; // long bow shots still count
        // The check below runs on vanilla's ~2 s refresh, so a target that
        // died just inside its window is still noticed on the next refresh.
        private const float RefreshGrace = 2.5f;
        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static readonly List<int> scratch = new List<int>();

        // One entry per target, refreshed in place on every hit (no
        // allocation per hit during a fight).
        private static Pending Slot(UnityEngine.Object target)
        {
            int id = target.GetInstanceID();
            Pending p;
            if (pending.TryGetValue(id, out p)) return p;
            if (pending.Count > 256) Prune(true);
            p = new Pending();
            pending[id] = p;
            return p;
        }

        // ---- Kills -------------------------------------------------------
        // Same tier as your progression: 2.5 XP (about two hits' worth),
        // +1 per tier above (max +3); one tier below 1.25, two 0.6, older 0.3.
        // +50% per star. Sneak-attack kills also give Sneak XP.
        internal static void RecordHit(Character target, Player player, HitData hit,
            int targetTier, int stars, bool unaware) =>
            RecordKill(target, player, hit, hit != null ? hit.m_skill : Skills.SkillType.None,
                targetTier, stars, unaware, 1f, "kill");

        // Damage over time and knockback finish kills later: poison up to 15 s,
        // fire 10 s, a strong push (falls, lava) 10 s; otherwise 6 s.
        private static float KillWindow(HitData hit)
        {
            float w = 6f;
            if (hit.m_damage.m_poison > 0f) w = Mathf.Max(w, 15f);
            if (hit.m_damage.m_fire > 0f || hit.m_damage.m_spirit > 0f) w = Mathf.Max(w, 10f);
            if (hit.m_pushForce > 30f) w = Mathf.Max(w, 10f);
            return w;
        }

        // Kill reward against your last conquered biome (diff 0): +per
        // biome beyond (max 3), halved per biome older (max 3 halvings).
        internal static float KillXpFor(int diff) =>
            diff >= 0 ? Plugin.KillXp.Value + Plugin.KillXpPerBiome.Value * Mathf.Min(diff, 3)
                      : Plugin.KillXp.Value * Mathf.Pow(0.5f, Mathf.Min(3, -diff));

        private static void RecordKill(Character target, Player player, HitData hit, Skills.SkillType skill,
            int targetTier, int stars, bool unaware, float scale, string reason)
        {
            if (!target || !player || hit == null || skill == Skills.SkillType.None ||
                target.IsBoss() || target.IsTamed() || target.IsPlayer()) return;
            int diff = targetTier - DynamicExperience.PlayerTierOf(player);
            // Older creatures are reduced, never worthless.
            float xp = KillXpFor(diff);
            xp *= (1f + 0.5f * (Mathf.Clamp(stars, 1, 4) - 1)) * scale;
            float sneak = scale < 1f ? 0f : unaware && player.IsCrouching()
                ? Plugin.SneakKillXp.Value * Mathf.Pow(0.5f, Mathf.Min(3, Mathf.Max(0, -diff))) : 0f;
            Pending p = Slot(target);
            p.Target = target; p.Creature = target; p.Position = target.transform.position;
            p.Time = UnityEngine.Time.time; p.Skill = skill; p.Xp = xp; p.SneakXp = sneak;
            p.Reason = reason; p.Diff = diff; p.IsKill = true; p.Window = KillWindow(hit);
        }

        // Your Blood Magic summons' kills give Blood Magic XP: 60% of a kill,
        // or a full kill while you fight alongside them (attacked, cast or
        // blocked in the last 20 s). Their attacks run on the game that
        // controls them: normally yours.
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class SummonHit
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                Player player = Player.m_localPlayer;
                if (!player || !__instance || hit == null || __instance.IsPlayer() || __instance.IsTamed()) return;
                Character attacker = hit.GetAttacker();
                if (!attacker || attacker.IsPlayer() || !attacker.IsTamed()) return;
                Tameable summon = attacker.GetComponent<Tameable>();
                if (summon == null || summon.m_levelUpOwnerSkill == Skills.SkillType.None) return;
                ZNetView view = attacker.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) return;
                if (Vector3.Distance(attacker.transform.position, player.transform.position) > 60f) return;
                BaseAI ai = __instance.GetBaseAI();
                if (ai == null || !ai.IsEnemy(player)) return;
                RecordKill(__instance, player, hit, summon.m_levelUpOwnerSkill,
                    DynamicExperience.GetEntityTier(__instance), __instance.GetLevel(), false,
                    DynamicExperience.RecentlyActive ? 1f : Plugin.SummonKillShare.Value / 100f,
                    DynamicExperience.RecentlyActive ? "summon_kill_fighting" : "summon_kill");
            }
        }

        private static void Pay(Pending p)
        {
            Player player = Player.m_localPlayer;
            if (!player) return;
            string why = p.IsKill && AldrDiagnostics.Active ? p.Reason + "_tier_diff_" + p.Diff : p.Reason;
            SkillReward.Grant(player, p.Skill, p.Xp, why);
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
                if (UnityEngine.Time.time - p.Time <= p.Window) Pay(p);
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
            Pending p = Slot(target);
            p.Target = target; p.Creature = null; p.Position = target.transform.position;
            p.Time = UnityEngine.Time.time; p.Skill = skill; p.Xp = xp; p.SneakXp = 0f;
            p.Reason = reason; p.IsKill = false; p.Window = 6f;
        }

        // Felling a whole standing tree: 2 XP, +1 per tool tier the tree needs.
        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
        private static class TreeHit
        {
            private static void Prefix(TreeBase __instance, HitData hit) =>
                RecordObject(__instance, hit, Skills.SkillType.WoodCutting,
                    Plugin.TreeXp.Value + Mathf.Max(0, __instance.m_minToolTier), "tree_felled");
        }

        // Deposits of a biome-exclusive material (base = 4 XP, +1 per tool tier).
        // Multi-chunk deposits (copper, tin, ...) pay every chunk you break:
        // base / (2 * sqrt(chunks)), so a bigger node is worth more in total
        // (base * sqrt(chunks) / 2) while each chunk still pays noticeably.
        // Mining the node out completely pays the base again on top. Chunks
        // that fall off unsupported count for whoever hit the rock just before.
        private static readonly Dictionary<int, float> rockHits = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> rockChunks = new Dictionary<int, int>();
        private static readonly System.Reflection.FieldInfo HitAreas5 =
            AccessTools.Field(typeof(MineRock5), "m_hitAreas");
        private static readonly System.Reflection.FieldInfo HitAreas =
            AccessTools.Field(typeof(MineRock), "m_hitAreas");

        private static void NoteRockHit(Component rock)
        {
            if (rockHits.Count > 64) { rockHits.Clear(); rockChunks.Clear(); }
            rockHits[rock.GetInstanceID()] = UnityEngine.Time.time;
        }

        // A chunk of a deposit you hit in the last 6 s just broke.
        private static void PayChunk(Component rock, System.Reflection.FieldInfo areas, int minToolTier)
        {
            if (!rock) return;
            int id = rock.GetInstanceID();
            float hitTime;
            if (!rockHits.TryGetValue(id, out hitTime) || UnityEngine.Time.time - hitTime > 6f) return;
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead() ||
                Vector3.Distance(player.transform.position, rock.transform.position) > Range) return;
            int chunks;
            if (!rockChunks.TryGetValue(id, out chunks))
            {
                object list = areas?.GetValue(rock);
                chunks = list is System.Collections.ICollection c ? c.Count : list is Array a ? a.Length : 0;
                rockChunks[id] = chunks;
            }
            if (chunks <= 0) return;
            float baseXp = Plugin.DepositXp.Value + Mathf.Max(0, minToolTier);
            SkillReward.Grant(player, Skills.SkillType.Pickaxes,
                baseXp / (2f * Mathf.Sqrt(chunks)), "deposit_chunk");
        }

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        private static class DepositHit5
        {
            private static void Prefix(MineRock5 __instance, HitData hit)
            {
                Player player = Player.m_localPlayer;
                if (!__instance || hit == null || !player || hit.GetAttacker() != player ||
                    hit.GetTotalDamage() <= 0f || !RegionalDeposit(__instance.m_dropItems)) return;
                NoteRockHit(__instance);
                // Completion bonus: paid if the whole node is gone shortly after.
                RecordObject(__instance, hit, Skills.SkillType.Pickaxes,
                    Plugin.DepositXp.Value + Mathf.Max(0, __instance.m_minToolTier), "deposit_mined_out");
            }
        }

        // Sent to every game when a chunk breaks, so this works in multiplayer
        // whoever controls the rock.
        [HarmonyPatch(typeof(MineRock5), "RPC_SetAreaHealth")]
        private static class DepositChunk
        {
            private static void Postfix(MineRock5 __instance, float health)
            {
                if (health <= 0f && __instance)
                    PayChunk(__instance, HitAreas5, __instance.m_minToolTier);
            }
        }

        // Older single-object deposits (vanilla MineRock) follow the same rules:
        // its "Hide" RPC reaches every game when one of its areas breaks.
        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        private static class DepositHit
        {
            private static void Prefix(MineRock __instance, HitData hit)
            {
                Player player = Player.m_localPlayer;
                if (!__instance || hit == null || !player || hit.GetAttacker() != player ||
                    hit.GetTotalDamage() <= 0f || !RegionalDeposit(__instance.m_dropItems)) return;
                NoteRockHit(__instance);
                if (__instance.m_removeWhenDestroyed)
                    RecordObject(__instance, hit, Skills.SkillType.Pickaxes,
                        Plugin.DepositXp.Value + Mathf.Max(0, __instance.m_minToolTier), "deposit_mined_out");
            }
        }

        [HarmonyPatch(typeof(MineRock), "RPC_Hide")]
        private static class DepositArea
        {
            private static void Postfix(MineRock __instance) =>
                PayChunk(__instance, HitAreas, __instance != null ? __instance.m_minToolTier : 0);
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
                    if (now - p.Time <= p.Window + RefreshGrace && !player.IsDead() && !player.IsTeleporting() &&
                        Vector3.Distance(player.transform.position, p.Position) <= (p.IsKill ? KillRange : Range))
                        Pay(p);
                    scratch.Add(kv.Key);
                }
                else if (now - p.Time > p.Window + RefreshGrace) scratch.Add(kv.Key);
            }
            foreach (int id in scratch) pending.Remove(id);
        }

        private static void Prune(bool all)
        {
            scratch.Clear();
            float now = UnityEngine.Time.time;
            foreach (var kv in pending)
                if (all || now - kv.Value.Time > kv.Value.Window + RefreshGrace) scratch.Add(kv.Key);
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
                SkillReward.Grant(Player.m_localPlayer, Skills.SkillType.Farming, Plugin.CropXp.Value, "crop_harvested");
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
                SkillReward.Grant(__instance, Skills.SkillType.Cooking, Plugin.FirstDishXp.Value, "first_station_dish");
            }
        }

        internal static void Reset() { pending.Clear(); rockHits.Clear(); rockChunks.Clear(); stationProducts = null; }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class AccomplishmentsWorldStart
    {
        private static void Postfix() => Accomplishments.Reset();
    }
}
