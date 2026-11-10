using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using UnityEngine;

namespace WiresAcoustics
{
    // Removes the short ringing click baked into many of the game's sound effects.
    // Where each click sits was worked out ahead of time from the game's own clips
    // (data/DeclickTable.tsv.gz, built by dev/DeclickTableBuilder.cs; dev/DECLICK_CLIPS.tsv
    // says where every clip is used). Only a few hundred samples around each click are read
    // and written back into the clip; nothing else changes and nothing extra is kept.
    // The game unloads unused assets after a world loads, after sleeping and every hour
    // (Game.CollectResources), and a clip loaded again comes back from disk with its clicks
    // but the same instance id. So the fix isn't remembered per clip: each time a listed
    // clip starts, 16 samples at its first click are compared with the stored original and
    // with what they were after fixing, and the clicks are fixed again if it was reloaded.
    // A clip the table doesn't know (renamed or new after a game update) is searched the
    // slow way on worker threads instead, and named in the debug log.
    internal static class Declick
    {
        private const int Workers = 3;
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static readonly Queue<AudioClip> Waiting = new Queue<AudioClip>();
        private static readonly List<KeyValuePair<AudioClip, float[]>> Ready = new List<KeyValuePair<AudioClip, float[]>>();
        private sealed class Entry
        {
            internal int Length;                  // exported clip's length; the game's copy can differ
            internal List<DeclickMath.Fix>[] Fixes; // null: checked, nothing to fix
            internal int PrintChannel, PrintPos;
            internal float[] Print;               // 16 samples from 8 before PrintPos
            internal bool Checked, Lined;         // lined up with the game's copy (once per name)
            internal int Shift;
            internal float[] Fixed;               // the same 16 samples after fixing
        }
        private static Dictionary<string, Entry> s_table;
                private static int s_unaligned, s_fixed, s_refixed;
        private static int s_busy;
        // the array overloads by reflection (the compiler would bind the Span ones, which this build can't reference)
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static readonly System.Reflection.MethodInfo SetData =
            typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        // Main thread, as the game starts a sound effect (Travel).
        internal static void Prepare(AudioSource src)
        {
            if (GetData == null || SetData == null) return;
            if (CopyTest.On)
            {
                CopyTest.Swap(src); // runs even with the mod switched off (test)
                return;
            }
            Apply();
            if (!Plugin.Enabled.Value) return;
            AudioClip clip = src.clip;
            if (!Fits(clip)) return;
            if (Load() && s_table.TryGetValue(Key(clip), out Entry entry))
            {
                if (entry.Fixes == null) return;                 // checked, nothing to fix
                if (EnsureFixed(clip, entry)) return;
            }
            Start(clip);
            Pump();
        }

        private static string Key(AudioClip clip) => clip.name + "|" + clip.channels;

        private static string Names(List<string> names) =>
            string.Join(", ", names.GetRange(0, System.Math.Min(40, names.Count))) + (names.Count > 40 ? ", ..." : "");

        private static bool Fits(AudioClip clip) =>
            clip != null && clip.loadState == AudioDataLoadState.Loaded &&
            clip.loadType == AudioClipLoadType.DecompressOnLoad &&
            clip.channels >= 1 && clip.channels <= 8 &&
            clip.samples >= 1024 && clip.samples <= clip.frequency * 10;

