using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Movement: how it gets where it's going. Straight at you when close, a charger's run-up, back
    // round to your front, flyers weaving in front of you (the maneuvers themselves: Maneuver.cs).
    public sealed partial class WiresAIPlugin
    {
        // From vanilla's MoveTo while it chases (Maneuver.cs): close in on where it is, not where it'll be,
        // unless it's far and getting away. Only while it can sense it (no knowing where it is otherwise).
        private static void Straight(MonsterAI ai, Behavior b, ref Vector3 point)
        {
            Character t = Target(ai);
            if (t == null || SinceSensed(ai) > .5f || b.Action == Posture.Retreat || !(b.Character is Humanoid h)) return;
            Vector3 tp = t.transform.position, me = h.transform.position;
            Vector3 raw = t.GetVelocity(); raw.y = 0f;
            // vanilla's chase point is the target led by its raw speed: anything else (a flee, a search) isn't touched
            float lead = raw.magnitude * InterceptTime(ai) + 1f;
            if ((point - tp).sqrMagnitude > lead * lead) return;
            Vector3 v = Moving(t); v.y = 0f; // what it's really doing, not this frame's twitch
            Vector3 toMe = me - tp; toMe.y = 0f;
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            float reach = w != null ? w.m_shared.m_aiAttackRange : 2f;
            float gap = toMe.magnitude - t.GetRadius() - reach;
            bool away = Vector3.Dot(v, toMe) < 0f; // getting away from it
            // coming at it, or half a second off: straight at it; else led by how it's really moving
            // (vanilla leads by this frame's speed, so a nudge sent it the wrong way)
            point = !away || gap < h.m_runSpeed * Tune.StraightWithin ? tp : tp + v * InterceptTime(ai);
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
            if (Roll(p, ++b.Decisions) >= .35f + .6f * Wits(p)) return false;
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
    }
}
