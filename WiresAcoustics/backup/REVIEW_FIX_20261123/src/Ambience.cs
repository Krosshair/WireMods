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
        // Rain stays audible through the roof and walls. This adjusts only
        // the original ambience bed, not window voices, wind or bird calls.
        // The weather blend follows the actual environment without switching
        // clips or adding an artificial rain emitter.
        // Weather ambience uses the same general emitter-field path as
        // any other sound domain; no custom rain volume/roof mixing.
        // Roof rain uses a quicker spatial response than the 1 s general
        // ambience transition. Sound still fades continuously, never snaps.

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
        private static readonly float[] LastSeen = new float[Slots];
        private static readonly int[] LastWorldGen = new int[Slots];
        // A temporarily missed room ray must not mute an opening. The
        // grace period covers several room scans, but doors and geometry
        // changes still invalidate the detection on the next measurements.
        private const float OpeningGrace = .6f;
        private static float s_insideAt = -1f;
        private static GameObject s_root;

        private static void RememberPan(AudioSource src)
        {
            if (src == null) return;
            AcousticFilter f = AcousticFilter.For(src);
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

        // Valheim's clear-weather biome recording is not m_windLoopSource:
        // Heath clear (Plains) and other clear presets use the 2D
        // SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only.
        // Identify the *recording*, not the biome, to preserve rain,
        // wildlife and other environmental beds as diffuse ambience.
        private static AudioClip s_identifiedWindClip;
        private static bool s_identifiedIsWind;
        internal static bool IsWindTexture(AudioClip clip)
        {
            // Unity's AudioClip.name crosses the native/managed boundary.
            // Both Pan and Glide inspect this source each frame, so only
            // classify again when Valheim switches the authored clip.
            if (!ReferenceEquals(clip, s_identifiedWindClip))
            {
                s_identifiedWindClip = clip;
                s_identifiedIsWind = clip != null && clip.name != null &&
                    clip.name.IndexOf("Wind_", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return s_identifiedIsWind;
        }

        // Every frame, from the game's audio update: out in the open, the wind in your own ears. It
        // roars in the ear facing it and drops behind your head; the game plays it the same in both
        // ears whichever way you face. Up to 2 dB louder side-on, 3 dB quieter back to it (by ear).
        // Audio-thread gain is independent of the vanilla wind AudioSource.volume.
        // Valheim restores that volume in FixedUpdate, not every render Update.
        private static float s_windModulation = 1f;
        private static float s_windLean;
        private static bool s_windLeanOn;
        internal static void Pan(AudioMan man, Transform listener, float dt)
        {
            Fade(dt);
            Glide(man, dt);
            Vector3 windFrom = Vector3.zero;
            float windAmount = 0f;
            if (EnvMan.instance != null)
            {
                windFrom = WindFrom();
                if (windFrom.sqrMagnitude > 0f)
                {
                    // The wind changes direction even while the player
                    // stands completely still. Room rays are cached in that
                    // case: recompute their cheap directional weighting
                    // only when the actual wind heading changes, not a new
                    // physics cast or an additional frame loop.
                    if (Plugin.Occlusion.Value && Room.WindSurveyReady &&
                        Vector3.Dot(windFrom, s_exposureWindFrom) < .992f)
                    {
                        s_exposureTarget = Room.Exposure(windFrom, out Vector3 way);
                        s_windWay = way.sqrMagnitude > 0f ? way : windFrom;
                        s_exposureWindFrom = windFrom;
                    }
                    // Wind has a physical bearing even when the optional
                    // wall-muffling setting is disabled. Only use the
                    // room-routed bearing when those rays are enabled.
                    if (Plugin.Occlusion.Value && s_windWay.sqrMagnitude > 0f)
                        windFrom = s_windWay;
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
            // The vanilla wind AudioSource.volume is assigned by
            // AudioMan.FixedUpdate, not AudioMan.Update. Multiplying it here
            // compounds gain at high FPS and during pauses. Apply gust and
            // directional gain to our independent audio-thread DSP instead.
            float windDb = 0f;
            if (EnvMan.instance != null && w.volume > 0f)
            {
                float strength = Mathf.Clamp01(EnvMan.instance.GetWindIntensity());
                float t = Time.time;
                float n = Mathf.PerlinNoise(t * .23f, 3.7f) * .65f + Mathf.PerlinNoise(t * .71f, 9.1f) * .35f;
                windDb = (n - .5f) * 2f * 4f * strength;
            }
            if (on)
            {
                Vector3 fwd = listener.forward; fwd.y = 0f;
                float facing = fwd.sqrMagnitude > 1e-4f ? Vector3.Dot(fwd.normalized, windFrom) : 0f;
                float side = Mathf.Abs(Vector3.Dot(listener.right, windFrom));
                windDb += (2f * side - 3f * Mathf.Max(0f, -facing)) * (s_windLean / .6f);
            }
            s_windModulation = Mathf.Pow(10f, windDb / 20f);
            AcousticFilter windFilter = w.GetComponent<AcousticFilter>();
            if (windFilter == null && Mathf.Abs(s_windModulation - 1f) > 1e-4f)
                windFilter = w.gameObject.AddComponent<AcousticFilter>();
            if (windFilter != null)
            {
                windFilter.Kind = AcousticFilter.Kinds.Ambient;
                windFilter.SourceLevel = s_windModulation;
                if (!windFilter.enabled) windFilter.enabled = true;
                windFilter.Active = windFilter.NeedsWork;
            }
            WindTexture(man, listener, windFrom, windAmount);
        }

        // The actual outdoor biome wind texture also needs direction and
        // upwind shelter. Previously only AudioMan.m_windLoopSource received
        // this treatment, leaving the Plains reeds recording permanently
        // centered and nearly unchanged behind walls. This is a BROAD
        // environmental field: keep it wide, not a fake point emitter.
        // Uses existing 0.2-second wind exposure and already scheduled
        // frame Pan; no new physics or update hooks.
        private static void WindTexture(AudioMan man, Transform listener,
            Vector3 windFrom, float strength)
        {
            AudioSource src = AmbientLoop(man);
            if (src == null) return;
            bool wind = Plugin.Wind.Value && IsWindTexture(src.clip);
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (!wind)
            {
                // Same AudioSource is reused for rain, birds and weather.
                // Do not carry wind-direction DSP or amplitude into them.
                if (f != null && f.Kind == AcousticFilter.Kinds.Ambient)
                {
                    f.SourceLevel = 1f;
                    f.Hrtf = false;
                    f.Active = f.NeedsWork;
                    if (f.SavedAmbientPan)
                    {
                        src.panStereo = f.OriginalAmbientPan;
                        f.SavedAmbientPan = false;
                    }
                }
                return;
            }
            if (f == null) f = src.gameObject.AddComponent<AcousticFilter>();
            f.Kind = AcousticFilter.Kinds.Ambient;
            float s = Mathf.Min(1f, AcousticMath.OcclusionStrength);
            // Same upwind exposure response as the game's regular wind
            // loop: at most -12 dB in full shelter, naturally glided.
            float shelter = Mathf.Lerp(1f, Mathf.Lerp(.25f, 1f, s_exposure), s);
            f.SourceLevel = s_windModulation * shelter;
            float direction = Mathf.Clamp01(strength);
            bool hasBearing = windFrom.sqrMagnitude > 1e-4f;
            bool hrtf = Plugin.Hrtf.Value && hasBearing && direction > .015f;
            if (hrtf)
            {
                f.TakePanning(src.spread);
                // A field of reeds has many emitters. Preserve most of its
                // width while giving the windward side a perceptible bearing.
                f.Width = Mathf.Lerp(1f, .5f, direction);
                f.SetDirection(listener.InverseTransformDirection(windFrom));
            }
            f.Hrtf = hrtf;
            float pan = !Plugin.Hrtf.Value && hasBearing ?
                Mathf.Clamp(Vector3.Dot(listener.right, windFrom), -1f, 1f) *
                direction * .5f : 0f;
            if (src.panStereo != pan)
            {
                RememberPan(src);
                src.panStereo = pan;
            }
            if (!f.enabled) f.enabled = true;
            f.Active = f.NeedsWork;
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
            float s = AcousticMath.OcclusionStrength;
            // inside from about a third closed in, fully from about two thirds
            // and glided there over a second (a room ray or two changing near a window moved it in
            // steps, so the bed jumped)
            float insideNow = s > 0f ? Mathf.Clamp01(enclosure) : 0f;
            if (s_insideAt < 0f) s_inside = insideNow; // initial measurement starts at the true room state
            s_insideAt = Time.time;
            s_insideTarget = insideNow;
            AudioSource amb = AmbientLoop(man), wind = WindLoop(man);
            // A new room survey can rebind the ambient source immediately.
            // If Valheim is still crossfading with zero source volume,
            // defer binding until its new recording is actually audible.
            s_routedAmbientClip = amb != null && amb.isPlaying &&
                amb.volume > .0001f ? amb.clip : null;
            EnvSetup current = EnvMan.instance != null ?
                EnvMan.instance.GetCurrentEnvironment() : null;
            if (current != null && current.m_isWet && current.m_ambientLoop != null)
                s_lastRainClip = current.m_ambientLoop;
            bool rainNow = s_lastRainClip != null && amb != null &&
                amb.clip == s_lastRainClip;
            // the wind in the lee of things: quieter and down to its low rumble, out of the gusts
            // (sheltered: 12 dB less, low-passed at 800 Hz; by ear, no source)
            Vector3 windFrom = WindFrom();
            float exposure = 1f;
            if (windFrom.sqrMagnitude > 0f)
            {
                exposure = Room.Exposure(windFrom, out Vector3 way);
                s_windWay = way.sqrMagnitude > 0f ? Vector3.Slerp(s_windWay.sqrMagnitude > 0f ? s_windWay : way, way, .5f) : windFrom;
            }
            s_exposureWindFrom = windFrom;
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
            int n = s_insideTarget > .001f ?
                Mathf.Min(Openings, Portals.List.Count) : 0;
            Vector3 listener = Hearing.At;
            // Portals are ordered by aperture ray count. That does NOT
            // tell us which one sounds loudest from this listener: a big
            // distant roof opening can otherwise steal one of the two slots
            // from the open double door immediately in front of us.
            // Select the two strongest measured listener-facing apertures,
            // using the SAME cached geometry; no scene queries.
            int best0 = -1, best1 = -1;
            float gain0 = -1f, gain1 = -1f;
            for (int k = 0; k < Portals.List.Count; k++)
            {
                Portals.Portal aperture = Portals.List[k];
                float d = (aperture.Point - listener).magnitude;
                float strength = WeatherAcoustics.OpeningGain(
                    aperture.Share, aperture.Radius, d);
                if (strength > gain0)
                {
                    best1 = best0; gain1 = gain0;
                    best0 = k; gain0 = strength;
                }
                else if (strength > gain1)
                {
                    best1 = k; gain1 = strength;
                }
            }
            s_bestOpening = Mathf.Max(0f, gain0);
            float openingEnergy = 0f;
            for (int j = 0; j < Slots; j++) Used[j] = false;
            for (int op = 0; op < n; op++)
            {
                int picked = op == 0 ? best0 : best1;
                if (picked < 0) continue;
                Portals.Portal p = Portals.List[picked];
                // Match the SAME physical opening, not merely the same
                // facing direction. Two windows along one wall often have
                // identical normals. Favor world-space proximity, with
                // facing as an additional constraint. We compare against
                // the proposed opening position rather than list rank.
                Vector3 to = p.Point + p.Dir * OutsideBy;
                int slot = -1;
                float best = float.MaxValue;
                for (int j = 0; j < Slots; j++)
                {
                    if (Used[j] || !Placed[j]) continue;
                    float dot = Vector3.Dot(Dir[j], p.Dir);
                    if (dot < .65f) continue;
                    float delta = (At[j] - to).sqrMagnitude;
                    if (delta > 36f) continue; // a different opening, not a small scan wobble
                    float score = delta + (1f - dot) * 4f;
                    if (score < best) { best = score; slot = j; }
                }
                if (slot < 0)
                    for (int j = 0; j < Slots; j++)
                        if (!Used[j] && !Sounding(j)) { slot = j; Placed[j] = false; break; }
                if (slot < 0) continue;
                Used[slot] = true;
                LastSeen[slot] = Time.time;
                LastWorldGen[slot] = Occlusion.WorldGen;
                At[slot] = Placed[slot] ? Vector3.Lerp(At[slot], to, .5f) : to;
                Dir[slot] = p.Dir;
                Placed[slot] = true;
                float apertureGain = WeatherAcoustics.OpeningGain(
                    p.Share, p.Radius, (At[slot] - listener).magnitude);
                float width = Area.WidthAt(p.Radius, (At[slot] - listener).magnitude);
                // Close outdoor rainfall remains clear through an open
                // door even if most other room directions hit walls.
                // Rain uses its faster shelter transition; generic ambience
                // and wind keep their original, slower indoor fade.
                float rainInside = s_inside;
                // Rain is no longer artificially emitted by one/two door
                // sources. The separately measured ground-impact field
                // accounts for all exposed exterior directions instead.
                Play(slot, 0, amb, rainNow ? 0f :
                    apertureGain * rainInside, width, listener);
                Play(slot, 1, wind, apertureGain * s_inside, width, listener);
                // Opening sources reuse the original looping recording.
                // Trade the 2D diffuse bed for directional sound as real
                // apertures become dominant instead of duplicating its
                // full amplitude at the doorway.
                Copy ambientCopy = Copies[slot, 0];
                if (ambientCopy != null && ambientCopy.Target > 0f)
                    openingEnergy += ambientCopy.Target * ambientCopy.Target;
            }
            s_openingMixTarget = Mathf.Sqrt(Mathf.Clamp01(openingEnergy));
            for (int j = 0; j < Slots; j++)
                if (!Used[j])
                {
                    // One failed room measurement does not mean a window
                    // disappeared. Retain its position and original loop
                    // through short detection gaps, but never keep a window
                    // after the room transitions back to open air.
                    bool grace = Placed[j] && s_insideTarget > .05f &&
                        LastWorldGen[j] == Occlusion.WorldGen &&
                        Time.time - LastSeen[j] < OpeningGrace;
                    if (!grace)
                    {
                        Play(j, 0, amb, 0f, 0f, listener);
                        Play(j, 1, wind, 0f, 0f, listener);
                        if (!Sounding(j)) Placed[j] = false;
                    }
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
        private static void Bed(AudioSource src, float gain = 1f,
            float cutoff = 22000f, bool weatherBed = false)
        {
            if (src == null) return;
            gain = weatherBed ? gain : gain * s_gain;
            cutoff = weatherBed ? cutoff : Mathf.Min(cutoff, s_cutoff);
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null)
            {
                if (gain >= .999f && cutoff >= 20000f && !AcousticFilter.GlobalActive) return;
                f = src.gameObject.AddComponent<AcousticFilter>();
            }
            f.Kind = AcousticFilter.Kinds.Ambient;
            if (s_inside > 0f && f.Hrtf && src != null && AudioMan.instance != null && src != WindLoop(AudioMan.instance)) f.Hrtf = false;
            f.Set(gain, cutoff, false);
            bool want = f.Hrtf || gain < .999f || cutoff < 20000f ||
                        Mathf.Abs(f.SourceLevel - 1f) > .001f || AcousticFilter.GlobalActive;
            if (!f.enabled) f.enabled = true;
            f.Active = want;
        }

        private static float s_exposure = 1f;
        // Shared with Directions: the wind contribution to every spatial
        // source is reduced in the same lee used by our ambient wind.
        internal static float WindExposure => Room.WindSurveyReady ?
            Mathf.Clamp01(s_exposure) : 1f;
        private static Vector3 s_windWay;
        private static Vector3 s_exposureWindFrom;
        private static float s_insideTarget, s_exposureTarget = 1f;
        private static float s_bestOpening; // measured external access for debug reports
        private static float s_openingMixTarget, s_openingMix;
        private static AudioClip s_routedAmbientClip; // bound when the weather recording really starts
        private static AudioClip s_lastRainClip; // suppress rain copies while vanilla fades weather out
        private static float s_bedGain = -1f, s_bedCut = -1f, s_windGain = -1f, s_windCut = -1f;
        // Room measurements update discretely (~5 Hz). Give the
        // distributed exterior field its own smooth time response before
        // the existing 150 ms audio-thread filter glide. Ordinary placed
        // SFX and immediate build-piece geometry remain unchanged.
        private const float FieldTau = .38f;
        private static float s_fieldGain = 1f, s_fieldCutoff = 22000f;
        private static bool s_fieldPrimed;
        // Roof rays fluctuate when the 64-direction room survey intersects
        // a beam or seam. Attack fairly promptly, but hold the effect
        // perceptually by releasing more slowly across missed surveys.
        private const float RoofAttackTau = .42f, RoofReleaseTau = 1.25f;
        private static float s_roofPresence;

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
            // Fast enough to react to a new roof/door in about .25 s, while
            // the general environmental bed retains its original smoothness.
            s_openingMix = Mathf.MoveTowards(s_openingMix,
                s_openingMixTarget, 2f * dt);
            s_exposure += (s_exposureTarget - s_exposure) * (1f - Mathf.Exp(-dt / ExposureTau));
            float s = AcousticMath.OcclusionStrength;
            float k = Mathf.Min(1f, s) * s_inside;
            s_gain = Mathf.Clamp(Mathf.Lerp(1f, Mathf.Pow(Room.WallBed, s), s_inside), 0f, 1f);
            s_cutoff = AcousticMath.LogLerp(22000f, Occlusion.WallCut, k);
            // Rain is still striking exterior walls and the roof: preserve
            // the broad rainfall texture through the structure. Its original
            // audio loop remains continuous and filtered, never replaced by
            // a room-local synthetic sound or a binary indoor switch.
            // Valheim's wetness flag alone does not identify the AUDIO
            // currently playing. The active EnvSetup supplies the exact
            // environmental recording. Don't apply roof-rain coloration to
            // an unrelated ambient clip while weather clips crossfade.
            EnvSetup env = EnvMan.instance != null
                ? EnvMan.instance.GetCurrentEnvironment() : null;
            AudioSource outdoor = AmbientLoop(man);
            // Room jobs deliberately stop when the listener is stationary.
            // Valheim changes its ambient AudioClip independently of that
            // geometry. Rebind the existing opening sources from this
            // already-running audio update when the audible clip changes.
            // One identity comparison; no new frame hook, casts or poll job.
            if (Room.WindSurveyReady && s_insideTarget > .001f &&
                outdoor != null && outdoor.clip != null &&
                outdoor.isPlaying && outdoor.volume > .0001f &&
                !ReferenceEquals(outdoor.clip, s_routedAmbientClip))
                Update(man, Room.Enclosure);
            bool rainSelected = env != null && env.m_isWet &&
                env.m_ambientLoop != null;
            if (rainSelected) s_lastRainClip = env.m_ambientLoop;
            // The active weather recording remains Valheim's one loop.
            // Emitter geometry is virtual: the shared Batch wall/portal
            // solver computes its received spectral response per surface
            // patch and EnvironmentalField aggregates the result once.
            bool rainPlaying = s_lastRainClip != null &&
                outdoor != null && outdoor.clip == s_lastRainClip;
            float k2 = Mathf.Min(1f, s);
            float windGain = Mathf.Lerp(1f, Mathf.Lerp(.25f, 1f, s_exposure), k2);
            float windCut = AcousticMath.LogLerp(22000f, Mathf.Lerp(800f, 22000f, s_exposure), k2);
            // One physical emitter FIELD, one original Valheim recording.
            // Other ambient textures retain their existing wind/shelter
            // behavior until they register their own source domain.
            bool fieldActive = rainPlaying && EnvironmentalField.Ready;
            // Field validity can briefly drop between room surveys.
            // Keep the one rain clip and its roof coloration continuous.
            // The standard room bed is a temporary field fallback.
            if (rainPlaying)
            {
                if (!s_fieldPrimed)
                {
                    s_fieldGain = s_bedGain >= 0f ? s_bedGain : 1f;
                    s_fieldCutoff = s_bedCut >= 80f ? s_bedCut : 22000f;
                    s_fieldPrimed = true;
                }
                float fallbackGain = s_gain *
                    (1f - Mathf.Clamp01(s_openingMix));
                float fieldGainTarget = fieldActive ?
                    EnvironmentalField.Gain : fallbackGain;
                float fieldCutTarget = fieldActive ?
                    EnvironmentalField.Cutoff : s_cutoff;
                float response = 1f - Mathf.Exp(
                    -Mathf.Max(0f, dt) / FieldTau);
                s_fieldGain = Mathf.Lerp(s_fieldGain,
                    fieldGainTarget, response);
                s_fieldCutoff = AcousticMath.LogLerp(
                    s_fieldCutoff, fieldCutTarget, response);
            }
            else s_fieldPrimed = false;

            // Same old built-roof response, but mixed *after* the general
            // environment field, using ONE original rain recording.
            // Asymmetric exponential transition avoids intermittent roof
            // patter "starting/stopping" after a missed room ray.
            float roofTarget = rainPlaying ?
                WeatherAcoustics.RoofRainPresence(
                    Room.BuiltRoof, Room.Enclosure) : 0f;
            float roofTau = roofTarget > s_roofPresence ?
                RoofAttackTau : RoofReleaseTau;
            float roofResponse = 1f - Mathf.Exp(
                -Mathf.Max(0f, dt) / roofTau);
            s_roofPresence = Mathf.Lerp(s_roofPresence,
                roofTarget, roofResponse);
            float rainGain = s_fieldGain, rainCut = s_fieldCutoff;
            if (rainPlaying)
                WeatherAcoustics.BlendRoof(
                    s_fieldGain, s_fieldCutoff, s_roofPresence,
                    out rainGain, out rainCut);
            float ambientCut = rainPlaying ?
                rainCut :
                Plugin.Wind.Value &&
                IsWindTexture(outdoor != null ? outdoor.clip : null) ?
                Mathf.Min(s_cutoff, windCut) : s_cutoff;
            float diffuseBed = rainPlaying ?
                rainGain :
                s_gain * (1f - Mathf.Clamp01(s_openingMix));
            if (Mathf.Abs(diffuseBed - s_bedGain) > 1e-4f ||
                Mathf.Abs(ambientCut - s_bedCut) > .5f)
            {
                s_bedGain = diffuseBed; s_bedCut = ambientCut;
                Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);
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
                    c.Level = Mathf.MoveTowards(c.Level, c.Target,
                        2f * dt);
                    if (c.Level <= 0f && c.Target <= 0f) { c.Src.Stop(); continue; }
                    c.Src.volume = (c.Game != null ? c.Game.volume : 0f) * c.Level;
                    // Wind copies share the same authored gust/direction gain,
                    // without ever writing back into vanilla's source volume.
                    if (w == 1 && c.F != null) c.F.SourceLevel = s_windModulation;
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
            EnvironmentalField.Reset();
            RandomAmbients.Clear();
            s_gain = 1f; s_cutoff = 22000f; s_inside = 0f; s_windLean = 0f; s_windModulation = 1f; s_insideAt = -1f;
 
            s_insideTarget = 0f; s_bestOpening = 0f;
            s_openingMixTarget = s_openingMix = 0f;
            s_routedAmbientClip = s_lastRainClip = null;
            s_identifiedWindClip = null; s_identifiedIsWind = false;
            s_exposureWindFrom = Vector3.zero; s_windWay = Vector3.zero;
            s_exposureTarget = s_exposure = 1f;
            s_bedGain = s_bedCut = s_windGain = s_windCut = -1f;
            s_fieldGain = 1f; s_fieldCutoff = 22000f;
            s_fieldPrimed = false;
            s_roofPresence = 0f;
            for (int o = 0; o < Slots; o++)
            {
                Placed[o] = false;
                LastSeen[o] = 0f;
                LastWorldGen[o] = 0;
                for (int w = 0; w < 2; w++)
                {
                    Copy c = Copies[o, w];
                    if (c != null && c.Src != null) c.Src.Stop();
                    if (c != null && c.F != null) c.F.SourceLevel = 1f;
                }
            }
            if (AudioMan.instance == null) return;
            foreach (AudioSource src in new[] { AmbientLoop(AudioMan.instance), WindLoop(AudioMan.instance) })
                if (src != null && src.GetComponent<AcousticFilter>() is AcousticFilter f)
                {
                    f.Set(1f, 22000f, false);
                    f.SourceLevel = 1f;
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

        internal static string Summary() => "ambience gain " + s_gain.ToString("F2") + ", low-pass " + s_cutoff.ToString("F0") + " Hz, inside " + s_inside.ToString("F2") + ", nearest opening access " + s_bestOpening.ToString("F2") + ", routed mix " + s_openingMix.ToString("F2") + ", " + EnvironmentalField.Summary() + ", roof rain mix " + s_roofPresence.ToString("F2") + ", random one-shots " + RandomAmbients.Count;
    }
}