        private static bool Load()
        {
            if (s_table != null) return true;
            try
            {
                var table = new Dictionary<string, Entry>();
                using (Stream raw = typeof(Declick).Assembly.GetManifestResourceStream("WiresAcoustics.DeclickTable.tsv.gz"))
                using (var unzip = new GZipStream(raw, CompressionMode.Decompress))
                using (var reader = new StreamReader(unzip))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        string[] f = line.Split('\t');
                        int ch = int.Parse(f[1]);
                        var e = new Entry { Length = int.Parse(f[2]) };
                        if (f[3] != "-")
                        {
                            e.Fixes = new List<DeclickMath.Fix>[ch];
                            foreach (string item in f[3].Split(';'))
                            {
                                string[] p = item.Split(':');
                                int c = int.Parse(p[0]);
                                if (e.Fixes[c] == null) e.Fixes[c] = new List<DeclickMath.Fix>();
                                e.Fixes[c].Add(new DeclickMath.Fix { Pos = int.Parse(p[1]), Round = byte.Parse(p[2]), Passes = byte.Parse(p[3]) });
                            }
                            string[] q = f[4].Split(':');
                            e.PrintChannel = int.Parse(q[0]);
                            e.PrintPos = int.Parse(q[1]);
                            string[] v = q[2].Split(',');
                            e.Print = new float[v.Length];
                            for (int i = 0; i < v.Length; i++) e.Print[i] = int.Parse(v[i]) / 32767f;
                        }
                        table[f[0] + "|" + f[1]] = e;
                    }
                }
                s_table = table;
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[declick] couldn't read the click table: " + e.Message);
                s_table = new Dictionary<string, Entry>();
                return true;
            }
        }

        // False when the clip doesn't line up with the table; it's then left to the search.
        private static bool EnsureFixed(AudioClip clip, Entry entry)
        {
            if (!entry.Checked)
            {
                entry.Checked = true;
                entry.Lined = LineUp(clip, entry, out entry.Shift);
                if (!entry.Lined)
                {
                    s_unaligned++;
                    if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + clip.name + " doesn't line up with the table, searching it instead");
                }
            }
            if (!entry.Lined) return false;
            float[] now = ReadPrint(clip, entry);
            if (now == null) return true;
            if (entry.Fixed != null && Distance(now, entry.Fixed) <= Distance(now, entry.Print)) return true;   // still fixed
            bool again = entry.Fixed != null;
            int n = FixAt(clip, entry);
            entry.Fixed = ReadPrint(clip, entry);
            if (again) s_refixed++; else s_fixed++;
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("[declick] " + clip.name + ": " + n + " clicks fixed" + (again ? " again (the game reloaded it)" : "") +
                    " [" + s_fixed + " clips fixed, " + s_refixed + " re-fixed so far]");
            return true;
        }

        private static float[] ReadPrint(AudioClip clip, Entry entry)
        {
            int ch = clip.channels, at = entry.PrintPos - 8 + entry.Shift, len = entry.Print.Length;
            if (at < 0 || at + len > clip.samples) return null;
            var data = new float[len * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, at })) return null;
            var one = new float[len];
            for (int k = 0; k < len; k++) one[k] = data[k * ch + entry.PrintChannel];
            return one;
        }

        private static float Distance(float[] a, float[] b)
        {
            float d = 0f;
            for (int i = 0; i < a.Length; i++) d = System.Math.Max(d, System.Math.Abs(a[i] - b[i]));
            return d;
        }

        // Redo the listed fixes, one stretch around each group of nearby clicks at a time.
        private static int FixAt(AudioClip clip, Entry entry)
        {
            int ch = clip.channels, frames = clip.samples, count = 0, shift = entry.Shift;
            for (int c = 0; c < ch && c < entry.Fixes.Length; c++)
            {
                if (entry.Fixes[c] == null) continue;
                var sorted = new List<DeclickMath.Fix>();
                foreach (DeclickMath.Fix f in entry.Fixes[c])
                {
                    int pos = f.Pos + shift;
                    if (pos >= 0 && pos < frames) sorted.Add(new DeclickMath.Fix { Pos = pos, Round = f.Round, Passes = f.Passes });
                }
                sorted.Sort((a, b) => a.Pos.CompareTo(b.Pos));
                int i = 0;
                while (i < sorted.Count)
                {
                    int j = i + 1;
                    while (j < sorted.Count && sorted[j].Pos - sorted[j - 1].Pos <= 2 * DeclickMath.Margin) j++;
                    var group = sorted.GetRange(i, j - i);
                    int s0 = System.Math.Max(0, group[0].Pos - DeclickMath.Margin);
                    int s1 = System.Math.Min(frames, group[group.Count - 1].Pos + DeclickMath.Margin);
                    var data = new float[(s1 - s0) * ch];
                    if ((bool)GetData.Invoke(clip, new object[] { data, s0 }))
                    {
                        var one = new float[s1 - s0];
                        for (int k = 0; k < one.Length; k++) one[k] = data[k * ch + c];
                        DeclickMath.Replay(one, s0, frames, group);
                        for (int k = 0; k < one.Length; k++) data[k * ch + c] = one[k];
                        SetData.Invoke(clip, new object[] { data, s0 });
                        count += group.Count;
                    }
                    i = j;
                }
            }
            return count;
        }

        // Where the stored samples sit in the game's copy: searched over the length
        // difference (extra samples at either end) plus a little either side.
        private static bool LineUp(AudioClip clip, Entry entry, out int shift)
        {
            shift = 0;
            int ch = clip.channels, frames = clip.samples, d = frames - entry.Length;
            int lo = System.Math.Min(0, d) - 64, hi = System.Math.Max(0, d) + 64;
            int from = System.Math.Max(0, entry.PrintPos - 8 + lo);
            int to = System.Math.Min(frames, entry.PrintPos + 8 + hi);
            if (to - from < entry.Print.Length) return false;
            var data = new float[(to - from) * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, from })) return false;
            float best = float.MaxValue;
            for (int off = lo; off <= hi; off++)
            {
                int start = entry.PrintPos - 8 + off - from;
                if (start < 0 || start + entry.Print.Length > to - from) continue;
                float worst = 0f;
                for (int k = 0; k < entry.Print.Length && worst < best; k++)
                    worst = System.Math.Max(worst, System.Math.Abs(data[(start + k) * ch + entry.PrintChannel] - entry.Print[k]));
                if (worst < best) { best = worst; shift = off; }
            }
            return best <= .004f;
        }

        private static void Pump()
        {
            while (Volatile.Read(ref s_busy) < Workers && Waiting.Count > 0) Start(Waiting.Dequeue());
        }

        private static void Start(AudioClip clip)
        {
            if (clip == null) return;
            int id = clip.GetInstanceID();
            if (Seen.Contains(id) || clip.loadState != AudioDataLoadState.Loaded) return;
            if (!Fits(clip)) { Seen.Add(id); return; }
            Seen.Add(id);
            int ch = clip.channels, frames = clip.samples, rate = clip.frequency;
            var data = new float[frames * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) return;
            string name = clip.name;
            Interlocked.Increment(ref s_busy);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int found = 0;
                    var one = new float[frames];
                    for (int c = 0; c < ch; c++)
                    {
                        for (int i = 0; i < frames; i++) one[i] = data[i * ch + c];
                        float[] clean = DeclickMath.Clean(one, rate, out int n);
                        found += n;
                        for (int i = 0; i < frames; i++) data[i * ch + c] = clean[i];
                    }
                    if (found > 0) lock (Ready) Ready.Add(new KeyValuePair<AudioClip, float[]>(clip, data));
                    if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + name + " (not in the table): " + found + " clicks removed");
                }
                finally { Interlocked.Decrement(ref s_busy); }
            });
        }

        private static void Apply()
        {
            lock (Ready)
            {
                if (Ready.Count == 0) return;
                foreach (var done in Ready)
                    if (done.Key != null) SetData.Invoke(done.Key, new object[] { done.Value, 0 });
                Ready.Clear();
            }
        }
    }
}
