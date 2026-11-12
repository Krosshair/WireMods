using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Each biome's own sound (setting "Biome ambience"), on top of what the game already gives it.
    // The game gives nearly every clear biome the same loop (wind in reeds) and random sounds only to
    // the Meadows and Black Forest (birds, an owl at night, leaves, breezes), the Swamp, the Ashlands
    // and the Deep North; the Mountain, the Plains, the Mistlands and the sea get nothing, and a clear
    // night in the Mistlands is silent. Here the game's own open-world ambience is shared out to
    // where it fits instead of silence (the forest's leaves and breezes, the Deep North's gusts and
    // calm loops, the mountain-top wind), never doubling what a biome already plays:
    //  - loops from all round like the game's own, so indoors they're muffled with the rest
    //    (Ambience);
    //  - one-shots placed out in the world round you, heard like the game's own random sounds (out
    //    in the open air, through walls and openings, with the HRTF), fewer in rain.
    // Which sounds go where, how loud and how often is a choice by ear, not from a source.
    internal static class Biomes
    {
        private sealed class Calls
        {
            internal string[] Names;   // the sounds, by the start of their names
            internal bool Day = true, Night = true;
            internal float Volume, Every;  // how loud (the game's 0 to 1) and about how many seconds apart
            internal List<AudioClip> Clips;
            internal float Next;
        }

        // A loop under everything, by day, by night or both.
        // A loop under everything, by day, by night or both. The game's calm loops are a minute
        // long; one on its own would come round again and again, so a layer takes turns between a few
        // (crossfaded every two minutes or so, each from a new place in it).
        private sealed class Layer
        {
            internal string[] Names;
            internal float Volume;
            internal bool Day = true, Night = true;
            internal int Turn;
            internal float NextTurn;
        }

        private sealed class Biome
        {
            internal Heightmap.Biome Is;
            internal Layer[] Beds;
            internal Calls[] Calls;
        }

        // Only the game's open-world ambience, moved to where it fits and nothing else: its forest
        // sounds (leaves, breezes, the canopy), the Deep North's wind gusts and calm snowfield loops,
        // the mountain-top wind, the Swamp's water. (The game itself pairs the Deep North's loops
        // with the Swamp, the Plains and the Mistlands in its Jotun invasion weather.)
        private static readonly string[] Leaves = { "Hit_Leaves" };
        private static readonly string[] Breeze = { "Amb_WindBreeze_S" };
        private static readonly string[] NorthWind = { "Amb_Blizzard_OneShots_Wind" };
        private static readonly string[] Water = { "Splash_Water_Small", "Underwater_WavePassby" };
        private static readonly string[] Canopy = { "Wind_LeavesBlow_NoWind_Loop" }; // 2.5 s: a gust through the leaves, not a loop
        private static readonly string[] Calm = { "Amb_DeepNorth_Loop_02", "Amb_DeepNorth_Loop_03" }; // a minute each, taking turns

        // As much going on, and as varied, in every biome as the Deep North has (its own loop and a
        // gust every several seconds from twenty variations), on top of what each already has and
        // never the same sound twice: calm, not busy.
        private static readonly Biome[] All =
        {
            // Meadows: the game has birdsong, an owl at night, leaves, breezes, the reeds. Added: the
            // north's wind far off.
            new Biome { Is = Heightmap.Biome.Meadows,
                Calls = new[] { new Calls { Names = NorthWind, Volume = .3f, Every = 20f } } },
            // Black Forest: the game shares the Meadows' sounds and plays its canopy only now and then.
            // Added: the canopy more often, the north's wind far off.
            new Biome { Is = Heightmap.Biome.BlackForest,
                Calls = new[] {
                    new Calls { Names = Canopy, Volume = .45f, Every = 9f },
                    new Calls { Names = NorthWind, Volume = .3f, Every = 18f } } },
            // Swamp: the game has its water, beetles and insects, no wind or leaves. Added: the calm
            // northern loops, leaves, breezes.
            new Biome { Is = Heightmap.Biome.Swamp,
                Beds = new[] { new Layer { Names = Calm, Volume = .15f } },
                Calls = new[] {
                    new Calls { Names = Leaves, Volume = .45f, Every = 12f },
                    new Calls { Names = Breeze, Volume = .45f, Every = 14f } } },
            // Mountain: the game has a snowfield loop in snow and the blizzard in storms, no other
            // sounds. Added: the calm northern loops, the north's gusts, breezes.
            new Biome { Is = Heightmap.Biome.Mountain,
                Beds = new[] { new Layer { Names = Calm, Volume = .18f } },
                Calls = new[] {
                    new Calls { Names = NorthWind, Volume = .5f, Every = 8f },
                    new Calls { Names = Breeze, Volume = .5f, Every = 12f } } },
            // Plains: the game has only the reeds. Added: the calm northern loops, leaves in the grass,
            // breezes, the north's gusts.
            new Biome { Is = Heightmap.Biome.Plains,
                Beds = new[] { new Layer { Names = Calm, Volume = .15f } },
                Calls = new[] {
                    new Calls { Names = Leaves, Volume = .45f, Every = 10f },
                    new Calls { Names = Breeze, Volume = .5f, Every = 10f },
                    new Calls { Names = NorthWind, Volume = .35f, Every = 16f } } },
            // Mistlands: the game has nothing in clear weather. Added: the calm northern loops, the
            // canopy, leaves, breezes, the north's wind far off.
            new Biome { Is = Heightmap.Biome.Mistlands,
                Beds = new[] { new Layer { Names = Calm, Volume = .18f } },
                Calls = new[] {
                    new Calls { Names = Canopy, Volume = .45f, Every = 9f },
                    new Calls { Names = Leaves, Volume = .45f, Every = 10f },
                    new Calls { Names = Breeze, Volume = .45f, Every = 12f },
                    new Calls { Names = NorthWind, Volume = .3f, Every = 18f } } },
            // The sea: the game has the waves. Added: breezes, the north's gusts, water.
            new Biome { Is = Heightmap.Biome.Ocean,
                Calls = new[] {
                    new Calls { Names = Breeze, Volume = .5f, Every = 10f },
                    new Calls { Names = NorthWind, Volume = .4f, Every = 12f },
                    new Calls { Names = Water, Volume = .35f, Every = 14f } } },
            // Ashlands and the Deep North: the game's own are rich already. Left as they are.
        };


        private const float Near = 8f, Far = 26f, Fade = 4f; // calls 8 to 26 m off; beds fade over 4 s
        private static bool s_found;
        private static int s_lastEnvCount = -1;
        private static readonly Dictionary<string, AudioClip> Loaded = new Dictionary<string, AudioClip>();
        private static readonly List<AudioSource> Beds = new List<AudioSource>(4);
        private static readonly Dictionary<AudioSource, float> BedTargets = new Dictionary<AudioSource, float>(4);
        private static GameObject s_root;
        private static int s_played;
        private static float s_last;

        internal static List<AudioSource> Playing => Beds;

        // From Ambience's update (up to 5 times a second; once a second while you stand still).
        internal static void Update(AudioMan man)
        {
            float dt = Mathf.Clamp(Time.time - s_last, 0f, 1f);
            s_last = Time.time;
            Biome b = Plugin.BiomeAmbience.Value ? Current() : null;
            bool night = EnvMan.IsNight(), wet = EnvMan.IsWet();
            if (b != null) Find();
            BedsTo(man, b, night, dt);
            if (b == null || b.Calls == null) return;
            float now = Time.time;
            foreach (Calls c in b.Calls)
            {
                if (c.Clips == null || c.Clips.Count == 0) continue;
                if (c.Next <= 0f) { c.Next = now + Random.Range(.3f, 1f) * c.Every; continue; }
                if (now < c.Next) continue;
                c.Next = now + Random.Range(.6f, 1.4f) * c.Every * (wet ? 3f : 1f); // fewer in rain
                if (night ? !c.Night : !c.Day) continue;
                Play(man, c.Clips[Random.Range(0, c.Clips.Count)], c.Volume);
            }
        }

        // Where you are: out in the world (not in a dungeon), and which of the biomes above.
        private static Biome Current()
        {
            global::Player p = global::Player.m_localPlayer;
            if (p == null || p.InInterior() || EnvMan.instance == null) return null;
            Heightmap.Biome now = EnvMan.instance.GetCurrentBiome();
            foreach (Biome b in All) if (b.Is == now) return b;
            return null;
        }

        // The sounds named above, from what the game has loaded: its random calls for every place and
        // its weather loops (environments from later content are added as it loads, so looked up again
        // when their number changes).
        private static void Find()
        {
            int envs = EnvMan.instance != null ? EnvMan.instance.m_environments.Count : 0;
            if (s_found && envs == s_lastEnvCount) return;
            s_found = true;
            s_lastEnvCount = envs;
            Loaded.Clear();
            AudioMan man = AudioMan.instance;
            if (man != null)
            {
                foreach (AudioMan.BiomeAmbients a in man.m_randomAmbients)
                {
                    Add(a.m_randomAmbientClips); Add(a.m_randomAmbientClipsDay); Add(a.m_randomAmbientClipsNight);
                }
                Add(man.m_windAudio);
            }
            if (EnvMan.instance != null) foreach (EnvSetup e in EnvMan.instance.m_environments) Add(e.m_ambientLoop);
            foreach (Biome b in All)
                if (b.Calls != null)
                    foreach (Calls c in b.Calls)
                    {
                        c.Clips = new List<AudioClip>();
                        foreach (KeyValuePair<string, AudioClip> kv in Loaded)
                            foreach (string n in c.Names)
                                if (kv.Key.StartsWith(n, System.StringComparison.Ordinal)) { c.Clips.Add(kv.Value); break; }
                    }
        }

        private static void Add(List<AudioClip> clips) { if (clips != null) foreach (AudioClip c in clips) Add(c); }
        private static void Add(AudioClip c) { if (c != null && !Loaded.ContainsKey(c.name)) Loaded[c.name] = c; }

        // A call out in the world, the way the game makes its own random ones (AudioMan's random
        // ambient prefab), so it gets everything they get here (AudioProvenance: placed in the open
        // air, walls, openings, the HRTF).
        private static void Play(AudioMan man, AudioClip clip, float volume)
        {
            if (man.m_randomAmbientPrefab == null || clip == null) return;
            float a = Random.value * Mathf.PI * 2f, d = Random.Range(Near, Far);
            Vector3 at = Hearing.At + new Vector3(Mathf.Sin(a) * d, 0f, Mathf.Cos(a) * d);
            GameObject go = Object.Instantiate(man.m_randomAmbientPrefab, at, Quaternion.identity, man.transform);
            ZSFX z = go.GetComponent<ZSFX>();
            if (z == null) { Object.Destroy(go); return; }
            z.m_audioClips = new[] { clip };
            z.m_minVol = z.m_maxVol = volume;
            // a long one fades out over its last second; a short one just plays to its end
            z.m_fadeOutOnAwake = clip.length > 2f;
            z.m_fadeOutDelay = clip.length - 1f;
            z.m_fadeOutDuration = 1f;
            if (go.GetComponent<TimedDestruction>() is TimedDestruction t) { t.m_timeout = clip.length * 1.5f + 1f; t.Trigger(); }
            AudioProvenance.Play(z);
            s_played++;
        }

        // The beds: each wanted loop fades in, anything else fades out (4 s); the game's ambience
        // volume (its mixer) on top.
        private static void BedsTo(AudioMan man, Biome b, bool night, float dt)
        {
            foreach (AudioSource src in Beds) BedTargets[src] = 0f;
            if (b != null && b.Beds != null)
                foreach (Layer l in b.Beds)
                {
                    if (night ? !l.Night : !l.Day) continue;
                    if (Time.time >= l.NextTurn)
                    {
                        l.NextTurn = Time.time + Random.Range(90f, 150f);
                        if (l.NextTurn > 0f && l.Names.Length > 1) l.Turn = (l.Turn + 1) % l.Names.Length;
                    }
                    if (!Loaded.TryGetValue(l.Names[l.Turn % l.Names.Length], out AudioClip clip) || clip == null) continue;
                    AudioSource src = null;
                    foreach (AudioSource s2 in Beds) if (s2.clip == clip) { src = s2; break; }
                    if (src == null)
                    {
                        foreach (AudioSource s2 in Beds) if (!s2.isPlaying) { src = s2; break; }
                        if (src == null) { src = Make(man); Beds.Add(src); }
                        src.clip = clip;
                        src.volume = 0f;
                        src.time = Random.value * Mathf.Max(0f, clip.length - 1f);
                        src.Play();
                    }
                    BedTargets[src] = l.Volume;
                }
            foreach (AudioSource src in Beds)
            {
                float target = BedTargets[src];
                src.volume = Mathf.MoveTowards(src.volume, target, dt / Fade * Mathf.Max(.05f, Mathf.Max(target, src.volume)));
                if (target <= 0f && src.volume <= 0f && src.isPlaying) src.Stop();
            }
        }

        private static AudioSource Make(AudioMan man)
        {
            if (s_root == null)
            {
                s_root = new GameObject("WiresAcousticsBiome");
                Object.DontDestroyOnLoad(s_root);
            }
            var go = new GameObject("bed");
            go.transform.SetParent(s_root.transform, false);
            AudioSource src = go.AddComponent<AudioSource>(); // one per object: the muffling filter goes on it (Ambience)
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f; // from all round, like the game's own loop
            src.outputAudioMixerGroup = man.m_ambientMixer;
            src.bypassReverbZones = true;
            src.priority = 0;
            return src;
        }

        internal static void Stop()
        {
            foreach (AudioSource src in Beds) if (src != null) { src.Stop(); src.volume = 0f; }
        }

        internal static string Summary()
        {
            Biome b = Current();
            var sb = new System.Text.StringBuilder("biome ambience: ");
            sb.Append(b == null ? "none here" : b.Is.ToString());
            foreach (AudioSource src in Beds) if (src != null && src.isPlaying) sb.Append(", bed ").Append(src.clip.name).Append(' ').Append(src.volume.ToString("F2"));
            sb.Append(", calls played ").Append(s_played);
            s_played = 0;
            return sb.ToString();
        }
    }
}
