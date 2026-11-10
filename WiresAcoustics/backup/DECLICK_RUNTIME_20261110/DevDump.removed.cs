using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // TEMPORARY (dev): when a world loads, writes the game's own decoded footstep clips to
    // BepInEx/WiresAcoustics_dump as 32-bit float WAVs, untouched, to compare with the
    // exported files. Remove after the comparison.
    internal static class DevDump
    {
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static bool s_done;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class WorldLoaded
        {
            private static void Postfix()
            {
                if (s_done || GetData == null) return;
                s_done = true;
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "WiresAcoustics_dump");
                Directory.CreateDirectory(dir);
                int n = 0;
                foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                {
                    if (clip == null || !clip.name.StartsWith("Player_Footstep_")) continue;
                    if (clip.loadState != AudioDataLoadState.Loaded || clip.loadType != AudioClipLoadType.DecompressOnLoad) continue;
                    var data = new float[clip.samples * clip.channels];
                    if (!(bool)GetData.Invoke(clip, new object[] { data, 0 })) continue;
                    Write(Path.Combine(dir, clip.name + ".wav"), data, clip.channels, clip.frequency);
                    n++;
                }
                Plugin.Log.LogInfo("[dev-dump] wrote " + n + " footstep clips to " + dir);
            }
        }

        private static void Write(string path, float[] data, int ch, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = data.Length * 4;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)3); w.Write((short)ch); w.Write(rate); w.Write(rate * ch * 4); w.Write((short)(ch * 4)); w.Write((short)32);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (float v in data) w.Write(v);
            }
        }
    }
}
