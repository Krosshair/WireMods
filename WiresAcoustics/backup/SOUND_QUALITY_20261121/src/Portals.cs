using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Nearest directions for room-ray gap classification. The adjacency
    // graph is constructed once; sound sources and playback are not created
    // from these samples. A short ray leaking between pieces is treated as
    // a seam rather than a new window emitter.
    internal static class Portals
    {
        private const int Neighbours = 6;
        private static int[][] s_neighbours;

        internal static int[][] NeighboursOf(Vector3[] directions)
        {
            if (s_neighbours != null &&
                s_neighbours.Length == directions.Length)
                return s_neighbours;

            int count = directions.Length;
            var result = new int[count][];
            var ordered = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                ordered.Clear();
                for (int j = 0; j < count; j++)
                    if (j != i) ordered.Add(j);
                Vector3 basis = directions[i];
                ordered.Sort((a, b) =>
                    Vector3.Dot(directions[b], basis)
                        .CompareTo(Vector3.Dot(directions[a], basis)));
                result[i] = ordered.GetRange(0,
                    Mathf.Min(Neighbours, ordered.Count)).ToArray();
            }
            s_neighbours = result;
            return result;
        }
    }
}
