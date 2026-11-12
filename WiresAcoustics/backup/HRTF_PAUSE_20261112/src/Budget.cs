using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Which playing sounds have their filter's processing on: every one with something to do
    // (muffling, air, distance, direction), switched off when it stops. The audio thread's time in
    // the filters is measured for the debug log.
    internal static class Budget
    {
        private const float Every = .25f;
        private static readonly List<AcousticFilter> Playing = new List<AcousticFilter>(128);
        private static float s_next, s_load;
        private static int s_on, s_calls, s_hrtfCalls;
        private static long s_stamp;
        private static float s_perCall;

        // A sound playing (Occlusion, Recheck, Voices, Ocean): processed at once, so a new sound
        // behind a wall starts muffled.
        internal static void Note(AcousticFilter f, float dist)
        {
            f.LastDist = dist;
            if (!f.InBudget)
            {
                f.InBudget = true;
                Playing.Add(f);
            }
            if (f.Cue) { f.Active = false; return; }
            if (!f.enabled) Toggle(f, true); // only after the mod was switched off and on
            if (!f.Active) { f.Active = true; s_on++; }
        }

        // Switching a filter component on (Debug log: timed, it can wait for Unity's audio mixer; now rare).
        private static int s_toggles;
        private static long s_toggleTicks, s_toggleWorst;
        private static void Toggle(AcousticFilter f, bool on)
        {
            if (!Plugin.DebugLog.Value) { f.enabled = on; return; }
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            f.enabled = on;
            t = System.Diagnostics.Stopwatch.GetTimestamp() - t;
            s_toggles++; s_toggleTicks += t;
            if (t > s_toggleWorst) s_toggleWorst = t;
        }

        internal static string Toggles()
        {
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            string r = "filters switched " + s_toggles + " times (" + (s_toggleTicks * f).ToString("F3") + " ms total, worst " + (s_toggleWorst * f).ToString("F3") + " ms)";
            s_toggles = 0; s_toggleTicks = 0L; s_toggleWorst = 0L;
            return r;
        }

        // A few times a second, from Room's tick.
        internal static void Step(float now)
        {
            if (now < s_next) return;
            s_next = now + Every;
            AcousticFilter.RefreshOutputFormat(); // device changes: sample-accurate delays

            AcousticFilter.Diagnostics = Plugin.DebugLog.Value;
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            long ticks = AcousticFilter.TakeTicks(out s_calls, out s_hrtfCalls);
            if (s_stamp != 0L && stamp > s_stamp) s_load = (float)ticks / (stamp - s_stamp);
            s_perCall = s_calls > 0 ? (float)(ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / s_calls) : 0f;
            s_stamp = stamp;

            for (int i = Playing.Count - 1; i >= 0; i--)
            {
                AcousticFilter f = Playing[i];
                bool gone = f == null || f.Source == null || !f.Source.isPlaying;
                if (!gone) continue;
                if (!ReferenceEquals(f, null))
                {
                    f.InBudget = false;
                    if (f != null) { Silence(f, false); if (f.Kind != AcousticFilter.Kinds.Ambient) f.Active = false; }
                }
                Playing[i] = Playing[Playing.Count - 1];
                Playing.RemoveAt(Playing.Count - 1);
            }

            s_on = 0;
            Ranked.Clear();
            foreach (AcousticFilter f in Playing)
            {
                // processed if there's anything to do (nothing in the way, no HRTF, near: off, free)
                bool want = !f.Cue && f.NeedsWork;
                f.Active = want;
                if (!want) continue;
                s_on++;
                if (f.Kind == AcousticFilter.Kinds.Ambient || f.Kind == AcousticFilter.Kinds.Outside || f.Voice)
                {
                    Silence(f, false); f.Cheap = false; // the room's own sound and voices: always in full
                    continue;
                }
                f.Heard = Heard(f);
                // too quiet to hear (under -60 dB), back on from -54 dB so it doesn't flicker
                bool mute = f.Mute ? f.Heard < .002f : f.Heard < .001f;
                if (mute != f.Mute) Silence(f, mute);
                if (!f.Mute) Ranked.Add(f);
            }
            // The HRTF, most of the cost, for the loudest few; the rest keep their muffling and
            // distance, with Unity's panning. A sound already on the HRTF keeps it a little longer
            // (Sticky), so nothing flips back and forth between the two.
            Ranked.Sort(Louder);
            s_full = 0;
            for (int i = 0; i < Ranked.Count; i++)
            {
                AcousticFilter f = Ranked[i];
                bool full = i < Full || (!f.Cheap && i < Full + Sticky);
                f.Cheap = !full;
                if (full) s_full++;
            }
            s_muted = s_on - Ranked.Count;
        }

        // A sound too quiet to hear also gives way first when Unity has more sounds than voices to
        // mix (its lowest priority), so the voices go to sounds you can hear.
        internal static void Silence(AcousticFilter f, bool mute)
        {
            if (f.Mute == mute) return;
            f.Mute = mute;
            if (f.Source == null) return;
            if (mute) { f.OwnPriority = f.Source.priority; f.Source.priority = 256; }
            else if (f.OwnPriority >= 0) { f.Source.priority = f.OwnPriority; f.OwnPriority = -1; }
        }

        private const int Full = 24, Sticky = 4;
        private static readonly List<AcousticFilter> Ranked = new List<AcousticFilter>(128);
        private static readonly System.Comparison<AcousticFilter> Louder = (a, b) => b.Heard.CompareTo(a.Heard);
        private static int s_full, s_muted;

        // How loud a sound reaches you (amplitude, roughly): Unity's fall-off at its distance and
        // everything the mod does to it. Not its own volume: the game fades many sounds in from 0, and
        // a sound muted at its start would miss its first moments. Custom fall-off curves count as
        // full (reading them would cost more than this saves).
        private static float Heard(AcousticFilter f)
        {
            AudioSource src = f.Source;
            float d = f.LastDist, fall = 1f;
            if (src.spatialBlend >= .5f)
            {
                if (src.rolloffMode == AudioRolloffMode.Logarithmic) fall = d <= src.minDistance ? 1f : src.minDistance / d;
                else if (src.rolloffMode == AudioRolloffMode.Linear) fall = Mathf.Clamp01((src.maxDistance - d) / Mathf.Max(.01f, src.maxDistance - src.minDistance));
            }
            return fall * f.TargetGain * f.DistanceGain * f.SourceLevel;
        }

        internal static void Clear()
        {
            foreach (AcousticFilter f in Playing) if (f != null) { f.InBudget = false; Silence(f, false); }
            Playing.Clear();
            s_on = 0;
        }

        // Debug: how much the processing changes loudness, overall and for the sounds it changes most.
        private static readonly List<KeyValuePair<float, string>> Levels = new List<KeyValuePair<float, string>>();
        internal static string Loudness()
        {
            AcousticFilter.Meter = Plugin.DebugLog.Value;
            double inSum = 0d, outSum = 0d;
            Levels.Clear();
            foreach (AcousticFilter f in Playing)
            {
                if (f == null || f.MeterIn <= 1e-9) { if (f != null) { f.MeterIn = 0d; f.MeterOut = 0d; } continue; }
                inSum += f.MeterIn; outSum += f.MeterOut;
                float db = (float)(10d * System.Math.Log10(System.Math.Max(1e-12, f.MeterOut) / f.MeterIn));
                Levels.Add(new KeyValuePair<float, string>(db, f.name + " " + db.ToString("+0.0;-0.0") + " dB at " + f.LastDist.ToString("F0") + " m" + " [" + f.Explain() + "]"));
                f.MeterIn = 0d; f.MeterOut = 0d;
            }
            if (inSum <= 1e-9) return "level: nothing measured";
            Levels.Sort((a, b) => a.Key.CompareTo(b.Key));
            var sb = new System.Text.StringBuilder("level after processing " + (10d * System.Math.Log10(outSum / inSum)).ToString("+0.0;-0.0") + " dB overall; most changed:");
            for (int i = 0; i < Levels.Count && i < 6; i++) sb.Append(" " + Levels[i].Value + ";");
            return sb.ToString();
        }

        // Debug: sounds Unity is playing silently (past its real voices), and the nearest of them.
        private static string Virtual()
        {
            int n = 0; float near = float.MaxValue; string name = "";
            foreach (AcousticFilter f in Playing)
            {
                if (f == null || f.Source == null || !f.Source.isVirtual || f.Source.volume < .01f) continue; // silent anyway
                n++;
                if (f.LastDist < near) { near = f.LastDist; name = f.name; }
            }
            return n == 0 ? "none silenced for voices" : n + " silenced for voices (nearest " + name + " at " + near.ToString("F0") + " m)";
        }

        internal static string Summary() =>
            Virtual() + "; processing " + s_on + " of " + Playing.Count + " playing (" + s_full + " with the HRTF, " + s_muted + " too quiet to hear), audio thread " + (s_load * 100f).ToString("F1") + "% in " + s_calls + " callbacks (" + s_hrtfCalls + " with the HRTF), " + s_perCall.ToString("F1") + " us each";
    }
}
