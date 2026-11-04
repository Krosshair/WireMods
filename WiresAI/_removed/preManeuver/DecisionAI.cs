using System;
using HarmonyLib;
using UnityEngine;

namespace WiresEnemyVariation
{
    public sealed partial class EnemyVariationPlugin
    {
        private enum Posture : byte { Vanilla, Commit, Probe, Hesitate, Retreat }

        // A single Unity engine-scheduled callback, NO Update/Coroutine/polling.
private static float Roll(Individual p, uint index)
        {
            uint seed = p.Seed ^ (index * 0x9E3779B9u) ^ 0xA5A5C33Du;
            seed ^= seed >> 16; seed *= 0x7FEB352Du;
            seed ^= seed >> 15; seed *= 0x846CA68Bu; seed ^= seed >> 16;
            return (seed + .5f) / 4294967296f;
        }

        // Invoked ONLY on existing target scans, damage, and successful-hit RPCs.
        // It changes vanilla's existing state; vanilla performs all navigation.
        private void ChoosePosture(MonsterAI ai, Behavior b, Character target,
            Individual p, float drive, float groupEffect, float now)
        {
            if (p == null || b == null || b.Character == null) return;
            if (b.Action == Posture.Retreat && b.Lease != null) return;
            if (now < b.NextDecisionAt) return;
            float hp = Mathf.Clamp01(b.Character.GetHealthPercentage());
            float preyWounded = Mathf.Clamp01(1f - target.GetHealthPercentage());
            float confidence = Mathf.Clamp01(b.Confidence);
            float fear = Mathf.Clamp01(b.Fear * p.FearSensitivity);
            float aggression = p.Aggression, courage = p.Courage;
            float disadvantage = Mathf.Clamp01(-groupEffect);
            float retreat = Mathf.Clamp01(.012f - .10f * aggression - .10f * courage +
                .19f * disadvantage + .17f * (1f-hp) + .24f * fear -
                .19f * confidence - .09f * b.Rage);
            // Pack contagion: social species copy their friends. Fearful ones follow the
            // fearless more readily than the other way round, and bold ones scoff at runners.
            float social = Mathf.Clamp01(p.SpeciesGroupFactor - .5f) * (.6f + .4f * p.Cohesion);
            float followCommit = 0f;
            if (social > 0f)
            {
                if (b.PackRetreating > 0)
                {
                    float n = Mathf.Min(1f, b.PackRetreating / 2f);
                    if (courage > .3f) followCommit += social * .2f * courage * n; // "what a coward"
                    else retreat = Mathf.Clamp01(retreat + social * .35f * Mathf.Clamp01(.5f - courage) * n);
                }
                if (b.PackCommitting > 0)
                    followCommit += social * .25f * Mathf.Min(1f, b.PackCommitting / 2f) *
                        (1f + Mathf.Clamp01(-courage) + Mathf.Clamp01(b.PackCourage - courage));
            }
            float roll = Roll(p, ++b.Decisions);
            Posture chosen;
            // Opening move shows personality: timid ones hang back, bold ones charge.
            bool opening = b.Counts[1] + b.Counts[2] + b.Counts[3] + b.Counts[4] == 0;
            float temper = aggression + courage;
            if (now - b.LastRetreat < 10f) retreat = 0f; // no retreat spam
            if (opening && temper < -.4f && roll >= retreat)
            { SetPosture(ai, b, p, Posture.Hesitate, now, "opening-timid", drive, retreat, confidence, hp, preyWounded);
              b.DecisionReason = null; b.NextDecisionAt = now + 3f; return; }
            if (opening && temper > .5f && roll >= retreat)
            { SetPosture(ai, b, p, Posture.Commit, now, "opening-bold", drive, retreat, confidence, hp, preyWounded);
              b.DecisionReason = null; b.NextDecisionAt = now + 3f; return; }
            if (roll < retreat && !b.Character.InAttack())
                chosen = Posture.Retreat;
            else
            {
                float direct = Mathf.Clamp01(followCommit + .46f + .26f*aggression +
                    .14f*courage + .20f*confidence + .13f*preyWounded +
                    .14f*groupEffect + .12f*b.Rage - .15f*fear);
                float strategic = Mathf.Clamp01(.33f + .22f*p.Tactics -
                    .10f*aggression + .08f*disadvantage);
                float choice = (roll - retreat) / Mathf.Max(.001f, 1f-retreat);
                chosen = choice < direct ? Posture.Commit :
                    choice < direct + strategic * (1f-direct) ?
                    Posture.Probe : Posture.Hesitate;
                // Smart ones wait their turn when two packmates are already on it.
                if (chosen == Posture.Commit && b.Committers >= 2 && p.Tactics > .15f &&
                    Roll(p, ++b.Decisions) < p.Tactics + .3f)
                { chosen = Posture.Probe; b.DecisionReason = "waiting-turn"; }
            }
            string reason = b.DecisionReason;
            if (reason == null) reason = "target-refresh";
            SetPosture(ai, b, p, chosen, now, reason, drive, retreat,
                confidence, hp, preyWounded);
            b.DecisionReason = null;
            b.NextDecisionAt = now + (chosen == Posture.Retreat ? 5f :
                Mathf.Lerp(2.0f, 4.8f, Roll(p, ++b.Decisions)));
        }

