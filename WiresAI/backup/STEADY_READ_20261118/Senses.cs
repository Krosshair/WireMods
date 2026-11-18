using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Senses and finding you: each one sees a little better or worse than its kind; a lost target
    // is searched for in a spread, not by all walking the same line; no path tries a way round
    // first; high ground is waited out (the clever ones out of sight); a shot from out of view is
    // only worked out by the really clever. Target scans and hits only.
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

        private static bool ReallyClever(Individual p) => p.Tactics > .5f;

        // ---- Sight: its own view distance and angle, set once ----
        private static void ApplySenses(Character c, Physical ph)
        {
            if (ph.Individual == null || ph.Individual.Fixed || !(c.GetBaseAI() is BaseAI ai)) return;
            if (ph.OriginalView <= 0f) { ph.OriginalView = ai.m_viewRange; ph.OriginalAngle = ai.m_viewAngle; }
            ai.m_viewRange = ph.OriginalView * ph.Individual.Sight;
            ai.m_viewAngle = Mathf.Min(180f, ph.OriginalAngle * ph.Individual.Sight);
        }

        // ---- A lost target: each goes to its own part of where it was, the clever ones where it was heading ----
        private static void Search(MonsterAI ai, Behavior b, Individual p, Character target)
        {
            if (SinceSensed(ai) < 1f)
            {
                b.Searching = false;
                b.SearchSteps = 0;
                b.LastSeenVel = target.GetVelocity();
                return;
            }
            // Got where it last knew of it and still nothing: on the way it was going, a step at a
            // time (vanilla wanders round at random there), a few steps, then vanilla's wander.
            if (b.Searching)
            {
                if (!BeenAtLast(ai) || b.SearchSteps >= 3) return;
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
                StartManeuver(ai, b, p, Move.Sidestep, target, to, 4f, "hide");
                b.NoPathScans = 0;
                return true;
            }
            return false;
        }
        private static bool ReallyCleverOrRoll(Individual p, Behavior b) => ReallyClever(p) || Roll(p, ++b.Decisions) < .5f * SmartChance(p);

        // ---- Shot from out of sight: vanilla knows exactly where from. Only the really clever do here;
        // the rest only know roughly which way it came from, not how far ----
        private static void ShotFromHiding(MonsterAI ai, Character attacker, bool hadTarget)
        {
            // vanilla only points it at the shooter when it had no target; the dim ones keep that
            if (hadTarget && (!Phys.TryGetValue(ai.GetComponent<Character>(), out Physical pre) ||
                pre.Individual == null || !ReallyClever(pre.Individual))) return;
            if (attacker == null || Instance == null || !Instance.Active || !Brains.TryGetValue(ai, out Behavior b) ||
                b.View == null || !b.View.IsOwner() || !Phys.TryGetValue(b.Character, out Physical ph) ||
                ph.Individual == null || Target(ai) != attacker || ai.CanSeeTarget(attacker)) return;
            Individual p = ph.Individual;
            Vector3 me = b.Character.transform.position, from = attacker.transform.position;
            if (!ReallyClever(p))
            {
                Vector3 dir = from - me; float dist = dir.magnitude; dir.y = 0f;
                if (dist < .1f) return;
                // off by up to 30 degrees for the dimmest, and half way: it can't judge depth
                float off = 30f * (1f - Mathf.Clamp01((p.Tactics + 1f) * .5f)) * (2f * Roll(p, ++b.Decisions) - 1f);
                from = me + Quaternion.Euler(0f, off, 0f) * dir.normalized * (.5f * dist);
            }
            LastKnown(ai) = from;
            BeenAtLast(ai) = false;
            b.Searching = true; // it has its lead; no spreading on top
        }
    }
}
