using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace WiresAcoustics
{
    // Runs DeclickMath once per sound effect clip and writes the result back into the
    // clip itself, so nothing extra is kept in memory. The first time a clip is played
    // its samples are copied out and cleaned on a worker thread; the cleaned samples go
    // back into the clip on the next sound effect (any clip), so that first play is
    // still the original. Only clips decompressed on load (the short effects) can be
    // read and written this way; streamed ones are left alone.
    internal static class Declick
    {
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static readonly List<KeyValuePair<AudioClip, float[]>> Ready = new List<KeyValuePair<AudioClip, float[]>>();
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
            AudioClip clip = src.clip;
            if (clip == null || !Plugin.Enabled.Value) return;
            int id = clip.GetInstanceID();
            if (Seen.Contains(id) || clip.loadState != AudioDataLoadState.Loaded) return;
            Seen.Add(id);
            int ch = clip.channels, frames = clip.samples;
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad || ch < 1 || ch > 8 ||
                frames < 1024 || frames > clip.frequency * 10) return;
            var data = new float[frames * ch];
            if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) return;
            string name = clip.name;
            int rate = clip.frequency;
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
                lock (Ready) Ready.Add(new KeyValuePair<AudioClip, float[]>(clip, data));
                if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + name + ": " + found + " clicks removed");
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
