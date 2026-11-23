using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // The game's original environmental loops remain continuous. Shelter,
    // estimated from the existing room rays, only alters their gain and
    // spectrum. No synthetic rain/wind emitters or opening registration.
    // Outdoor wind keeps its direction cues and gust variation.
    internal static class Ambience
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> AmbientLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_ambientLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> WindLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_windLoopSource");
         private static float s_gain = 1f, s_cutoff = 22000f, s_inside;
        private static float s_insideAt = -1f;
        // Retain an audible ambient bed in enclosed rooms. Openings affect
        // the room's measured shelter, not the lifetime of the recording.
        private const float AmbientWallFloor = .64f; // -3.9 dB: sheltered but still present
        private const float AmbientWallCutoff = 7200f;
        private static float s_ambientGain = 1f, s_ambientCutoff = 22000f;
        // Unplaced ambient one-shots (anything on the ambience channel the game plays flat): muffled
        // like the bed while they play.
        private static readonly List<AcousticFilter> RandomAmbients = new List<AcousticFilter>(12);

        // The original weather and wind beds are not sounds produced inside
        // the room. Their direct energy is filtered by the wall-bed model,
        // but must not be fed into a listener-centred AudioReverbZone.
        private struct OriginalReverb
        {
            internal AudioSource Source;
            internal bool Bypass;
            internal float Mix;
        }
        private static OriginalReverb s_ambReverb, s_windReverb;
        private static void MakeOriginalDry(AudioSource source)
        {
            if (source == null) return;
            bool wind = AudioMan.instance != null &&
                source == WindLoop(AudioMan.instance);
            OriginalReverb old = wind ? s_windReverb : s_ambReverb;
            if (old.Source != source)
            {
                RestoreOriginal(ref old);
                old.Source = source;
                old.Bypass = source.bypassReverbZones;
                old.Mix = source.reverbZoneMix;
                if (wind) s_windReverb = old; else s_ambReverb = old;
            }
            source.bypassReverbZones = true;
            source.reverbZoneMix = 0f;
        }

        private static void RestoreOriginal(ref OriginalReverb old)
        {
            if (old.Source != null)
            {
                old.Source.bypassReverbZones = old.Bypass;
                old.Source.reverbZoneMix = old.Mix;
            }
            old = default;
        }

        private static void RememberPan(AudioSource src)
        {
            if (src == null) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>() ?? src.gameObject.AddComponent<AcousticFilter>();
            if (!f.SavedAmbientPan) { f.SavedAmbientPan = true; f.OriginalAmbientPan = src.panStereo; }
        }

        internal static void RestorePlaced()
        {
            RestoreOriginal(ref s_ambReverb);
            RestoreOriginal(ref s_windReverb);
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null) continue;
                if (f.Source != null && f.SavedAmbientPan) f.Source.panStereo = f.OriginalAmbientPan;
                f.SavedAmbientPan = false;
            }
        }

        internal static bool IsAmbient(AudioSource src) =>
            AudioMan.instance != null && src.outputAudioMixerGroup != null && src.outputAudioMixerGroup == AudioMan.instance.m_ambientMixer;

        // Every frame, from the game's audio update: out in the open, the wind in your own ears. It
        // roars in the ear facing it and drops behind your head; the game plays it the same in both
        // ears whichever way you face. Up to 2 dB louder side-on, 3 dB quieter back to it (by ear).
        private static float s_windLean;
        private static bool s_windLeanOn;
        internal static void Pan(AudioMan man, Transform listener, float dt)
        {
            Glide(man, dt);
            Vector3 windFrom = Vector3.zero;
            float windAmount = 0f;
            if (Plugin.Occlusion.Value && EnvMan.instance != null)
            {
                windFrom = WindFrom();
                if (windFrom.sqrMagnitude > 0f)
                {
                    // heard from where it reaches you: round and over what's upwind (Room.Exposure)
                    if (s_windWay.sqrMagnitude > 0f) windFrom = s_windWay;
                    windAmount = Mathf.Clamp01(EnvMan.instance.GetWindIntensity()) * (1f - s_inside);
                }
            }
            s_windLean = Mathf.MoveTowards(s_windLean, windAmount * .6f, dt);
            AudioSource w = WindLoop(man);
            if (w == null) return;
            bool hrtf = Plugin.Hrtf.Value, on = s_windLean > .001f;
            if (hrtf && (on || s_windLeanOn))
                Lean(w, windFrom.sqrMagnitude > 0f ? listener.InverseTransformDirection(windFrom) : Vector3.forward, on ? s_windLean : 0f);
            s_windLeanOn = hrtf && on;
            float pan = hrtf || !on ? 0f : Mathf.Clamp(Vector3.Dot(listener.right, windFrom), -1f, 1f) * s_windLean;
            if (w.panStereo != pan) { RememberPan(w); w.panStereo = pan; }
            // Wind isn't steady: it comes in gusts and lulls, more so the harder it blows. The game
            // plays one even loop; here its level rises and falls slowly (up to 4 dB either way at a
            // gale, a gust every few seconds; by ear). The game sets its volume every frame, so
            // nothing adds up.
            if (EnvMan.instance != null && w.volume > 0f)
            {
                float strength = Mathf.Clamp01(EnvMan.instance.GetWindIntensity());
                float t = Time.time;
                float n = Mathf.PerlinNoise(t * .23f, 3.7f) * .65f + Mathf.PerlinNoise(t * .71f, 9.1f) * .35f; // slow swells, quicker gusts
                w.volume *= Mathf.Pow(10f, (n - .5f) * 2f * 4f * strength / 20f);
            }
            if (on)
            {
                // the game sets its volume every frame, so this never adds up
                Vector3 fwd = listener.forward; fwd.y = 0f;
                float facing = fwd.sqrMagnitude > 1e-4f ? Vector3.Dot(fwd.normalized, windFrom) : 0f;
                float side = Mathf.Abs(Vector3.Dot(listener.right, windFrom));
                w.volume *= Mathf.Pow(10f, (2f * side - 3f * Mathf.Max(0f, -facing)) * (s_windLean / .6f) / 20f);
            }
        }

        // The HRTF on the unplaced wind loop, as strong as the lean.
        private static void Lean(AudioSource src, Vector3 local, float amount)
        {
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null) { if (amount <= .01f) return; f = src.gameObject.AddComponent<AcousticFilter>(); }
            f.Kind = AcousticFilter.Kinds.Ambient;
            f.Width = 1f - amount;
            f.SetDirection(local);
            f.Hrtf = amount > .01f;
            Need(f);
        }

        // From the room measurement (every 0.2 s).
        internal static void Update(AudioMan man, float enclosure)
        {
            // the game's own cold and freezing (what its status effects go by), for the air (Filter)
            AcousticFilter.AirWeather = EnvMan.IsFreezing() ? AcousticFilter.Freezing : EnvMan.IsCold() ? AcousticFilter.Cold : AcousticFilter.Warm;
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            // inside from about a third closed in, fully from about two thirds
            // and glided there over a second (a room ray or two changing near a window moved it in
            // steps, so the bed jumped)
            // Room rays estimate shelter, not a binary indoor switch.
            // Even a fully sheltered room keeps the original weather loop.
            float insideNow = s > 0f ?
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.20f, .80f, enclosure)) : 0f;
            if (s_insideAt < 0f) s_inside = insideNow; // the first measurement: straight there
            s_insideAt = Time.time;
            s_insideTarget = insideNow;
             AudioSource amb = AmbientLoop(man), wind = WindLoop(man);
            // the wind in the lee of things: quieter and down to its low rumble, out of the gusts
            // (sheltered: 12 dB less, low-passed at 800 Hz; by ear, no source)
            Vector3 windFrom = WindFrom();
            float exposure = 1f;
            if (windFrom.sqrMagnitude > 0f)
            {
                exposure = Room.Exposure(windFrom, out Vector3 way);
                s_windWay = way.sqrMagnitude > 0f ? Vector3.Slerp(s_windWay.sqrMagnitude > 0f ? s_windWay : way, way, .5f) : windFrom;
            }
            s_exposureTarget = exposure;
            s_bedGain = s_windGain = -1f; // applied again at least this often (a filter may have been reset)
            // the beds themselves glide every frame (Glide), not in steps at each measurement
            for (int i = RandomAmbients.Count - 1; i >= 0; i--)
            {
                AcousticFilter f = RandomAmbients[i];
                if (f == null || f.Source == null) { RandomAmbients.RemoveAt(i); continue; }
                f.Set(s_ambientGain, s_ambientCutoff, false);
                Need(f);
            }
        }
        // A loop as the bed through the walls: muffled from all round, no direction.
        private static void Bed(AudioSource src, float gain = 1f, float cutoff = 22000f, bool ambient = false)
        {
            if (src == null) return;
            MakeOriginalDry(src);
            gain *= ambient ? s_ambientGain : s_gain;
            cutoff = Mathf.Min(cutoff, ambient ? s_ambientCutoff : s_cutoff);
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (gain >= .999f && cutoff >= 20000f && !AcousticFilter.GlobalActive) return;
                f = src.gameObject.AddComponent<AcousticFilter>();
            }
            f.Kind = AcousticFilter.Kinds.Ambient;
            if (s_inside > 0f && f.Hrtf && src != null && AudioMan.instance != null && src != WindLoop(AudioMan.instance)) f.Hrtf = false;
            f.Set(gain, cutoff, false);
            bool want = f.Hrtf || gain < .999f || cutoff < 20000f || AcousticFilter.GlobalActive;
            if (!f.enabled) f.enabled = true;
            f.Active = want;
        }

        private static float s_exposure = 1f;
        private static Vector3 s_windWay;
        private static float s_insideTarget, s_exposureTarget = 1f;
        private static float s_bedGain = -1f, s_bedCut = -1f, s_windGain = -1f, s_windCut = -1f;

        // How closed in you are and how much wind reaches you are measured 5 times a second; set
        // straight from those, the ambience and wind stepped (each measurement half way there, then
        // the filter's 0.15 s glide, then nothing till the next): "stepping through tiers". They
        // glide every frame now at the same speed: closed in at most a whole step a second as
        // before, the wind's exposure half way every 0.2 s as before, as a smooth curve
        // (time constant 0.2 s / ln 2).
        private const float ExposureTau = .2885f;
        private static void Glide(AudioMan man, float dt)
        {
            s_inside = Mathf.MoveTowards(s_inside, s_insideTarget, dt);
             s_exposure += (s_exposureTarget - s_exposure) * (1f - Mathf.Exp(-dt / ExposureTau));
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            float k = Mathf.Min(1f, s) * s_inside;
            // Retain perceptual transmission. The previous wall-bed model
            // could approach digital silence and doubled its own filtering.
            s_gain = Mathf.Lerp(1f, .72f, k);
            s_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f),
                Mathf.Log(8000f), k));
            // Keep the outside's diffuse sound present even when no individual
            // window is sampled. It should be quiet and muffled, not absent.
            // This is for ambient loops; wind's original sheltered bed stays as-is.
            s_ambientGain = Mathf.Lerp(1f, AmbientWallFloor, k);
            s_ambientCutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f),
                Mathf.Log(AmbientWallCutoff), k));
            float k2 = Mathf.Min(1f, s);
            float windGain = Mathf.Lerp(1f, Mathf.Lerp(.25f, 1f, s_exposure), k2);
            float windCut = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(Mathf.Lerp(800f, 22000f, s_exposure)), k2));
            // only while something is changing
            if (Mathf.Abs(s_ambientGain - s_bedGain) > 1e-4f || Mathf.Abs(s_ambientCutoff - s_bedCut) > .5f)
            {
                s_bedGain = s_ambientGain; s_bedCut = s_ambientCutoff;
                Bed(AmbientLoop(man), ambient: true);
            }
            if (Mathf.Abs(s_gain * windGain - s_windGain) > 1e-4f || Mathf.Abs(Mathf.Min(windCut, s_cutoff) - s_windCut) > .5f)
            {
                s_windGain = s_gain * windGain; s_windCut = Mathf.Min(windCut, s_cutoff);
                Bed(WindLoop(man), windGain, windCut);
            }
        }
        private static Vector3 WindFrom()
        {
            if (EnvMan.instance == null) return Vector3.zero;
            Vector3 d = EnvMan.instance.GetWindDir();
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? -d.normalized : Vector3.zero;
        }

        internal static void Apply(GameObject go, bool starting)
        {
            AcousticFilter f = go.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (s_ambientGain >= .999f && !AcousticFilter.GlobalActive) return;
                f = go.AddComponent<AcousticFilter>();
            }
            f.Hrtf = false;
            f.Placed = false;
            f.Physical(false);
            f.Kind = AcousticFilter.Kinds.Ambient;
            if (!RandomAmbients.Contains(f)) RandomAmbients.Add(f);
            f.Set(s_ambientGain, s_ambientCutoff, starting);
            Need(f);
        }

        // A random call that's heard from its place again (Occlusion): out of the list.
        internal static void Forget(AcousticFilter f)
        {
            if (!RandomAmbients.Remove(f)) return;
            if (f.SavedAmbientPan && f.Source != null) f.Source.panStereo = f.OriginalAmbientPan;
            f.SavedAmbientPan = false;
        }

        // The filter's callback only while there's something to do.
        private static void Need(AcousticFilter f)
        {
            bool want = f.Hrtf || s_ambientGain < .999f || s_ambientCutoff < 20000f || AcousticFilter.GlobalActive;
            if (!f.enabled) f.enabled = true;
            f.Active = want;
        }

        internal static void Reset()
        {
            RestorePlaced();
            RandomAmbients.Clear();
            s_gain = s_ambientGain = 1f;
            s_cutoff = s_ambientCutoff = 22000f;
            s_inside = s_insideTarget = 0f;
            s_exposure = s_exposureTarget = 1f;
            s_windLean = 0f;
            s_windLeanOn = false;
            s_insideAt = -1f;
            s_bedGain = s_bedCut = s_windGain = s_windCut = -1f;
            AudioMan man = AudioMan.instance;
            if (man == null) return;
            AudioSource a = AmbientLoop(man), w = WindLoop(man);
            if (a != null) ResetFilter(a);
            if (w != null) ResetFilter(w);
        }

        private static void ResetFilter(AudioSource src)
        {
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null) return;
            f.Set(1f, 22000f, false);
            f.Hrtf = false;
            f.Active = false;
        }

        internal static void Dispose() { Reset(); }

        internal static string ProfilerSummary()
        {
            AudioMan man = AudioMan.instance;
            AudioSource src = man != null ? AmbientLoop(man) : null;
            return "weather clip " +
                (src != null && src.clip != null ? src.clip.name : "none") +
                ", native volume " + (src != null ? src.volume.ToString("F3") : "none") +
                ", playing " + (src != null && src.isPlaying) +
                ", shelter " + s_inside.ToString("F2") +
                ", transmission " + s_ambientGain.ToString("F2") +
                ", cutoff " + s_ambientCutoff.ToString("F0") + "Hz" +
                ", extra weather voices 0";
        }

        internal static string Now() =>
            "weather through room: " + Db(s_ambientGain) + " dB, cutoff " +
            s_ambientCutoff.ToString("F0") + " Hz";
        private static string Db(float a) => a <= 1e-5f ? "-inf" : (20f * Mathf.Log10(a)).ToString("F0");

        internal static string Summary() => "ambience gain " + s_ambientGain.ToString("F2") + ", low-pass " + s_ambientCutoff.ToString("F0") + " Hz, inside " + s_inside.ToString("F2") + ", random one-shots " + RandomAmbients.Count;
    }
}
