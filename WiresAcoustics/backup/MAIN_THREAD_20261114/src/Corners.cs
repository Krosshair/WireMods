using UnityEngine;

namespace WiresAcoustics
{
    // Sound round the edges of walls: a gap right beside you, a window, the end of a wall, over a half
    // wall. Found, not guessed: the walls come from the last check's line (the one nearest you and the
    // one nearest the sound, Occlusion.Block), and along each, in its own plane (from its surface, so a
    // turned or slanted wall works), short probes cross it every 0.2 m to each side, up and down. The
    // first place it's open is the edge. 0.2 m is half the narrowest opening the game builds (the
    // Grausten 2x2 window, 0.4 m between its side colliders), so no built opening falls between two
    // probes. Each probe is two rays 16 cm apart (Occlusion.Twin either side) and both must be open: a
    // seam between pieces is a centimetre or two, a real gap isn't.
    // Probes go out as far as going round could still beat going through: where the detour round the
    // edge costs as much as that wall does at 125 Hz (Diffraction.DetourFor), at most 5.5 m.
    // Next check, listener -> edge -> sound is tried like an opening: walls on both legs, the bend by
    // the real detour (Diffraction.Round: about 5 dB right at the edge, more the deeper into the
    // wall's shadow) and the extra distance. So beside a gap a sound is nearly as loud as in the open
    // and heard from the gap, and it fades in gradually as you go behind the wall.
    internal static class Corners
    {
        internal const float Spacing = .2f, Reach = 5.5f;
        internal const int Dirs = 4, MaxEdges = Slots * Dirs;
        internal static readonly int MaxProbes = Mathf.CeilToInt(Reach / Spacing);

        internal struct Walls
        {
            internal bool Near, Far;
            internal Vector3 NearAt, NearN, FarAt, FarN;
            internal float NearGain, FarGain; // each wall's own loss, as the first wall a sound comes through
        }

        // The walls on a checked line. The far one only if it's another wall (Occlusion.SameWall).
        internal static Walls From(in Occlusion.Block b)
        {
            var w = new Walls();
            if (!b.HasWall) return w;
            w.Near = true; w.NearAt = b.NearAt; w.NearN = b.NearN; w.NearGain = b.NearGain;
            if ((b.FarAt - b.NearAt).sqrMagnitude > Occlusion.SameWall * Occlusion.SameWall)
            {
                w.Far = true; w.FarAt = b.FarAt; w.FarN = b.FarN; w.FarGain = b.FarGain;
            }
            return w;
        }

        // The four ways along a wall's plane: both sides, up, down.
        internal static Vector3 Dir(Vector3 n, int k)
        {
            Vector3 along = Vector3.Cross(Vector3.up, n);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.Cross(Vector3.right, n);
            along.Normalize();
            Vector3 vert = Vector3.Cross(n, along).normalized;
            return k == 0 ? along : k == 1 ? -along : k == 2 ? vert : -vert;
        }

        // How many probes each way: out to where the detour costs what the wall does. Round an edge y
        // off a straight line a m from you and b m from the sound, the detour is y^2/2 (1/a + 1/b).
        internal static int Probes(Vector3 listener, Vector3 at, Vector3 sound, float wallGain)
        {
            float a = Mathf.Max(.05f, (at - listener).magnitude), b = Mathf.Max(.05f, (sound - at).magnitude);
            float y = Mathf.Sqrt(2f * Diffraction.DetourFor(wallGain) / (1f / a + 1f / b));
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(y, Reach) / Spacing), 1, MaxProbes);
        }

        internal static Vector3 Probe(Vector3 at, Vector3 dir, int i) => at + dir * (Spacing * (i + 1));

        // One wall's probe pass, kept: where it was hit, its plane, how far the probes reached, the
        // world then, and the edges found (one per way at most). Edges are places in the world, so they
        // hold for as long as nothing is built or removed near that stretch of wall. A sound keeps two
        // (Slots), one per wall in the way, and each wall is matched to them on its own: walking along
        // a wall, round to its other side, or the far wall coming and going doesn't probe again.
        internal sealed class Record
        {
            internal bool Valid;
            internal Vector3 At, N;
            internal float Span;
            internal int Gen;
            internal readonly Vector3[] Edges = new Vector3[Dirs];
            internal int Count;
        }
        internal const int Slots = 2;

        // A wall hit at "at" (plane n) that this record already covers.
        internal static bool Covers(Record r, Vector3 at, Vector3 n)
        {
            if (!r.Valid || Mathf.Abs(Vector3.Dot(r.N, n)) < .99f) return false;
            Vector3 d = at - r.At;
            float off = Vector3.Dot(d, r.N);
            if (Mathf.Abs(off) >= Occlusion.SameWall || (d - r.N * off).magnitude > r.Span) return false;
            return !Occlusion.ChangedNear(r.Gen, r.At, r.At, r.Span + Occlusion.SameWall);
        }

        internal static int Match(Record[] recs, Vector3 at, Vector3 n)
        {
            for (int i = 0; i < Slots; i++) if (Covers(recs[i], at, n)) return i;
            return -1;
        }

        internal static float LengthGain(Vector3 listener, Vector3 p, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            float path = (p - listener).magnitude + (sound - p).magnitude;
            return Mathf.Min(1f, direct / Mathf.Max(.01f, path)); // the longer way spreads the sound further (1/r)
        }
    }
}
