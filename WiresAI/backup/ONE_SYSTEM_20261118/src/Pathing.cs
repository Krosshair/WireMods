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

        // ---- How a target is moving, as a creature reads it ----
        // Not every twitch: what it has kept doing over the last fraction of a second (an average that
        // forgets over ReadTime), and only as much as it has been going one way (the average velocity
        // against the average speed: tapping left and right averages out to nothing, so it's read as
        // standing still, not as about to go either way). Used for every lead and prediction.
        // Players only (their input can twitch); creatures move as their AI steers them.
        private const float ReadTime = .4f; // picked: long enough to ignore a tap, short enough to see a real turn
        private sealed class Track { internal Vector3 V; internal float Speed, At = -1f; internal int Frame = -1; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Character, Track> Tracks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Character, Track>();

        private static Vector3 Moving(Character c)
        {
            if (c == null) return Vector3.zero;
            Vector3 raw = c.GetVelocity();
            if (!(c is Player)) return raw;
            Track tr = Tracks.GetOrCreateValue(c);
            int frame = Time.frameCount;
            if (tr.Frame != frame)
            {
                float now = Time.time, dt = now - tr.At;
                // not read lately: start from standing, as if just noticed
                if (tr.At < 0f || dt > .5f) { tr.V = Vector3.zero; tr.Speed = 0f; dt = Time.deltaTime; }
                float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / ReadTime);
                Vector3 flat = new Vector3(raw.x, 0f, raw.z);
                tr.V = Vector3.Lerp(tr.V, flat, k);
                tr.Speed = Mathf.Lerp(tr.Speed, flat.magnitude, k);
                tr.At = now; tr.Frame = frame;
            }
            float steady = tr.Speed > .01f ? Mathf.Clamp01(tr.V.magnitude / tr.Speed) : 0f;
            Vector3 read = tr.V * (steady * steady);
            read.y = raw.y;
            return read;
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
            point = !away || gap < h.m_runSpeed * .5f ? tp : tp + v * InterceptTime(ai);
        }

        // When an attack hits, before it has ever been seen to (Pacing's swing timing). Measured from
        // the game's own hit event once it has hit; until then, read from the creature's animations:
        // the hit events ("Hit", "OnAttackTrigger") in its clips, the middle one, once per kind.
        private static readonly System.Collections.Generic.Dictionary<string, float> KindHit =
            new System.Collections.Generic.Dictionary<string, float>();
        private static readonly System.Collections.Generic.List<float> s_times = new System.Collections.Generic.List<float>(16);

        private static bool HitTime(Behavior b, ItemDrop.ItemData.SharedData sh, out float t)
        {
            if (HitDelay.TryGetValue(sh, out t)) return true;
            string kind = b.Kind ?? "";
            if (!KindHit.TryGetValue(kind, out t)) KindHit[kind] = t = FromClips(b.Character);
            return t > 0f;
        }

        private static float FromClips(Character c)
        {
            Animator a = c != null ? c.GetComponentInChildren<Animator>() : null;
            RuntimeAnimatorController rc = a != null ? a.runtimeAnimatorController : null;
            if (rc == null) return 0f;
            s_times.Clear();
            foreach (AnimationClip clip in rc.animationClips)
            {
                if (clip == null) continue;
                foreach (AnimationEvent e in clip.events)
                    if (e.functionName == "Hit" || e.functionName == "OnAttackTrigger") { s_times.Add(e.time); break; }
            }
            if (s_times.Count == 0) return 0f;
            s_times.Sort();
            return s_times[s_times.Count / 2];
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
