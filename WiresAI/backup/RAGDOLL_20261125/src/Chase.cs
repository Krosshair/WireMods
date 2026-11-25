using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Decisions while chasing, on each look (Tuning.cs has the stages): giving up a chase it can't
    // win, searching where it lost you, a way round when there's no path or it's stuck, waiting out
    // of sight when you're up out of reach.
    public sealed partial class WiresAIPlugin
    {
        private static readonly AccessTools.FieldRef<MonsterAI, Vector3> LastKnown =
            AccessTools.FieldRefAccess<MonsterAI, Vector3>("m_lastKnownTargetPos");
        private static readonly AccessTools.FieldRef<MonsterAI, bool> BeenAtLast =
            AccessTools.FieldRefAccess<MonsterAI, bool>("m_beenAtLastPos");
        private static readonly AccessTools.FieldRef<int> ViewBlockMask =
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(BaseAI), "m_viewBlockMask"));

        // Same test the vanilla code uses for "can't reach it, it's up there" (Maneuver.cs).
        private const float AboveReach = 1.6f;

        // ---- A lost target: each goes to its own part of where it was, the clever ones where it was heading ----
        private static void Search(MonsterAI ai, Behavior b, Individual p, Character target)
        {
            if (SinceSensed(ai) < 1f)
            {
                b.Searching = false;
                b.SearchSteps = 0;
                b.LastSeenVel = Moving(target);
                return;
            }
            // Got where it last knew of it and still nothing: on the way it was going, a step at a
            // time (vanilla wanders round at random there), a few steps, then vanilla's wander.
            if (b.Searching)
            {
                if (!BeenAtLast(ai) || b.SearchSteps >= Tune.SearchSteps) return;
                Vector3 way = b.LastSeenVel; way.y = 0f;
                if (way.sqrMagnitude < .25f) { way = LastKnown(ai) - b.Character.transform.position; way.y = 0f; }
                if (way.sqrMagnitude < 1e-4f) return;
                LastKnown(ai) += way.normalized * 2f * ai.m_randomMoveRange;
                BeenAtLast(ai) = false;
                b.SearchSteps++;
                return;
            }
            b.Searching = true;
            Vector3 at = LastKnown(ai);
            // clever ones lead it the way vanilla leads a runner it can see
            if (Roll(p, ++b.Decisions) < SmartChance(p)) at += b.LastSeenVel * InterceptTime(ai);
            // a pack spreads out over where it went, each to its own side (fixed per creature, not
            // random each time), but only if it's really gone from there: just round a corner, they
            // go to where it was
            if ((target.transform.position - LastKnown(ai)).sqrMagnitude > 4f * ai.m_randomMoveRange * ai.m_randomMoveRange && b.Mates > 0)
                at += Quaternion.Euler(0f, p.Seed % 360u, 0f) * Vector3.forward * (.5f * ai.m_randomMoveRange);
            LastKnown(ai) = at;
            BeenAtLast(ai) = false;
        }

        // ---- No path, target not above: a way round first (two scans), then it gives up (Pacing.cs) ----
        private bool TryDetour(MonsterAI ai, Behavior b, Individual p, Character target)
        {
            if (b.Maneuver != Move.None || b.Character.InAttack()) return false;
            Vector3 me = b.Character.transform.position;
            Vector3 to = target.transform.position - me; to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return false;
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
            float reach = 2f * ai.m_randomMoveRange; // vanilla's own wander step
            float first = Roll(p, ++b.Decisions) < .5f ? 1f : -1f;
            for (int i = 0; i < 2; i++)
            {
                Vector3 point = me + side * (i == 0 ? first : -first) * reach + to.normalized * (.5f * reach);
                if (!HavePath(ai, point)) continue;
                b.FixedPoint = point;
                StartManeuver(ai, b, p, Move.Sidestep, target, to, reach / Mathf.Max(.5f, b.Character.m_runSpeed), "detour");
                return true;
            }
            return false;
        }

        // ---- Target up high and out of reach: clever melee ones wait where it can't see them ----
        private bool TryHide(MonsterAI ai, Behavior b, Individual p, Character target)
        {
            if (p.Ranged || b.Maneuver != Move.None || b.Character.InAttack() || !ReallyCleverOrRoll(p, b)) return false;
            Vector3 me = b.Character.transform.position;
            Vector3 eye = target.m_eye != null ? target.m_eye.position : target.transform.position + Vector3.up * 1.6f;
            float r = 2f * ai.m_randomMoveRange;
            float start = 360f * Roll(p, ++b.Decisions);
            int mask = ViewBlockMask();
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = me + Quaternion.Euler(0f, start + 45f * i, 0f) * Vector3.forward * r;
                Vector3 head = point + Vector3.up * b.Character.GetHeight() * .8f;
                if (!Physics.Linecast(eye, head, mask) || !HavePath(ai, point)) continue;
                b.FixedPoint = point;
                Vector3 to = target.transform.position - me; to.y = 0f;
                StartManeuver(ai, b, p, Move.Sidestep, target, to, Tune.HideFor, "hide");
                b.NoPathScans = 0;
                return true;
            }
            return false;
        }
        private static bool ReallyCleverOrRoll(Individual p, Behavior b) => ReallyClever(p) || Roll(p, ++b.Decisions) < .5f * SmartChance(p);

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
            if (fight < Tune.MidFight) { b.NoPathScans = 0; return false; } // mid-fight
            float agg = GroupAggression(p, b) + .5f * b.Rage;
            if (agg > Tune.Hateful) return false; // just chases you and hates you
            float share = Mathf.Lerp(Tune.GiveUpShortest, 1f, Mathf.Clamp01((agg + 1f) / 1.5f)) / Mathf.Max(.01f, Instance._behaviour.Value);
            bool give = SinceSensed(ai) > 30f * share || fight > 60f * share;
            if (!give)
            {
                Vector3 tp = target.transform.position;
                // up out of reach is waited out instead (Senses.cs: hiding, holding off)
                if (tp.y - self.transform.position.y > AboveReach || HavePath(ai, tp)) b.NoPathScans = 0;
                else if (++b.NoPathScans >= Tune.NoPathLooks) give = true;
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

        // On each look: running after something it can sense, out of reach, and no closer than last time
        // (and hardly moved): stuck on something, a way round.
        private void StuckCheck(MonsterAI ai, Behavior b, Individual p, Character target, float now)
        {
            Character self = b.Character;
            Vector3 me = self.transform.position;
            float gap = Vector3.Distance(target.transform.position, me) - target.GetRadius();
            float dt = now - b.LastLookAt;
            bool chasing = b.Maneuver == Move.None && !self.InAttack() && SinceSensed(ai) < 1f &&
                (b.Action == Posture.Commit || b.Action == Posture.Vanilla) && gap > 4f;
            if (chasing && b.LastLookAt > 0f && dt > .2f && dt < 10f)
            {
                // a quarter of what it would have covered running: barely moved and no closer
                float expect = Tune.StuckShare * self.m_runSpeed * dt;
                if ((me - b.LastLookPos).magnitude < expect && b.LastLookGap - gap < expect) TryDetour(ai, b, p, target);
            }
            b.LastLookAt = now; b.LastLookPos = me; b.LastLookGap = gap;
        }
    }
}
