using System.Collections;
using UnityEngine;

namespace WiresAcoustics
{
    // How loud Unity's reverb actually is. The physics says how much reverberant energy a sound should
    // have next to its direct energy; Unity's reverb unit isn't specified in those terms, so it's
    // measured: with every game sound muted (not paused: pausing the listener pauses the reverb unit
    // too, the first try measured nothing), a short noise burst plays straight to you, once dry and
    // then into the reverb at several send levels, and the game's final output is summed.
    // Reverb energy = total - dry. Results go to the log; the constants below are set from them.
    internal static class Calibration
    {
        // The reverb unit's setting the measurements are made at (and Room keeps it there).
        internal const float Decay = 1f, Late = 1000f, Reflections = 0f;

        // Measured in game (decay 1 s, the levels above): at a send of 1 the reverb carries 10.4 times
        // the direct energy (+10.2 dB), and it scales with the send squared (1 -> 0.5: 3.96 times less).
        private const float FullSend = 10.4f, Power = 2f;
        // Unity's tail gains little energy from a longer decay: 2 s at -3 dB measured 2.6 dB under 1 s,
        // so energy ~ decay ^ 0.13 and the start is lowered by that much to keep it the same.
        internal const float TailExponent = .13f;

        // The send that gives a reverberant / direct energy ratio of ratio^2.
        internal static float MixFor(float ratio)
        {
            float energy = ratio * ratio / FullSend;
            return Mathf.Clamp(Mathf.Pow(energy, 1f / Power), 0f, 1f);
        }

        internal static bool Running;

