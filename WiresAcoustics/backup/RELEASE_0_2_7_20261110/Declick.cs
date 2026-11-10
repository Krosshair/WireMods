using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using UnityEngine;

namespace WiresAcoustics
{
    // Repairs two defects baked into many of the game's sound effects, inside the game's own
    // clips (nothing is copied and no audio ships with the mod):
    //  - short ringing bursts in the middle of a clip, heard as sharp clicks;
    //  - clips trimmed to start mid-waveform (first sample far from zero), which jump straight
    //    there from silence: a click at the start of every play.
    // Where the bursts are was worked out ahead of time from the game's clips
    // (data/DeclickTable.tsv.gz, built by dev/DeclickTableBuilder.cs; dev/DECLICK_CLIPS.tsv says
    // where every clip is used). Footsteps get the cleaning that was checked by ear (smoothing
    // steps plus the top end taken out of the tail); other effects the ring-matched bursts only.
    // A clip that starts mid-waveform is moved a few milliseconds later inside its own length
    // (the game's clips end in digital silence, so nothing audible falls off the end) and the
    // freed start rises smoothly from silence to its first sample. Looping clips are not moved.
    // The game unloads unused assets after a world loads, after sleeping and every hour
    // (Game.CollectResources); a clip loaded again comes back from disk unrepaired but with the
    // same instance id. So nothing is remembered per clip: each time a listed clip starts, 16
    // of its samples are compared with the original's and with what they were after the repair.
    // A clip the table doesn't know (renamed, changed or new after a game update) is searched on
    // a worker thread instead and named in the debug log.
    internal static class Declick
    {
        private sealed class Entry
        {
            internal char Kind;                       // 'C' footstep cleaning, 'R' ring-matched, '-' none
            internal int[] Tail;                      // C: where the tail cut starts, per channel
            internal List<DeclickMathC.Step>[] Steps; // C
            internal List<DeclickMath.Fix>[] Fixes;   // R
            internal bool Lead;                       // starts mid-waveform
            internal int PrintChannel, PrintPos;
            internal float[] Print;                   // 16 samples of the original from 8 before PrintPos
            internal float[] Fixed;                   // the same 16 after the repair (this session)
            internal bool Differs;                    // the game's clip isn't the one the table was built from
        }

        private static Dictionary<string, Entry> s_table;
        private static readonly HashSet<int> Searched = new HashSet<int>();
        private static readonly List<KeyValuePair<AudioClip, float[]>> Ready = new List<KeyValuePair<AudioClip, float[]>>();
        private static int s_fixed, s_refixed, s_leads;
        // the array overloads by reflection (the compiler would bind the Span ones, which this build can't reference)
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static readonly System.Reflection.MethodInfo SetData =
            typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        // Main thread, as the game starts a sound effect (Travel).
        internal static void Prepare(AudioSource src)
        {
            if (GetData == null || SetData == null) return;
            Apply();
            if (!Plugin.Enabled.Value) return;
            AudioClip clip = src.clip;
            if (!Fits(clip)) return;
            if (Load() && s_table.TryGetValue(clip.name + "|" + clip.channels, out Entry entry))
            {
                if (entry.Kind == '-' && !entry.Lead) return; // checked, nothing to repair
                if (!entry.Differs && Ensure(clip, entry, src.loop)) { CaptureTest.Step(src, clip, true); return; }
            }
            CaptureTest.Step(src, clip, false);
            Search(clip, src.loop);
        }

        private static bool Fits(AudioClip clip) =>
            clip != null && clip.loadState == AudioDataLoadState.Loaded &&
            clip.loadType == AudioClipLoadType.DecompressOnLoad &&
            clip.channels >= 1 && clip.channels <= 8 &&
            clip.samples >= 1024 && clip.samples <= clip.frequency * 10;

        // True when the clip is (now) repaired; false when it isn't the clip the table knows.
        private static bool Ensure(AudioClip clip, Entry entry, bool loop)
        {
            float[] now = ReadPrint(clip, entry);
            if (now == null) { entry.Differs = true; return false; }
            bool again = entry.Fixed != null;
            if (again)
            {
                if (Distance(now, entry.Fixed) <= Distance(now, entry.Print)) return true; // still repaired
            }
            else if (Distance(now, entry.Print) > .004f)
            {
                entry.Differs = true;
                if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + clip.name + " isn't the clip the table was built from, searching it instead");
                return false;
            }
            int lead = Repair(clip, entry, loop, out int spots);
            entry.Fixed = ReadPrint(clip, entry);
            if (again) s_refixed++; else s_fixed++;
            if (lead > 0) s_leads++;
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("[declick] " + clip.name + ": " + spots + " spots" +
                    (lead > 0 ? ", start moved " + (1000f * lead / clip.frequency).ToString("F1") + " ms with a lead-in" : "") +
                    (again ? " (again: the game reloaded it)" : "") +
                    " [" + s_fixed + " clips, " + s_leads + " lead-ins, " + s_refixed + " redone]");
            return true;
        }

        private static int Repair(AudioClip clip, Entry entry, bool loop, out int spots)
        {
            spots = 0;
            int ch = clip.channels, frames = clip.samples;
            bool move = entry.Lead && !loop;
            if (entry.Kind == 'R' && !move) { spots = FixAt(clip, entry); return 0; } // only around the clicks
            var data = new float[frames * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) return 0;
            if (entry.Kind != '-')
            {
                var one = new float[frames];
                for (int c = 0; c < ch; c++)
                {
                    for (int i = 0; i < frames; i++) one[i] = data[i * ch + c];
                    if (entry.Kind == 'C')
                    {
                        DeclickMathC.Replay(one, entry.Tail[c], entry.Steps[c]);
                        spots += entry.Steps[c] != null ? entry.Steps[c].Count : 0;
                    }
                    else if (entry.Fixes[c] != null)
                    {
                        var inside = entry.Fixes[c].FindAll(f => f.Pos < frames);
                        DeclickMath.Replay(one, 0, frames, inside);
                        spots += inside.Count;
                    }
                    for (int i = 0; i < frames; i++) data[i * ch + c] = one[i];
                }
            }
            int lead = move ? LeadIn(data, ch, frames, clip.frequency) : 0;
            SetData.Invoke(clip, new object[] { data, 0 });
            return lead;
        }

