using UnityEngine;

namespace WiresAcoustics
{
    // Rain is an extended, continuously renewed *ground impact field*, not
    // one distant "weather sound" behind an average room wall. We do not
    // simulate droplets or allocate AudioSources per impact: at most four
    // audible sectors represent many conceptual impacts on exposed outdoor
    // ground. They play decorrelated portions of Valheim's own rain texture.
    //
    // The positions/visibility come from the EXACT SAME 64 room rays already
    // cast for room acoustics. Every finished measurement rebuilds the eight
    // azimuth regions. No additional raycasts, physics jobs, MonoBehaviour
    // frame callbacks, rain particles, or synthetic audio.
    internal static class RainField
    {
        internal const int Sectors = 8;
        internal const int MaxAudible = 4;
        private const float Far = 8f; // representative outdoor impact range, not a hard wall distance
        private const float EarToGround = 1.5f;
        private static readonly float[] Open = new float[Sectors];
        private static readonly float[] Total = new float[Sectors];
        private static readonly float[] Weight = new float[Sectors];
        private static readonly Vector3[] Bearings = new Vector3[Sectors];
        private static readonly Vector3[] At = new Vector3[Sectors];
        private static readonly float[] Level = new float[Sectors];
        private static readonly AudioSource[] Sources = new AudioSource[Sectors];
        private static readonly AcousticFilter[] Filters = new AcousticFilter[Sectors];
        private static GameObject s_root;
        private static AudioSource s_bridge;
        private static AcousticFilter s_bridgeFilter;
        private static float s_bridgeLevel, s_lastVolume;
        private static bool s_ready;
        private static float s_visible;

        // Pure, testable mapping from the measured angle-direction to
        // conceptual outdoor ground regions. An exposed ray is open to the
        // environment, unlike a ray stopped at a wall or beneath the roof.
        internal static int Sector(Vector3 d)
        {
            float a = Mathf.Atan2(d.z, d.x);
            if (a < 0f) a += 2f * Mathf.PI;
            return Mathf.Min(Sectors - 1, (int)(a * Sectors / (2f * Mathf.PI)));
        }

        internal static bool ImpactRay(Vector3 d) =>
            d.y >= -.24f && d.y <= .65f;

        internal static float Exposure(float open, float total) =>
            total > .001f ? Mathf.Sqrt(Mathf.Clamp01(open / total)) : 0f;

        internal static void Measure(Vector3 from, Vector3[] dirs)
        {
            for (int i = 0; i < Sectors; i++)
            {
                Open[i] = Total[i] = 0f;
                Bearings[i] = Vector3.zero;
            }
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 d = dirs[i];
                if (!ImpactRay(d)) continue;
                int s = Sector(d);
                // Favor level rays (outdoor rain on the ground), but include
                // diagonally elevated rays reaching outside through doors.
                float w = 1f - .35f * Mathf.Abs(d.y);
                Total[s] += w;
                if (!Room.Out(i)) continue;
                Open[s] += w;
                Bearings[s] += new Vector3(d.x, 0f, d.z).normalized * w;
            }

            // Select strongest four from the fixed 8 sectors without sorting
            // allocations. More voices cannot amplify the original weather
            // recording: normalized sum-of-squared channel weights <= 1.
            for (int i = 0; i < Sectors; i++)
                Weight[i] = Exposure(Open[i], Total[i]);
            for (int keep = MaxAudible; keep < Sectors; keep++)
            {
                int weakest = -1;
                float least = float.MaxValue;
                for (int i = 0; i < Sectors; i++)
                {
                    if (Weight[i] <= 0f || Weight[i] >= least) continue;
                    least = Weight[i]; weakest = i;
                }
                if (weakest >= 0) Weight[weakest] = 0f;
            }

