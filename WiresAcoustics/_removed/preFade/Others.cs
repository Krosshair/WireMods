using UnityEngine;

namespace WiresAcoustics
{
    // Placed sounds the game (or another mod) plays without its sound-effect system: ship wake and
    // water sounds, the in-water splashing, the shield generator's hum, the Ashlands burn sounds, other
    // mods' own sources. Every 2 seconds the playing placed sources without a filter get one: the HRTF
    // (by how wide the source was made) and, from then on, twice-a-second wall, opening and corner
    // checks on worker threads (Occlusion.Recheck). Sound effects, voice chat, the sea, ambience and
    // menu, music and interface sounds are handled elsewhere or left alone.
    internal static class Others
    {
        internal const float Every = 2f;
        private static int s_found;

        internal static void Scan(AudioMan man)
        {
            AudioSource ocean = Ambience.Ocean(man);
            bool hrtf = Plugin.Hrtf.Value;
            foreach (AudioSource src in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (src == null || !src.isPlaying || src.spatialBlend < .5f || src == ocean) continue;
                if (man.m_guiMixer != null && src.outputAudioMixerGroup == man.m_guiMixer) continue;
                AcousticFilter f = src.GetComponent<AcousticFilter>();
                if (f == null)
                {
                    if (src.GetComponent<ZSFX>() != null) continue; // sound effects: Occlusion
                    f = src.gameObject.AddComponent<AcousticFilter>();
                    f.Kind = AcousticFilter.Kinds.Other;
                    s_found++;
                }
                if (f.Kind != AcousticFilter.Kinds.Other) continue;
                if (hrtf && !f.Hrtf) { f.TakePanning(src.spread); f.Hrtf = true; }
            }
        }

        internal static string Summary()
        {
            string s = "other sources picked up " + s_found;
            s_found = 0;
            return s;
        }
    }
}
