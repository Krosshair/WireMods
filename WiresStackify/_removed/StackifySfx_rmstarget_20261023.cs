using System.Collections.Generic;
using UnityEngine;

namespace WiresStackify
{
    // Stackify's sounds: the game's own sound effects, played only on your
    // side and mixed to a set loudness.
    //  Each clip's loudness is measured once (RMS of its audible part) and
    //  the sound is brought to the level asked for. One sound source can't
    //  go above full volume, so a sound that needs more is played as up to
    //  three identical layers, started together with no random delay so
    //  they add up cleanly instead of flamming.
    //  Every copy may play: the game's limit on how many copies of a sound
    //  can play at once doesn't apply to these.
    //
    // The mix (RMS targets):
    internal static class StackifySfx
    {
        internal const float Click = 0.16f;  // the button: pick up / put down
        internal const float Chest = 0.17f;  // each chest a quick stack or restock used
        internal const float Sort = 0.16f;   // a sort: the bag rummage
        internal const float SortTail = 0.09f; // a sort: the settle under it

        private const float FallbackGain = 2f;   // clips that can't be measured
        private const float MaxGain = 3f;
        private const float Quiet = 0.003f;      // samples below this are silence

        private static readonly Dictionary<AudioClip, float> Rms = new Dictionary<AudioClip, float>();
        // AudioClip.GetData(float[], int), picked by its exact parameters.
        private static readonly System.Reflection.MethodInfo GetData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });

        // pitch: centre pitch; jitter: random +/- share; spatial: 0 heard
        // the same everywhere (UI), 1 fully placed in the world.
        internal static void Play(EffectList list, Vector3 pos, float loudness, float pitch = 1f, float jitter = 0f,
            float spatial = 0f, AudioClip[] pool = null)
        {
            if (list == null || list.m_effectPrefabs == null) return;
            foreach (EffectList.EffectData d in list.m_effectPrefabs)
            {
                if (d == null || !d.m_enabled || d.m_prefab == null) continue;
                ZSFX template = d.m_prefab.GetComponentInChildren<ZSFX>(true);
                if (template == null) continue;
                AudioClip[] clips = pool != null && pool.Length > 0 ? pool : template.m_audioClips;
                if (clips == null || clips.Length == 0) continue;
                AudioClip clip = clips[Random.Range(0, clips.Length)];
                if (clip == null) continue;

                float rms = Measure(clip);
                float gain = rms > 0f ? loudness / rms : FallbackGain;
                gain = Mathf.Clamp(gain, 0.15f, MaxGain);
                int layers = Mathf.Clamp(Mathf.CeilToInt(gain - 0.001f), 1, 3);
                float vol = Mathf.Min(1f, gain / layers);
                float p = pitch * (1f + Random.Range(-jitter, jitter));

                for (int i = 0; i < layers; i++)
                {
                    GameObject go;
                    ZNetView.m_forceDisableInit = true;
                    try { go = Object.Instantiate(d.m_prefab, pos, Quaternion.identity); }
                    finally { ZNetView.m_forceDisableInit = false; }
                    if (go == null) continue;
                    foreach (ZSFX sfx in go.GetComponentsInChildren<ZSFX>(true))
                    {
                        sfx.m_audioClips = new[] { clip };
                        sfx.m_minVol = sfx.m_maxVol = vol;
                        sfx.m_minPitch = sfx.m_maxPitch = p;
                        sfx.m_minDelay = sfx.m_maxDelay = 0f;
                        sfx.m_maxConcurrentSources = 0;
                        AudioSource src = sfx.GetComponent<AudioSource>();
                        if (src != null) src.spatialBlend = spatial;
                    }
                    Object.Destroy(go, 10f);
                }
            }
        }

        // RMS over the part of the clip that isn't silence, once per clip.
        // Clips the game keeps compressed can't be read: -1, and they get a
        // fixed boost instead.
        private static float Measure(AudioClip clip)
        {
            float v;
            if (Rms.TryGetValue(clip, out v)) return v;
            v = -1f;
            try
            {
                if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Loaded && clip.samples > 0)
                {
                    float[] data = new float[clip.samples * clip.channels];
                    if ((bool)GetData.Invoke(clip, new object[] { data, 0 }))
                    {
                        double sum = 0; int n = 0;
                        foreach (float s in data)
                        {
                            float a = s < 0 ? -s : s;
                            if (a < Quiet) continue;
                            sum += a * a; n++;
                        }
                        if (n > 0) v = (float)System.Math.Sqrt(sum / n);
                    }
                }
            }
            catch (System.Exception) { v = -1f; }
            Rms[clip] = v;
            return v;
        }
    }
}
