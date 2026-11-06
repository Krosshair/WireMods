using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace WiresDamageVignette
{
    // Preserve the pitch/timbre of the actual recorded heartbeats.
    // BPM is controlled by WHEN intact beats are triggered, never by
    // resampling the heartbeat or changing AudioSource.pitch.
    internal sealed class HeartbeatSound : MonoBehaviour
    {
        private const string ResourceName = "WiresDamageVignette.heartbeat.wav";
        private const float OriginalBpm = 67f;
        private const float CriticalBpm = 110f;
        private const float BeatClipSeconds = 0.66f;
        private const float ClipFadeInSeconds = 0.006f;
        private const float ClipFadeOutSeconds = 0.040f;

        // Original recording: 8.98 seconds, 44.1 kHz, stereo, ten
        // complete lub-dub cycles. The onsets below were measured from
        // the waveform and begin just before each full recorded beat.
        // The clip is NEVER modified on disk.
        private static readonly float[] BeatOnsets =
        {
            0.18f, 1.08f, 1.96f, 2.86f, 3.80f,
            4.65f, 5.53f, 6.41f, 7.30f, 8.24f
        };

        private AudioSource _source;
        private AudioClip _recording;
        private AudioClip[] _beatClips;
        private bool _ready;
        private bool _usingOneShots;
        private bool _wasActive;
        private float _secondsUntilBeat;
        private int _nextBeatIndex;
        private readonly System.Random _naturalRandom =
            new System.Random(unchecked(Environment.TickCount ^ 0x56A91));
        private ManualLogSource _log;

        private void Awake()
        {
            _log = BepInEx.Logging.Logger.CreateLogSource(
                "Wire's Health Awareness Audio");
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.pitch = 1f;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.priority = 96;
            _source.volume = 0f;
            _source.ignoreListenerPause = false;

            // This licensed WAV is embedded as a .NET resource inside
            // WiresHealthAwareness.dll. Never opens any URL, web
            // request or external audio file.
            LoadEmbeddedAudio();
        }

        private void LoadEmbeddedAudio()
        {
            try
            {
                int sampleRate;
                int channels;
                float[] samples;
                using (Stream stream = typeof(Plugin).Assembly
                    .GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        _log.LogError(
                            "Embedded heartbeat audio resource missing: " +
                            ResourceName);
                        return;
                    }

                    samples = ReadPcm16Wave(stream,
                        out sampleRate, out channels);
                }

                _recording = MakeClip(
                    "Wire_HealthAwareness_Heartbeat_Original",
                    samples, channels, sampleRate);
                if (_recording == null)
                {
                    _log.LogError(
                        "Couldn't create heartbeat AudioClip from " +
                        "the embedded recording.");
                    return;
                }

                try
                {
                    _usingOneShots = CreateNativePitchBeats(
                        samples, sampleRate, channels);
                }
                catch (Exception exception)
                {
                    _usingOneShots = false;
                    _log.LogWarning(
                        "Embedded heartbeat slicing failed; " +
                        "falling back to the original 67-BPM " +
                        "recording at natural pitch: " +
                        exception.Message);
                }

                if (!_usingOneShots)
                {
                    ReleaseBeatClips();
                    _source.clip = _recording;
                    _source.loop = true;
                    _source.pitch = 1f;
                    _log.LogWarning(
                        "Using embedded original-speed 67-BPM " +
                        "heartbeat fallback.");
                }

                _ready = true;
                _log.LogInfo("Embedded heartbeat ready: " +
                    _recording.length.ToString("F2") + "s, " +
                    _recording.channels + " channel(s); " +
                    (_usingOneShots
                        ? "native-pitch variable BPM, 10 recorded beats."
                        : "original-speed fallback."));
            }
            catch (Exception exception)
            {
                _ready = false;
                ReleaseBeatClips();
                if (_recording != null)
                {
                    Destroy(_recording);
                    _recording = null;
                }
                _log.LogError("Failed to load embedded heartbeat: " +
                    exception);
            }
        }

        private static AudioClip MakeClip(string name,
            float[] samples, int channels, int sampleRate)
        {
            if (samples == null || channels <= 0 ||
                sampleRate <= 0 || samples.Length == 0 ||
                samples.Length % channels != 0)
                return null;

            AudioClip result = AudioClip.Create(name,
                samples.Length / channels, channels, sampleRate, false);
            if (result == null)
                return null;

            if (!SetSamples(result, samples))
            {
                Destroy(result);
                return null;
            }
            return result;
        }

        private static bool SetSamples(AudioClip clip, float[] samples)
        {
            // Resolve the float[] overload explicitly to avoid the
            // newer System.Memory Span overload in net472 builds.
            System.Reflection.MethodInfo method = typeof(AudioClip)
                .GetMethod("SetData", new[] {
                    typeof(float[]), typeof(int)
                });
            return method != null &&
                (bool)method.Invoke(clip, new object[] { samples, 0 });
        }
        private bool CreateNativePitchBeats(
            float[] recordingSamples, int sampleRate, int channels)
        {
            int framesInClip = recordingSamples.Length / channels;
            if (sampleRate <= 0 || channels <= 0 || framesInClip <= 0)
                return false;

            _beatClips = new AudioClip[BeatOnsets.Length];
            int framesPerBeat = Mathf.RoundToInt(
                BeatClipSeconds * sampleRate);
            int fadeIn = Mathf.Max(1,
                Mathf.RoundToInt(ClipFadeInSeconds * sampleRate));
            int fadeOut = Mathf.Max(1,
                Mathf.RoundToInt(ClipFadeOutSeconds * sampleRate));

            for (int i = 0; i < BeatOnsets.Length; ++i)
            {
                int beginFrame = Mathf.RoundToInt(
                    BeatOnsets[i] * sampleRate);
                if (beginFrame < 0 ||
                    beginFrame + framesPerBeat > framesInClip)
                    return false;

                float[] oneBeat = new float[framesPerBeat * channels];
                Array.Copy(recordingSamples, beginFrame * channels,
                    oneBeat, 0, oneBeat.Length);

                // Only taper the very beginning/end of the excerpt.
                // Its attack and double beat remain completely intact.
                // This also prevents clicks when tails overlap at high BPM.
                for (int frame = 0; frame < framesPerBeat; ++frame)
                {
                    float gain = Mathf.Min(1f,
                        frame / (float)fadeIn,
                        (framesPerBeat - 1 - frame) /
                        (float)fadeOut);
                    for (int c = 0; c < channels; ++c)
                        oneBeat[frame * channels + c] *= gain;
                }

                AudioClip beat = MakeClip(
                    "Wire_RecordedBeat_" + (i + 1),
                    oneBeat, channels, sampleRate);
                if (beat == null)
                    return false;
                _beatClips[i] = beat;
            }

            return true;
        }

        private static float[] ReadPcm16Wave(Stream stream,
            out int sampleRate, out int channels)
        {
            sampleRate = 0;
            channels = 0;
            ushort format = 0;
            ushort bits = 0;
            byte[] pcm = null;
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (new string(reader.ReadChars(4)) != "RIFF")
                    throw new InvalidDataException("Expected RIFF WAV.");
                reader.ReadUInt32();
                if (new string(reader.ReadChars(4)) != "WAVE")
                    throw new InvalidDataException("Expected WAVE.");

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = new string(reader.ReadChars(4));
                    uint length = reader.ReadUInt32();
                    long end = stream.Position + length;
                    if (end > stream.Length)
                        throw new InvalidDataException(
                            "Corrupt WAV chunk length.");

                    if (chunk == "fmt ")
                    {
                        if (length < 16)
                            throw new InvalidDataException("Bad WAV fmt.");
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                    }
                    else if (chunk == "data")
                    {
                        if (length > int.MaxValue)
                            throw new InvalidDataException("Oversized WAV.");
                        pcm = reader.ReadBytes((int)length);
                    }

                    stream.Position = end + (length & 1);
                }
            }

            if (format != 1 || bits != 16 || pcm == null ||
                channels <= 0 || sampleRate <= 0 ||
                pcm.Length % (channels * 2) != 0)
                throw new InvalidDataException(
                    "Heartbeat file must be PCM 16-bit WAV.");

            short[] input = new short[pcm.Length / 2];
            Buffer.BlockCopy(pcm, 0, input, 0, pcm.Length);
            float[] output = new float[input.Length];
            for (int i = 0; i < input.Length; i++)
                output[i] = input[i] / 32768f;
            return output;
        }

        // volume and loudness curve retain their previous behavior.
        // BPM is now a scheduling interval; every sample plays at pitch 1.
        internal void UpdateSound(bool active, float severity, float volume,
            VolumeCurve curve, float targetBpm, float variationPercent)
        {
            if (_source == null || !_ready)
                return;

            float exponent = Mathf.Clamp01(severity);
            float endGain = 0.93f * 0.93f / 0.58f;
            float gain = curve == VolumeCurve.Exponential
                ? 0.58f * Mathf.Pow(0.93f / 0.58f, 2f * exponent)
                : Mathf.Lerp(0.58f, endGain, exponent);
            // Existing loudness scale; retain the user's saved setting.
            float desired = active
                ? Mathf.Clamp01(volume * 2.4f * gain)
                : 0f;

            // Use Valheim's SFX / GUI mixer and the master/SFX sliders.
            if (AudioMan.instance != null &&
                AudioMan.instance.m_guiMixer != null)
                _source.outputAudioMixerGroup =
                    AudioMan.instance.m_guiMixer;

            // Audible promptly on sudden severe hits, and return to
            // silence quickly after the stress subsides. At low HP,
            // desired stays > 0, so it settles to the normal heartbeat.
            float volumeSlew = active ? 3.0f : 2.7f;
            _source.volume = Mathf.MoveTowards(_source.volume,
                desired, Time.unscaledDeltaTime * volumeSlew);

            if (active)
            {
                if (!_wasActive)
                {
                    _secondsUntilBeat = 0f;
                    _wasActive = true;
                }

                if (_usingOneShots)
                {
                    _secondsUntilBeat -= Time.unscaledDeltaTime;
                    if (_secondsUntilBeat <= 0f)
                    {
                        // One complete pre-recorded beat at ORIGINAL pitch.
                        _source.PlayOneShot(_beatClips[_nextBeatIndex], 1f);
                        _nextBeatIndex =
                            (_nextBeatIndex + 1) % _beatClips.Length;

                        // Respiratory-like gentle drift + very small
                        // beat-to-beat variation; neither changes pitch.
                        float variation = Mathf.Clamp(
                            variationPercent, 0f, 8f) * 0.01f;
                        float slowDrift = Mathf.Sin(
                            Time.unscaledTime * (2f * Mathf.PI / 5.2f));
                        float microVariation =
                            (float)(_naturalRandom.NextDouble() * 2.0 - 1.0);
                        float change = variation *
                            (0.75f * slowDrift + 0.25f * microVariation);
                        float bpm = Mathf.Clamp(targetBpm, OriginalBpm,
                            150f) * (1f + change);

                        float secondsPerBeat = 60f /
                            Mathf.Max(45f, bpm);
                        // Carry over small frame timing errors without
                        // generating a pile of beats after a lag spike.
                        _secondsUntilBeat = secondsPerBeat +
                            Mathf.Max(-0.05f, _secondsUntilBeat);
                    }
                }
                else if (!_source.isPlaying)
                {
                    // Audible fallback: no slicing, no acceleration.
                    _source.pitch = 1f;
                    _source.Play();
                }
            }
            else
            {
                _wasActive = false;
                _secondsUntilBeat = 0f;
                if (_source.isPlaying && _source.volume <= 0.0001f)
                    _source.Stop();
            }
        }

        private void ReleaseBeatClips()
        {
            if (_beatClips == null)
                return;
            foreach (AudioClip clip in _beatClips)
                if (clip != null)
                    Destroy(clip);
            _beatClips = null;
        }

        internal void StopOnDeath()
        {
            if (_source == null) return;
            _wasActive = false;
            _secondsUntilBeat = 0f;
            _source.volume = Mathf.MoveTowards(_source.volume, 0f,
                Mathf.Max(Time.unscaledDeltaTime, 1f / 240f) * 12.5f);
            if (_source.volume <= 0.001f)
            {
                _source.volume = 0f;
                _source.Stop();
            }
        }
        internal void StopNow()
        {
            if (_source == null)
                return;
            _source.volume = 0f;
            _source.Stop();
            _wasActive = false;
            _secondsUntilBeat = 0f;
        }

        private void OnDestroy()
        {
            StopNow();

            ReleaseBeatClips();
            if (_recording != null)
                Destroy(_recording);
            if (_log != null)
                BepInEx.Logging.Logger.Sources.Remove(_log);
        }
    }
}
