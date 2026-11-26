using UnityEngine;

namespace WiresAcoustics
{
    // Early reflections with direction. Unity's reverb zone is the same from every side, which is right
    // for the late tail (in a real room it comes from everywhere) but not for the first reflections:
    // those come off particular walls, and the ears hear where. So the zone's reflections are off while
    // this runs, and the strongest few walls round you each give every sound one reflection:
    //  - the walls: picked from the room's rays (Room), 4 times a second;
    //  - per sound and wall, on the worker threads (Directions' job): the sound mirrored in the wall
    //    (an image source), so the delay is the extra path and the level the spreading over it, times
    //    what the wall gives back (sqrt(1 - a) at 500 Hz);
    //  - per sound, on the audio thread: its signal delayed and scaled into one shared line per wall;
    //  - once for all sounds, on the listener (after every sound in the same buffer, so nothing comes
    //    late): each wall's line through the HRTF from the wall's direction, added to the mix.
    // So the HRTF cost is per wall (4), not per sound.
    internal static class Reflections
    {
        internal const int Walls = 4;
        private const int Size = 8192, Mask = Size - 1; // the shared lines: longer than any buffer
        internal const int History = 16384;             // per sound: up to a third of a second of delay

        // the walls: plane (normal toward you, offset), what they give back, the distance scale
        internal static readonly Vector4[] Planes = new Vector4[Walls];
        internal static readonly float[] Back = new float[Walls], Scale = new float[Walls];
        internal static readonly Vector3[] Toward = new Vector3[Walls]; // from you to each wall
        internal static volatile bool On;
        internal static Quaternion CameraInverse = Quaternion.identity;

        private static readonly float[][] s_lines = { new float[Size], new float[Size], new float[Size], new float[Size] };
        private static volatile int s_read;

        // From Room after each measurement (main thread, 64 rays). Up to 4 walls round you, strongest
        // first (what comes back from a sound at your head: sqrt(1 - a) over the path 2r), each a
        // different plane. Not the floor or ground under you: that reflection arrives with the sound
        // itself and is the ground effect (Directions.Ground). Not tree trunks: they scatter.
        internal static void Pick(Vector3 from, Vector3[] dirs, bool[] hit, float[] opening, float[] dist,
            Vector3[] normals, float[] alpha, bool[] tree, bool[] built, float roomScale)
        {
            On = Plugin.RoomReverb.Value && Plugin.Hrtf.Value && AudioSettings.speakerMode == AudioSpeakerMode.Stereo;
            int n = 0;
            for (int k = 0; k < Walls; k++) Back[k] = 0f;
            if (!On) return;
            float[] score = s_score;
            for (int k = 0; k < Walls; k++) score[k] = 0f;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (!hit[i] || opening[i] >= 0f || tree[i] || dist[i] < .2f) continue;
                Vector3 nrm = normals[i];
                if (Vector3.Dot(nrm, dirs[i]) > 0f) nrm = -nrm;
                if (nrm.y > .7f) continue; // a floor or the ground below you
                Vector3 p = from + dirs[i] * dist[i];
                float w = Vector3.Dot(nrm, p);
                float s = Mathf.Sqrt(Mathf.Clamp01(1f - alpha[i])) / (2f * dist[i]);
                int same = -1;
                for (int k = 0; k < n; k++)
                    if (Vector3.Dot(nrm, (Vector3)Planes[k]) > .9f && Mathf.Abs(Planes[k].w - w) < .5f) { same = k; break; }
                if (same >= 0)
                {
                    if (s <= score[same]) continue;
                    Set(same, nrm, w, alpha[i], built[i], roomScale, s, score, dirs[i]);
                    continue;
                }
                if (n < Walls) { Set(n++, nrm, w, alpha[i], built[i], roomScale, s, score, dirs[i]); continue; }
                int weakest = 0;
                for (int k = 1; k < Walls; k++) if (score[k] < score[weakest]) weakest = k;
                if (s > score[weakest]) Set(weakest, nrm, w, alpha[i], built[i], roomScale, s, score, dirs[i]);
            }
        }

        private static readonly float[] s_score = new float[Walls];

        private static void Set(int k, Vector3 nrm, float w, float a, bool isBuilt, float roomScale, float s, float[] score, Vector3 dir)
        {
            Planes[k] = new Vector4(nrm.x, nrm.y, nrm.z, w);
            Back[k] = Mathf.Sqrt(Mathf.Clamp01(1f - a));
            // Built rooms are heard at half size like the reverb (Room); a cliff is as far as it is.
            Scale[k] = isBuilt ? roomScale : 1f;
            Toward[k] = -nrm;
            score[k] = s;
        }

