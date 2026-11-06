using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Positionless ambience: the environment loop (rain, birds, insects), wind, ocean, and the random
    // ambient one-shots. They have no place to cast a ray to, so they're muffled by the space instead:
    // the more closed in you are (share of level and upward rays that hit something), the duller and
    // quieter the outside sounds. Openings (Portals) let the outside in: the bigger and nearer, the
    // less muffled, and the environment loop and wind lean toward them: with the HRTF on, from the
    // openings' real direction (behind, above, as well as left and right: the HRTF on the loop's
    // centre, its stereo width kept, more of it the more closed in you are); without it, stereo pan.
    internal static class Ambience
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> AmbientLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_ambientLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> WindLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_windLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> OceanLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static float s_gain = 1f, s_cutoff = 22000f, s_open, s_pan, s_lean;
        private static AudioSource s_leanSrc0, s_leanSrc1; // the two loops and their filters, found once
        private static AcousticFilter s_leanF0, s_leanF1;
        private static Vector3 s_openDir;

        // Every frame, from the game's audio update: lean the environment loop and wind toward the
        // openings (with the HRTF: their real direction; without: left/right as you face). The
        // left/right lean is also what random ambient calls get (Apply), HRTF or not.
        internal static void Pan(AudioMan man, Transform listener, float dt)
        {
            bool leaning = Plugin.Occlusion.Value && s_open > 0f && Room.Enclosure >= .35f && s_openDir.sqrMagnitude > 0f;
            float amount = leaning ? Mathf.Clamp01(Room.Enclosure * 1.5f - .4f) : 0f;
            // The outside comes in both ways at once: muffled through the walls (from all around) and
            // clear through the openings (from their direction). How much leans toward the openings
            // is the openings' share of what you hear.
            float wall = Mathf.Lerp(1f, .3f, Room.Enclosure * Room.Enclosure);
            float share = s_open * s_open / Mathf.Max(1e-4f, s_open * s_open + wall * wall);
            float target = leaning ? Mathf.Clamp(Vector3.Dot(listener.right, s_openDir) * .8f, -.8f, .8f) * amount * share : 0f;
            s_pan = Mathf.MoveTowards(s_pan, target, 2f * dt);
            bool hrtf = Plugin.Hrtf.Value;
            float lean = hrtf ? Mathf.MoveTowards(s_lean, amount * .8f * share, dt) : 0f;
            if (lean > 0f || s_lean > 0f)
            {
                s_lean = lean;
                Vector3 local = s_openDir.sqrMagnitude > 0f ? listener.InverseTransformDirection(s_openDir) : Vector3.forward;
                Lean(AmbientLoop(man), local);
                Lean(WindLoop(man), local);
            }
            // the loops' stereo pan: the lean without the HRTF, untouched (centre) with it
            float loopPan = hrtf ? 0f : s_pan;
            if (AmbientLoop(man) is AudioSource a && a != null && a.panStereo != loopPan) a.panStereo = loopPan;
            if (WindLoop(man) is AudioSource w && w != null && w.panStereo != loopPan) w.panStereo = loopPan;
        }

        // The HRTF on an unplaced loop, as strong as the lean (Width is how little direction it gets).
        private static void Lean(AudioSource src, Vector3 local)
        {
            if (src == null) return;
            AcousticFilter f = src == s_leanSrc0 ? s_leanF0 : src == s_leanSrc1 ? s_leanF1 : null;
            if (f == null)
            {
                f = src.GetComponent<AcousticFilter>();
                if (f == null)
                {
                    if (s_lean <= .01f) return;
                    f = src.gameObject.AddComponent<AcousticFilter>();
                }
                if (s_leanSrc0 == null || s_leanSrc0 == src) { s_leanSrc0 = src; s_leanF0 = f; }
                else { s_leanSrc1 = src; s_leanF1 = f; }
            }
            f.Kind = AcousticFilter.Kinds.Ambient;
            f.Width = 1f - s_lean;
            f.SetDirection(local);
            f.Hrtf = s_lean > .01f;
            Need(f);
        }

        internal static AudioSource Ocean(AudioMan man) => OceanLoop(man);

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
            // the ocean loop is placed at the real shore and handled like any placed sound (Ocean)
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
            f.Kind = AcousticFilter.Kinds.Ambient;
            f.Set(s_gain, s_cutoff, false);
            Need(f);
        }

        internal static void Apply(GameObject go, bool starting)
        {
            // A random ambient call (a bird, an insect) starting indoors leans toward the openings too,
            // on top of the game's own left/right placement (a call already on the left stays left,
            // it just leans): the room of panning it has left goes toward the openings.
            if (starting && s_pan != 0f && go.GetComponent<AudioSource>() is AudioSource src && src != null)
            {
                float own = src.panStereo;
                src.panStereo = Mathf.Clamp(own + s_pan * (1f - Mathf.Abs(own)), -1f, 1f);
            }
            AcousticFilter f = go.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (s_gain >= .999f && !AcousticFilter.GlobalActive) return;
                f = go.AddComponent<AcousticFilter>();
            }
            f.Set(s_gain, s_cutoff, starting);
            Need(f);
        }

        // The filter's callback only while there's something to do (muffled, leaning, under water).
        private static void Need(AcousticFilter f)
        {
            bool want = f.Hrtf || s_gain < .999f || s_cutoff < 20000f || AcousticFilter.GlobalActive;
            if (f.enabled != want) f.enabled = want;
        }

        internal static void Reset()
        {
            s_gain = 1f; s_cutoff = 22000f; s_open = 0f; s_lean = 0f;
            if (AudioMan.instance == null) return;
            s_pan = 0f;
            if (AmbientLoop(AudioMan.instance) is AudioSource a && a != null) a.panStereo = 0f;
            if (WindLoop(AudioMan.instance) is AudioSource w && w != null) w.panStereo = 0f;
            foreach (AudioSource src in new[] { AmbientLoop(AudioMan.instance), WindLoop(AudioMan.instance), OceanLoop(AudioMan.instance) })
                if (src != null && src.GetComponent<AcousticFilter>() is AcousticFilter f)
                {
                    f.Set(1f, 22000f, false);
                    if (f.Kind == AcousticFilter.Kinds.Ambient) f.Hrtf = false;
                }
        }

        internal static string Summary() => "ambience gain " + s_gain.ToString("F2") + ", low-pass " + s_cutoff.ToString("F0") + " Hz, openness " +
            s_open.ToString("F2") + ", pan " + s_pan.ToString("F2");
    }
}
