using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace WiresAcoustics
{
    // Runs DeclickMath once per player footstep clip (every surface, climbing and mud included) and writes the result back into
    // the clip itself, so nothing extra is kept in memory. The first time a clip is
    // played its samples are copied out and cleaned on a worker thread; the cleaned
    // samples go back into the clip on the next footstep (any clip), so that first play
    // is still the original.
    internal static class Declick
    {
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static readonly List<KeyValuePair<AudioClip, float[]>> Ready = new List<KeyValuePair<AudioClip, float[]>>();
        // the array overloads by reflection (the compiler would bind the Span ones, which this build can't reference)
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static readonly System.Reflection.MethodInfo SetData =
            typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        // Main thread, as the game starts a clip (Travel).
        internal static void Prepare(AudioClip clip)
        {
            if (clip == null || GetData == null || SetData == null) return;
            string name = clip.name;
            if (!name.StartsWith("Player_Footstep_", System.StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Foot_Mud_", System.StringComparison.OrdinalIgnoreCase)) return;
            Apply();
            if (!Plugin.Enabled.Value) return;
            int id = clip.GetInstanceID();
            if (Seen.Contains(id) || clip.loadState != AudioDataLoadState.Loaded) return;
            Seen.Add(id);
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.channels != 1 || clip.samples < 1024 || clip.samples > 48000 * 5) return;
            var data = new float[clip.samples];
            if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                float[] clean = DeclickMath.Clean(data, out int found);
                if (found == 0 && ReferenceEquals(clean, data)) return;
                lock (Ready) Ready.Add(new KeyValuePair<AudioClip, float[]>(clip, clean));
                if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[declick] " + name + ": " + found + " spots smoothed");
            });
        }

        private static void Apply()
        {
            lock (Ready)
            {
                foreach (var done in Ready)
                    if (done.Key != null) SetData.Invoke(done.Key, new object[] { done.Value, 0 });
                Ready.Clear();
            }
        }
    }
}
