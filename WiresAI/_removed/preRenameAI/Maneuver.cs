using HarmonyLib;
using UnityEngine;

namespace WiresEnemyVariation
{
    // Positioning on top of vanilla movement. Decisions happen on target scans and hits;
    // during a maneuver vanilla's circling walks to our point instead of a random one.
    public sealed partial class EnemyVariationPlugin
    {
        private enum Move : byte { None, Flank, BackOff, Hold, Pincer, Sidestep }

        private static readonly AccessTools.FieldRef<MonsterAI, float> PauseTimer =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_pauseTimer");
        private static readonly AccessTools.FieldRef<MonsterAI, float> InterceptTime =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_interceptTime");
        private static int _maneuvers;
        private static readonly System.Func<BaseAI, Vector3, bool> HavePath =
            AccessTools.MethodDelegate<System.Func<BaseAI, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "HavePath"));

        // Fights only from range: every weapon it can carry reaches 8 m or more.
        private static bool IsRanged(Character c)
        {
            if (!(c is Humanoid h)) return false;
            bool any = false;
            bool Check(GameObject[] items)
            {
                if (items == null) return true;
                foreach (GameObject go in items)
                {
                    ItemDrop d = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (d == null) continue;
                    var sh = d.m_itemData.m_shared;
                    var t = sh.m_itemType;
                    if (t != ItemDrop.ItemData.ItemType.OneHandedWeapon && t != ItemDrop.ItemData.ItemType.TwoHandedWeapon &&
                        t != ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft && t != ItemDrop.ItemData.ItemType.Bow) continue;
                    any = true;
                    if (t != ItemDrop.ItemData.ItemType.Bow && sh.m_aiAttackRange < 8f) return false;
                }
                return true;
            }
            if (!Check(h.m_defaultItems) || !Check(h.m_randomWeapon)) return false;
            if (h.m_randomSets != null)
                foreach (var set in h.m_randomSets) if (!Check(set.m_items)) return false;
            return any;
        }

        private static bool IsCaster(Character c)
        {
            string n = c.gameObject.name;
            return n.IndexOf("Shaman", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Mage", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("JotunWitch", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Everyone is a bit smarter than vanilla; clever ones far more often.
        private static float SmartChance(Individual p) => .12f + .5f * Mathf.Clamp01((p.Tactics + 1f) * .5f);

        // Crowds leave the fancy stuff mostly to the clever ones.
        private static float TacticsChance(Individual p, int allies)
        {
            float chance = SmartChance(p);
            if (allies > 2) chance *= p.Tactics > .3f ? .6f : .3f;
            return chance;
        }

        private void ChooseManeuver(MonsterAI ai, Behavior b, Character target, Individual p, float now)
        {
            if (b.Maneuver != Move.None || b.Action == Posture.Retreat || b.Character.InAttack()) return;
            Character self = b.Character;
            Vector3 toTarget = target.transform.position - self.transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            float chance = TacticsChance(p, b.Allies);

            // Runners: smart ones aim further ahead instead of trailing behind.
            Vector3 vel = target.GetVelocity(); vel.y = 0f;
            if (vel.magnitude > 4f && Vector3.Dot(vel, toTarget) > 0f)
                InterceptTime(ai) = Mathf.Lerp(b.Intercept, 2f, Mathf.Clamp01(p.Tactics + .3f));
            else
                InterceptTime(ai) = b.Intercept;

            Move pick = Move.None;
            string why = null;
            float holdDist = 0f, holdTime = 0f;
            float smart = SmartChance(p);
            // Archers and spitters: back off when you close in.
            if (p.Ranged && dist < 6f && Roll(p, ++b.Decisions) < smart + .3f)
            { pick = Move.Hold; why = "keep-distance"; holdDist = 12f; holdTime = 2.5f; }
            // Casters stay behind their melee friends.
            else if (p.Caster && dist < 7f && b.MatesNearTarget > 0 && Roll(p, ++b.Decisions) < smart + .2f)
            { pick = Move.Hold; why = "caster-back"; holdDist = 11f; holdTime = 2.5f; }
            // Ranged without a clear shot: step sideways to find one.
            else if (p.Ranged && dist < 30f && self.GetTimeSinceLastAttack() > 4f && !ai.CanSeeTarget(target) &&
                Roll(p, ++b.Decisions) < chance + .2f)
            {
                Vector3 side = Quaternion.Euler(0f, Roll(p, ++b.Decisions) < .5f ? 90f : -90f, 0f) * toTarget.normalized;
                b.FixedPoint = self.transform.position + side * 5f;
                StartManeuver(ai, b, p, Move.Sidestep, target, toTarget, 2f, "line-of-sight");
                return;
            }
            // Can't reach it (ledge, rock, roof): wait out of reach and spread around instead of loitering.
            else if (dist < 25f && (target.transform.position.y - self.transform.position.y > 1.6f ||
                self.GetTimeSinceLastAttack() > 5f) && !HavePath(ai, target.transform.position) &&
                Roll(p, ++b.Decisions) < chance + .25f)
            { pick = Move.Hold; why = "unreachable"; holdDist = 13f; holdTime = 4f; }
            if (pick != Move.None)
            {
                Vector3 back = -toTarget.normalized;
                float spread = Mathf.Lerp(-50f, 50f, Roll(p, ++b.Decisions)); // packs fan out
                b.HoldDir = Quaternion.Euler(0f, spread, 0f) * back;
                b.HoldDist = holdDist;
                StartManeuver(ai, b, p, pick, target, toTarget, holdTime, why);
                return;
            }
            if (dist > 8f) return;
            // Target behind it and it turns slowly: get out and come back at a better angle.
            if (Vector3.Angle(self.transform.forward, toTarget) > 110f && self.m_turnSpeed < 200f &&
                Roll(p, ++b.Decisions) < chance * 1.2f)
            { pick = Move.BackOff; why = "target-behind"; }
            // Just got staggered or parried: don't swing into the same thing again.
            else if (now - b.StaggeredAt < 3f && Roll(p, ++b.Decisions) < chance)
            { pick = Move.Flank; why = "after-stagger"; }
            // Turtling player: go around the shield. Learns within the fight.
            else if (target.IsBlocking() && Roll(p, ++b.Decisions) < chance +
                .1f * Mathf.Min(1f, b.SeenBlocks / 4f) + .15f * Mathf.Min(1f, b.SeenParries / 2f))
            { pick = Move.Flank; why = "target-blocking"; }
            // A packmate is already on the target: come from the other side.
            else if (b.MatesNearTarget > 0 && Roll(p, ++b.Decisions) < chance)
            {
                pick = Move.Pincer; why = "pincer";
                Vector3 mates = b.MatesCentroid - target.transform.position; mates.y = 0f;
                b.HoldDir = mates.sqrMagnitude > .01f ? -mates.normalized : -toTarget.normalized;
                b.HoldDist = 2.8f;
            }
            // Packmates flanking: social ones may join in.
            else if (b.PackFlanking > 0 &&
                Roll(p, ++b.Decisions) < .5f * Mathf.Clamp01(p.SpeciesGroupFactor - .5f))
            { pick = Move.Flank; why = "following-flank"; }
            // Pillar dancing: no swing for a while at close range, come round the other way.
            else if (now - b.EncounterStart > 6f && self.GetTimeSinceLastAttack() > 6f && dist < 8f &&
                Roll(p, ++b.Decisions) < chance)
            { pick = Move.Flank; why = "pillar"; }
            // Melee spacing: hover just out of reach and wait for a whiff.
            else if (!p.Ranged && p.Tactics > 0f && target is Player && dist < 5f &&
                Roll(p, ++b.Decisions) < .5f * smart)
            {
                b.HoldDir = -toTarget.normalized; b.HoldDist = 3.6f; b.Spacing = true;
                StartManeuver(ai, b, p, Move.Hold, target, toTarget, 2.5f, "spacing");
                return;
            }
            if (pick == Move.None) return;
            float duration = pick == Move.BackOff ? 1.4f : Mathf.Lerp(1.6f, 2.6f, Roll(p, ++b.Decisions));
            StartManeuver(ai, b, p, pick, target, toTarget, duration, why);
        }

        private void StartManeuver(MonsterAI ai, Behavior b, Individual p, Move pick, Character target,
            Vector3 toTarget, float duration, string why)
        {
            b.Maneuver = pick;
            b.ManeuverTarget = target;
            b.ManeuverSide = Vector3.Dot(target.transform.right, -toTarget) >= 0f ? 1f : -1f;
            if (why == "pillar") b.ManeuverSide = -b.ManeuverSide; // the other way round
            _maneuvers++;
            // Force vanilla's circling branch for the maneuver's length.
            b.Lease2 = ai.gameObject.AddComponent<ManeuverLease>();
            b.Lease2.AI = ai;
            b.Lease2.Interval = ai.m_circleTargetInterval;
            b.Lease2.Duration = ai.m_circleTargetDuration;
            // Some creatures never circle (interval 0); give them one circle for the maneuver.
            if (ai.m_circleTargetInterval <= 0f) ai.m_circleTargetInterval = .05f;
            ai.m_circleTargetDuration = duration;
            PauseTimer(ai) = ai.m_circleTargetInterval + .01f;
            b.Lease2.Begin(duration);
            b.Counts[0]++;
            if (_verbose.Value)
                Logger.LogInfo("[WEV maneuver] " + b.View.GetZDO().m_uid + " " + pick + " reason=" + why);
        }

        internal void EndManeuver(MonsterAI ai)
        {
            if (ai == null || !Brains.TryGetValue(ai, out Behavior b)) return;
            StopManeuver(b);
        }

        private static void StopManeuver(Behavior b)
        {
            if (b.Maneuver == Move.None) return;
            b.Maneuver = Move.None;
            b.ManeuverTarget = null;
            b.Spacing = false;
            _maneuvers--;
            if (b.Lease2 != null) { ManeuverLease l = b.Lease2; b.Lease2 = null; l.Restore(); l.Cancel(); }
            b.NextDecisionAt = 0f;
        }

        private static Vector3 ManeuverPoint(Behavior b, Vector3 fallback)
        {
            Character t = b.ManeuverTarget;
            if (t == null || t.IsDead()) return fallback;
            Vector3 tp = t.transform.position, me = b.Character.transform.position;
            if (b.Maneuver == Move.Sidestep) return b.FixedPoint;
            if (b.Maneuver == Move.Hold || b.Maneuver == Move.Pincer)
                return tp + b.HoldDir * b.HoldDist;
            if (b.Maneuver == Move.Flank)
                // Behind the target's shoulder on the side we're already on.
                return tp + (Quaternion.Euler(0f, 140f * b.ManeuverSide, 0f) * t.transform.forward) * 2.5f;
            // Back off sideways to gain an angle.
            Vector3 away = me - tp; away.y = 0f;
            return me + (Quaternion.Euler(0f, 50f * b.ManeuverSide, 0f) * away.normalized) * 4f;
        }

        [HarmonyPatch(typeof(BaseAI), "MoveTo")]
        private static class ManeuverMove
        {
            // Exits on the first check unless some enemy is mid-maneuver.
            private static void Prefix(BaseAI __instance, ref Vector3 point, ref bool run)
            {
                if (_maneuvers <= 0 || !(__instance is MonsterAI ai) ||
                    !Brains.TryGetValue(ai, out Behavior b) || b.Maneuver == Move.None) return;
                point = ManeuverPoint(b, point);
                run = true;
            }
        }
    }

    public sealed partial class EnemyVariationPlugin
    {
        // Player-side: dodging or eating/drinking opens a window. Tell enemies close by.
        private const int OpenDodge = 0, OpenConsume = 1, OpenSwing = 2, OpenStagger = 3;

        private static void BroadcastOpening(Player pl, int kind)
        {
            if (Instance == null || !Instance.Active || pl == null || pl != Player.m_localPlayer) return;
            Vector3 pos = pl.transform.position;
            System.Collections.Generic.List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c.IsDead() || c.IsTamed() || !(c.GetBaseAI() is MonsterAI) ||
                    (c.transform.position - pos).sqrMagnitude > 100f) continue;
                ZNetView v = c.GetComponent<ZNetView>();
                if (v != null && v.IsValid()) v.InvokeRPC("WEV_Opening_v1", pl.GetPlayerID(), kind);
            }
        }

        private void OnOpening(MonsterAI ai, long pid, int kind)
        {
            if (!Active || ai == null) return;
            Behavior b = Brain(ai);
            if (b.View == null || !b.View.IsValid() || !b.View.IsOwner() ||
                !(Target(ai) is Player pl) || pl.GetPlayerID() != pid ||
                !Phys.TryGetValue(b.Character, out Physical ph) || ph.Individual == null) return;
            Individual p = ph.Individual;
            float now = Time.time, wait = 0f, chance = SmartChance(p) + .15f;
            string why = "punish-opening";
            if (kind == OpenSwing)
            {
                // Only the ones hovering at the edge of reach care about a swing.
                if (!b.Spacing) return;
                chance = SmartChance(p) + .2f; wait = .45f; why = "whiff-punish";
            }
            else if (kind == OpenDodge)
            {
                b.SeenRolls++;
                // Seen you roll before: wait the roll out, then go.
                if (b.SeenRolls >= 2) wait = .5f;
                why = "roll-punish";
            }
            else if (kind == OpenStagger)
            { chance = .5f + .3f * Mathf.Clamp01((p.Tactics + 1f) * .5f); why = "player-staggered"; }
            if (Roll(p, ++b.Decisions) >= chance) return;
            StopManeuver(b);
            if (b.Action == Posture.Retreat) return;
            SetPosture(ai, b, p, Posture.Commit, now, why, 0f, 0f, b.Confidence, 1f, 0f);
            PauseTimer(ai) = 0f;
            ai.m_minAttackInterval = 0f;
            b.HoldUntil = now + wait;
            b.DelayDecided = true; // no extra feint on a punish
            b.NextDecisionAt = now + 1.5f;
        }

        [HarmonyPatch(typeof(Player), "Dodge")]
        private static class DodgeOpening
        {
            private static void Postfix(Player __instance) => BroadcastOpening(__instance, OpenDodge);
        }

        [HarmonyPatch(typeof(Player), "ConsumeItem")]
        private static class ConsumeOpening
        {
            private static void Postfix(Player __instance, bool __result)
            {
                if (__result) BroadcastOpening(__instance, OpenConsume);
            }
        }

        [HarmonyPatch(typeof(Humanoid), "StartAttack")]
        private static class SwingOpening
        {
            private static void Postfix(Humanoid __instance, bool __result)
            {
                if (__result && __instance is Player pl) BroadcastOpening(pl, OpenSwing);
            }
        }

        [HarmonyPatch(typeof(Character), "RPC_Stagger")]
        private static class StaggerReaction
        {
            private static void Postfix(Character __instance)
            {
                if (Instance == null) return;
                if (__instance is Player pl) { BroadcastOpening(pl, OpenStagger); return; }
                if (!(__instance.GetBaseAI() is MonsterAI ai) ||
                    !Brains.TryGetValue(ai, out Behavior b) || b.View == null || !b.View.IsOwner()) return;
                // Staggered while its target was blocking: that was a parry.
                Character t = Target(ai);
                if (t != null && t.IsBlocking()) b.SeenParries++;
                b.StaggeredAt = Time.time;
                b.NextDecisionAt = 0f;
                b.DecisionReason = "staggered";
            }
        }
    }

    public sealed partial class EnemyVariationPlugin
    {
        // Delayed swings: sometimes hold a beat before attacking so the timing can't be learned.
        [HarmonyPatch(typeof(MonsterAI), "DoAttack")]
        private static class DelayedAttack
        {
            private static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                if (Instance == null || !Instance.Active || !Brains.TryGetValue(__instance, out Behavior b) ||
                    b.View == null || !b.View.IsOwner()) return true;
                float now = Time.time;
                if (now < b.HoldUntil) { __result = false; return false; }
                if (b.DelayDecided || !Phys.TryGetValue(b.Character, out Physical ph) || ph.Individual == null)
                    return true;
                b.DelayDecided = true;
                Individual p = ph.Individual;
                float chance = .6f * SmartChance(p) + .25f * Mathf.Min(1f, b.SeenParries / 2f);
                if (Roll(p, ++b.Decisions) >= chance) return true;
                b.HoldUntil = now + Mathf.Lerp(.2f, .65f, Roll(p, ++b.Decisions));
                __result = false;
                return false;
            }
            private static void Postfix(MonsterAI __instance, bool __result)
            {
                if (__result && Brains.TryGetValue(__instance, out Behavior b)) b.DelayDecided = false;
            }
        }
    }

    public sealed class ManeuverLease : MonoBehaviour
    {
        internal MonsterAI AI;
        internal float Interval, Duration;
        private bool _ended;
        internal void Begin(float duration) => Invoke(nameof(Expire), duration);
        internal void Cancel() { _ended = true; CancelInvoke(nameof(Expire)); Destroy(this); }
        // Creature unloaded mid-maneuver: still release the maneuver count.
        private void OnDestroy() { if (!_ended) { _ended = true; EnemyVariationPlugin.Instance?.EndManeuver(AI); } }
        internal void Restore()
        {
            if (AI == null) return;
            AI.m_circleTargetInterval = Interval;
            AI.m_circleTargetDuration = Duration;
        }
        private void Expire() => EnemyVariationPlugin.Instance?.EndManeuver(AI); // restores and destroys
    }
}
