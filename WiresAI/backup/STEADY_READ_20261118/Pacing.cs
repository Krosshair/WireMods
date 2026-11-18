using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Pacing: vanilla's long pauses and idle circling are a dim creature's way of fighting. The rest
    // keep the pressure on, by personality and by their pack's: they get their next attack ready
    // sooner, stand facing you instead of loitering around, back off to charge again (chargers),
    // weave in front of you instead of circling (flyers), and give up a chase they can't win
    // (unless they're the hateful kind). Runs on target scans, attack ends and in vanilla's own
    // flyer-circling call; nothing of its own per frame.
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

        // Its own aggression pulled toward its pack's, as far as it sticks with the pack.
        private static float GroupAggression(Individual p, Behavior b)
        {
            if (b.Mates <= 0) return p.Aggression;
            return Mathf.Lerp(p.Aggression, b.PackAggression, .5f * Mathf.Clamp01((p.Cohesion + 1f) * .5f));
        }

        // How hard it keeps the pressure on, 0..1: aggression (with the pack's), confidence and anger.
        private static float Pressure(Individual p, Behavior b) =>
            Mathf.Clamp01(.5f * (GroupAggression(p, b) + 1f) * .6f + .25f * Mathf.Clamp01(b.Confidence) + .15f * b.Rage);

        // Dim ones keep vanilla's circling: the further below average wits, the likelier.
        private static bool KeepsCircling(Individual p) => p.Tactics < 0f && Roll(p, 0x51C1u) < -p.Tactics;

        // ---- In reach with an attack ready: attacking comes first ----
        // Any of its attacks that reaches the target, is off cooldown, and the creature's own
        // minimum wait has passed: the equipped one first, else another it has. With lead, "ready"
        // counts the time it needs to turn to the target, so it's facing it when the attack is.
        private static ItemDrop.ItemData ReadyAttack(MonsterAI ai, Behavior b, bool lead)
        {
            if (!(b.Character is Humanoid h) || h.InAttack()) return null;
            Character t = Target(ai);
            if (t == null) return null;
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
        private const float ArrivalGap = .5f; // not in reach for this long before: it arrived
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
            done.m_lastAttackTime -= wait * (.3f + .6f * Mathf.Clamp01(pace));
        }

        // ---- Chargers: too close to use the attack it needs a run-up for: back off and face it ----
        private static ItemDrop.ItemData RunUpAttack(Humanoid h, float dist)
        {
            foreach (ItemDrop.ItemData item in h.GetInventory().GetAllItems())
            {
                var sh = item.m_shared;
                if (item.IsWeapon() && sh.m_aiAttackRangeMin > 0f && dist < sh.m_aiAttackRangeMin &&
                    sh.m_aiTargetType == ItemDrop.ItemData.AiTarget.Enemy) return item;
            }
            return null;
        }

        private bool TryRunUp(MonsterAI ai, Behavior b, Individual p, Character target, float now)
        {
            if (!(b.Character is Humanoid h) || b.Maneuver != Move.None || b.Action == Posture.Retreat ||
                h.InAttack() || Instance._behaviour.Value <= 0f) return false;
            Vector3 to = target.transform.position - h.transform.position; to.y = 0f;
            float dist = to.magnitude - target.GetRadius();
            ItemDrop.ItemData charge = RunUpAttack(h, dist);
            if (charge == null) return false;
            // the clever ones nearly always, the dim ones now and then
            if (Roll(p, ++b.Decisions) >= .35f + .6f * Mathf.Clamp01((p.Tactics + 1f) * .5f)) return false;
            var sh = charge.m_shared;
            b.HoldDir = to.sqrMagnitude > 1e-4f ? -to.normalized : -h.transform.forward;
            b.HoldDist = .5f * (sh.m_aiAttackRangeMin + sh.m_aiAttackRange) + target.GetRadius(); // the middle of where it works
            // long enough to get there and turn round
            float time = (b.HoldDist - dist) / Mathf.Max(.5f, h.m_runSpeed) + 180f / Mathf.Max(30f, h.m_turnSpeed);
            StartManeuver(ai, b, p, Move.Hold, target, to, time, "run-up");
            return true;
        }

        // ---- Ended up beside or behind its target: back round to the front, not loitering there ----
        private bool TryFront(MonsterAI ai, Behavior b, Individual p, Character target)
        {
            if (b.Maneuver != Move.None || b.Action == Posture.Retreat || b.Character.InAttack() ||
                KeepsCircling(p) || Instance._behaviour.Value <= 0f) return false;
            Vector3 from = b.Character.transform.position - target.transform.position; from.y = 0f;
            Vector3 fwd = target.transform.forward; fwd.y = 0f;
            if (from.sqrMagnitude < 1e-4f || fwd.sqrMagnitude < 1e-4f) return false;
            float angle = Vector3.SignedAngle(fwd, from, Vector3.up);
            if (Mathf.Abs(angle) < FrontHalfArc()) return false;
            if (Roll(p, ++b.Decisions) >= SmartChance(p) + .3f) return false;
            // the near edge of the front, on the side it's on
            b.HoldDir = Quaternion.Euler(0f, Mathf.Sign(angle) * .5f * FrontHalfArc(), 0f) * fwd.normalized;
            b.HoldDist = Mathf.Max(1.5f, from.magnitude);
            float time = Mathf.Abs(angle) * Mathf.Deg2Rad * b.HoldDist / Mathf.Max(.5f, b.Character.m_runSpeed);
            StartManeuver(ai, b, p, Move.Hold, target, -from, time, "to-front");
            return true;
        }

        // Half the width the player sees in front of them: the game camera's horizontal field of view.
        private static float s_frontHalf;
        private static float FrontHalfArc()
        {
            if (s_frontHalf > 0f) return s_frontHalf;
            float vfov = GameCamera.instance != null ? GameCamera.instance.m_fov : 65f;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            s_frontHalf = Mathf.Atan(Mathf.Tan(vfov * .5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            return s_frontHalf;
        }

        // ---- Flyers: weave in front of the target at vanilla's distance, then dive when ready ----
        // Decided on target scans (flyers are otherwise left to vanilla). An exceptionally dim one,
        // two standard deviations below its kind, circles the vanilla way.
        private static void DecideArc(MonsterAI ai, Behavior b)
        {
            bool want = b.Character != null && b.Character.IsFlying() && Instance._behaviour.Value > 0f &&
                Phys.TryGetValue(b.Character, out Physical ph) && ph.Individual != null && ph.Individual.Dimness < 2f;
            if (want == b.Arcing) return;
            b.Arcing = want;
            _arcing += want ? 1 : -1;
        }

        // From CircleGivesWay below (one hook on vanilla's circling for both). True: handled.
        private static class FlyerArc
        {
            internal static bool Weave(BaseAI __instance, MonsterAI ai, Behavior b, float dt, Vector3 point, float distance, bool run)
            {
                if (!b.Arcing || !b.Character.IsFlying() || !Phys.TryGetValue(b.Character, out Physical ph)) return false;
                Character t = Target(ai);
                if (t == null) return false;
                Individual p = ph.Individual;
                Vector3 fwd = t.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                // sideways at its own cruising speed, slowing at the edges, never round the back
                float radius = Mathf.Max(1f, distance);
                // A boss, or a flyer at least half as long as its circling distance (a dragon, not
                // a bird): wide passes, its own length further out, climbing into each turn.
                if (b.BodyLength <= 0f) b.BodyLength = BodyLength(b.Character);
                bool big = b.Character.IsBoss() || b.BodyLength >= .5f * radius;
                // a boss tightens its passes as the fight goes on, down to vanilla's distance
                if (big) radius += b.BodyLength * (b.Character.IsBoss() ? 1f - b.Escalation : 1f);
                float w = b.Character.m_flySlowSpeed / radius;
                float phase = (p.Seed & 0xFFFF) / 65535f * Mathf.PI * 2f;
                float t0 = Time.time;
                float sweep = Mathf.Sin(w * t0 + phase);
                float angle = FrontHalfArc() * sweep;
                Vector3 at = point + Quaternion.Euler(0f, angle, 0f) * fwd.normalized * radius;
                // up and down between vanilla's own heights: the big ones high at the ends of a pass
                // and low through the middle, the small ones drifting on their own, slower than across
                float rise = big ? Mathf.Abs(sweep) : .5f + .5f * Mathf.Sin(.5f * w * t0 + 2f * phase);
                if (b.Character.IsBoss()) rise *= 1f - .5f * b.Escalation; // and flies lower
                at.y = point.y + Mathf.Lerp(__instance.m_flyAltitudeMin, __instance.m_flyAltitudeMax, rise);
                MoveToCall(__instance, dt, at, 0f, run);
                return true;
            }
        }

        // Its longest extent, from its visible body (once per creature).
        private static float BodyLength(Character c)
        {
            float best = .1f;
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue; // not its effects
                Vector3 s = r.bounds.size;
                best = Mathf.Max(best, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }
            return best;
        }

        // ---- Openings and timing ----
        // A good opening: the target is mid-swing, or turned away (it's outside what the target sees).
        // Where it'll be facing when the hit lands: it's turning (tracked while in reach), so the
        // creature reads that on, like a player would, and can get it wrong (JudgeErr).
        private static bool GoodOpening(Behavior b, Character target)
        {
            if (target == null || b.Character == null) return false;
            if (target.InAttack()) return true;
            Vector3 from = b.Character.transform.position - target.transform.position; from.y = 0f;
            Vector3 fwd = target.transform.forward; fwd.y = 0f;
            if (from.sqrMagnitude < 1e-4f || fwd.sqrMagnitude < 1e-4f) return false;
            float t = b.Character is Humanoid h && h.GetCurrentWeapon() is ItemDrop.ItemData w &&
                HitTime(b, w.m_shared, out float d) ? d : 0f;
            float ahead = b.TurnRate * t + b.JudgeErr * FrontHalfArc();
            return Vector3.Angle(Quaternion.Euler(0f, ahead, 0f) * fwd, from) > FrontHalfArc();
        }

        // Tracks how fast its target turns, from the frames it stands in reach (vanilla stops it there every frame).
        private static void WatchTurn(Behavior b, Character target, float now)
        {
            Vector3 fwd = target.transform.forward; fwd.y = 0f;
            float dt = now - b.LastFwdAt;
            if (b.LastFwdAt > 0f && dt > 0f && dt < .25f && fwd.sqrMagnitude > 1e-4f)
                b.TurnRate = Vector3.SignedAngle(b.LastFwd, fwd, Vector3.up) / dt;
            else b.TurnRate = 0f;
            b.LastFwd = fwd; b.LastFwdAt = now;
        }

        // ---- Judgement: a read of the moment that can be wrong, like a player's ----
        // A new guess per attack: an error in reach lengths (and in its read of where you face),
        // a few percent of its reach (3% for the clever to 7% for the dimmest): close calls go either way.
        private static void NewJudgement(Behavior b, Individual p)
        {
            float u1 = Mathf.Max(1e-6f, Roll(p, ++b.Decisions)), u2 = Roll(p, ++b.Decisions);
            float g = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
            b.JudgeErr = g * (.03f + .04f * (1f - Mathf.Clamp01((p.Tactics + 1f) * .5f))); // about 5% of its reach, like a player
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
            if (gap <= 0f || !HitTime(b, sh, out float hit) || gap > h.m_runSpeed * hit + 2f) return; // in reach already (vanilla's), or far off
            if (!b.View.IsOwner() || !Phys.TryGetValue(h, out Physical ph) || ph.Individual == null) return;
            Vector3 v = t.GetVelocity(); v.y = 0f;
            Vector3 meThen = me + to.normalized * h.m_runSpeed * hit, tThen = tp + v * hit;
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
            Vector3 v = target.GetVelocity(); v.y = 0f;
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

        // ---- Chases: give up sooner when it's going nowhere, unless it's the hateful kind ----
        // Vanilla gives up after 30 s without sensing the target, 60 s without a fight, or past its
        // chase distance. A timid or calm one gives up in as little as a third of that, or when it
        // has had no path to the target for three scans in a row; a highly aggressive one keeps
        // vanilla's. Never mid-fight, never raids or hunters, never bosses.
        private bool GivesUp(MonsterAI ai, Behavior b)
        {
            Character self = b.Character, target = Target(ai);
            if (target == null || self == null || self.IsBoss() || self.IsTamed() || ai.IsEventCreature() || ai.HuntPlayer() ||
                b.View == null || !b.View.IsOwner() || !Phys.TryGetValue(self, out Physical ph) || ph.Individual == null)
            { b.NoPathScans = 0; return false; }
            Individual p = ph.Individual;
            float fight = Mathf.Min(SinceAttacking(ai), SinceHurt(ai));
            if (fight < 5f) { b.NoPathScans = 0; return false; } // mid-fight
            float agg = GroupAggression(p, b) + .5f * b.Rage;
            if (agg > .5f) return false; // just chases you and hates you
            float share = Mathf.Lerp(1f / 3f, 1f, Mathf.Clamp01((agg + 1f) / 1.5f)) / Mathf.Max(.01f, Instance._behaviour.Value);
            bool give = SinceSensed(ai) > 30f * share || fight > 60f * share;
            if (!give)
            {
                Vector3 tp = target.transform.position;
                // up out of reach is waited out instead (Senses.cs: hiding, holding off)
                if (tp.y - self.transform.position.y > AboveReach || HavePath(ai, tp)) b.NoPathScans = 0;
                else if (++b.NoPathScans >= 3) give = true;
                else TryDetour(ai, b, p, target); // a way round first
            }
            if (!give) return false;
            // vanilla's own giving up (MonsterAI.UpdateTarget); idle movement walks it home
            SetAlerted(ai, false);
            Target(ai) = null;
            TargetStatic(ai) = null;
            SinceAttacking(ai) = 0f;
            UpdateTargetTimer(ai) = 5f;
            b.NoPathScans = 0;
            Restore(ai, b);
            if (_verbose.Value) Logger.LogInfo("[WAI chase] " + b.View.GetZDO().m_uid + " gave up");
            return true;
        }
    }
}
