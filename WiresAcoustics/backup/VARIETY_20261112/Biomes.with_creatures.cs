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

        // A loop under everything, by day, by night or both.
        private sealed class Layer
        {
            internal string Name;
            internal float Volume;
            internal bool Day = true, Night = true;
        }

        private sealed class Biome
        {
            internal Heightmap.Biome Is;
            internal Layer[] Beds;
            internal Calls[] Calls;
        }

        // The game's own sounds, by the start of their names (all loaded with its random calls and
        // its weather: AudioMan, EnvMan).
        private static readonly string[] Trees = { "Amb_WindingTunnel_OneShots_WoodTension", "Amb_WindingTunnel_OneShots_BranchTension", "Amb_WindingTunnel_OneShots_LargeWoodTension" };
        private static readonly string[] Roots = { "Amb_WindingTunnel_OneShots_RootTension" };
        private static readonly string[] Insects = { "Amb_WindingTunnel_OneShots_InsectMoving" };
        private static readonly string[] Critters = { "Amb_Caves_Critter" };
        private static readonly string[] Bats = { "Amb_Caves_Bat" };
        private static readonly string[] Owl = { "63 Animals_ Owl" };
        private static readonly string[] Wolves = { "Amb_Caves_WolfHowl" };
        private static readonly string[] Ice = { "Amb_Caves_IceBreak" };
        private static readonly string[] Gusts = { "Amb_WindBreeze_S" };
        private static readonly string[] FarGusts = { "Amb_Blizzard_OneShots_Wind", "Amb_Morkhalla_OneShots_Wind" };
        private static readonly string[] Grass = { "Hit_Leaves" };
        private static readonly string[] Ruins = { "Amb_OS_DvergrRuins_Creaks", "Amb_OS_DvergrRuins_RockCrumble_Lowpassed" };
        private static readonly string[] Chains = { "AMB_OS_DvergrRuins_Chains" };
        private static readonly string[] Squish = { "Amb_OS_DvergrRuins_Squish" };
        private static readonly string[] Drips = { "Amb_Caves_WaterDrop" };
        private static readonly string[] Splash = { "Splash_Water_Small", "Splash_WaterEmerge" };
        private static readonly string[] Breath = { "Amb_Caves_SpookBreath" };
        private const string Canopy = "Wind_LeavesBlow_NoWind_Loop", Birds = "Amb_Birds_ForestPark", MountainWind = "Wind_BlowingLoop3",
            Snowfield = "Amb_DeepNorth_Loop_01", Hollow = "Amb_WindingTunnel_Main_01", Ruin = "AMB_DvergrRuins_MainLoop", Damp = "Amb_Eerie_CaveLoop3";

        // As much going on, and as varied, in every biome as the Deep North has (one loop of its own
        // and a call every several seconds from some twenty variations): calm, not busy.
        private static readonly Biome[] All =
        {
            new Biome { Is = Heightmap.Biome.Meadows,
                Beds = new[] { new Layer { Name = Birds, Volume = .2f, Night = false } },
                Calls = new[] {
                    new Calls { Names = Gusts, Volume = .45f, Every = 14f, Night = false },
                    new Calls { Names = Grass, Volume = .4f, Every = 14f },
                    new Calls { Names = Insects, Day = false, Volume = .5f, Every = 9f },
                    new Calls { Names = Critters, Day = false, Volume = .35f, Every = 18f },
                    new Calls { Names = Owl, Day = false, Volume = .55f, Every = 22f } } },
            new Biome { Is = Heightmap.Biome.BlackForest,
                Beds = new[] { new Layer { Name = Canopy, Volume = .3f }, new Layer { Name = Hollow, Volume = .12f, Day = false } },
                Calls = new[] {
                    new Calls { Names = Trees, Volume = .6f, Every = 8f },
                    new Calls { Names = Roots, Volume = .5f, Every = 16f },
                    new Calls { Names = Gusts, Volume = .45f, Every = 16f },
                    new Calls { Names = Owl, Day = false, Volume = .6f, Every = 16f },
                    new Calls { Names = Insects, Day = false, Volume = .5f, Every = 12f },
                    new Calls { Names = Bats, Day = false, Volume = .4f, Every = 20f } } },
            new Biome { Is = Heightmap.Biome.Swamp,
                Beds = new[] { new Layer { Name = Damp, Volume = .12f, Day = false } },
                Calls = new[] {
                    new Calls { Names = Roots, Volume = .5f, Every = 14f },
                    new Calls { Names = Drips, Volume = .5f, Every = 9f },
                    new Calls { Names = Squish, Volume = .45f, Every = 12f },
                    new Calls { Names = Splash, Volume = .45f, Every = 14f },
                    new Calls { Names = Insects, Day = false, Volume = .5f, Every = 10f },
                    new Calls { Names = Critters, Day = false, Volume = .35f, Every = 18f } } },
            new Biome { Is = Heightmap.Biome.Mountain,
                Beds = new[] { new Layer { Name = Snowfield, Volume = .35f }, new Layer { Name = MountainWind, Volume = .2f } },
                Calls = new[] {
                    new Calls { Names = Gusts, Volume = .55f, Every = 8f },
                    new Calls { Names = FarGusts, Volume = .45f, Every = 10f },
                    new Calls { Names = Ice, Volume = .5f, Every = 22f },
                    new Calls { Names = Wolves, Day = false, Volume = .55f, Every = 16f } } },
            new Biome { Is = Heightmap.Biome.Plains,
                Beds = new[] { new Layer { Name = Birds, Volume = .08f, Night = false } },
                Calls = new[] {
                    new Calls { Names = Gusts, Volume = .5f, Every = 10f },
                    new Calls { Names = Grass, Volume = .45f, Every = 10f },
                    new Calls { Names = FarGusts, Volume = .3f, Every = 18f },
                    new Calls { Names = Insects, Day = false, Volume = .55f, Every = 6f },
                    new Calls { Names = Critters, Day = false, Volume = .35f, Every = 16f } } },
            new Biome { Is = Heightmap.Biome.Mistlands,
                Beds = new[] { new Layer { Name = Canopy, Volume = .22f }, new Layer { Name = Ruin, Volume = .15f } },
                Calls = new[] {
                    new Calls { Names = Trees, Volume = .55f, Every = 10f },
                    new Calls { Names = Ruins, Volume = .5f, Every = 12f },
                    new Calls { Names = Insects, Volume = .5f, Every = 9f },
                    new Calls { Names = Squish, Volume = .45f, Every = 14f },
                    new Calls { Names = Critters, Volume = .4f, Every = 14f },
                    new Calls { Names = Chains, Volume = .35f, Every = 30f },
                    new Calls { Names = Breath, Day = false, Volume = .35f, Every = 26f } } },
            new Biome { Is = Heightmap.Biome.Ocean,
                Calls = new[] {
                    new Calls { Names = Gusts, Volume = .5f, Every = 9f },
                    new Calls { Names = Splash, Volume = .4f, Every = 12f },
                    new Calls { Names = FarGusts, Volume = .3f, Every = 16f } } },
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
                    if (!Loaded.TryGetValue(l.Name, out AudioClip clip) || clip == null) continue;
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
