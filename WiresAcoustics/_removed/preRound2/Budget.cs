using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // How many sounds get this mod's audio processing at once. Each processed sound runs a callback on
    // Unity's audio thread, and while that thread is busy the game's own calls into the audio engine
    // wait for it: too many at once means crackling sound and, worse, stutter and hitches in the game
    // (a swamp in the rain plays a lot at once). So only the nearest playing sounds are processed,
    // up to a count that follows how busy the audio thread actually is: the callbacks' time is
    // measured, and the count drops quickly when they take more than a fifth of the real time and
    // creeps back up when they take under a tenth. (Measured on a fast desktop in a swamp: about 50
    // sounds took over a fifth of the real time, so a slower machine needs the count to come down.) Sounds past the count are left to Unity (its own panning,
    // no muffling, no callback). Voices, the sea, the ambience loops and the menu are always processed.
    internal static class Budget
    {
        internal const int Most = 32, Least = 8;
        private const float Every = .25f, High = .2f, Low = .1f;
        private static int s_count = 24;
        private static readonly List<AcousticFilter> Playing = new List<AcousticFilter>(128);
        private static float s_cutoff = float.MaxValue, s_next, s_load, s_nextEchoDrop;
        private static int s_on, s_calls;
        private static long s_stamp;

        private static readonly System.Comparison<AcousticFilter> Nearest = (a, b) =>
        {
            if (a.AlwaysOn != b.AlwaysOn) return a.AlwaysOn ? -1 : 1;
            return a.LastDist.CompareTo(b.LastDist);
        };

        // A sound playing (Occlusion, Recheck, Voices, Ocean): processed at once if it's within the
        // count, so a new sound behind a wall starts muffled.
        internal static void Note(AcousticFilter f, float dist)
        {
            f.LastDist = dist;
            bool fresh = !f.InBudget;
            if (fresh)
            {
                f.InBudget = true;
                Playing.Add(f);
            }
            if (f.Cue) { if (f.enabled) f.enabled = false; return; }
            bool want = f.AlwaysOn || s_on < s_count || dist <= s_cutoff;
            if (want && !f.enabled) { f.enabled = true; s_on++; }
            else if (!want && fresh && f.enabled) f.enabled = false; // a new sound past the count (one already counted waits for Step)
        }

        // A few times a second, from Room's tick.
        internal static void Step(float now)
        {
            if (now < s_next) return;
            s_next = now + Every;

            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            long ticks = AcousticFilter.TakeTicks(out s_calls);
            if (s_stamp != 0L && stamp > s_stamp) s_load = (float)ticks / (stamp - s_stamp);
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
                    if (f != null && f.enabled && f.Kind != AcousticFilter.Kinds.Ambient) f.enabled = false;
                }
                Playing[i] = Playing[Playing.Count - 1];
                Playing.RemoveAt(Playing.Count - 1);
            }

            Playing.Sort(Nearest);
            int placed = 0;
            s_on = 0;
            s_cutoff = float.MaxValue;
            foreach (AcousticFilter f in Playing)
            {
                bool want = !f.Cue && (f.AlwaysOn || placed < s_count);
                if (!f.AlwaysOn)
                {
                    if (placed == s_count - 1) s_cutoff = f.LastDist;
                    placed++;
                }
                if (f.enabled != want) f.enabled = want;
                if (want) s_on++;
            }

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
            s_cutoff = float.MaxValue;
        }

        internal static string Summary() =>
            "processing " + s_on + " of " + Playing.Count + " playing (up to " + s_count + "), audio thread " + (s_load * 100f).ToString("F1") + "% in " + s_calls + " callbacks";
    }
}
