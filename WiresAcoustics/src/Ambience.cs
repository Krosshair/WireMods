using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // The outside heard from inside. The game plays its ambience (the biome's loop of birds, insects,
    // reeds or rain, and the wind) flat, the same wherever you are. Here, once you're inside:
    //  - the loops themselves become what comes through the walls round you: quiet and dull, from
    //    all round (each wall's loss as the first wall a sound comes through, Occlusion; low-pass
    //    at the walls' 318 Hz);
    //  - and the same loops play from your openings, the doors and windows (Portals), up to two:
    //    copies of them in step with the game's, placed just outside each opening and heard from
    //    there (HRTF). How loud: the opening's share of the sky round you (the outside is sound from
    //    every direction, an opening lets in the part of it it shows you), as amplitude
    //    sqrt(share). A door of about a tenth of the view comes in about 10 dB under outside, what
    //    homes with a window open measure (Locher et al. 2018). Close the door and that part goes.
    // Out in the open the loops are the game's again, with the wind in your ears (Pan).
    internal static class Ambience
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> AmbientLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_ambientLoopSource");
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> WindLoop = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_windLoopSource");
        private const int Openings = 2;
        private const float OutsideBy = 1f; // how far outside its opening a copy sits (m)
        private static float s_gain = 1f, s_cutoff = 22000f, s_inside;
        // Unplaced ambient one-shots (anything on the ambience channel the game plays flat): muffled
        // like the bed while they play.
        private static readonly List<AcousticFilter> RandomAmbients = new List<AcousticFilter>(12);

        // One copy of a loop at an opening.
        private sealed class Copy
        {
            internal AudioSource Src, Game;
            internal AcousticFilter F;
            internal float Level, Target, Width;
        }
        // Copies stay with their opening: an opening keeps its copy as long as it's found (matched by
        // direction), a new one gets a free copy that fades in where it is, and one that's gone fades
        // out where it was. (By rank instead, two openings swapping places, or a third one turning up,
        // sent a copy flying across the room at full level: rain cutting in and out by a window.)
        private const int Slots = 2 * Openings; // the ones in use, and as many fading out
        private static readonly Copy[,] Copies = new Copy[Slots, 2]; // [slot, 0 ambience / 1 wind]
        private static readonly Vector3[] At = new Vector3[Slots];
        private static readonly Vector3[] Dir = new Vector3[Slots];
        private static readonly bool[] Placed = new bool[Slots];
        private static readonly bool[] Used = new bool[Slots];
        private static float s_insideAt = -1f;
        private static GameObject s_root;

        private static void RememberPan(AudioSource src)
        {
            if (src == null) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>() ?? src.gameObject.AddComponent<AcousticFilter>();
            if (!f.SavedAmbientPan) { f.SavedAmbientPan = true; f.OriginalAmbientPan = src.panStereo; }
        }

        internal static void RestorePlaced()
        {
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
            Fade(dt);
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
            float insideNow = s > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, .7f, enclosure)) : 0f;
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
                f.Set(s_gain, s_cutoff, false);
                Need(f);
            }
            // the openings
            int n = s_inside > 0f ? Mathf.Min(Openings, Portals.List.Count) : 0;
            Vector3 listener = Hearing.At;
            for (int j = 0; j < Slots; j++) Used[j] = false;
            for (int op = 0; op < n; op++)
            {
                Portals.Portal p = Portals.List[op];
                // the copy already at this opening (pointing the same way), else a silent one
                int slot = -1;
                float best = .8f;
                for (int j = 0; j < Slots; j++)
                {
                    float dot = Vector3.Dot(Dir[j], p.Dir);
                    if (!Used[j] && Placed[j] && Sounding(j) && dot > best) { best = dot; slot = j; }
                }
                if (slot < 0)
                    for (int j = 0; j < Slots; j++)
                        if (!Used[j] && !Sounding(j)) { slot = j; Placed[j] = false; break; }
                if (slot < 0) continue;
                Used[slot] = true;
                Vector3 to = p.Point + p.Dir * OutsideBy;
                At[slot] = Placed[slot] ? Vector3.Lerp(At[slot], to, .5f) : to;
                Dir[slot] = p.Dir;
                Placed[slot] = true;
                float level = Mathf.Sqrt(Mathf.Clamp01(p.Share)) * s_inside;
                float width = Area.WidthAt(p.Radius, (At[slot] - listener).magnitude);
                Play(slot, 0, amb, level, width, listener);
                Play(slot, 1, wind, level, width, listener);
            }
            for (int j = 0; j < Slots; j++)
                if (!Used[j])
                {
                    Play(j, 0, amb, 0f, 0f, listener); // fades out where it is
                    Play(j, 1, wind, 0f, 0f, listener);
                    if (!Sounding(j)) Placed[j] = false;
                }
        }

        // A slot whose copies are still heard (or fading).
        private static bool Sounding(int j)
        {
            for (int w = 0; w < 2; w++)
            {
                Copy c = Copies[j, w];
                if (c != null && c.Src != null && c.Src.isPlaying && (c.Level > 0f || c.Target > 0f)) return true;
            }
            return false;
        }

        // A loop as the bed through the walls: muffled from all round, no direction.
        private static void Bed(AudioSource src, float gain = 1f, float cutoff = 22000f)
        {
            if (src == null) return;
            gain *= s_gain;
            cutoff = Mathf.Min(cutoff, s_cutoff);
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
            s_gain = Mathf.Clamp(Mathf.Lerp(1f, Mathf.Pow(Room.WallBed, s), s_inside), 0f, 1f);
            s_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(Occlusion.WallCut), k));
            float k2 = Mathf.Min(1f, s);
            float windGain = Mathf.Lerp(1f, Mathf.Lerp(.25f, 1f, s_exposure), k2);
            float windCut = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(Mathf.Lerp(800f, 22000f, s_exposure)), k2));
            // only while something is changing
            if (Mathf.Abs(s_gain - s_bedGain) > 1e-4f || Mathf.Abs(s_cutoff - s_bedCut) > .5f)
            {
                s_bedGain = s_gain; s_bedCut = s_cutoff;
                Bed(AmbientLoop(man));
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

        // One opening's copy of a loop: started in step with the game's, its level following the
        // game's (fades, weather) times the opening's.
        private static void Play(int o, int which, AudioSource game, float level, float width, Vector3 listener)
        {
            Copy c = Copies[o, which];
            bool want = level > .001f && game != null && game.clip != null && game.isPlaying && game.volume > .0001f;
            if (!want)
            {
                if (c != null) c.Target = 0f; // fades out (Fade), then stops
                return;
            }
            if (c == null || c.Src == null) c = Copies[o, which] = Make(o, which);
            AudioSource src = c.Src;
            if (src.clip != game.clip || !src.isPlaying)
            {
                src.clip = game.clip;
                src.outputAudioMixerGroup = game.outputAudioMixerGroup;
                src.timeSamples = Mathf.Min(game.timeSamples, game.clip.samples - 1);
                src.volume = 0f;
                c.Level = 0f;
                src.Play();
            }
            src.transform.position = At[o];
            src.pitch = game.pitch;
            c.Game = game;
            c.Target = level;
            c.Width = width;
            AcousticFilter f = c.F;
            bool hrtf = Plugin.Hrtf.Value;
            if (hrtf) { f.TakePanning(0f); f.Width = width; }
            else src.spread = width * 180f;
            f.Hrtf = hrtf;
            Budget.Note(f, (At[o] - listener).magnitude);
        }

        // Every frame: each copy's level glides to its opening's (half a second for all of it, so an
        // opening found or lost, or a door shut, fades instead of cutting), and follows the game's
        // own level for its loop (its fades, the weather).
        private static void Fade(float dt)
        {
            for (int o = 0; o < Slots; o++)
                for (int w = 0; w < 2; w++)
                {
                    Copy c = Copies[o, w];
                    if (c == null || c.Src == null || !c.Src.isPlaying) continue;
                    c.Level = Mathf.MoveTowards(c.Level, c.Target, 2f * dt);
                    if (c.Level <= 0f && c.Target <= 0f) { c.Src.Stop(); continue; }
                    c.Src.volume = (c.Game != null ? c.Game.volume : 0f) * c.Level;
                }
        }

        private static Copy Make(int o, int which)
        {
            if (s_root == null)
            {
                s_root = new GameObject("WiresAcousticsOutside");
                Object.DontDestroyOnLoad(s_root);
            }
            var go = new GameObject("outside_" + (which == 0 ? "ambience_" : "wind_") + o);
            go.transform.SetParent(s_root.transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1000f; src.maxDistance = 1001f; // its level is set here, not by distance
            src.dopplerLevel = 0f;
            src.bypassReverbZones = true;
            src.priority = 16;
            AcousticFilter f = go.AddComponent<AcousticFilter>();
            f.Kind = AcousticFilter.Kinds.Outside;
            return new Copy { Src = src, F = f };
        }

        internal static void Apply(GameObject go, bool starting)
        {
            AcousticFilter f = go.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (s_gain >= .999f && !AcousticFilter.GlobalActive) return;
                f = go.AddComponent<AcousticFilter>();
            }
            f.Hrtf = false;
            f.Placed = false;
            f.Physical(false);
            f.Kind = AcousticFilter.Kinds.Ambient;
            if (!RandomAmbients.Contains(f)) RandomAmbients.Add(f);
            f.Set(s_gain, s_cutoff, starting);
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
            bool want = f.Hrtf || s_gain < .999f || s_cutoff < 20000f || AcousticFilter.GlobalActive;
            if (!f.enabled) f.enabled = true;
            f.Active = want;
        }

        internal static void Reset()
        {
            RestorePlaced();
            RandomAmbients.Clear();
            s_gain = 1f; s_cutoff = 22000f; s_inside = 0f; s_windLean = 0f; s_insideAt = -1f;
            for (int o = 0; o < Slots; o++)
            {
                Placed[o] = false;
                for (int w = 0; w < 2; w++)
                {
                    Copy c = Copies[o, w];
                    if (c != null && c.Src != null) c.Src.Stop();
                }
            }
            if (AudioMan.instance == null) return;
            foreach (AudioSource src in new[] { AmbientLoop(AudioMan.instance), WindLoop(AudioMan.instance) })
                if (src != null && src.GetComponent<AcousticFilter>() is AcousticFilter f)
                {
                    f.Set(1f, 22000f, false);
                    f.Hrtf = false;
                }
        }

        // Debug: what comes in from outside, for the "now" line.
        internal static string Now()
        {
            if (s_inside <= 0f) return "outside sound: as the game plays it";
            var sb = new System.Text.StringBuilder("outside sound: through the walls ");
            sb.Append(Db(s_gain)).Append(" dB at ").Append(s_cutoff.ToString("F0")).Append(" Hz");
            for (int o = 0; o < Slots; o++)
            {
                Copy c = Copies[o, 0];
                if (c == null || c.Src == null || !c.Src.isPlaying || c.Target <= 0f) continue;
                sb.Append(", opening ").Append(o + 1).Append(' ').Append(Db(c.Target)).Append(" dB, ").Append((c.Width * 100f).ToString("F0")).Append("% wide");
            }
            return sb.ToString();
        }

        private static string Db(float a) => a <= 1e-5f ? "-inf" : (20f * Mathf.Log10(a)).ToString("F0");

        internal static string Summary() => "ambience gain " + s_gain.ToString("F2") + ", low-pass " + s_cutoff.ToString("F0") + " Hz, inside " + s_inside.ToString("F2") + ", random one-shots " + RandomAmbients.Count;
    }
}
