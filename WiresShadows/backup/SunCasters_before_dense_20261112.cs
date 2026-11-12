using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Building pieces the sun can't reach are left out of the sun's shadow map. The sun is one light
    // and its map keeps only the nearest thing in the way, so a piece that is in shadow all over (a
    // chest in a basement, a floor under a roof, a wall behind a hill) can't add anything to it: every
    // line from the sun to that piece, or past it, already meets something nearer the sun first. Such a
    // piece's sun shadow is switched off; what you see doesn't change.
    //  - "In shadow all over": lines toward the sun from points spread through the piece's box (more
    //    for bigger pieces) all hit another piece, rock or the ground. Done a batch of pieces at a
    //    time on worker threads, every piece within the sun's shadow range again every few seconds,
    //    so it follows the sun.
    //  - Unity switches a renderer's shadow off for every light at once, so the pieces near a light
    //    that still uses Unity's own shadows (a carried torch, a ship's lantern) keep theirs, and
    //    this mod's own point light maps go by each piece's real setting (ModeOf).
    //  - Building, removing or breaking a piece, or a door opening, puts the pieces around it back
    //    at once (until they're checked again): a roof gone lets the sun in straight away.
    //  - A big change of the light's direction (sun to moon) puts everything back first.
    //  - Doors, ships and carts are never left out (they move). Other mods' merged meshes aren't
    //    pieces and are never touched.
    internal static class SunCasters
    {
        private sealed class Entry
        {
            internal Piece Piece;
            internal Renderer[] Renderers;
            internal ShadowCastingMode[] Modes; // as they were before being left out
            internal HashSet<int> Colliders;
            internal Bounds Box;
            internal bool Measured, Excluded, Off;
            internal float HeldUntil; // put back, not to be left out again before this
        }

        private const float Margin = 20f;          // past the shadow distance (a long shadow from just outside)
        private const float TorchMargin = 3f;      // past a Unity-shadowed light's range (it moves between checks)
        private const float RestoreRadius = 16f;   // round a built, removed or opened piece
        private const float Interval = .25f;       // seconds between batches
        private const int RaysPerBatch = 6000;
        private const int MaxHits = 4;
        private const float BigTurn = 5f;          // degrees: the light jumped (sun to moon)
        private const float RayLength = 1000f;

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<Piece, Entry> ByPiece = new Dictionary<Piece, Entry>();
        private static readonly Dictionary<Renderer, ShadowCastingMode> Original = new Dictionary<Renderer, ShadowCastingMode>();
        internal static readonly HashSet<Light> UnityShadowed = new HashSet<Light>();

        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        private static readonly List<Entry> s_batch = new List<Entry>();
        private static readonly List<int> s_batchStart = new List<int>(), s_batchCount = new List<int>();
        private static int s_cursor, s_mask;
        private static float s_next;
        private static Vector3 s_lastDir;
        private static bool s_haveDir, s_wasOn;
        private static int s_left, s_checked, s_most;

        // Each renderer's shadow setting as the piece has it, left out or not.
        internal static ShadowCastingMode ModeOf(Renderer r) =>
            r != null && Original.TryGetValue(r, out ShadowCastingMode m) ? m : r.shadowCastingMode;

        internal static void Add(Piece piece)
        {
            if (piece == null || ByPiece.ContainsKey(piece)) return;
            var e = new Entry { Piece = piece };
            ByPiece[piece] = e;
            Entries.Add(e);
            PutBackNear(piece.transform.position);
        }

        internal static void Gone(Piece piece)
        {
            if (piece == null || !ByPiece.TryGetValue(piece, out Entry e)) return;
            Restore(e);
            ByPiece.Remove(piece);
            Entries.Remove(e);
            PutBackNear(piece.transform.position);
        }

        // A door opened or closed: the sun may come in.
        internal static void Opened(Vector3 at) => PutBackNear(at);

        // Gathered and done once per batch (a zone loading builds hundreds of pieces at once).
        private static readonly List<Vector3> s_changes = new List<Vector3>();
        private static void PutBackNear(Vector3 at)
        {
            if (s_left == 0 || s_changes.Count >= 256) return;
            foreach (Vector3 c in s_changes)
                if ((c - at).sqrMagnitude < 4f) return;
            s_changes.Add(at);
        }

        private static void PutBackChanged(float now)
        {
            if (s_changes.Count == 0) return;
            if (s_left > 0)
                foreach (Entry e in Entries)
                {
                    if (!e.Off) continue;
                    foreach (Vector3 at in s_changes)
                        if ((e.Box.center - at).sqrMagnitude < RestoreRadius * RestoreRadius)
                        {
                            Restore(e);
                            e.HeldUntil = now + 1f;
                            break;
                        }
                }
            s_changes.Clear();
        }

        private static void Measure(Entry e)
        {
            e.Measured = true;
            Piece p = e.Piece;
            if (p.GetComponentInChildren<Door>(true) != null || p.GetComponentInChildren<Ship>(true) != null ||
                p.GetComponentInChildren<Vagon>(true) != null)
            {
                e.Excluded = true;
                return;
            }
            foreach (Rigidbody b in p.GetComponentsInChildren<Rigidbody>(true))
                if (!b.isKinematic) { e.Excluded = true; return; }
            var rs = new List<Renderer>();
            bool have = false;
            Bounds box = default;
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                rs.Add(r);
                if (!have) { box = r.bounds; have = true; }
                else box.Encapsulate(r.bounds);
            }
            if (!have) { e.Excluded = true; return; }
            e.Renderers = rs.ToArray();
            e.Modes = new ShadowCastingMode[e.Renderers.Length];
            e.Box = box;
            e.Colliders = new HashSet<int>();
            foreach (Collider c in p.GetComponentsInChildren<Collider>(true)) e.Colliders.Add(c.GetInstanceID());
        }

        private static void LeaveOut(Entry e)
        {
            if (e.Off) return;
            for (int i = 0; i < e.Renderers.Length; i++)
            {
                Renderer r = e.Renderers[i];
                if (r == null) continue;
                e.Modes[i] = r.shadowCastingMode;
                if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                Original[r] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            e.Off = true;
            s_left++;
            if (s_left > s_most) s_most = s_left;
        }

        private static void Restore(Entry e)
        {
            if (!e.Off) return;
            for (int i = 0; i < e.Renderers.Length; i++)
            {
                Renderer r = e.Renderers[i];
                if (r == null || !Original.TryGetValue(r, out ShadowCastingMode m)) continue;
                // only if nothing else changed it since
                if (r.shadowCastingMode == ShadowCastingMode.Off) r.shadowCastingMode = m;
                Original.Remove(r);
            }
            e.Off = false;
            s_left--;
        }

        internal static void RestoreAll()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            foreach (Entry e in Entries) Restore(e);
            s_left = 0;
            Original.Clear();
            s_changes.Clear();
            s_haveDir = false;
        }

        // From ShadowCache.Update (every frame; real work 4 times a second).
        internal static void Step(bool on)
        {
            if (!on)
            {
                if (s_wasOn) RestoreAll();
                s_wasOn = false;
                return;
            }
            s_wasOn = true;
            if (s_pending)
            {
                if (!s_job.IsCompleted && Time.time < s_next) return;
                s_job.Complete();
                s_pending = false;
                Read();
            }
            PutBackChanged(Time.time);
            if (Time.time < s_next) return;
            s_next = Time.time + Interval;

            Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
            Camera cam = Utils.GetMainCamera();
            if (sun == null || cam == null || !sun.enabled || sun.shadows == LightShadows.None)
            {
                if (s_left > 0) RestoreAll();
                return;
            }
            Vector3 toSun = -sun.transform.forward;
            if (s_haveDir && Vector3.Angle(toSun, s_lastDir) > BigTurn && s_left > 0)
                foreach (Entry e in Entries) Restore(e);
            s_lastDir = toSun;
            s_haveDir = true;

            Entries.RemoveAll(Dead);
            Vector3 eye = cam.transform.position;
            float range = QualitySettings.shadowDistance + Margin;
            float now = Time.time;
            CloseToTorches(now);

            if (!s_cmds.IsCreated)
            {
                s_cmds = new NativeArray<RaycastCommand>(RaysPerBatch, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(RaysPerBatch * MaxHits, Allocator.Persistent);
            }
            if (s_mask == 0) s_mask = LayerMask.GetMask("piece", "static_solid", "terrain");
            var q = new QueryParameters(s_mask, false, QueryTriggerInteraction.Ignore, false);
            s_batch.Clear(); s_batchStart.Clear(); s_batchCount.Clear();
            int rays = 0, looked = 0;
            while (looked < Entries.Count)
            {
                if (s_cursor >= Entries.Count) s_cursor = 0;
                Entry e = Entries[s_cursor];
                if (!e.Measured && (e.Piece.transform.position - eye).sqrMagnitude > range * range)
                {
                    s_cursor++; looked++;
                    continue; // measured once it's near
                }
                if (!e.Measured) Measure(e);
                if (e.Excluded) { s_cursor++; looked++; continue; }
                if ((e.Box.center - eye).sqrMagnitude > range * range)
                {
                    // out of the sun's shadow range: put back, so it never comes in left out
                    if (e.Off) Restore(e);
                    s_cursor++; looked++;
                    continue;
                }
                int n = Samples(e.Box, out int nx, out int ny, out int nz);
                if (rays + n > RaysPerBatch) break;
                s_batch.Add(e); s_batchStart.Add(rays); s_batchCount.Add(n);
                Vector3 min = e.Box.min, size = e.Box.size;
                for (int ix = 0; ix < nx; ix++)
                    for (int iy = 0; iy < ny; iy++)
                        for (int iz = 0; iz < nz; iz++)
                        {
                            // inset 5% from the box's faces
                            var t = new Vector3(Along(ix, nx), Along(iy, ny), Along(iz, nz));
                            Vector3 p = min + Vector3.Scale(size, t);
                            s_cmds[rays++] = new RaycastCommand(p, toSun, q, RayLength);
                        }
                s_cursor++; looked++;
            }
            if (rays == 0) return;
            s_checked += s_batch.Count;
            s_job = RaycastCommand.ScheduleBatch(s_cmds.GetSubArray(0, rays), s_hits.GetSubArray(0, rays * MaxHits), 32, MaxHits);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static bool Dead(Entry e)
        {
            if (e.Piece != null) return false;
            if (e.Off)
            {
                foreach (Renderer r in e.Renderers) Original.Remove(r);
                s_left--;
            }
            return true;
        }

        private static float Along(int i, int n) => n == 1 ? .5f : .05f + .9f * i / (n - 1);

        // 2 to 4 points along each side of the box, by its size (one every metre or so).
        private static int Samples(Bounds b, out int nx, out int ny, out int nz)
        {
            nx = Mathf.Clamp(Mathf.CeilToInt(b.size.x) + 1, 2, 4);
            ny = Mathf.Clamp(Mathf.CeilToInt(b.size.y) + 1, 2, 4);
            nz = Mathf.Clamp(Mathf.CeilToInt(b.size.z) + 1, 2, 4);
            return nx * ny * nz;
        }

        private static void Read()
        {
            float now = Time.time;
            for (int k = 0; k < s_batch.Count; k++)
            {
                Entry e = s_batch[k];
                if (e.Piece == null || !ByPiece.ContainsKey(e.Piece)) continue;
                bool hidden = true;
                int start = s_batchStart[k], count = s_batchCount[k];
                for (int r = start; r < start + count && hidden; r++)
                {
                    bool blocked = false;
                    for (int h = 0; h < MaxHits; h++)
                    {
                        Collider c = s_hits[r * MaxHits + h].collider;
                        if (c == null) break; // the rest of the slots are left over from before
                        if (!e.Colliders.Contains(c.GetInstanceID())) { blocked = true; break; }
                    }
                    if (!blocked) hidden = false;
                }
                if (hidden && now >= e.HeldUntil && !NearTorch(e)) LeaveOut(e);
                else if (!hidden && e.Off) Restore(e);
            }
            s_batch.Clear();
        }

        // Lights that use Unity's own shadows need every piece in their range.
        private static readonly List<Vector4> s_torches = new List<Vector4>();
        private static void CloseToTorches(float now)
        {
            s_torches.Clear();
            UnityShadowed.RemoveWhere(l => l == null);
            foreach (Light l in UnityShadowed)
                if (l.enabled && l.shadows != LightShadows.None && l.gameObject.activeInHierarchy)
                {
                    Vector3 p = l.transform.position;
                    s_torches.Add(new Vector4(p.x, p.y, p.z, l.range + TorchMargin));
                }
            if (s_torches.Count == 0 || s_left == 0) return;
            foreach (Entry e in Entries)
                if (e.Off && NearTorch(e))
                {
                    Restore(e);
                    e.HeldUntil = now + 1f;
                }
        }

        private static bool NearTorch(Entry e)
        {
            foreach (Vector4 t in s_torches)
            {
                float r = t.w + e.Box.extents.magnitude;
                if ((e.Box.center - (Vector3)t).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        internal static void Dispose()
        {
            RestoreAll();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }

        internal static string Summary()
        {
            // the most at once since the last report (the benchmark ends on a phase with this off)
            string s = "sun shadows: up to " + s_most + " of " + Entries.Count + " pieces left out, " + s_checked + " checked";
            s_checked = 0; s_most = s_left;
            return s;
        }
    }
}
