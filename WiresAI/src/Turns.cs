using UnityEngine;

namespace WiresAI
{
    // The pack's turns at one target (Doom 2016's attack tokens). Only so many of a pack attack the
    // same target at once: Tune.TurnsBase, one more for an aggressive pack, one fewer for a clever
    // one, at least one. The rest hold just out of reach and step in the moment one finishes. A dim
    // one, an enraged one, a lone one or a boss doesn't wait for anyone.
    // A turn is held while its attack runs (InAttack); checked when it would attack (DoAttack, the
    // ready-attack check), at most every quarter second per creature.
    public sealed partial class WiresAIPlugin
    {
        private static bool HasTurn(MonsterAI ai, Behavior b)
        {
            float now = Time.time;
            if (now < b.TurnCheckedAt + .25f) return b.TurnOk;
            b.TurnCheckedAt = now;
            b.TurnOk = true;
            Character self = b.Character, t = Target(ai);
            if (t == null || self == null || self.IsBoss() || b.Mates <= 0 || b.Rage > Tune.TurnsRage ||
                !Phys.TryGetValue(self, out Physical ph) || ph.Individual == null || KeepsCircling(ph.Individual)) return true;
            Individual p = ph.Individual;
            long pack = GroupOf(self);
            int on = 0;
            Scene scene = Observe(t);
            for (int i = 0; i < scene.Actors.Count; i++)
            {
                Character c = scene.Actors[i];
                if (c == null || c == self || !c.InAttack() || c.IsDead() || !(c.GetBaseAI() is MonsterAI oa) ||
                    Target(oa) != t || GroupOf(c) != pack) continue;
                on++;
            }
            int limit = Mathf.Max(1, Tune.TurnsBase + (GroupAggression(p, b) > Tune.TurnsAggressive ? 1 : 0) - (Wits(p) > Tune.TurnsClever ? 1 : 0) - (Nerve(p, b) < Tune.TurnsTimid ? 1 : 0)); // a timid one hangs back more
            b.TurnOk = on < limit;
            return b.TurnOk;
        }

        // No turn: hold just out of reach, facing in, till one comes free (the maneuver gives way then).
        private void WaitTurn(MonsterAI ai, Behavior b, Individual p)
        {
            if (b.Maneuver != Move.None || !(b.Character is Humanoid h)) return;
            Character t = Target(ai);
            if (t == null) return;
            Vector3 from = h.transform.position - t.transform.position; from.y = 0f;
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            float reach = w != null ? w.m_shared.m_aiAttackRange : 2f;
            b.HoldDir = from.sqrMagnitude > 1e-4f ? from.normalized : -t.transform.forward;
            b.HoldDist = t.GetRadius() + reach + Tune.TurnsMargin;
            StartManeuver(ai, b, p, Move.Hold, t, -from, Tune.TacticsEvery, "waiting-turn");
        }
    }
}