            float energy = 0f;
            for (int i = 0; i < Sectors; i++) energy += Weight[i] * Weight[i];
            float normalizer = energy > 1f ? 1f / Mathf.Sqrt(energy) : 1f;
            s_visible = 0f;
            for (int i = 0; i < Sectors; i++)
            {
                Weight[i] *= normalizer;
                s_visible += Weight[i] * Weight[i];
                Vector3 d = Bearings[i].sqrMagnitude > 1e-5f ?
                    Bearings[i].normalized : new Vector3(
                        Mathf.Cos(2f * Mathf.PI * (i + .5f) / Sectors),
                        0f, Mathf.Sin(2f * Mathf.PI * (i + .5f) / Sectors));
                // The source is an outdoor ground patch in the open cone,
                // not the indoor listener's position or a roof impact.
                At[i] = from + d * Far - Vector3.up * EarToGround;
                if (Sources[i] != null) Sources[i].transform.position = At[i];
            }
            s_visible = Mathf.Sqrt(s_visible);
            s_ready = true;
        }

        // Called within the already-existing Ambience.Glide/AudioMan.Update.
        // No new update handler or native physics. Four regions are
        // selected per survey; old sources may overlap while fading for
        // a maximum of 250 ms when the four regions change.
        internal static float Glide(AudioSource original, AudioClip chosenRain,
            float weatherVolume, bool rainSelected, bool playingRain,
            float enclosure, float indirectGain, float indirectCutoff, float dt)
        {
            // Source existence follows EnvMan weather, not the delayed game
            // ambient AudioSource clip swap. No conceptual outdoor rain patch
            // needs to be visible for the indirect diffuse rain to continue.
            bool rain = rainSelected && chosenRain != null && original != null;
            if (rain) s_lastVolume = Mathf.Clamp01(weatherVolume);
            float indoor = Mathf.Clamp01(enclosure * 4f);
            float active = rain && s_ready ? .75f * indoor : 0f;
            float energy = 0f;
            for (int i = 0; i < Sectors; i++)
            {
                float desired = Weight[i] * active;
                float current = Level[i];
                current = Mathf.MoveTowards(current, desired, 6f * Mathf.Max(0f, dt));
                Level[i] = current;
                if (current <= .0001f)
                {
                    if (Sources[i] != null && Sources[i].isPlaying) Sources[i].Stop();
                    continue;
                }
                if (Sources[i] == null) Create(i);
                AudioSource src = Sources[i];
                // On rain exit, retain the former rain recording through
                // its fade. Never try to Play a null weather AudioClip.
                if (rain && (src.clip != chosenRain || !src.isPlaying))
                {
                    src.Stop();
                    src.clip = chosenRain;
                    src.outputAudioMixerGroup = original.outputAudioMixerGroup;
                    // The recording is a stochastic rain texture; offset
                    // each sector by a fixed phase to avoid coherent, comb-
                    // filtered copies from identical simultaneous samples.
                    int samples = Mathf.Max(1, chosenRain.samples);
                    int offset = (int)((long)samples * (i + 1) / (Sectors + 1));
                    int baseSample = playingRain ?
                        Mathf.Clamp(original.timeSamples, 0, samples - 1) : 0;
                    src.timeSamples = (int)(((long)baseSample + offset) % samples);
                    src.Play();
                }
                src.volume = s_lastVolume * current;
                src.pitch = original.pitch;
                AcousticFilter f = Filters[i];
                f.Hrtf = Plugin.Hrtf.Value;
                if (f.Hrtf)
                {
                    f.Width = .55f; // broad patch of many droplets, not a point
                    f.SetDirection(Hearing.Camera != null ?
                        Hearing.Camera.InverseTransformDirection(
                            At[i] - Hearing.At) : At[i] - Hearing.At);
                }
                if (!f.enabled) f.enabled = true;
                f.Active = f.NeedsWork;
                energy += current * current;
            }
            // Before Valheim completes its own ambient-loop crossfade,
            // keep a continuous 2D filtered rain bed from the selected
            // recording. It then yields progressively to Valheim's clip.
            // No weather-related delay is imposed by audio clip identity.
            float need = WeatherAcoustics.BridgeNeeded(rain,
                playingRain, original != null ? original.volume : 0f,
                weatherVolume);
            s_bridgeLevel = Mathf.MoveTowards(s_bridgeLevel, need,
                4f * Mathf.Max(0f, dt));
            if (s_bridgeLevel <= .0001f)
            {
                if (s_bridge != null && s_bridge.isPlaying) s_bridge.Stop();
            }
            else if (rain)
            {
                if (s_bridge == null) CreateBridge();
                if (s_bridge.clip != chosenRain || !s_bridge.isPlaying)
                {
                    s_bridge.Stop();
                    s_bridge.clip = chosenRain;
                    s_bridge.outputAudioMixerGroup = original.outputAudioMixerGroup;
                    s_bridge.Play();
                }
                s_bridge.volume = s_lastVolume * s_bridgeLevel;
                s_bridge.pitch = original.pitch;
                s_bridgeFilter.Set(Mathf.Clamp01(indirectGain),
                    Mathf.Clamp(indirectCutoff, 80f, 22000f), false);
                s_bridgeFilter.Active = s_bridgeFilter.NeedsWork;
            }
            // Also fade when the selected weather stops being rainy.
            // Previously it held its old volume until an abrupt Stop().
            if (s_bridge != null && s_bridge.isPlaying)
                s_bridge.volume = s_lastVolume * s_bridgeLevel;
            return Mathf.Sqrt(Mathf.Clamp01(energy));
        }

