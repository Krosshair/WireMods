using System.Collections.Generic;
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
    // Paths through them (as in Vercidium's ray traced audio): a sound with walls in the way is also
    // tried through the biggest openings: listener -> opening (clear, that's how the opening was
    // found) -> sound. The path's loss is the walls between the opening and the sound, the bend
    // round the opening's nearest edge (Diffraction.Opening) and the extra distance (inverse
    // distance law: direct / path length). The direct sound and the best way round add by energy,
    // and where it's heard from follows the precedence effect (Batch).
    internal static class Portals
    {
        internal struct Portal { internal Vector3 Point, Dir; internal float Share, Dist, Radius; }
        internal static readonly List<Portal> List = new List<Portal>(4);
        internal const int Routes = 2;            // openings tried per blocked sound
        private const int Neighbours = 6;
        private const float MinEnclosure = .35f;  // more open than this: everything is open anyway
        private static int[][] s_nb;
        private static readonly bool[] Escaped = new bool[64];
        private const float RaySolidAngle = 4f * Mathf.PI / 64f; // each room ray's share of all round
        private static readonly int[] Label = new int[64];
        private static readonly int[] Stack = new int[64];
        private static readonly List<Portal> Found = new List<Portal>(16);

        // From the room measurement (every 0.2 s), with its gaps (Room.Out: out through an opening or
        // to the sky; a lone seam between pieces isn't one).
        internal static void Find(Vector3 from, Vector3[] dirs, float enclosure, float meanPath)
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
                Escaped[i] = up && Room.Out(i);
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
                        else if (Room.Hit[k]) { wallDist += Room.Dist[k]; walls++; }
                    }
                }
                Vector3 dir = sum.normalized;
                float dist = walls > 0 ? wallDist / walls : meanPath;
                // as wide as it looks: its rays' solid angle as a cone, its edge that far off at its distance
                float cosHalf = Mathf.Clamp(1f - count * RaySolidAngle / (2f * Mathf.PI), -1f, 1f);
                float radius = dist * Mathf.Min(4f, Mathf.Tan(Mathf.Acos(cosHalf)));
                Found.Add(new Portal { Dir = dir, Dist = dist, Point = from + dir * dist, Share = (float)count / Mathf.Max(1, level), Radius = radius });
            }
            Found.Sort((a, b) => b.Share.CompareTo(a.Share));
            for (int i = 0; i < Found.Count && i < 4; i++) List.Add(Found[i]);
        }

        internal static int[][] NeighboursOf(Vector3[] dirs)
        {
            if (s_nb == null) s_nb = BuildNeighbours(dirs);
            return s_nb;
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

        internal static float LengthGain(Vector3 listener, Portal p, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            float path = (p.Point - listener).magnitude + (sound - p.Point).magnitude;
            return Mathf.Min(1f, direct / Mathf.Max(.01f, path)); // the longer way spreads the sound further (1/r)
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
