using System.Collections.Generic;
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
        internal static void PlaceAsAmbient(AudioSource src)
        {
            if (src == null || src.spatialBlend < .5f) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>() ??
                               src.gameObject.AddComponent<AcousticFilter>();
            if (!f.ConvertedAmbient)
            {
                f.ConvertedAmbient = true;
                f.OldAmbientBlend = src.spatialBlend;
            }
            src.spatialBlend = 0f;
        }
        private static void RememberPan(AudioSource src)
        {
            if (src == null) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>() ??
                               src.gameObject.AddComponent<AcousticFilter>();
            if (!f.SavedAmbientPan)
            {
                f.SavedAmbientPan = true;
                f.OriginalAmbientPan = src.panStereo;
            }
        }

        internal static void RestorePlaced()
        {
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null) continue;
                AudioSource src = f.Source;
                if (src != null)
                {
                    if (f.ConvertedAmbient) src.spatialBlend = f.OldAmbientBlend;
                    if (f.SavedAmbientPan) src.panStereo = f.OriginalAmbientPan;
                }
                f.ConvertedAmbient = false;
                f.SavedAmbientPan = false;
            }
        }
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> AmbientLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_ambientLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> WindLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_windLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> OceanLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static float s_gain = 1f, s_cutoff = 22000f, s_open, s_pan, s_lean, s_share;
        private static AudioSource s_leanSrc0, s_leanSrc1; // the two loops and their filters, found once
        private static AcousticFilter s_leanF0, s_leanF1;
        private static Vector3 s_openDir;
        // World ambience one-shots are short lived, but can outlast a
        // transition through a door. Unlike the two persistent environment
        // loops they were previously muffled ONLY when their ZSFX started.
        private static readonly List<AcousticFilter> RandomAmbients = new List<AcousticFilter>(12);

        // Every frame, from the game's audio update: lean the environment loop and wind toward the
        // openings (with the HRTF: their real direction; without: left/right as you face). The
        // left/right lean is also what random ambient calls get (Apply), HRTF or not. Outside, the
        // wind loop leans toward where the wind comes from instead, and is louder or quieter by how
        // your ears face it.
        internal static void Pan(AudioMan man, Transform listener, float dt)
        {
            bool leaning = Plugin.Occlusion.Value && s_open > 0f && Room.Enclosure >= .35f && s_openDir.sqrMagnitude > 0f;
            float amount = leaning ? Mathf.Clamp01(Room.Enclosure * 1.5f - .4f) : 0f;
            // The outside comes in both ways at once: muffled through the walls (from all around) and
            // clear through the openings (from their direction). How much leans toward the openings
            // is the openings' share of what you hear.
            float share = s_share; // the openings' share of what you hear of the outside (Update)
            float target = leaning ? Mathf.Clamp(Vector3.Dot(listener.right, s_openDir) * .8f, -.8f, .8f) * amount * share : 0f;
            s_pan = Mathf.MoveTowards(s_pan, target, 2f * dt);
            bool hrtf = Plugin.Hrtf.Value;
            float lean = hrtf ? Mathf.MoveTowards(s_lean, amount * share, dt) : 0f;
            // Outside, the wind in your own ears: it roars in the ear facing it and drops behind your
            // head. The game plays its wind loop the same in both ears whichever way you face.
            Vector3 windFrom = Vector3.zero;
            float windAmount = 0f;
            if (Plugin.Occlusion.Value && EnvMan.instance != null)
            {
                Vector3 d = EnvMan.instance.GetWindDir();
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f)
                {
                    windFrom = -d.normalized;
                    windAmount = Mathf.Clamp01(EnvMan.instance.GetWindIntensity()) * Mathf.Clamp01(1f - Room.Enclosure * 1.5f);
                }
            }
            s_windLean = Mathf.MoveTowards(s_windLean, windAmount * .6f, dt);
            bool windWins = s_windLean > lean * share + .01f;
            if (lean > 0f || s_lean > 0f)
            {
                s_lean = lean;
                Vector3 local = s_openDir.sqrMagnitude > 0f ? listener.InverseTransformDirection(s_openDir) : Vector3.forward;
                Lean(AmbientLoop(man), local, s_lean);
                if (!windWins) Lean(WindLoop(man), local, s_lean);
            }
            if (hrtf && windWins)
                Lean(WindLoop(man), windFrom.sqrMagnitude > 0f ? listener.InverseTransformDirection(windFrom) : Vector3.forward, s_windLean);
            else if (s_windLeanOn && lean <= 0f)
                Lean(WindLoop(man), Vector3.forward, 0f); // wind lean just ended and nothing else leans it
            s_windLeanOn = hrtf && windWins;
            // the loops' stereo pan: the lean without the HRTF, untouched (centre) with it
            float loopPan = hrtf ? 0f : s_pan;
            float windPan = hrtf ? 0f : windWins ? Mathf.Clamp(Vector3.Dot(listener.right, windFrom), -1f, 1f) * s_windLean : s_pan;
            if (AmbientLoop(man) is AudioSource a && a != null && a.panStereo != loopPan)
            {
                RememberPan(a);
                a.panStereo = loopPan;
            }
            if (WindLoop(man) is AudioSource w && w != null)
            {
                if (w.panStereo != windPan)
                {
                    RememberPan(w);
                    w.panStereo = windPan;
                }
                // louder with an ear to the wind (up to +2 dB), quieter with your back to it (down to
                // -3 dB); the game sets the volume every frame, so this never adds up
                if (s_windLean > .001f)
                {
                    Vector3 fwd = listener.forward; fwd.y = 0f;
                    float facing = fwd.sqrMagnitude > 1e-4f ? Vector3.Dot(fwd.normalized, windFrom) : 0f;
                    float side = Mathf.Abs(Vector3.Dot(listener.right, windFrom));
                    float db = (2f * side - 3f * Mathf.Max(0f, -facing)) * (s_windLean / .6f);
                    w.volume *= Mathf.Pow(10f, db / 20f);
                }
            }
        }

        private static float s_windLean;
        private static bool s_windLeanOn;

        // The HRTF on an unplaced loop, as strong as the lean (Width is how little direction it gets).
        private static void Lean(AudioSource src, Vector3 local, float amount)
        {
            if (src == null) return;
            AcousticFilter f = src == s_leanSrc0 ? s_leanF0 : src == s_leanSrc1 ? s_leanF1 : null;
            if (f == null)
            {
                f = src.GetComponent<AcousticFilter>();
                if (f == null)
                {
                    if (amount <= .01f) return;
                    f = src.gameObject.AddComponent<AcousticFilter>();
                }
                if (s_leanSrc0 == null || s_leanSrc0 == src) { s_leanSrc0 = src; s_leanF0 = f; }
                else { s_leanSrc1 = src; s_leanF1 = f; }
            }
            f.Kind = AcousticFilter.Kinds.Ambient;
            f.Width = 1f - amount;
            f.SetDirection(local);
            f.Hrtf = amount > .01f;
            Need(f);
        }

        internal static AudioSource Ocean(AudioMan man) => OceanLoop(man);

        internal static bool IsAmbient(AudioSource src) =>
            AudioMan.instance != null && src.outputAudioMixerGroup != null && src.outputAudioMixerGroup == AudioMan.instance.m_ambientMixer;

        // From the room measurement (every 0.2 s). The outside comes in two ways: through the open
        // directions (openings, open sky: 1 - enclosure) at full strength, and through the walls,
        // floors and roofs by what they let through (Room.WallEnergy, the mass law). The two add by
        // energy; the low-pass is their energy-weighted mix of none and the walls' (Occlusion.WallCut).
        internal static void Update(AudioMan man, float enclosure)
        {
            // the game's own cold and freezing (what its status effects go by), for the air (Filter)
            AcousticFilter.AirWeather = EnvMan.IsFreezing() ? AcousticFilter.Freezing : EnvMan.IsCold() ? AcousticFilter.Cold : AcousticFilter.Warm;
            AcousticFilter.AirHumidity = Humidity(Time.deltaTime > 0f ? .2f : 0f);
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            s_open = Portals.Openness(out s_openDir);
            float openE = Mathf.Clamp01(1f - enclosure), wallE = Mathf.Clamp01(Room.WallEnergy);
            float total = openE + wallE;
            float gain = Mathf.Sqrt(Mathf.Clamp01(total));
            float cut = total > 1e-6f ? Mathf.Exp((openE * Mathf.Log(22000f) + wallE * Mathf.Log(Occlusion.WallCut)) / total) : Occlusion.WallCut;
            s_share = total > 1e-6f ? openE / total : 0f;
            s_gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, gain, s), .001f, 1f);
            s_cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), Mathf.Log(cut), s)), Occlusion.WallCut, 22000f);
            Set(AmbientLoop(man));
            Set(WindLoop(man));
            // Refresh active random outdoor calls (birds, insects, etc.) at
            // the EXISTING enclosure-measurement cadence, not per frame.
            // Preserve their original spatial pan and apply only global room
            // filtering, like the continuous rain/bird environment loop.
            for (int i = RandomAmbients.Count - 1; i >= 0; i--)
            {
                AcousticFilter f = RandomAmbients[i];
                if (f == null || f.Source == null)
                {
                    RandomAmbients.RemoveAt(i);
                    continue;
                }
                f.Set(s_gain, s_cutoff, false);
                Need(f);
            }
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
                RememberPan(src);
                src.panStereo = Mathf.Clamp(own + s_pan * (1f - Mathf.Abs(own)), -1f, 1f);
            }
            AcousticFilter f = go.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (s_gain >= .999f && !AcousticFilter.GlobalActive) return;
                f = go.AddComponent<AcousticFilter>();
            }
            // A random outdoor cue can first spawn spatially while outside,
            // then be converted to room ambience once the listener enters.
            // Drop its old point-source HRTF, Doppler/distance range and
            // direction tracking rather than retaining effects from Sfx.
            f.Hrtf = false;
            f.Placed = false;
            f.Physical(false);
            f.Kind = AcousticFilter.Kinds.Ambient;
            if (!RandomAmbients.Contains(f)) RandomAmbients.Add(f);
            f.Set(s_gain, s_cutoff, starting);
            Need(f);
        }

        // The air's humidity from the game's weather. Fog: the game's fog is Unity's exponential fog,
        // e^(-density d) (Unity: unity_FogParams.y = density / ln 2 in exp2), so the visibility (the
        // meteorological optical range, where 5% is left: IALA / WMO) is ln 20 / density. Under 1 km is
        // fog, 1 to 2 km mist (UK Met Society); fog forms near 100% humidity (Wikipedia, Fog), mist by
        // the WMO's criteria at over 95%. Rain: near the ground, humidity rises within about 30 minutes
        // of the rain starting to 85-90% where the air was already moist, as it is here (LIAISE field
        // campaign, Remote Sensing 2026); the middle of that, 87.5%, the same for light rain and
        // storms (rain rate made no clear difference there). 30 real minutes in game time: a game day
        // is the game's day length (1800 s) for 24 hours. No measurement of the way back after rain
        // was found: it goes back at the same pace.
        private const float BaseHumidity = 75.6f, RainHumidity = 87.5f, MistHumidity = 95f, FogHumidity = 100f;
        private static float s_rain;
        private static float Humidity(float dt)
        {
            float fog = BaseHumidity;
            float density = RenderSettings.fog ? RenderSettings.fogDensity : 0f;
            if (density > 0f)
            {
                float visibility = Mathf.Log(20f) / density;
                if (visibility < 1000f) fog = FogHumidity;
                else if (visibility < 2000f) fog = MistHumidity;
            }
            float day = EnvMan.instance != null ? Mathf.Max(1f, EnvMan.instance.m_dayLengthSec) : 1800f;
            float onset = 30f * 60f * day / 86400f; // seconds of game time for 30 real minutes
            s_rain = Mathf.MoveTowards(s_rain, EnvMan.IsWet() ? 1f : 0f, dt / onset);
            float rain = Mathf.Lerp(BaseHumidity, RainHumidity, s_rain);
            return Mathf.Max(fog, rain);
        }

        // A random call that's heard from its place again (Occlusion): out of the ambience list,
        // its own stereo pan back.
        internal static void Forget(AcousticFilter f)
        {
            if (!RandomAmbients.Remove(f)) return;
            if (f.SavedAmbientPan && f.Source != null) f.Source.panStereo = f.OriginalAmbientPan;
            f.SavedAmbientPan = false;
        }

        // The filter's callback only while there's something to do (muffled, leaning, under water).
        private static void Need(AcousticFilter f)
        {
            bool want = f.Hrtf || s_gain < .999f || s_cutoff < 20000f || AcousticFilter.GlobalActive;
            if (!f.enabled) f.enabled = true;
            f.Active = want;
        }

        internal static void Reset()
        {
            RestorePlaced();
            RandomAmbients.Clear();
            s_gain = 1f; s_cutoff = 22000f; s_open = 0f; s_lean = 0f;
            if (AudioMan.instance == null) return;
            s_pan = 0f;
            // Original stereo pans were restored by RestorePlaced above.
            // Never overwrite a different vanilla/modded default with zero.
            foreach (AudioSource src in new[] { AmbientLoop(AudioMan.instance), WindLoop(AudioMan.instance), OceanLoop(AudioMan.instance) })
                if (src != null && src.GetComponent<AcousticFilter>() is AcousticFilter f)
                {
                    f.Set(1f, 22000f, false);
                    if (f.Kind == AcousticFilter.Kinds.Ambient) f.Hrtf = false;
                }
        }

        // Debug-only, once per existing Room report (~3 seconds). Positionless
        // ambient bird LOOP and real 3D seagull SFX are different systems.
        // Report the CURRENT per-source filter state AFTER path processing,
        // not only the initial wall-ray result from ZSFX.UpdateReverb.
        internal static string WildlifeDiagnostics()
        {
            if (!Plugin.DebugLog.Value) return "";
            var sb = new System.Text.StringBuilder("wildlife sources: ");
            AudioMan man = AudioMan.instance;
            if (man != null)
            {
                AudioSource ambient = AmbientLoop(man);
                if (ambient != null && ambient.clip != null)
                {
                    AcousticFilter af = ambient.GetComponent<AcousticFilter>();
                    sb.Append("environment=").Append(ambient.clip.name)
                      .Append(" [").Append(af != null && af.Active ? "filtered" : "unfiltered")
                      .Append("]; ");
                }
            }
            int matches = 0;
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || f.Source == null || !f.Source.isPlaying) continue;
                string obj = f.name;
                string clip = f.Source.clip != null ? f.Source.clip.name : "";
                if (obj.IndexOf("seagull", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    obj.IndexOf("bird", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    clip.IndexOf("seagull", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    clip.IndexOf("bird", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                matches++;
                if (matches > 4) continue;
                sb.Append(obj).Append('/').Append(clip)
                  .Append(" active=").Append(f.Active)
                  .Append(" kind=").Append(f.Kind)
                  .Append(" distance=").Append(f.LastDist.ToString("F1"))
                  .Append(" blocked=").Append(f.LastOcc.ToString("F2"))
                  .Append(" routed=").Append(f.Routed)
                  .Append(" [").Append(f.Explain())
                  .Append(", ").Append(f.CurrentProcessing()).Append("]; ");
            }
            if (matches == 0) sb.Append("none captured");
            else if (matches > 4) sb.Append("plus ").Append(matches - 4).Append(" more");
            return sb.ToString();
        }

        internal static string Summary() => "ambience gain " + s_gain.ToString("F2") + ", low-pass " + s_cutoff.ToString("F0") + " Hz, openness " +
            s_open.ToString("F2") + ", pan " + s_pan.ToString("F2") + ", random one-shots " + RandomAmbients.Count;
    }
}
