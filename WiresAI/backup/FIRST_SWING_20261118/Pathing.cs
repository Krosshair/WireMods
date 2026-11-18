using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Noticing and chasing. Vanilla looks for a target every 2 s with a player within 50 m, else every
    // 6 s; here by how close the nearest player is: twice a second up close, slower than vanilla far
    // off, so it evens out. Anything right next to a creature is noticed whatever way it faces. A
    // creature closing in on what it can sense goes straight at it (vanilla aims where the target
    // will be, and overran it up close). One running that hasn't got any closer since its last look
    // tries a way round.
    public sealed partial class WiresAIPlugin
    {
        // Nearest player within: seconds between looks. The distances and times are picked, not from a source.
        private static float LookEvery(float dist) => dist < 10f ? .5f : dist < 25f ? 1f : dist < 50f ? 3f : 8f;

        private static void SetLookTimer(MonsterAI ai)
        {
            Player near = Player.GetClosestPlayer(ai.transform.position, 60f);
            float d = near != null ? Vector3.Distance(near.transform.position, ai.transform.position) : 999f;
            UpdateTargetTimer(ai) = LookEvery(d);
        }

        // Right next to it: noticed whatever way it's facing (heard, felt).
        private const float FeelRange = 3f;
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), typeof(Character), typeof(bool))]
        private static class Feel
        {
            private static void Postfix(BaseAI __instance, Character target, ref bool __result)
            {
                if (__result || target == null || Instance == null || !Instance.Active || Instance._behaviour.Value <= 0f ||
                    !(__instance is MonsterAI) || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs)) ||
                    (target is Player p && (p.InGhostMode() || p.InDebugFlyMode()))) return;
                if ((target.transform.position - __instance.transform.position).sqrMagnitude < FeelRange * FeelRange) __result = true;
            }
        }

        // From vanilla's MoveTo while it chases (Maneuver.cs): close in on where it is, not where it'll be,
        // unless it's far and getting away. Only while it can sense it (no knowing where it is otherwise).
        private static void Straight(MonsterAI ai, Behavior b, ref Vector3 point)
        {
            Character t = Target(ai);
            if (t == null || SinceSensed(ai) > .5f || b.Action == Posture.Retreat || !(b.Character is Humanoid h)) return;
            Vector3 tp = t.transform.position, me = h.transform.position;
            Vector3 v = t.GetVelocity(); v.y = 0f;
            // vanilla's chase point is the target led by its speed: anything else (a flee, a search) isn't touched
            if ((point - tp).sqrMagnitude > (v.magnitude * InterceptTime(ai) + 1f) * (v.magnitude * InterceptTime(ai) + 1f)) return;
            Vector3 toMe = me - tp; toMe.y = 0f;
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            float reach = w != null ? w.m_shared.m_aiAttackRange : 2f;
            float gap = toMe.magnitude - t.GetRadius() - reach;
            bool away = Vector3.Dot(v, toMe) < 0f; // getting away from it
            if (!away || gap < h.m_runSpeed * .5f) point = tp; // coming at it, or half a second off: straight at it
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
                float expect = .25f * self.m_runSpeed * dt;
                if ((me - b.LastLookPos).magnitude < expect && b.LastLookGap - gap < expect) TryDetour(ai, b, p, target);
            }
            b.LastLookAt = now; b.LastLookPos = me; b.LastLookGap = gap;
        }
    }
}
