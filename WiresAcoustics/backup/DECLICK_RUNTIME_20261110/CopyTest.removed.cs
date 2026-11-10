using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // TEST: every player footstep clip gets a separate copy made by the mod, cleaned with
    // the earlier "declicked3" processing (DeclickMathC), and the game plays the copy
    // instead. Nothing is edited inside the game's own clips. The copies are prepared on
    // worker threads right after a world loads; a clip played before its copy is ready
    // plays the original that once.
    internal static class CopyTest
    {
        internal const bool On = true;
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static readonly System.Reflection.MethodInfo SetData =
            typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
        private static readonly Dictionary<int, AudioClip> Copies = new Dictionary<int, AudioClip>();
        private static readonly HashSet<int> Started = new HashSet<int>();
        private static readonly HashSet<int> Ours = new HashSet<int>();
        private static readonly List<Done> Ready = new List<Done>();
        private static int s_made, s_swapped, s_queued, s_leads;

        private struct Done { internal int Id; internal string Name; internal int Ch, Frames, Rate, Found; internal float[] Data; }

        internal static bool Wants(AudioClip clip) =>
            clip != null && (clip.name.StartsWith("Player_Footstep_") || clip.name.StartsWith("Foot_Mud_")) &&
            clip.loadState == AudioDataLoadState.Loaded && clip.loadType == AudioClipLoadType.DecompressOnLoad &&
            clip.channels >= 1 && clip.channels <= 2;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class WorldLoaded
        {
            private static void Postfix()
            {
                if (GetData == null) return; // runs even with the mod switched off (test)
                foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                    if (Wants(clip)) Queue(clip);
                if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[copy-test] " + s_queued + " footstep clips queued");
            }
        }

        // Main thread, as a sound starts (Declick.Prepare).
        internal static void Swap(AudioSource src)
        {
            Collect();
            AudioClip clip = src.clip;
            if (clip == null || Ours.Contains(clip.GetInstanceID())) return;
            if (Copies.TryGetValue(clip.GetInstanceID(), out AudioClip copy) && copy != null)
            {
                CaptureTest.Step(src, clip, true);
                src.clip = copy;
                s_swapped++;
                if (Plugin.DebugLog.Value && s_swapped % 25 == 0) Plugin.Log.LogInfo("[copy-test] " + s_swapped + " footsteps played from cleaned copies");
                return;
            }
            if (Wants(clip)) { CaptureTest.Step(src, clip, false); Queue(clip); }
        }

        private static void Queue(AudioClip clip)
        {
            int id = clip.GetInstanceID();
            if (!Started.Add(id)) return;
            int ch = clip.channels, frames = clip.samples, rate = clip.frequency;
            var data = new float[frames * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) return;
            string name = clip.name;
            s_queued++;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int found = 0;
                var one = new float[frames];
                for (int c = 0; c < ch; c++)
                {
                    for (int i = 0; i < frames; i++) one[i] = data[i * ch + c];
                    float[] clean = DeclickMathC.Clean(one, true, out int n);
                    found += n;
                    for (int i = 0; i < frames; i++) data[i * ch + c] = clean[i];
                }
                lock (Ready) Ready.Add(new Done { Id = id, Name = name, Ch = ch, Frames = frames, Rate = rate, Found = found, Data = data });
            });
        }

        private static void Collect()
        {
            lock (Ready)
            {
                foreach (Done d in Ready)
                {
                    // A clip trimmed to start mid-waveform (first sample well away from zero) jumps
                    // straight there from silence: a click at the start of every play. The part
                    // that was cut off isn't in the file, so 3 ms are put in front that rise
                    // smoothly from silence to the first sample; nothing of the clip is faded.
                    float[] data = d.Data;
                    int frames = d.Frames, lead = 0;
                    float peak = 0f, first = 0f;
                    for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                    for (int c = 0; c < d.Ch; c++) first = Mathf.Max(first, Mathf.Abs(data[c]));
                    if (first > .02f * peak && first > 1e-3f)
                    {
                        lead = Mathf.Max(8, d.Rate * 3 / 1000);
                        var longer = new float[(frames + lead) * d.Ch];
                        for (int i = 0; i < lead; i++)
                        {
                            float g = .5f - .5f * Mathf.Cos(Mathf.PI * (i + 1) / (lead + 1));
                            for (int c = 0; c < d.Ch; c++) longer[i * d.Ch + c] = data[c] * g;
                        }
                        System.Array.Copy(data, 0, longer, lead * d.Ch, data.Length);
                        data = longer;
                        frames += lead;
                        s_leads++;
                    }
                    AudioClip copy = AudioClip.Create(d.Name, frames, d.Ch, d.Rate, false);
                    SetData.Invoke(copy, new object[] { data, 0 });
                    Copies[d.Id] = copy;
                    Ours.Add(copy.GetInstanceID());
                    s_made++;
                    if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[copy-test] " + d.Name + ": cleaned copy ready (" + d.Found + " spots" + (lead > 0 ? ", starts at " + (100f * first / peak).ToString("F0") + "% of peak: lead-in added" : "") + ") [" + s_made + "/" + s_queued + ", " + s_leads + " with lead-in]");
                }
                Ready.Clear();
            }
        }
    }
}
