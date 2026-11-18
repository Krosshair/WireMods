using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // Perception: what a creature knows (Tuning.cs has the stages). How often it looks for a target,
    // by how close the nearest player is; noticing anything right by it whichever way it faces; its
    // own sight; how it reads your movement (what you keep doing, not every twitch); when its attacks
    // hit (measured, or read from its animations); where you'll be facing; its small misjudgements;
    // where an unseen shot came from. Everything later works from what's known here.
    public sealed partial class WiresAIPlugin
    {
        // Nearest player within: seconds between looks. The distances and times are picked, not from a source.
        private static float LookEvery(float dist) =>
            dist < Tune.LookNear ? Tune.LookNearEvery : dist < Tune.LookMid ? Tune.LookMidEvery : dist < Tune.LookFar ? Tune.LookFarEvery : Tune.LookAwayEvery;

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
                float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Tune.ReadTime);
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
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), typeof(Character), typeof(bool))]
        private static class Feel
        {
            private static void Postfix(BaseAI __instance, Character target, ref bool __result)
            {
                if (__result || target == null || Instance == null || !Instance.Active || Instance._behaviour.Value <= 0f ||
                    !(__instance is MonsterAI) || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs)) ||
                    (target is Player p && (p.InGhostMode() || p.InDebugFlyMode()))) return;
                if ((target.transform.position - __instance.transform.position).sqrMagnitude < Tune.Feel * Tune.Feel) __result = true;
            }
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

        private static bool ReallyClever(Individual p) => p.Tactics > Tune.ReallyClever;

        // ---- Sight: its own view distance and angle, set once ----
        private static void ApplySenses(Character c, Physical ph)
        {
            if (ph.Individual == null || ph.Individual.Fixed || !(c.GetBaseAI() is BaseAI ai)) return;
            if (ph.OriginalView <= 0f) { ph.OriginalView = ai.m_viewRange; ph.OriginalAngle = ai.m_viewAngle; }
            ai.m_viewRange = ph.OriginalView * ph.Individual.Sight;
            ai.m_viewAngle = Mathf.Min(180f, ph.OriginalAngle * ph.Individual.Sight);
        }

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
                float off = 30f * (1f - Wits(p)) * (2f * Roll(p, ++b.Decisions) - 1f);
                from = me + Quaternion.Euler(0f, off, 0f) * dir.normalized * (.5f * dist);
            }
            LastKnown(ai) = from;
            BeenAtLast(ai) = false;
            b.Searching = true; // it has its lead; no spreading on top
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
            b.JudgeErr = g * Mathf.Lerp(Tune.JudgeDim, Tune.JudgeClever, Wits(p));
        }
    }
}
