using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // What the ground and the forest do to a sound outdoors, by the published methods.
    //
    // Ground (ISO 9613-2:1996, 7.3): a sound skimming over the ground interferes with its own
    // reflection. Over porous ground (grass, soil, anything plants grow in; G = 1) the standard's
    // broadband method, equation (10): A = 4.8 - (2 hm / d)(17 + 300 / d) dB, never below 0, hm the
    // mean height of the path above the ground. Over hard ground (paving, water, ice; G = 0) its
    // general method with G = 0, the same at every frequency: -1.5 dB at the source, -1.5 at the
    // receiver and -3q in between (louder). Equation (10) is for porous ground or mixed ground most
    // of which is porous; mixed ground mostly hard needs the per-octave method, which one gain and
    // one low-pass can't follow: left at 0 dB.
    //
    // Forest (Hoover, as given with ISO 9613-2's foliage method by Arup): A = 0.01 r f^(1/3) dB,
    // r the metres travelled through the forest, at most 200 m (the standard's limit for foliage).
    // Where the forest is is the game's own: a point is in it where the game's forest noise is inside
    // the range one of that biome's trees is planted in (ZoneSystem vegetation, "in forest").
    internal static class Outdoors
    {
        private const int Samples = 16;

        internal static float Ground(Vector3 source, Vector3 listener)
        {
            if (ZoneSystem.instance == null) return 1f;
            float sea = ZoneSystem.instance.m_waterLevel;
            if (!Surface(source, sea, out float gs, out _) || !Surface(listener, sea, out float gr, out _)) return 1f;
            float hs = Mathf.Max(0f, source.y - gs), hr = Mathf.Max(0f, listener.y - gr);
            float d = (source - listener).magnitude;
            Vector3 flat = source - listener; flat.y = 0f;
            float dp = flat.magnitude;
            if (d < 1f) return 1f;
            float sumH = 0f;
            int porous = 0, n = 0;
            int hardS = 0, nS = 0, hardR = 0, nR = 0, hardM = 0, nM = 0;
            float regionS = Mathf.Min(dp, 30f * hs), regionR = Mathf.Min(dp, 30f * hr);
            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples;
                Vector3 p = Vector3.Lerp(source, listener, t);
                if (!Surface(p, sea, out float g, out bool hard)) continue;
                sumH += Mathf.Max(0f, p.y - g);
                n++;
                if (!hard) porous++;
                float fromS = t * dp, fromR = (1f - t) * dp;
                if (fromS <= regionS) { nS++; if (hard) hardS++; }
                else if (fromR <= regionR) { nR++; if (hard) hardR++; }
                else { nM++; if (hard) hardM++; }
            }
            if (n == 0) return 1f;
            float a;
            if (porous * 2 >= n)
            {
                float hm = sumH / n;
                a = Mathf.Max(0f, 4.8f - 2f * hm / d * (17f + 300f / d));
            }
            else if (porous == 0)
            {
                float q = dp <= 30f * (hs + hr) ? 0f : 1f - 30f * (hs + hr) / dp;
                a = -1.5f - 1.5f - 3f * q;
            }
            else return 1f;
            return Mathf.Pow(10f, -a / 20f);
        }

        // The ground (or the sea's surface over it) under p, and whether it's hard by ISO 9613-2.
        private static bool Surface(Vector3 p, float sea, out float height, out bool hard)
        {
            hard = false;
            if (!Heightmap.GetHeight(p, out height)) return false;
            if (height < sea) { height = sea; hard = true; return true; } // water
            hard = Room.Paved(p);
            return true;
        }

        // Metres of the way from one to the other inside the game's forest, and what that takes.
        internal static void Forest(Vector3 source, Vector3 listener, out float gain, out float cutoff)
        {
            gain = 1f; cutoff = 22000f;
            if (WorldGenerator.instance == null || ZoneSystem.instance == null) return;
            Vector3 flat = source - listener; flat.y = 0f;
            float dp = flat.magnitude;
            if (dp < 1f) return;
            int inside = 0;
            for (int i = 0; i < Samples; i++)
            {
                Vector3 p = Vector3.Lerp(listener, source, (i + .5f) / Samples);
                if (InForest(p)) inside++;
            }
            float r = Mathf.Min(200f, dp * inside / Samples);
            if (r <= 0f) return;
            float low = Hoover(r, 125f), mid = Hoover(r, 2000f);
            gain = Mathf.Pow(10f, -low / 20f);
            float x = Mathf.Pow(10f, (mid - low) / 10f) - 1f; // the low-pass that takes the rest at 2 kHz
            cutoff = x <= 1e-4f ? 22000f : Mathf.Clamp(2000f / Mathf.Sqrt(x), 200f, 22000f);
        }

        private static float Hoover(float r, float f) => .01f * r * Mathf.Pow(f, 1f / 3f);

        // Per biome, the forest-noise ranges its trees are planted in (from the game's vegetation list).
        private static Dictionary<Heightmap.Biome, List<Vector2>> s_ranges;
        private static bool InForest(Vector3 p)
        {
            if (s_ranges == null) Build();
            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(p);
            if (!s_ranges.TryGetValue(biome, out List<Vector2> ranges) || ranges.Count == 0) return false;
            float f = WorldGenerator.GetForestFactor(p);
            foreach (Vector2 r in ranges) if (f >= r.x && f <= r.y) return true;
            return false;
        }

        private static void Build()
        {
            s_ranges = new Dictionary<Heightmap.Biome, List<Vector2>>();
            foreach (ZoneSystem.ZoneVegetation v in ZoneSystem.instance.m_vegetation)
            {
                if (v == null || !v.m_enable || !v.m_inForest || v.m_prefab == null || v.m_prefab.GetComponent<TreeBase>() == null) continue;
                foreach (Heightmap.Biome b in System.Enum.GetValues(typeof(Heightmap.Biome)))
                {
                    if (b == Heightmap.Biome.None || (v.m_biome & b) == 0) continue;
                    if (!s_ranges.TryGetValue(b, out List<Vector2> list)) s_ranges[b] = list = new List<Vector2>();
                    list.Add(new Vector2(v.m_forestTresholdMin, v.m_forestTresholdMax));
                }
            }
            int n = 0;
            foreach (var kv in s_ranges) n += kv.Value.Count;
            Plugin.Log.LogInfo("[acoustics] forest: " + n + " forest tree kinds in " + s_ranges.Count + " biomes");
        }

        internal static void Reset() => s_ranges = null;
    }
}
