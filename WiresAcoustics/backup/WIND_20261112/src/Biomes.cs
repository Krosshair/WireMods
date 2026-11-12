using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Each biome's own sound (setting "Biome ambience"). The game gives nearly every clear biome the
    // same loop (wind in reeds) and random calls only to the Meadows and Black Forest (birds, an owl
    // at night), the Swamp, the Ashlands and the Deep North; the Mountain, the Plains, the Mistlands
    // and the sea get nothing, and a clear night in the Mistlands is silent. Here, from sounds the
    // game already has loaded (its random calls for every place, its weather loops, its wind):
    //  - a quiet bed per biome where it needs one (a forest's canopy, the mountain wind), from all
    //    round like the game's own loop, so indoors it's muffled with the rest (Ambience);
    //  - calls and noises placed out in the world round you, by day and by night: trees creaking in
    //    the Black Forest, wolves far off in the mountains at night, insects in the Plains grass,
    //    old dvergr ruins groaning in the Mistlands. They're heard like the game's own random calls
    //    (out in the open air, through walls and openings, with the HRTF), and fewer in rain.
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

        private sealed class Biome
        {
            internal Heightmap.Biome Is;
            internal string Bed;       // a loop under everything, or null
            internal float BedVolume;
            internal Calls[] Calls;
        }

        private static readonly string[] Trees = { "Amb_WindingTunnel_OneShots_WoodTension", "Amb_WindingTunnel_OneShots_BranchTension", "Amb_WindingTunnel_OneShots_LargeWoodTension" };
        private static readonly string[] Roots = { "Amb_WindingTunnel_OneShots_RootTension" };
        private static readonly string[] Insects = { "Amb_WindingTunnel_OneShots_InsectMoving" };
        private static readonly string[] Owl = { "63 Animals_ Owl" };
        private static readonly string[] Wolves = { "Amb_Caves_WolfHowl" };
        private static readonly string[] Ice = { "Amb_Caves_IceBreak" };
        private static readonly string[] Gusts = { "Amb_WindBreeze_S" };
        private static readonly string[] Grass = { "Hit_Leaves" };
        private static readonly string[] Ruins = { "Amb_OS_DvergrRuins_Creaks", "Amb_OS_DvergrRuins_RockCrumble_Lowpassed" };
        private static readonly string[] Drips = { "Amb_Caves_WaterDrop" };
        private const string Canopy = "Wind_LeavesBlow_NoWind_Loop", MountainWind = "Wind_BlowingLoop3";

        private static readonly Biome[] All =
        {
            new Biome { Is = Heightmap.Biome.Meadows, Calls = new[] {
                new Calls { Names = Insects, Day = false, Volume = .25f, Every = 25f },
                new Calls { Names = Owl, Day = false, Volume = .35f, Every = 50f } } },
            new Biome { Is = Heightmap.Biome.BlackForest, Bed = Canopy, BedVolume = .18f, Calls = new[] {
                new Calls { Names = Trees, Volume = .35f, Every = 18f },
                new Calls { Names = Roots, Volume = .3f, Every = 40f },
                new Calls { Names = Insects, Day = false, Volume = .25f, Every = 30f },
                new Calls { Names = Owl, Day = false, Volume = .35f, Every = 35f } } },
            new Biome { Is = Heightmap.Biome.Swamp, Calls = new[] {
                new Calls { Names = Roots, Volume = .3f, Every = 30f },
                new Calls { Names = Drips, Volume = .3f, Every = 20f },
                new Calls { Names = Insects, Day = false, Volume = .25f, Every = 25f } } },
            new Biome { Is = Heightmap.Biome.Mountain, Bed = MountainWind, BedVolume = .15f, Calls = new[] {
                new Calls { Names = Gusts, Volume = .35f, Every = 15f },
                new Calls { Names = Ice, Volume = .3f, Every = 60f },
                new Calls { Names = Wolves, Day = false, Volume = .3f, Every = 45f } } },
            new Biome { Is = Heightmap.Biome.Plains, Calls = new[] {
                new Calls { Names = Gusts, Volume = .3f, Every = 20f },
                new Calls { Names = Grass, Volume = .25f, Every = 25f },
                new Calls { Names = Insects, Day = false, Volume = .25f, Every = 15f } } },
            new Biome { Is = Heightmap.Biome.Mistlands, Bed = Canopy, BedVolume = .12f, Calls = new[] {
                new Calls { Names = Trees, Volume = .3f, Every = 22f },
                new Calls { Names = Ruins, Volume = .3f, Every = 35f },
                new Calls { Names = Insects, Volume = .25f, Every = 20f } } },
            new Biome { Is = Heightmap.Biome.Ocean, Calls = new[] {
                new Calls { Names = Gusts, Volume = .3f, Every = 20f } } },
        };

        private const float Near = 12f, Far = 32f, Fade = 4f; // calls 12 to 32 m off; beds fade over 4 s
        private static bool s_found;
        private static int s_lastEnvCount = -1;
        private static readonly Dictionary<string, AudioClip> Loaded = new Dictionary<string, AudioClip>();
        private static AudioSource s_bedA, s_bedB; // the current bed and the one fading out
        private static float s_bedTarget;
        private static GameObject s_root;
        private static int s_played;

        internal static AudioSource Bed => s_bedA;
        internal static AudioSource Fading => s_bedB;
        private static float s_last;

        // From Ambience's update (up to 5 times a second; once a second while you stand still).
        internal static void Update(AudioMan man)
        {
            float dt = Mathf.Clamp(Time.time - s_last, 0f, 1f);
            s_last = Time.time;
            Biome b = Plugin.BiomeAmbience.Value ? Current() : null;
            BedTo(man, b, dt);
            if (b == null || b.Calls == null) return;
            Find();
            float now = Time.time;
            bool night = EnvMan.IsNight(), wet = EnvMan.IsWet();
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

        // The bed: one loop, crossfaded when the biome changes; the game's ambience volume (its
        // mixer) on top.
        private static void BedTo(AudioMan man, Biome b, float dt)
        {
            AudioClip want = null;
            if (b != null && b.Bed != null) { Find(); Loaded.TryGetValue(b.Bed, out want); }
            float volume = want != null ? b.BedVolume : 0f;
            if (want != null && (s_bedA == null || s_bedA.clip != want))
            {
                // the old one fades out, a new one fades in
                AudioSource old = s_bedA;
                s_bedA = s_bedB != null && s_bedB.clip == want ? s_bedB : Make(man);
                s_bedB = old;
                if (s_bedA.clip != want || !s_bedA.isPlaying)
                {
                    s_bedA.clip = want;
                    s_bedA.volume = 0f;
                    s_bedA.time = Random.value * Mathf.Max(0f, want.length - 1f);
                    s_bedA.Play();
                }
            }
            s_bedTarget = volume;
            float step = dt / Fade;
            if (s_bedA != null)
            {
                s_bedA.volume = Mathf.MoveTowards(s_bedA.volume, want != null && s_bedA.clip == want ? s_bedTarget : 0f, step * Mathf.Max(.05f, s_bedTarget));
                if (s_bedA.volume <= 0f && (want == null || s_bedA.clip != want) && s_bedA.isPlaying) s_bedA.Stop();
            }
            if (s_bedB != null)
            {
                s_bedB.volume = Mathf.MoveTowards(s_bedB.volume, 0f, step * .25f);
                if (s_bedB.volume <= 0f && s_bedB.isPlaying) s_bedB.Stop();
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
            if (s_bedA != null) s_bedA.Stop();
            if (s_bedB != null) s_bedB.Stop();
        }

        internal static string Summary()
        {
            Biome b = Current();
            string s = "biome ambience: " + (b == null ? "none here" : b.Is + (s_bedA != null && s_bedA.isPlaying ? ", bed " + s_bedA.clip.name + " " + s_bedA.volume.ToString("F2") : "") + ", calls played " + s_played);
            s_played = 0;
            return s;
        }
    }
}