        private static void EnsureRoot()
        {
            if (s_root != null) return;
            s_root = new GameObject("WiresAcousticsRainField");
            Object.DontDestroyOnLoad(s_root);
        }

        private static void CreateBridge()
        {
            EnsureRoot();
            GameObject go = new GameObject("rain_indirect_weather_bridge");
            go.transform.SetParent(s_root.transform, false);
            s_bridge = go.AddComponent<AudioSource>();
            s_bridge.loop = true;
            s_bridge.playOnAwake = false;
            s_bridge.spatialBlend = 0f;
            s_bridge.bypassReverbZones = true;
            s_bridge.priority = 16;
            s_bridgeFilter = go.AddComponent<AcousticFilter>();
            s_bridgeFilter.Kind = AcousticFilter.Kinds.Ambient;
            s_bridgeFilter.Hrtf = false;
        }

        private static void Create(int i)
        {
            EnsureRoot();
            GameObject go = new GameObject("rain_outdoor_ground_" + i);
            go.transform.SetParent(s_root.transform, false);
            go.transform.position = At[i];
            AudioSource src = go.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1000f; src.maxDistance = 1001f;
            src.dopplerLevel = 0f;
            src.bypassReverbZones = true;
            src.priority = 16;
            Sources[i] = src;
            AcousticFilter f = go.AddComponent<AcousticFilter>();
            f.Kind = AcousticFilter.Kinds.Outside;
            Filters[i] = f;
        }

        internal static string Summary() =>
            "rain-field rays " + s_visible.ToString("F2") +
            ", active " + ActiveCount() +
            ", indirect bridge " + s_bridgeLevel.ToString("F2");

        private static int ActiveCount()
        {
            int n = 0;
            for (int i = 0; i < Sectors; i++)
                if (Level[i] > .0001f) n++;
            return n;
        }

        internal static void Reset()
        {
            s_ready = false; s_visible = 0f;
            s_bridgeLevel = s_lastVolume = 0f;
            if (s_bridge != null) s_bridge.Stop();
            s_bridge = null; s_bridgeFilter = null;
            for (int i = 0; i < Sectors; i++)
            {
                Open[i] = Total[i] = Weight[i] = Level[i] = 0f;
                Bearings[i] = At[i] = Vector3.zero;
                if (Sources[i] != null) Sources[i].Stop();
                Sources[i] = null;
                Filters[i] = null;
            }
            if (s_root != null) Object.Destroy(s_root);
            s_root = null;
        }
    }
}
