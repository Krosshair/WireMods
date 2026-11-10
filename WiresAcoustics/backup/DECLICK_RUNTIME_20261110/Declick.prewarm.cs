using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Runs DeclickMath once per sound effect clip and writes the result back into the
    // clip itself, so nothing extra is kept in memory. When a world loads, every clip
    // already in memory is queued, footsteps first, and worked through on a few worker
    // threads; a clip that gets played before its turn jumps the queue. Copying a clip's
    // samples out and writing the cleaned ones back happens on the main thread whenever a
    // sound effect starts, so a clip played before it's done still plays the original once.
    // Only clips decompressed on load (the short effects) can be read and written this
    // way; streamed ones are left alone.
    internal static class Declick
    {
        private const int Workers = 3;
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static readonly Queue<AudioClip> Waiting = new Queue<AudioClip>();
        private static readonly List<KeyValuePair<AudioClip, float[]>> Ready = new List<KeyValuePair<AudioClip, float[]>>();
        private static int s_busy, s_done, s_clicks;
        // the array overloads by reflection (the compiler would bind the Span ones, which this build can't reference)
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static readonly System.Reflection.MethodInfo SetData =
            typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class WorldLoaded
        {
            private static void Postfix()
            {
                if (GetData == null || SetData == null || !Plugin.Enabled.Value) return;
                var later = new List<AudioClip>();
                foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                {
                    if (!Fits(clip) || Seen.Contains(clip.GetInstanceID())) continue;
                    if (clip.name.IndexOf("Foot", System.StringComparison.OrdinalIgnoreCase) >= 0) Waiting.Enqueue(clip);
                    else later.Add(clip);
                }
                foreach (AudioClip clip in later) Waiting.Enqueue(clip);
                if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + Waiting.Count + " loaded clips queued");
                Pump();
            }
        }

        // Main thread, as the game starts a sound effect (Travel).
        internal static void Prepare(AudioSource src)
        {
            if (GetData == null || SetData == null) return;
            Apply();
            if (!Plugin.Enabled.Value) return;
            Start(src.clip);
            Pump();
        }

        private static bool Fits(AudioClip clip) =>
            clip != null && clip.loadState == AudioDataLoadState.Loaded &&
            clip.loadType == AudioClipLoadType.DecompressOnLoad &&
            clip.channels >= 1 && clip.channels <= 8 &&
            clip.samples >= 1024 && clip.samples <= clip.frequency * 10;

        private static void Pump()
        {
            while (Volatile.Read(ref s_busy) < Workers && Waiting.Count > 0) Start(Waiting.Dequeue());
        }

        private static void Start(AudioClip clip)
        {
            if (clip == null) return;
            int id = clip.GetInstanceID();
            if (Seen.Contains(id) || clip.loadState != AudioDataLoadState.Loaded) return;
            Seen.Add(id);
            if (!Fits(clip)) return;
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
                    Interlocked.Increment(ref s_done);
                    Interlocked.Add(ref s_clicks, found);
                    if (Plugin.DebugLog.Value && found > 0) Plugin.Log.LogInfo("[declick] " + name + ": " + found + " clicks removed");
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
            if (Plugin.DebugLog.Value && Waiting.Count == 0 && Volatile.Read(ref s_busy) == 0)
                Plugin.Log.LogInfo("[declick] queue finished: " + s_done + " clips checked, " + s_clicks + " clicks removed");
        }
    }
}