        // Directions' job: one sound against the walls (worker thread). Delay in seconds, level as a
        // share of the direct sound; 0 when the sound is behind that wall.
        internal static void Image(Vector3 source, Vector3 ear, float direct, Vector4 p0, Vector4 p1, Vector4 p2, Vector4 p3,
            Vector4 back, Vector4 scale, out Vector4 delay, out Vector4 level)
        {
            delay = Vector4.zero; level = Vector4.zero;
            for (int k = 0; k < Walls; k++)
            {
                Vector4 p = k == 0 ? p0 : k == 1 ? p1 : k == 2 ? p2 : p3;
                if (back[k] <= 0f) continue;
                Vector3 nrm = new Vector3(p.x, p.y, p.z);
                float side = Vector3.Dot(nrm, source) - p.w;
                if (side <= 0f) continue;
                Vector3 mirror = source - 2f * side * nrm;
                float path = (ear - mirror).magnitude, d = Mathf.Max(direct, .5f);
                if (path <= d) continue;
                delay[k] = (path - d) * scale[k] / 343f;
                level[k] = back[k] * d / path;
            }
        }

        // A sound's callback (audio thread): its signal into the walls' lines, each delayed and scaled.
        // A delay that holds still is read a whole sample at a time (moved by under half a sample:
        // nothing to hear); a moving one is read between samples, gliding over the buffer.
        internal static unsafe void Send(float* x, int frames, float[] history, ref int at, float[] now, float[] target, int rate, float glide, bool start)
        {
            const int H = History - 1;
            int read = s_read, w = at;
            fixed (float* h = history, cur = now, want = target)
            {
                for (int i = 0; i < frames; i++) h[(w + i) & H] = x[i];
                float inv = 1f / frames;
                for (int k = 0; k < Walls; k++)
                {
                    float d1 = want[k] * rate, g1 = want[Walls + k];
                    if (d1 < 1f) d1 = 1f; else if (d1 > History - 2 - frames) d1 = History - 2 - frames;
                    float d0 = start ? d1 : cur[k], g0 = start ? g1 : cur[Walls + k];
                    d1 = d0 + (d1 - d0) * glide; g1 = g0 + (g1 - g0) * glide;
                    cur[k] = d1; cur[Walls + k] = g1;
                    if (g0 < 1e-4f && g1 < 1e-4f) continue;
                    float dg = (g1 - g0) * inv;
                    fixed (float* l = s_lines[k])
                    {
                        float diff = d1 - d0;
                        if (diff < .5f && diff > -.5f)
                        {
                            int from = w + History - (int)(d1 + .5f);
                            float g = g0;
                            for (int i = 0; i < frames; i++, g += dg) l[(read + i) & Mask] += h[(from + i) & H] * g;
                        }
                        else
                        {
                            float pos = w + History - d0, step = 1f - diff * inv, g = g0;
                            for (int i = 0; i < frames; i++, pos += step, g += dg)
                            {
                                int j = (int)pos;
                                float f = pos - j, p = h[j & H];
                                l[(read + i) & Mask] += (p + (h[(j + 1) & H] - p) * f) * g;
                            }
                        }
                    }
                }
            }
            at = (w + frames) & H;
        }

        private static readonly EarState[] s_ears = { new EarState(), new EarState(), new EarState(), new EarState() };
        private static readonly bool[] s_running = new bool[Walls];
        private static float[] s_mono, s_out;

        // The listener's callback: every sound has sent this buffer; each wall's line through the HRTF.
        internal static unsafe void Mix(float[] data, int channels)
        {
            int frames = data.Length / Mathf.Max(1, channels), read = s_read;
            Ears ears = Ears.Current;
            bool play = On && channels == 2 && ears != null && frames > 0 && frames <= Size;
            if (s_mono == null || s_mono.Length < frames) { s_mono = new float[frames]; s_out = new float[2 * frames]; }
            Quaternion inv = CameraInverse;
            fixed (float* mono = s_mono, o = s_out, d = data)
                for (int k = 0; k < Walls; k++)
                {
                    float energy = 0f;
                    fixed (float* l = s_lines[k])
                        for (int i = 0; i < frames; i++)
                        {
                            int j = (read + i) & Mask;
                            float v = l[j];
                            l[j] = 0f;
                            mono[i] = v;
                            energy += v * v;
                        }
                    if (!play) { s_running[k] = false; continue; }
                    if (energy < 1e-12f && !s_running[k]) continue;
                    bool start = !s_running[k];
                    s_running[k] = energy >= 1e-12f;
                    Vector3 dir = inv * Toward[k];
                    s_ears[k].Process(ears, mono, null, o, frames, dir.x, dir.y, dir.z, Mathf.Clamp01(AcousticFilter.Strength), start);
                    for (int i = 0; i < 2 * frames; i++) d[i] += o[i];
                }
            s_read = (read + frames) & Mask;
        }
    }

    // On the game's listener: its filter runs on the final mix, after every sound.
    internal sealed class ReflectionsHost : MonoBehaviour
    {
        private void OnAudioFilterRead(float[] data, int channels) => Reflections.Mix(data, channels);
    }
}
