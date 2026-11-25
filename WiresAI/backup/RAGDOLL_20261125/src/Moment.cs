using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // The moment (Tuning.cs has the stages): no long pauses. Attacking the moment it reaches you, any
    // ready attack in reach over repositioning or circling, the next attack sooner when it doesn't
    // chain, swinging early as you come into reach, closing in when you back off, feints dropped for
    // an opening, and when an attack hits. Runs where vanilla already does each frame (moving,
    // stopping, circling, attacking), plus attack ends; the pack's turns are Turns.cs.
    public sealed partial class WiresAIPlugin
    {
        private static readonly AccessTools.FieldRef<MonsterAI, float> SinceSensed =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_timeSinceSensedTargetCreature");
        private static readonly AccessTools.FieldRef<MonsterAI, float> SinceAttacking =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_timeSinceAttacking");
        private static readonly AccessTools.FieldRef<BaseAI, float> SinceHurt =
            AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");
        private static readonly AccessTools.FieldRef<MonsterAI, StaticTarget> TargetStatic =
            AccessTools.FieldRefAccess<MonsterAI, StaticTarget>("m_targetStatic");
        private static readonly AccessTools.FieldRef<MonsterAI, float> UpdateTargetTimer =
            AccessTools.FieldRefAccess<MonsterAI, float>("m_updateTargetTimer");
        private static readonly System.Action<BaseAI, bool> SetAlerted =
            AccessTools.MethodDelegate<System.Action<BaseAI, bool>>(AccessTools.Method(typeof(BaseAI), "SetAlerted"));
        private delegate bool MoveToFn(BaseAI ai, float dt, Vector3 point, float dist, bool run);
        private static readonly MoveToFn MoveToCall =
            AccessTools.MethodDelegate<MoveToFn>(AccessTools.Method(typeof(BaseAI), "MoveTo"));

        private static int _arcing;


        // ---- In reach with an attack ready: attacking comes first ----
        // Any of its attacks that reaches the target, is off cooldown, and the creature's own
        // minimum wait has passed: the equipped one first, else another it has. With lead, "ready"
        // counts the time it needs to turn to the target, so it's facing it when the attack is.
        private static ItemDrop.ItemData ReadyAttack(MonsterAI ai, Behavior b, bool lead)
        {
            if (!(b.Character is Humanoid h) || h.InAttack()) return null;
            Character t = Target(ai);
            if (t == null || !HasTurn(ai, b)) return null; // packmates have every turn at it (Turns.cs)
            Vector3 to = t.transform.position - h.transform.position;
            float dist = to.magnitude - t.GetRadius();
            float turn = 0f;
            if (lead) { to.y = 0f; turn = Vector3.Angle(h.transform.forward, to) / Mathf.Max(30f, h.m_turnSpeed); }
            if (h.GetTimeSinceLastAttack() + turn < ai.m_minAttackInterval) return null;
            float now = Time.time;
            ItemDrop.ItemData cur = h.GetCurrentWeapon();
            if (Usable(ai, cur, dist, now + turn)) return cur;
            foreach (ItemDrop.ItemData item in h.GetInventory().GetAllItems())
                if (item != cur && Usable(ai, item, dist, now + turn)) return item;
            return null;
        }

        private static bool Usable(MonsterAI ai, ItemDrop.ItemData item, float dist, float at)
        {
            if (item == null || !item.IsWeapon()) return false;
            var sh = item.m_shared;
            return sh.m_aiTargetType == ItemDrop.ItemData.AiTarget.Enemy && dist < sh.m_aiAttackRange &&
                dist >= sh.m_aiAttackRangeMin && at - item.m_lastAttackTime > sh.m_aiAttackInterval && ai.CanUseAttack(item);
        }

        // ---- Just got into reach: an attack that reaches is ready now (cooldowns waived), no feint ----
        private static void Arrived(MonsterAI ai, Behavior b)
        {
            if (Instance == null || !Instance.Active || Instance._behaviour.Value <= 0f || b.View == null || !b.View.IsOwner() ||
                !(b.Character is Humanoid h) || h.InAttack() || b.Action == Posture.Retreat) return;
            Character t = Target(ai);
            if (t == null) return;
            float dist = Vector3.Distance(t.transform.position, h.transform.position) - t.GetRadius();
            float now = Time.time;
            ItemDrop.ItemData pick = h.GetCurrentWeapon();
            // as if every cooldown had passed: the one in hand if it reaches, else one that does
            if (!Usable(ai, pick, dist, float.MaxValue))
            {
                pick = null;
                foreach (ItemDrop.ItemData item in h.GetInventory().GetAllItems())
                    if (Usable(ai, item, dist, float.MaxValue)) { pick = item; break; }
            }
            if (pick == null) return;
            pick.m_lastAttackTime = now - pick.m_shared.m_aiAttackInterval - .05f;
            SinceLastAttack(h) = Mathf.Max(SinceLastAttack(h), ai.m_minAttackInterval);
            Ready(ai, b, pick);
            b.HoldUntil = 0f;
            b.DelayDecided = true; // no feint on this one
        }

        // Hold that attack ready in hand (vanilla swaps weapons on its own timer otherwise).
        private static void Ready(MonsterAI ai, Behavior b, ItemDrop.ItemData item)
        {
            if (b.Character is Humanoid h && h.GetCurrentWeapon() != item) { h.EquipItem(item, false); WeaponTimer(ai) = 1f; }
        }

        // ---- Attack ended without chaining: the next one comes sooner ----
        private static void Hurry(MonsterAI ai, Behavior b, Individual p, ItemDrop.ItemData done)
        {
            if (done == null || KeepsCircling(p)) return; // vanilla rhythm for the dim ones
            float pace = Pressure(p, b) * Instance._behaviour.Value;
            float wait = done.m_shared.m_aiAttackInterval;
            // vanilla waits the whole interval; here everyone a good part less, a pressing one as little as a tenth of it
            done.m_lastAttackTime -= wait * (Tune.HurryBase + Tune.HurryPressure * Mathf.Clamp01(pace));
        }

        private delegate bool DoAttackFn(MonsterAI ai, Character target, bool isFriend);
        private static readonly DoAttackFn DoAttackCall =
            AccessTools.MethodDelegate<DoAttackFn>(AccessTools.Method(typeof(MonsterAI), "DoAttack"));

        // Still running at it, attack ready: swing early when it reckons the target will be in reach
        // by the time the hit lands (both closing in). Its reckoning can be off: sometimes it swings
        // too early and whiffs, sometimes too late. From vanilla's own MoveTo while chasing.
        private static void Anticipate(MonsterAI ai, Behavior b)
        {
            if (Instance == null || !Instance.Active || Instance._behaviour.Value <= 0f || b.View == null ||
                !(b.Character is Humanoid h) || h.InAttack() || b.Action == Posture.Retreat) return;
            // cheapest rejections first: this runs each frame a creature chases something
            Character t = Target(ai);
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            if (t == null || w == null) return;
            var sh = w.m_shared;
            float now = Time.time;
            if (now - w.m_lastAttackTime <= sh.m_aiAttackInterval || h.GetTimeSinceLastAttack() < ai.m_minAttackInterval) return;
            Vector3 me = h.transform.position, tp = t.transform.position;
            Vector3 to = tp - me; to.y = 0f;
            float gap = to.magnitude - t.GetRadius() - sh.m_aiAttackRange;
            if (gap <= 0f || !HitTime(b, sh, out float hit)) return; // in reach already (vanilla's)
            // once it swings it only creeps on: the game slows an attacking creature to its attack's
            // speed factor (0.2 of its speed unless the attack says otherwise) until the hit
            float creep = h.m_runSpeed * (sh.m_attack != null ? sh.m_attack.m_speedFactor : 0f) * hit;
            if (gap > creep + Mathf.Max(0f, Vector3.Dot(Moving(t), -to.normalized)) * hit + .5f) return; // can't close that by the hit
            if (!b.View.IsOwner() || !Phys.TryGetValue(h, out Physical ph) || ph.Individual == null) return;
            Vector3 v = Moving(t); v.y = 0f;
            Vector3 meThen = me + to.normalized * creep, tThen = tp + v * hit;
            float excess = Vector3.Distance(tThen, meThen) - t.GetRadius() - sh.m_aiAttackRange + b.JudgeErr * sh.m_aiAttackRange;
            if (excess > 0f) return;
            // and facing it by then: it keeps turning through the wind-up, at the slowed rate the
            // game gives attacks (m_speedFactorRotation, until the hit), so it only needs to be able
            // to get there. Misjudged like the rest.
            Vector3 aim = tThen - me; aim.y = 0f;
            Vector3 fwd = h.transform.forward; fwd.y = 0f;
            float off = Vector3.Angle(fwd, aim);
            if (sh.m_aiInvertAngleCheck) off = 180f - off;
            float turn = h.m_runTurnSpeed * (sh.m_attack != null ? sh.m_attack.m_speedFactorRotation : 0f) * hit;
            if (off - turn + b.JudgeErr * sh.m_aiAttackMaxAngle > sh.m_aiAttackMaxAngle) return;
            NewJudgement(b, ph.Individual);
            DoAttackCall(ai, t, false);
        }

        private static void EndStep(MonsterAI ai)
        {
            if (ChainStates.TryGetValue(ai, out ChainState s) && s.Stepping) { s.Stepping = false; _stepping--; }
        }

        // How long each attack takes from its start to the moment it hits, measured from the game's
        // own hit event (the animation's), the same for every creature using that attack.
        private static readonly System.Collections.Generic.Dictionary<ItemDrop.ItemData.SharedData, float> HitDelay =
            new System.Collections.Generic.Dictionary<ItemDrop.ItemData.SharedData, float>();

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
        private static class HitMoment
        {
            private static void Prefix(Humanoid __instance)
            {
                if (__instance.IsPlayer() || !(__instance.GetBaseAI() is MonsterAI ai) ||
                    !ChainStates.TryGetValue(ai, out ChainState s) || !s.Swinging || s.Swing == null || s.HitTimed) return;
                s.HitTimed = true;
                float d = Time.time - s.SwingStart;
                var sh = s.Swing.m_shared;
                HitDelay[sh] = HitDelay.TryGetValue(sh, out float old) ? Mathf.Lerp(old, d, .3f) : d;
            }
        }

        // Clever ones: if the target will have moved out of reach by the time the hit lands (it's
        // backing off), close in first, then swing. Decided once per attack.
        private static bool CloseInFirst(MonsterAI ai, Behavior b, Individual p, Character target, float now)
        {
            if (target == null || !(b.Character is Humanoid h)) return false;
            if (!b.TimingDecided) { b.TimingDecided = true; b.TimesHits = Roll(p, ++b.Decisions) < SmartChance(p); }
            if (!b.TimesHits) return false;
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            if (w == null || !HitTime(b, w.m_shared, out float t)) return false;
            Vector3 v = Moving(target); v.y = 0f;
            if (v.sqrMagnitude < .25f) return false;
            Vector3 me = h.transform.position, future = target.transform.position + v * t;
            float reach = w.m_shared.m_aiAttackRange;
            float excess = Vector3.Distance(future, me) - target.GetRadius() - reach + b.JudgeErr * reach;
            if (excess <= 0f) return false; // it reckons it'll still be in reach (maybe wrongly)
            ChainState s = ChainStates.GetOrCreateValue(ai);
            Vector3 dir = future - me; dir.y = 0f;
            s.StepDir = dir.normalized;
            s.StepRun = true;
            // just far enough to have it in reach when the hit lands, no further than it would chase
            s.StepUntil = now + Mathf.Min(t, excess / Mathf.Max(.5f, h.m_runSpeed));
            if (!s.Stepping) { s.Stepping = true; _stepping++; }
            b.HoldUntil = s.StepUntil;
            b.HoldIsFeint = false;
            b.TimesHits = false; // once: then it swings
            return true;
        }

        // ---- Vanilla circling: flyers weave instead (above); on the ground (dim ones, wary postures,
        // bosses early on) it gives way to a ready attack in reach ----
        [HarmonyPatch(typeof(BaseAI), "RandomMovementArroundPoint")]
        private static class CircleGivesWay
        {
            private static bool Prefix(BaseAI __instance, float dt, Vector3 point, float distance, bool run)
            {
                if (!(__instance is MonsterAI ai) || !Brains.TryGetValue(ai, out Behavior b) || b.Character == null) return true;
                if (_arcing > 0 && FlyerArc.Weave(__instance, ai, b, dt, point, distance, run)) return false;
                if (b.Maneuver != Move.None || b.Character.IsFlying() ||
                    !(ReadyAttack(ai, b, b.CirclePreTurn) is ItemDrop.ItemData w)) return true;
                Ready(ai, b, w);
                PauseTimer(ai) = 0f; // the circle is over; vanilla attacks next frame
                return false;
            }
        }

    }
}
