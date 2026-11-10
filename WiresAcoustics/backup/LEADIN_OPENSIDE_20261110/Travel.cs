using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sound takes time to travel, 343 m/s, both ways: a hit 60 m away is heard about 0.17 s after it
    // happens, and a loop stopping out there is heard stopping that much later too.
    //  - starting: every placed sound is held back by its distance (Unity's PlayDelayed: the clip
    //    starts late, nothing is buffered, no cost while waiting); under 1 ms isn't;
    //  - stopping: the game's stop and fade-out of a placed sound are held back the same way (run
    //    from Room's tick when due). A sound whose object is destroyed outright still stops at once.
    // No cap: a sound 300 m away is heard about 0.9 s late, as it would be. Game cues start and stop at once.
    internal static class Travel
    {
        internal const float Speed = 343f;
        private struct Pending { internal ZSFX Sfx; internal float At; internal bool Fade; }
        private static readonly List<Pending> Stops = new List<Pending>(16);
        private static bool s_running; // running a held-back stop: let it through
        private static int s_delayed, s_acceptedStarts, s_cancelledOldStops;
        private static int s_tracedFootsteps;
        // Source-level event diagnostics, NOT a sound-processing alteration.
        // Valheim picks the clip/pitch just before this accepted-play hook;
        // logging their actual identities can isolate a bad original sample.
        private static void TraceFootstep(AudioSource src, ZSFX z)
        {
            if (!Plugin.DebugLog.Value || s_tracedFootsteps >= 32 || src.clip == null) return;
            string clip = src.clip.name;
            if (clip.IndexOf("Footstep_Wood", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                clip.IndexOf("Footstep_Stone", System.StringComparison.OrdinalIgnoreCase) < 0) return;
            s_tracedFootsteps++;
            Plugin.Log.LogInfo("[footstep-clip] " + z.name + " / " + clip +
                " pitch=" + src.pitch.ToString("F3") +
                " duration=" + src.clip.length.ToString("F3") +
                " volume=" + src.volume.ToString("F2") +
                " at=" + Time.time.ToString("F2") + "s");
        }

        private static float DelayOf(ZSFX z, AudioSource src)
        {
            if (!Plugin.Enabled.Value || !Plugin.RealDistance.Value || src == null || src.spatialBlend < .5f) return 0f;
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            if (f != null && f.Cue) return 0f;
            AudioListener l = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (l == null) return 0f;
            float dist = (z.transform.position - l.transform.position).magnitude;
            return dist < .35f ? 0f : dist / Speed;
        }

        // Vanilla ZSFX.Play invokes AudioSource.Play ONLY after its own
        // concurrency gate and clip selection succeed. Replace that one
        // accepted-play call, not the entire method or a broad postfix.
        // This prevents rejected play requests from restarting an old sound.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class Start
        {
            private static readonly MethodInfo NativePlay = AccessTools.Method(
                typeof(AudioSource), nameof(AudioSource.Play), System.Type.EmptyTypes);
            private static readonly MethodInfo WrappedPlay = AccessTools.Method(
                typeof(Travel), nameof(PlayWithTravel));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> original)
            {
                var code = new List<CodeInstruction>(original);
                int count = 0;
                for (int i = 0; i < code.Count; i++)
                    if (code[i].operand is MethodInfo method && method == NativePlay)
                        count++;
                if (count != 1)
                {
                    Plugin.Log.LogWarning("[travel] Expected exactly one accepted " +
                        "AudioSource.Play in ZSFX.Play; found " + count +
                        ". Leaving vanilla playback untouched.");
                    return code;
                }
                for (int i = 0; i < code.Count; i++)
                {
                    CodeInstruction ins = code[i];
                    if (!(ins.operand is MethodInfo method) || method != NativePlay)
                        continue;
                    var loadSelf = new CodeInstruction(OpCodes.Ldarg_0);
                    loadSelf.labels.AddRange(ins.labels);
                    loadSelf.blocks.AddRange(ins.blocks);
                    ins.labels.Clear();
                    ins.blocks.Clear();
                    code.Insert(i, loadSelf);
                    ins.opcode = OpCodes.Call;
                    ins.operand = WrappedPlay;
                    break;
                }
                return code;
            }
        }

        private static void PlayWithTravel(AudioSource src, ZSFX z)
        {
            if (src != null && AcousticFilter.StartsMidWave(src.clip) && z.GetComponent<AcousticFilter>() is AcousticFilter fade)
                fade.ArmStartFade();
            // A NEW accepted play supersedes any stop still in flight from
            // a prior clip on this same component.
            s_acceptedStarts++;
            for (int i = Stops.Count - 1; i >= 0; i--)
                if (Stops[i].Sfx == z)
                {
                    Stops.RemoveAt(i);
                    s_cancelledOldStops++;
                }
            TraceFootstep(src, z);
            float delay = DelayOf(z, src);
            if (delay > .001f)
            {
                src.PlayDelayed(delay); // do not start, Stop, and restart
                s_delayed++;
            }
            else src.Play();
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Stop))]
        private static class Stop
        {
            private static bool Prefix(ZSFX __instance, AudioSource ___m_audioSource) => Hold(__instance, ___m_audioSource, false);
        }

        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.FadeOut))]
        private static class Fade
        {
            private static bool Prefix(ZSFX __instance, AudioSource ___m_audioSource) => Hold(__instance, ___m_audioSource, true);
        }

        // false: held back (runs later)
        private static bool Hold(ZSFX z, AudioSource src, bool fade)
        {
            if (s_running || src == null || !src.isPlaying) return true;
            // ZSFX has already set its volume to zero when a timed fade completes.
            // Deferring that Stop leaves isPlaying true, so ZSFX.CustomUpdate calls
            // Stop again every frame until the queued stop finally runs.
            // Once silent, the physical propagation delay is no longer audible.
            if (!fade && src.volume <= .0001f)
            {
                // Cancel any deferred event for this now-finished sound so a
                // pooled/reused source cannot inherit an obsolete stop later.
                for (int i = Stops.Count - 1; i >= 0; i--)
                    if (Stops[i].Sfx == z) Stops.RemoveAt(i);
                return true;
            }
            // A delayed FadeOut/Stop must be queued only once for a given source.
            // Repeated calls cannot extend or duplicate the event.
            for (int i = 0; i < Stops.Count; i++)
                if (Stops[i].Sfx == z)
                {
                    // An explicit Stop supersedes an already queued FadeOut, but
                    // retains the earliest scheduled propagation time.
                    if (!fade && Stops[i].Fade)
                    {
                        Pending pending = Stops[i];
                        pending.Fade = false;
                        Stops[i] = pending;
                    }
                    return false;
                }
            float d = DelayOf(z, src);
            if (d < .01f) return true;
            Stops.Add(new Pending { Sfx = z, At = Time.time + d, Fade = fade });
            return false;
        }

        // From Room's tick: held-back stops that are due.
        internal static void Step(float now)
        {
            for (int i = Stops.Count - 1; i >= 0; i--)
            {
                Pending p = Stops[i];
                if (p.Sfx != null && now < p.At) continue;
                Stops.RemoveAt(i);
                if (p.Sfx == null) continue;
                s_running = true;
                try { if (p.Fade) p.Sfx.FadeOut(); else p.Sfx.Stop(); }
                finally { s_running = false; }
            }
        }

        internal static void Clear()
        {
            s_running = true;
            try { foreach (Pending p in Stops) if (p.Sfx != null) { if (p.Fade) p.Sfx.FadeOut(); else p.Sfx.Stop(); } }
            finally { s_running = false; }
            Stops.Clear();
        }

        internal static string Summary()
        {
            string s = "accepted starts " + s_acceptedStarts +
                ", delayed by distance " + s_delayed +
                ", stale stops cancelled " + s_cancelledOldStops +
                " (stops waiting " + Stops.Count + ")";
            s_delayed = s_acceptedStarts = s_cancelledOldStops = 0;
            return s;
        }
    }
}
