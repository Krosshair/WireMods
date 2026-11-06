using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace WiresAcoustics
{
    // Openings (windows, doors, holes in the roof) and sound paths through them, from the room rays.
    //
    // Finding them: inside an enclosed space, a level or upward room ray that hits nothing left through
    // an opening. Neighbouring escaped rays are one opening; it lies about as far away as the walls
    // around it (the hit distance of the neighbouring rays that did hit), in the average direction of
    // its rays. Its share of the rays is how big it looks from where you are.
    //
    // Paths through them (diffraction, as in Vercidium's ray traced audio): a sound with walls in the
    // way is also tried through the biggest openings: listener -> opening (clear, that's how the
    // opening was found) -> sound. The path's walls are what lies between the opening and the sound,
    // plus a bend penalty (sound loses energy turning a corner: 0 straight on, 0.3 walls at 90
    // degrees, 0.6 turning back) and the extra distance (inverse distance law: direct / path length).
    // The quieter of direct and best path wins; through an opening the sound is heard from the
    // opening (the HRTF places it there).
    internal static class Portals
    {
        internal struct Portal { internal Vector3 Point, Dir; internal float Share, Dist; }
        internal static readonly List<Portal> List = new List<Portal>(4);
        internal const int Routes = 2;            // openings tried per blocked sound
        private const int Neighbours = 6;
        private const float MinEnclosure = .35f;  // more open than this: everything is open anyway
        private static int[][] s_nb;
        private static readonly bool[] Escaped = new bool[64];
        private static readonly int[] Label = new int[64];
        private static readonly int[] Stack = new int[64];
        private static readonly List<Portal> Found = new List<Portal>(16);

        // From the room measurement (every 0.2 s).
        internal static void Find(Vector3 from, Vector3[] dirs, NativeArray<RaycastHit> hits, float enclosure, float meanPath)
        {
            List.Clear();
            if (enclosure < MinEnclosure) return;
            int n = dirs.Length;
            if (s_nb == null) s_nb = BuildNeighbours(dirs);
            int level = 0;
            for (int i = 0; i < n; i++)
            {
                bool up = dirs[i].y > -.05f;
                if (up) level++;
                Escaped[i] = up && hits[i].collider == null;
                Label[i] = -1;
            }
            Found.Clear();
            for (int i = 0; i < n; i++)
            {
                if (!Escaped[i] || Label[i] >= 0) continue;
                int top = 0, count = 0, walls = 0;
                float wallDist = 0f;
                Vector3 sum = Vector3.zero;
                Stack[top++] = i;
                Label[i] = i;
                while (top > 0)
                {
                    int r = Stack[--top];
                    count++;
                    sum += dirs[r];
                    foreach (int k in s_nb[r])
                    {
                        if (Escaped[k]) { if (Label[k] < 0) { Label[k] = i; Stack[top++] = k; } }
                        else if (hits[k].collider != null) { wallDist += hits[k].distance; walls++; }
                    }
                }
                Vector3 dir = sum.normalized;
                float dist = walls > 0 ? wallDist / walls : meanPath;
                Found.Add(new Portal { Dir = dir, Dist = dist, Point = from + dir * dist, Share = (float)count / Mathf.Max(1, level) });
            }
            Found.Sort((a, b) => b.Share.CompareTo(a.Share));
            for (int i = 0; i < Found.Count && i < 4; i++) List.Add(Found[i]);
        }

        private static int[][] BuildNeighbours(Vector3[] dirs)
        {
            var nb = new int[dirs.Length][];
            var order = new List<int>(dirs.Length);
            for (int i = 0; i < dirs.Length; i++)
            {
                order.Clear();
                for (int k = 0; k < dirs.Length; k++) if (k != i) order.Add(k);
                Vector3 d = dirs[i];
                order.Sort((a, b) => Vector3.Dot(dirs[b], d).CompareTo(Vector3.Dot(dirs[a], d)));
                nb[i] = order.GetRange(0, Neighbours).ToArray();
            }
            return nb;
        }

        internal static float Bend(Portal p, Vector3 sound) =>
            .3f * (1f - Vector3.Dot(p.Dir, (sound - p.Point).normalized));

        internal static float LengthGain(Vector3 listener, Portal p, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            float path = (p.Point - listener).magnitude + (sound - p.Point).magnitude;
            return Mathf.Clamp(direct / Mathf.Max(.01f, path), .35f, 1f);
        }

        // Worth trying a path: walls in the way and openings known.
        internal static int Tries(float occDirect) => occDirect >= .3f ? Mathf.Min(Routes, List.Count) : 0;

        // A new sound, checked at once: the best path through the openings, if better than direct.
        internal static bool Route(Vector3 listener, Vector3 sound, ref float occ, out float lengthGain, out Vector3 heardFrom)
        {
            lengthGain = 1f;
            heardFrom = sound;
            bool routed = false;
            int tries = Tries(occ);
            for (int k = 0; k < tries; k++)
            {
                Portal p = List[k];
                float total = Occlusion.Blocking(p.Point, sound) + Bend(p, sound);
                if (total < occ - .1f)
                {
                    occ = total;
                    lengthGain = LengthGain(listener, p, sound);
                    heardFrom = p.Point;
                    routed = true;
                }
            }
            return routed;
        }

        // Positionless ambience: how much of the outside comes in (openings, bigger and nearer count
        // more) and from which way.
        internal static float Openness(out Vector3 dir)
        {
            float o = 0f;
            dir = Vector3.zero;
            foreach (Portal p in List)
            {
                float w = p.Share / (1f + p.Dist / 3f);
                o += w;
                dir += p.Dir * w;
            }
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
            return Mathf.Clamp01(o * 2.5f);
        }

        internal static string Summary()
        {
            if (List.Count == 0) return "openings none";
            var sb = new System.Text.StringBuilder("openings");
            foreach (Portal p in List) sb.Append(" " + p.Share.ToString("P0") + " at " + p.Dist.ToString("F1") + " m");
            return sb.ToString();
        }
    }
}