        private void SetPosture(MonsterAI ai, Behavior b, Individual p,
            Posture newAction, float now, string reason, float drive,
            float retreatChance, float confidence, float hp, float targetHPLoss)
        {
            Posture old = b.Action;
            b.Counts[(int)newAction]++;
            if (old == Posture.Retreat && newAction != Posture.Retreat)
                StopRetreat(ai,b);

            b.Action = newAction;
            if (newAction == Posture.Retreat)
            {
                // Wounded ones run to a packmate instead of just away.
                b.LastRetreat = now;
                if (hp < .5f && b.Leader != null && b.Leader != b.Character && !b.Leader.IsDead())
                {
                    SetRegroup(b, b.Leader);
                    AlertPack(b);
                }
                // Vanilla UpdateAI's EXISTING low-health Flee(dt, targetPosition)
                // branch handles all pathfinding. 1.01 allows fleeing while healthy.
                ai.m_fleeIfLowHealth = 1.01f;
                ai.m_fleeTimeSinceHurt = 10000000f;
                b.Lease = ai.gameObject.AddComponent<RetreatLease>();
                b.Lease.AI = ai;
                float duration = Mathf.Lerp(1.2f,3.6f,Roll(p,++b.Decisions));
                b.Lease.Begin(duration);
            }
            if (_verbose.Value && (old != newAction || reason != "target-refresh"))
            {
                Logger.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[WEV behavior] id={0} {1}->{2} reason={3} aggression={4:+0.00;-0.00;0} courage={5:+0.00;-0.00;0} drive={6:+0.00;-0.00;0} fleeChance={7:P1} selfHP={8:P0} targetHP={9:P0} rage={10:F2} fear={11:F2} confidence={12:F2}",
                    b.View.GetZDO().m_uid,old,newAction,reason,p.Aggression,p.Courage,
                    drive,retreatChance,hp,1f-targetHPLoss,b.Rage,b.Fear,confidence));
            }
        }

        private static void StopRetreat(MonsterAI ai, Behavior b)
        {
            SetRegroup(b, null);
            if (b.Lease != null)
            {
                RetreatLease lease=b.Lease; b.Lease=null;
                lease.Cancel();
            }
            ai.m_fleeIfLowHealth = b.FleeThreshold;
            ai.m_fleeTimeSinceHurt = b.FleeTime;
        }

        internal void CompleteRetreat(MonsterAI ai)
        {
            if (ai == null || !Brains.TryGetValue(ai,out Behavior b)) return;
            SetRegroup(b, null);
            // Restore even if ownership moved away, or the flee values stick if it comes back.
            ai.m_fleeIfLowHealth = b.FleeThreshold;
            ai.m_fleeTimeSinceHurt = b.FleeTime;
            if (b.View != null && b.View.IsValid() && b.View.IsOwner())
            {
                if (_verbose.Value)
                    Logger.LogInfo("[WEV behavior] " + b.View.GetZDO().m_uid + " Retreat->Vanilla reason=retreat-expired");
            }
            b.Lease = null;
            b.Action = Posture.Vanilla;
            b.NextDecisionAt = 0f;
        }

        private void ApplyPosture(MonsterAI ai, Behavior b)
        {
            if (b.Action == Posture.Retreat) return; // vanilla Flee() happens before combat
            if (b.Action == Posture.Commit)
            {
                // Avoid circle phases, but NEVER bypass animation/weapon restrictions.
                ai.m_circleTargetDuration *= .12f;
                ai.m_circleTargetInterval *= 2.2f;
            }
            else if (b.Action == Posture.Probe)
            {
                // Try to reposition instead of attacking immediately.
                ai.m_circleTargetInterval = Mathf.Max(.35f,ai.m_circleTargetInterval * .46f);
                ai.m_circleTargetDuration = Mathf.Max(.6f,ai.m_circleTargetDuration * .66f);
            }
            else if (b.Action == Posture.Hesitate)
            {
                ai.m_circleTargetInterval = Mathf.Max(.45f,ai.m_circleTargetInterval * .8f);
                ai.m_circleTargetDuration = Mathf.Max(.45f,ai.m_circleTargetDuration * .75f);
                // In Valheim many enemies have 0 vanilla minAttackInterval.
                // A hesitation can still add a SHORT minimum cooldown but it
                // will NOT rewrite weapon-specific cooldown or attack animations.
                ai.m_minAttackInterval = Mathf.Max(ai.m_minAttackInterval,
                    .35f + .5f*(1f-Mathf.Clamp01(b.Confidence)));
            }
        }

        // Called when a player ACTUALLY loses HP to a given attacker.
        private void RegisterPlayerHit(MonsterAI ai, float damage)
        {
            if (!Active || ai == null || damage <= 0f) return;
            Behavior b = Brain(ai);
            if (b.View == null || !b.View.IsValid() || !b.View.IsOwner()) return;
            float now=Time.time;
            b.Decay(now);
            float fraction=Mathf.Clamp01(damage / 100f); // bounded, unaffected by target's max HP.
            float before=b.Confidence;
            b.Confidence=Mathf.Clamp01(b.Confidence + .14f + .32f*fraction);
            b.Fear*=.82f; b.Rage=Mathf.Clamp01(b.Rage+.08f);
            b.NextDecisionAt=0f;
            b.DecisionReason="successful-player-hit";
            b.LastPlayerHit=now;
            if (_verbose.Value)
                Logger.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[WEV reaction] id={0} causedPlayerDamage={1:F1} confidence={2:F2}->{3:F2} rage={4:F2} fear={5:F2}",
                    b.View.GetZDO().m_uid,damage,before,b.Confidence,b.Rage,b.Fear));
            Refresh(ai,Target(ai),0f);
        }

        // Register on the spawn event; delivers actual damage to the AI owner.
        private void RegisterHitRpc(MonsterAI ai, ZNetView view)
        {
            if (ai == null || view == null || !view.IsValid()) return;
            view.Register<float>("WEV_PlayerHit_v1",
                (sender,damage) => RegisterPlayerHit(ai, Mathf.Clamp(damage,0f,10000f)));
        }

        [HarmonyPatch(typeof(Character),"RPC_Damage")]
        private static class PlayerDamageConfidence
        {
            private static void Prefix(Character __instance, out float __state)
            {
                __state = __instance is Player && __instance.GetComponent<ZNetView>() is ZNetView v &&
                    v.IsValid() && v.IsOwner() ? __instance.GetHealth() : -1f;
            }
            private static void Postfix(Character __instance, HitData hit, float __state)
            {
                if (Instance == null || !Instance.Active || __state < 0f ||
                    !(__instance is Player) || hit == null) return;
                float actual = __state - __instance.GetHealth();
                if (actual <= .01f) return;
                Character attacker=hit.GetAttacker();
                if (attacker == null || !(attacker.GetBaseAI() is MonsterAI)) return;
                ZNetView v=attacker.GetComponent<ZNetView>();
                if (v != null && v.IsValid() && !attacker.IsTamed())
                    v.InvokeRPC("WEV_PlayerHit_v1",actual);
            }
        }
    }
    // Unity-loadable top-level MonoBehaviour; only a one-shot Invoke callback.
    public sealed class RetreatLease : MonoBehaviour
    {
        internal MonsterAI AI;
        internal void Begin(float duration) => Invoke(nameof(Expire), duration);
        internal void Cancel() { CancelInvoke(nameof(Expire)); Destroy(this); }
        private void Expire()
        {
            EnemyVariationPlugin.Instance?.CompleteRetreat(AI);
            Destroy(this);
        }
    }
}
