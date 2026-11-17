using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Attack chains. Vanilla waits out each attack's own interval (m_aiAttackInterval, 2 s unless
    // the prefab says otherwise) and the creature's m_minAttackInterval before the next swing:
    // attack, pause, attack. An aggressive, confident creature can go straight into another one
    // when an attack ends, a different attack where it has more than one, after a very small step.
    // Every creature remembers which of its attacks land: a hit makes that attack likelier, a miss
    // less likely. Smart ones of the same kind nearby take the lesson too.
    // Runs only when an attack starts or ends, on hits, and while a step lasts.
    public sealed partial class WiresAIPlugin
    {
        private sealed class ChainState
        {
            internal readonly Dictionary<string, float> Weight = new Dictionary<string, float>();
            internal ItemDrop.ItemData Swing;
            internal bool Landed, Swinging, Stepping, StepRun, HitTimed;
            internal float SwingStart;
            internal int Left = -1; // attacks still to go in this chain; -1 not chaining
            internal float StepUntil;
            internal Vector3 StepDir;
        }
        private static readonly ConditionalWeakTable<MonsterAI, ChainState> ChainStates = new ConditionalWeakTable<MonsterAI, ChainState>();
        private static int _stepping;

        private static readonly AccessTools.FieldRef<Humanoid, float> SinceLastAttack =
            AccessTools.FieldRefAccess<Humanoid, float>("m_timeSinceLastAttack");
        private static readonly AccessTools.FieldRef<MonsterAI, float> WeaponTimer =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_updateWeaponTimer");
        private static readonly AccessTools.FieldRef<Attack, Humanoid> AttackBy =
            AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<Attack, bool> AttackDone =
            AccessTools.FieldRefAccess<Attack, bool>("m_attackDone");

        private const float LearnHit = 1.25f, LearnMiss = .8f, WeightMin = .3f, WeightMax = 3f;
        private const float ShareRange = 20f;

        private static float WeightOf(ChainState s, string attack) => s.Weight.TryGetValue(attack, out float w) ? w : 1f;
        private static void Learn(ChainState s, string attack, bool hit, float strength)
        {
            float step = Mathf.Pow(hit ? LearnHit : LearnMiss, strength);
            s.Weight[attack] = Mathf.Clamp(WeightOf(s, attack) * step, WeightMin, WeightMax);
        }

        // A thrown or shot attack lands after it ends: not judged by hit or miss.
        private static bool Melee(ItemDrop.ItemData item) =>
            item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class ChainSwingStart
        {
            private static void Postfix(Humanoid __instance, bool __result)
            {
                if (!__result || __instance.IsPlayer() || Instance == null || !Instance.Active ||
                    !(__instance.GetBaseAI() is MonsterAI ai) || !Brains.TryGetValue(ai, out Behavior b) ||
                    b.View == null || !b.View.IsOwner()) return;
                ChainState s = ChainStates.GetOrCreateValue(ai);
                s.Swing = __instance.GetCurrentWeapon();
                s.Landed = false;
                s.Swinging = true;
                s.SwingStart = Time.time;
                s.HitTimed = false;
            }
        }

        // On the attacker's side (Attack/Projectile call Damage there).
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class ChainSwingLanded
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (hit == null || !(hit.GetAttacker() is Humanoid h) || h.IsPlayer() || h == __instance ||
                    !(h.GetBaseAI() is MonsterAI ai) || !ChainStates.TryGetValue(ai, out ChainState s) || !s.Swinging) return;
                if (ai.IsEnemy(__instance)) s.Landed = true;
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
        private static class ChainSwingDone
        {
            private static void Prefix(Attack __instance)
            {
                if (AttackDone(__instance)) return;
                Humanoid h = AttackBy(__instance);
                if (h == null || h.IsPlayer() || !(h.GetBaseAI() is MonsterAI ai) ||
                    !ChainStates.TryGetValue(ai, out ChainState s) || !s.Swinging) return;
                s.Swinging = false;
                Instance?.SwingDone(ai, h, s);
            }
        }

        private void SwingDone(MonsterAI ai, Humanoid h, ChainState s)
        {
            if (!Active || !Brains.TryGetValue(ai, out Behavior b) || b.View == null || !b.View.IsOwner() ||
                !Phys.TryGetValue(h, out Physical ph) || ph.Individual == null) return;
            Individual p = ph.Individual;
            ItemDrop.ItemData done = s.Swing;
            if (done != null && Melee(done))
            {
                string name = done.m_shared.m_name;
                Learn(s, name, s.Landed, 1f);
                ShareLesson(ai, b, name, s.Landed);
            }
            Chain(ai, b, p, h, s, done);
        }

        // Smart ones of the same kind nearby see it hit or miss, and take half the lesson.
        private static void ShareLesson(MonsterAI from, Behavior mine, string attack, bool hit)
        {
            string kind = mine.Kind;
            Vector3 at = mine.Character.transform.position;
            foreach (BaseAI other in BaseAI.BaseAIInstances)
            {
                // cheapest checks first: a swarm ends attacks all the time
                if (other == from || (other.transform.position - at).sqrMagnitude > ShareRange * ShareRange ||
                    !(other is MonsterAI ai) || !Brains.TryGetValue(ai, out Behavior b) || b.Kind != kind ||
                    b.View == null || !b.View.IsOwner() || b.Character == null || b.Character.IsDead() ||
                    !Phys.TryGetValue(b.Character, out Physical ph) || ph.Individual == null)
                    continue;
                if (Roll(ph.Individual, ++b.Decisions) >= SmartChance(ph.Individual)) continue;
                Learn(ChainStates.GetOrCreateValue(ai), attack, hit, .5f);
            }
        }

        private void Chain(MonsterAI ai, Behavior b, Individual p, Humanoid h, ChainState s, ItemDrop.ItemData done)
        {
            Character target = Target(ai);
            if (target == null || target.IsDead() || h.IsStaggering() || b.Action == Posture.Retreat)
            { s.Left = -1; return; }
            if (s.Left < 0)
            {
                // how much it wants to keep going: aggression (-1..1, pulled toward its pack's) and confidence (0..1)
                float drive = Mathf.Clamp01(.5f * (GroupAggression(p, b) + 1f) * .5f + .5f * Mathf.Clamp01(b.Confidence));
                if (Roll(p, ++b.Decisions) >= drive) { NoChain(ai, b, p, target, done); return; }
                s.Left = 1 + Mathf.FloorToInt(3f * drive * Roll(p, ++b.Decisions)); // up to 4 in a row
            }
            if (s.Left == 0) { s.Left = -1; NoChain(ai, b, p, target, done); return; }
            s.Left--;

            ItemDrop.ItemData next = PickAttack(ai, h, s, done, target, p, b);
            if (next == null) { s.Left = -1; return; }
            // ready now: its own interval and the creature's minimum both counted as passed
            next.m_lastAttackTime = Time.time - next.m_shared.m_aiAttackInterval - .05f;
            SinceLastAttack(h) = Mathf.Max(SinceLastAttack(h), ai.m_minAttackInterval);
            if (h.GetCurrentWeapon() != next) h.EquipItem(next, false);
            WeaponTimer(ai) = 1f; // vanilla doesn't swap it back before it's used

            // Sometimes straight into it. A very small step first, sideways or back, mostly when it
            // repeats itself (the same attack again, or the only one it has), so it doesn't look
            // like the same thing spammed; now and then even when it switches.
            bool same = next == done || Options.Count <= 1;
            if (Roll(p, ++b.Decisions) >= (same ? .75f : .25f)) { b.HoldUntil = 0f; b.DelayDecided = true; return; }
            Vector3 to = target.transform.position - h.transform.position; to.y = 0f;
            float r = Roll(p, ++b.Decisions);
            Vector3 dir = r < .4f ? Vector3.Cross(Vector3.up, to) : r < .8f ? -Vector3.Cross(Vector3.up, to) : -to;
            float step = Mathf.Lerp(.12f, .3f, Roll(p, ++b.Decisions));
            s.StepDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
            s.StepUntil = Time.time + step;
            s.StepRun = false;
            if (!s.Stepping && s.StepDir != Vector3.zero) { s.Stepping = true; _stepping++; }
            b.HoldUntil = Time.time + step; // DelayedAttack holds the swing for the step
            b.HoldIsFeint = true;
            b.DelayDecided = true;
        }

        // Not going straight into another: the next one still comes sooner than vanilla's wait, and a
        // charger too close for its charge backs off to run at it again.
        private void NoChain(MonsterAI ai, Behavior b, Individual p, Character target, ItemDrop.ItemData done)
        {
            Hurry(ai, b, p, done);
            TryRunUp(ai, b, p, target, Time.time);
        }

        // A parried swing: that attack is taken less often (more than a plain miss).
        internal static void Parried(MonsterAI ai)
        {
            if (ChainStates.TryGetValue(ai, out ChainState s) && s.Swing != null && Melee(s.Swing))
                Learn(s, s.Swing.m_shared.m_name, false, 1.5f);
        }

        // Which attack next: what has been landing, a different one than the last where it has others.
        private static readonly List<ItemDrop.ItemData> Options = new List<ItemDrop.ItemData>();
        private static ItemDrop.ItemData PickAttack(MonsterAI ai, Humanoid h, ChainState s, ItemDrop.ItemData last,
            Character target, Individual p, Behavior b)
        {
            float dist = Vector3.Distance(target.transform.position, h.transform.position) - target.GetRadius();
            Options.Clear();
            foreach (ItemDrop.ItemData item in h.GetInventory().GetAllItems())
            {
                var sh = item.m_shared;
                if (!item.IsWeapon() || sh.m_aiTargetType != ItemDrop.ItemData.AiTarget.Enemy ||
                    !ai.CanUseAttack(item) || dist < sh.m_aiAttackRangeMin || dist > sh.m_aiAttackRange) continue;
                Options.Add(item);
            }
            if (Options.Count == 0) return null;
            float total = 0f;
            foreach (ItemDrop.ItemData item in Options)
                total += WeightOf(s, item.m_shared.m_name) * (item == last && Options.Count > 1 ? .35f : 1f);
            float pick = Roll(p, ++b.Decisions) * total;
            foreach (ItemDrop.ItemData item in Options)
            {
                pick -= WeightOf(s, item.m_shared.m_name) * (item == last && Options.Count > 1 ? .35f : 1f);
                if (pick <= 0f) return item;
            }
            return Options[Options.Count - 1];
        }

        // The step: vanilla stops moving in reach of its target; while a step lasts it moves instead.
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.StopMoving))]
        private static class ChainStep
        {
            private static bool Prefix(BaseAI __instance)
            {
                if (_stepping <= 0 || !(__instance is MonsterAI ai) ||
                    !ChainStates.TryGetValue(ai, out ChainState s) || !s.Stepping) return true;
                if (Time.time < s.StepUntil && !ai.GetComponent<Character>().IsDead())
                {
                    ai.MoveTowards(s.StepDir, s.StepRun);
                    return false;
                }
                s.Stepping = false;
                _stepping--;
                return true;
            }
        }
    }
}
