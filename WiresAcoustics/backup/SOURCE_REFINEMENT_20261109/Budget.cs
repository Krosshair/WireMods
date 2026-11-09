using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Levels of detail for the sound processing, like a game's model LODs. Every playing sound with
    // something to do is processed (muffling, air, distance, direction); none is switched off for
    // cost. The nearest get the full HRTF (the delay between the ears, the ear filters, the outer
    // ear's cues), the rest a cheaper direction (the level difference between the ears only), which
    // past that distance you can't tell apart anyway. How many get the full HRTF follows how busy
    // Unity's audio thread actually is: the callbacks' time is measured, and the count drops quickly
    // when they take more than a fifth of the real time and creeps back up under a tenth (a busy
    // audio thread holds up the game's own calls into the audio engine: stutter and hitches on a
    // slower machine). Voices, the sea, the ambience loops and the menu always get the full one.
    internal static class Budget
    {
        internal const int Most = 32, Least = 4;
        private const float Every = .25f, High = .2f, Low = .1f;
        private static int s_count = 16; // how many get the full HRTF
        private static readonly List<AcousticFilter> Playing = new List<AcousticFilter>(128);
        private static float s_priorityCutoff, s_next, s_load, s_nextEchoDrop;
        private static int s_on, s_calls, s_hrtfCalls;
        private static float s_echoShare;
        private static long s_stamp;
        private static float s_perCall;

        private static readonly System.Comparison<AcousticFilter> Audibility = (a, b) =>
        {
            if (a.AlwaysOn != b.AlwaysOn) return a.AlwaysOn ? -1 : 1;
            int score = b.Audibility.CompareTo(a.Audibility);
            return score != 0 ? score : a.LastDist.CompareTo(b.LastDist);
        };

        // A sound playing (Occlusion, Recheck, Voices, Ocean): processed at once, so a new sound
        // behind a wall starts muffled; full HRTF if it's as near as the nearest few.
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
            f.Full = f.AlwaysOn || f.Audibility >= s_priorityCutoff;
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

            AcousticFilter.Diagnostics = Plugin.DebugLog.Value;
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            long ticks = AcousticFilter.TakeTicks(out s_calls, out long echoTicks, out s_hrtfCalls);
            if (s_stamp != 0L && stamp > s_stamp) s_load = (float)ticks / (stamp - s_stamp);
            s_echoShare = ticks > 0 ? (float)echoTicks / ticks : 0f;
            s_perCall = s_calls > 0 ? (float)(ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / s_calls) : 0f;
            s_stamp = stamp;
            if (s_load > High) s_count = Mathf.Max(Least, s_count * 3 / 4);
            else if (s_load < Low) s_count = Mathf.Min(Most, s_count + 2);

            for (int i = Playing.Count - 1; i >= 0; i--)
            {
                AcousticFilter f = Playing[i];
                bool gone = f == null || f.Source == null || !f.Source.isPlaying;
                if (!gone) continue;
                if (!ReferenceEquals(f, null))
                {
                    f.InBudget = false;
                    if (f != null && f.Kind != AcousticFilter.Kinds.Ambient) f.Active = false;
                }
                Playing[i] = Playing[Playing.Count - 1];
                Playing.RemoveAt(Playing.Count - 1);
            }

            Playing.Sort(Audibility);
            int placed = 0;
            s_on = 0;
            s_priorityCutoff = 0f;
            foreach (AcousticFilter f in Playing)
            {
                // processed if there's anything to do (nothing in the way, no HRTF, near: off, free)
                bool want = !f.Cue && f.NeedsWork;
                f.Active = want;
                if (!want) continue;
                s_on++;
                // the nearest get the full HRTF, the rest the cheaper direction
                // a little give at the edge, so a sound near the limit doesn't flip back and forth
                bool full = f.AlwaysOn || placed < s_count || (f.Full && placed < s_count + 2);
                if (!f.AlwaysOn)
                {
                    if (placed == s_count - 1) s_priorityCutoff = f.Audibility;
                    placed++;
                }
                f.Full = full;
            }
            if (placed < s_count) s_priorityCutoff = 0f;

            if (now >= s_nextEchoDrop)
            {
                s_nextEchoDrop = now + 1f;
                AcousticFilter.DropEchoes();
            }
        }

        internal static void Clear()
        {
            foreach (AcousticFilter f in Playing) if (!ReferenceEquals(f, null)) f.InBudget = false;
            Playing.Clear();
            s_on = 0;
            s_priorityCutoff = 0f;
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
                Levels.Add(new KeyValuePair<float, string>(db, f.name + " " + db.ToString("+0.0;-0.0") + " dB at " + f.LastDist.ToString("F0") + " m" + (f.Full ? "" : " (light)") + " [" + f.Explain() + "]"));
                f.MeterIn = 0d; f.MeterOut = 0d;
            }
            if (inSum <= 1e-9) return "level: nothing measured";
            Levels.Sort((a, b) => a.Key.CompareTo(b.Key));
            var sb = new System.Text.StringBuilder("level after processing " + (10d * System.Math.Log10(outSum / inSum)).ToString("+0.0;-0.0") + " dB overall; most changed:");
            for (int i = 0; i < Levels.Count && i < 6; i++) sb.Append(" " + Levels[i].Value + ";");
            return sb.ToString();
        }

        internal static string Summary() =>
            "processing " + s_on + " of " + Playing.Count + " playing (full HRTF for the most audible " + s_count + "), audio thread " + (s_load * 100f).ToString("F1") + "% in " + s_calls + " callbacks (" + s_hrtfCalls + " with the HRTF), " + s_perCall.ToString("F1") + " us each, echo " + (s_echoShare * 100f).ToString("F0") + "% of it";
    }
}
