using System;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    // A short, distinct ascending chime when an idol survives the
    // resource-consumption roll. It is independent of the jackpot effect.
    internal static class ForgeIdolSound
    {
        private static AudioClip _clip;
        private static AudioSource _source;
        private static Player _owner;

        internal static void Play(Player player)
        {
            if (player == null)
                return;

            try
            {
                if (_clip == null)
                    _clip = CreateClip();

                if (_source == null || _owner != player)
                {
                    _source = player.gameObject.AddComponent<AudioSource>();
                    _owner = player;
                    _source.playOnAwake = false;
                    _source.loop = false;
                    _source.spatialBlend = 0f;
                    _source.volume = 0.42f;

                    if (AudioMan.instance != null)
                        _source.outputAudioMixerGroup =
                            AudioMan.instance.m_guiMixer;
                }

                _source.PlayOneShot(_clip);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "Goodtential: idol preservation sound failed: " +
                    ex.Message);
            }
        }

        private static AudioClip CreateClip()
        {
            const int sampleRate = 44100;
            const float duration = .34f;
            int count = (int)(sampleRate * duration);

            AudioClip clip = AudioClip.Create(
                "Goodtential - Idol Preserved",
                count, 1, sampleRate, false);

            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / sampleRate;
                double n1 = Tone(t, 0d, .18d, 659.25d);
                double n2 = Tone(t, .11d, .22d, 987.77d);
                samples[i] = (float)(.36d * n1 + .30d * n2);
            }

            // Resolve the float[] overload explicitly. Newer Unity
            // also exposes a ReadOnlySpan overload unavailable in net472.
            var setData = typeof(AudioClip).GetMethod(
                "SetData",
                new Type[] { typeof(float[]), typeof(int) });

            if (setData == null)
                throw new MissingMethodException(
                    "AudioClip.SetData(float[], int)");

            setData.Invoke(clip, new object[] { samples, 0 });
            return clip;
        }

        private static double Tone(
            double time, double start,
            double duration, double frequency)
        {
            double t = time - start;
            if (t < 0d || t >= duration)
                return 0d;

            double attack = Math.Min(1d, t / .012d);
            double decay = Math.Min(1d,
                (duration - t) / .065d);
            double envelope = attack * decay;

            return envelope *
                (Math.Sin(2d * Math.PI * frequency * t) +
                 .20d * Math.Sin(2d * Math.PI * frequency * 2d * t));
        }

        internal static void Cleanup()
        {
            if (_source != null)
                UnityEngine.Object.Destroy(_source);

            if (_clip != null)
                UnityEngine.Object.Destroy(_clip);

            _source = null;
            _owner = null;
            _clip = null;
        }
    }
}
