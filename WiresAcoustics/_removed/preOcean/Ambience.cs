using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Positionless ambience: the environment loop (rain, birds, insects), wind, ocean, and the random
    // ambient one-shots. They have no place to cast a ray to, so they're muffled by the space instead:
    // the more closed in you are (share of level and upward rays that hit something), the duller and
    // quieter the outside sounds. Openings (Portals) let the outside in: the bigger and nearer, the
    // less muffled, and the environment loop and wind lean toward them (stereo pan, smoothed).
    internal static class Ambience
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> AmbientLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_ambientLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> WindLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_windLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> OceanLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static float s_gain = 1f, s_cutoff = 22000f, s_open, s_pan;
        private static Vector3 s_openDir;

        // Every frame, from the game's audio update: lean the environment loop and wind toward the
        // openings (left/right as you face).
        internal static void Pan(AudioMan man, Transform listener, float dt)
        {
            float target = 0f;
            if (Plugin.Occlusion.Value && s_open > 0f && Room.Enclosure >= .35f && s_openDir.sqrMagnitude > 0f)
            {
                float x = Vector3.Dot(listener.right, s_openDir);
                target = Mathf.Clamp(x * .8f, -.8f, .8f) * Mathf.Clamp01(Room.Enclosure * 1.5f - .4f);
            }
            float pan = Mathf.MoveTowards(s_pan, target, 2f * dt);
            if (Mathf.Abs(pan - s_pan) < 1e-4f) return;
            s_pan = pan;
            if (AmbientLoop(man) is AudioSource a && a != null) a.panStereo = pan;
            if (WindLoop(man) is AudioSource w && w != null) w.panStereo = pan;
        }

        internal static bool IsAmbient(AudioSource src) =>
            AudioMan.instance != null && src.outputAudioMixerGroup != null && src.outputAudioMixerGroup == AudioMan.instance.m_ambientMixer;

        // From the room measurement (every 0.2 s).
        internal static void Update(AudioMan man, float enclosure)
        {
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            s_open = Portals.Openness(out s_openDir);
            float m = enclosure * enclosure * s * (1f - s_open);
            s_gain = Mathf.Clamp(Mathf.Lerp(1f, .3f, m), .05f, 1f);
            s_cutoff = Mathf.Clamp(Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(700f), m)), 200f, 22000f);
            Set(AmbientLoop(man));
            Set(WindLoop(man));
            Set(OceanLoop(man));
        }

        private static void Set(AudioSource src)
        {
            if (src == null) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (s_gain >= .999f && !AcousticFilter.GlobalActive) return;
                f = src.gameObject.AddComponent<AcousticFilter>();
            }
            f.Set(s_gain, s_cutoff, false);
        }

        internal static void Apply(GameObject go, bool starting)
        {
            AcousticFilter f = go.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (s_gain >= .999f && !AcousticFilter.GlobalActive) return;
                f = go.AddComponent<AcousticFilter>();
            }
            f.Set(s_gain, s_cutoff, starting);
        }

        internal static void Reset()
        {
            s_gain = 1f; s_cutoff = 22000f; s_open = 0f;
            if (AudioMan.instance == null) return;
            s_pan = 0f;
            if (AmbientLoop(AudioMan.instance) is AudioSource a && a != null) a.panStereo = 0f;
            if (WindLoop(AudioMan.instance) is AudioSource w && w != null) w.panStereo = 0f;
            foreach (AudioSource src in new[] { AmbientLoop(AudioMan.instance), WindLoop(AudioMan.instance), OceanLoop(AudioMan.instance) })
                if (src != null && src.GetComponent<AcousticFilter>() is AcousticFilter f) f.Set(1f, 22000f, false);
        }

        internal static string Summary() => "ambience gain " + s_gain.ToString("F2") + ", low-pass " + s_cutoff.ToString("F0") + " Hz, openness " +
            s_open.ToString("F2") + ", pan " + s_pan.ToString("F2");
    }
}
