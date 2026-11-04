using HarmonyLib;
using UnityEngine;

namespace WiresEnemyVariation
{
    // Positioning on top of vanilla movement. Decisions happen on target scans and hits;
    // during a maneuver vanilla's circling walks to our point instead of a random one.
    public sealed partial class EnemyVariationPlugin
    {
        private enum Move : byte { None, Flank, BackOff }

        private static readonly AccessTools.FieldRef<MonsterAI, float> PauseTimer =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_pauseTimer");
        private static readonly AccessTools.FieldRef<MonsterAI, float> InterceptTime =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_interceptTime");
        private static int _maneuvers;

        // Chance multiplier for tactics. Crowds leave it to the clever ones.
        private static float TacticsChance(Individual p, int allies)
        {
            float chance = Mathf.Clamp01(.15f + .6f * p.Tactics);
            if (allies > 2) chance *= p.Tactics > .3f ? .6f : .15f;
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

            if (dist > 8f) return;
            Move pick = Move.None;
            string why = null;
            // Target behind it and it turns slowly: get out and come back at a better angle.
            if (Vector3.Angle(self.transform.forward, toTarget) > 110f && self.m_turnSpeed < 200f &&
                Roll(p, ++b.Decisions) < chance * 1.2f)
            { pick = Move.BackOff; why = "target-behind"; }
            // Turtling player: go around the shield.
            else if (target.IsBlocking() && Roll(p, ++b.Decisions) < chance)
            { pick = Move.Flank; why = "target-blocking"; }
            // Packmates flanking: social ones may join in.
            else if (b.PackFlanking > 0 &&
                Roll(p, ++b.Decisions) < .5f * Mathf.Clamp01(p.SpeciesGroupFactor - .5f))
            { pick = Move.Flank; why = "following-flank"; }
            if (pick == Move.None) return;

            b.Maneuver = pick;
            b.ManeuverTarget = target;
            b.ManeuverSide = Vector3.Dot(target.transform.right, -toTarget) >= 0f ? 1f : -1f;
            _maneuvers++;
            // Force vanilla's circling branch for the maneuver's length.
            float duration = pick == Move.BackOff ? 1.4f : Mathf.Lerp(1.6f, 2.6f, Roll(p, ++b.Decisions));
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
            _maneuvers--;
            if (b.Lease2 != null) { ManeuverLease l = b.Lease2; b.Lease2 = null; l.Restore(); l.Cancel(); }
            b.NextDecisionAt = 0f;
        }

        private static Vector3 ManeuverPoint(Behavior b, Vector3 fallback)
        {
            Character t = b.ManeuverTarget;
            if (t == null || t.IsDead()) return fallback;
            Vector3 tp = t.transform.position, me = b.Character.transform.position;
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