        internal static void Start()
        {
            if (Running || Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            AudioListener listener = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            AudioReverbZone zone = Room.Zone;
            if (listener == null || zone == null || !zone.enabled)
            {
                Plugin.Log.LogInfo("[acoustics] reverb measurement: needs the game loaded with Room reverb on");
                Plugin.Calibrate.Value = false;
                yield break;
            }
            Running = true;
            var go = new GameObject("WiresAcousticsReverbTest");
            go.transform.SetParent(listener.transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            int rate = AudioSettings.outputSampleRate;
            var rng = new System.Random(1);
            float[] noise = new float[rate / 4];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * .25f;
            int at = 0;
            AudioClip clip = AudioClip.Create("burst", noise.Length, 1, rate, false, data =>
            {
                for (int i = 0; i < data.Length; i++) data[i] = at < noise.Length ? noise[at++] : 0f;
            });
            src.clip = clip;
            src.playOnAwake = false;
            // played like a game sound: in the world (half a metre ahead, at full volume there) and
            // through the game's own sound-effects mixer group (a source outside it didn't reach the
            // reverb at all on the second try)
            go.transform.localPosition = new Vector3(0f, 0f, .5f);
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 5f;
            src.maxDistance = 50f;
            src.dopplerLevel = 0f;
            UnityEngine.Audio.AudioMixerGroup sfx = null;
            foreach (ZSFX z in Object.FindObjectsByType<ZSFX>(FindObjectsSortMode.None))
            {
                AudioSource zs = z != null ? z.GetComponent<AudioSource>() : null;
                if (zs != null && zs.outputAudioMixerGroup != null && zs.spatialBlend > .5f) { sfx = zs.outputAudioMixerGroup; break; }
            }
            src.outputAudioMixerGroup = sfx;
            var muted = new System.Collections.Generic.List<AudioSource>();
            void Mute()
            {
                foreach (AudioSource a in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                    if (a != null && a != src && !a.mute) { a.mute = true; muted.Add(a); }
            }
            // The listener's own filter turned out to sit before Unity adds the reverb (it never saw any),
            // so the final output is read instead: the last 1024 samples of each channel every frame,
            // their mean power times the frame's time.
            float[] buf = new float[1024];
            double output = 0d;
            bool reading = false;
            IEnumerator Wait(float seconds)
            {
                float end = Time.realtimeSinceStartup + seconds, last = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup < end)
                {
                    Mute();
                    yield return null;
                    float now = Time.realtimeSinceStartup;
                    if (reading)
                    {
                        double p = 0d;
                        for (int c = 0; c < 2; c++)
                        {
                            AudioListener.GetOutputData(buf, c);
                            for (int i = 0; i < buf.Length; i++) p += buf[i] * buf[i];
                        }
                        output += p / buf.Length * (now - last);
                    }
                    last = now;
                }
            }

            var sb = new System.Text.StringBuilder("[acoustics] reverb measurement (mixer group " + (sfx != null ? sfx.name : "none") + ", zone " + (zone.enabled ? "on" : "off") + "; decay, send: reverb energy / direct energy):");
            double dry = 0d;
            // what's left with nothing playing (should be ~0)
            zone.room = -10000;
            yield return Wait(1.5f);
            output = 0d; reading = true;
            yield return Wait(2.55f);
            reading = false;
            double floor = output;
            sb.Append(" silence " + floor.ToString("E3") + ";");
            float[] decays = { 1f, 2f };
            float[] sends = { 0f, 1f, .5f, .25f, .125f };
            foreach (float decay in decays)
            {
                float tail = -1000f * TailExponent * Mathf.Log10(decay / Decay);
                zone.room = 0; zone.roomHF = 0;
                zone.decayTime = decay; zone.decayHFRatio = 1f;
                zone.reflections = (int)(Reflections + tail); zone.reflectionsDelay = .01f;
                zone.reverb = (int)(Late + tail); zone.reverbDelay = .02f;
                foreach (float send in sends)
                {
                    if (send == 0f && decay != decays[0]) continue;
                    src.bypassReverbZones = send == 0f;
                    src.reverbZoneMix = send;
                    yield return Wait(decay * 2f + .5f); // the last tail gone
                    output = 0d;
                    reading = true;
                    src.Play();
                    yield return Wait(.25f + decay * 2f + .3f);
                    reading = false;
                    double e = output - floor * (.25f + decay * 2f + .3f) / 2.55f;
                    sb.Append(" [" + output.ToString("E3") + "]");
                    if (send == 0f) { dry = e; sb.Append(" dry " + e.ToString("E3") + ";"); continue; }
                    double wet = System.Math.Max(0d, e - dry) / System.Math.Max(1e-12, dry);
                    sb.Append(" " + decay.ToString("F0") + " s, " + send.ToString("F3") + ": " + wet.ToString("F3") + " (" + (10d * System.Math.Log10(System.Math.Max(1e-9, wet))).ToString("F1") + " dB);");
                }
            }
            // Does Unity's reverb take the sound before or after its distance fade? The same at 25 m
            // (fading to half its level there): an after-the-fade send keeps the ratio as it was close up.
            {
                float tail0 = 0f;
                zone.decayTime = 1f;
                zone.reflections = (int)(Reflections + tail0);
                zone.reverb = (int)(Late + tail0);
                go.transform.localPosition = new Vector3(0f, 0f, 25f);
                src.minDistance = 1f;
                src.maxDistance = 50f;
                double farDry = 0d;
                foreach (float send in new[] { 0f, .5f })
                {
                    src.bypassReverbZones = send == 0f;
                    src.reverbZoneMix = send;
                    yield return Wait(2.5f);
                    output = 0d;
                    reading = true;
                    src.Play();
                    yield return Wait(2.55f);
                    reading = false;
                    double e = output - floor;
                    sb.Append(" [" + output.ToString("E3") + "]");
                    if (send == 0f) { farDry = e; sb.Append(" 25 m: dry " + e.ToString("E3") + " (" + (10d * System.Math.Log10(System.Math.Max(1e-12, e / dry))).ToString("F1") + " dB from close up);"); continue; }
                    double wet = System.Math.Max(0d, e - farDry) / System.Math.Max(1e-12, farDry);
                    sb.Append(" 25 m, 1 s, 0.500: " + wet.ToString("F3") + " (" + (10d * System.Math.Log10(System.Math.Max(1e-9, wet))).ToString("F1") + " dB);");
                }
            }
            Plugin.Log.LogInfo(sb.ToString());

            foreach (AudioSource a in muted) if (a != null) a.mute = false;
            Object.Destroy(go);
            Object.Destroy(clip);
            Running = false;
            Room.Reapply();
            Plugin.Calibrate.Value = false;
        }
    }
}