        // Moves the clip later inside its own length and lets the freed start rise from silence
        // to the first sample. Only as far as the end is digital silence; under 1 ms it's left.
        private static int LeadIn(float[] data, int ch, int frames, int rate)
        {
            float peak = 0f, first = 0f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            for (int c = 0; c < ch; c++) first = Mathf.Max(first, Mathf.Abs(data[c]));
            if (first <= .02f * peak || first <= 1e-3f) return 0;
            int lead = Mathf.Max(8, rate * 3 / 1000), quiet = 0;
            float floor = peak * 1e-4f;
            for (int i = frames - 1; i >= 0 && quiet < lead; i--, quiet++)
            {
                bool silent = true;
                for (int c = 0; c < ch; c++) if (Mathf.Abs(data[i * ch + c]) > floor) { silent = false; break; }
                if (!silent) break;
            }
            lead = Mathf.Min(lead, quiet);
            if (lead < rate / 1000) return 0;
            System.Array.Copy(data, 0, data, lead * ch, (frames - lead) * ch);
            for (int i = 0; i < lead; i++)
            {
                float g = .5f - .5f * Mathf.Cos(Mathf.PI * (i + 1) / (lead + 1));
                for (int c = 0; c < ch; c++) data[i * ch + c] = data[lead * ch + c] * g;
            }
            return lead;
        }

        private static float[] ReadPrint(AudioClip clip, Entry entry)
        {
            int ch = clip.channels, at = entry.PrintPos - 8, len = entry.Print.Length;
            if (at < 0 || at + len > clip.samples || entry.PrintChannel >= ch) return null;
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

        // Ring-matched clicks only: one stretch around each group of nearby clicks is read, redone and written back.
        private static int FixAt(AudioClip clip, Entry entry)
        {
            int ch = clip.channels, frames = clip.samples, count = 0;
            for (int c = 0; c < ch && c < entry.Fixes.Length; c++)
            {
                if (entry.Fixes[c] == null) continue;
                List<DeclickMath.Fix> sorted = entry.Fixes[c].FindAll(f => f.Pos < frames);
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

        // A clip the table doesn't know: bursts searched on a worker, written back (with the
        // lead-in) the next time any sound starts. Once per clip instance.
        private static void Search(AudioClip clip, bool loop)
        {
            int id = clip.GetInstanceID();
            if (!Searched.Add(id)) return;
            int ch = clip.channels, frames = clip.samples, rate = clip.frequency;
            var data = new float[frames * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) return;
            string name = clip.name;
            ThreadPool.QueueUserWorkItem(_ =>
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
                int lead = loop ? 0 : LeadInOffThread(data, ch, frames, rate);
                if (found > 0 || lead > 0) lock (Ready) Ready.Add(new KeyValuePair<AudioClip, float[]>(clip, data));
                if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + name + " (not in the table): " + found + " clicks" + (lead > 0 ? ", lead-in" : ""));
            });
        }

        private static int LeadInOffThread(float[] data, int ch, int frames, int rate) => LeadIn(data, ch, frames, rate); // pure math (Mathf only)

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

        private static bool Load()
        {
            if (s_table != null) return true;
            var table = new Dictionary<string, Entry>();
            try
            {
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
                        var e = new Entry { Kind = f[2][0], Lead = f[6] == "1" };
                        if (e.Kind == 'C')
                        {
                            string[] t = f[3].Split(',');
                            e.Tail = new int[ch];
                            e.Steps = new List<DeclickMathC.Step>[ch];
                            for (int c = 0; c < ch; c++) { e.Tail[c] = int.Parse(t[c]); e.Steps[c] = new List<DeclickMathC.Step>(); }
                            if (f[4] != "-")
                                foreach (string item in f[4].Split(';'))
                                {
                                    string[] p = item.Split(':');
                                    e.Steps[int.Parse(p[0])].Add(new DeclickMathC.Step { Pos = int.Parse(p[1]), Pass = byte.Parse(p[2]) });
                                }
                        }
                        else if (e.Kind == 'R')
                        {
                            e.Fixes = new List<DeclickMath.Fix>[ch];
                            foreach (string item in f[4].Split(';'))
                            {
                                string[] p = item.Split(':');
                                int c = int.Parse(p[0]);
                                if (e.Fixes[c] == null) e.Fixes[c] = new List<DeclickMath.Fix>();
                                e.Fixes[c].Add(new DeclickMath.Fix { Pos = int.Parse(p[1]), Round = byte.Parse(p[2]), Passes = byte.Parse(p[3]) });
                            }
                        }
                        if (f[5] != "-")
                        {
                            string[] q = f[5].Split(':');
                            e.PrintChannel = int.Parse(q[0]);
                            e.PrintPos = int.Parse(q[1]);
                            string[] v = q[2].Split(',');
                            e.Print = new float[v.Length];
                            for (int i = 0; i < v.Length; i++) e.Print[i] = int.Parse(v[i]) / 32767f;
                        }
                        table[f[0] + "|" + f[1]] = e;
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[declick] couldn't read the click table: " + e.Message);
                table.Clear();
            }
            s_table = table;
            return true;
        }
    }
}
