using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Content-derived open-area parameter for Valheim's VISUALLY perforated
    // panels that have broad, solid GAMEPLAY colliders. The acoustics is an
    // area-weighted composite partition, not a new acoustic attenuation law:
    // ASHRAE Fundamentals, Sound and Vibration, composite TL formula.
    //
    // Estimate projected opaque coverage from the ACTUAL renderer mesh
    // triangles just ONCE per prefab/collider type, rather than reconstructing
    // the mesh into acoustic raycast triangles each frame.
    internal static class OpenPanelArea
    {
        private const int Grid = 32;
        private const int MaxTriangles = 30000;
        private static readonly Dictionary<string, float> Cache =
            new Dictionary<string, float>(32);

        internal static float FromPrefab(Piece piece, BoxCollider box)
        {
            if (piece == null || box == null) return 0f;
            string key = piece.gameObject.name;
            if (key.EndsWith("(Clone)", StringComparison.Ordinal))
                key = key.Substring(0, key.Length - 7);
            // Each prefabricated panel type has its own known large collider
            // and narrow bar/frame colliders. Retain separate sample keys.
            key += ":" + box.size.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + box.size.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + box.size.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (Cache.TryGetValue(key, out float ratio)) return ratio;
            ratio = Analyze(piece, box);
            // A failed mesh read is NOT evidence the panel is solid.
            // Explicit content-authored fallback estimates below apply only
            // to these verified open Valheim 1.0.21 prefab IDs.
            if (ratio < 0f) ratio = AuthoredFallback(piece.gameObject.name);
            ratio = Mathf.Clamp01(ratio);
            Cache[key] = ratio;
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("[acoustics] panel " + key
                    + " free-area " + ratio.ToString("P0")
                    + " (prefab mesh projection or documented visual estimate)");
            return ratio;
        }

        private static float AuthoredFallback(string id)
        {
            // Visual estimates, never claimed to be measurements or acoustic
            // constants. These only apply when source meshes are unreadable.
            if (id.StartsWith("iron_grate", StringComparison.Ordinal)) return .70f;
            if (id.StartsWith("wood_fence_gate", StringComparison.Ordinal)) return .55f;
            if (id.StartsWith("wood_window", StringComparison.Ordinal)) return .55f;
            if (id.StartsWith("darkwood_decowall", StringComparison.Ordinal)) return .50f;
            if (id.StartsWith("ashwood_decowall", StringComparison.Ordinal)) return .55f;
            if (id.StartsWith("piece_dvergr_metal_wall_2x2", StringComparison.Ordinal)) return .50f;
            return 0f;
        }

        // Rasterize the union of projected mesh triangles onto the panel's
        // two longest local axes. A 32x32 mask costs 128 bytes and is computed
        // only once per prefab; it is not retained after authoring this ratio.
        private static float Analyze(Piece piece, BoxCollider box)
        {
            try
            {
                MeshFilter[] filters = piece.GetComponentsInChildren<MeshFilter>(false);
                if (filters == null || filters.Length == 0) return -1f;
                bool[] filled = new bool[Grid * Grid];
                int seen = 0, covered = 0;
                Vector3 extent = box.size, c = box.center;
                int normal = extent.x <= extent.y && extent.x <= extent.z ? 0 :
                             extent.y <= extent.z ? 1 : 2;
                int axisU = (normal + 1) % 3, axisV = (normal + 2) % 3;
                float minU = Component(c, axisU) - Component(extent, axisU) * .5f;
                float minV = Component(c, axisV) - Component(extent, axisV) * .5f;
                float invU = 1f / Mathf.Max(.0001f, Component(extent, axisU));
                float invV = 1f / Mathf.Max(.0001f, Component(extent, axisV));
                for (int f = 0; f < filters.Length; f++)
                {
                    MeshFilter filter = filters[f];
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !mesh.isReadable || mesh.triangles.Length > MaxTriangles)
                        continue;
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.enabled) continue;
                    Vector3[] verts = mesh.vertices;
                    int[] indices = mesh.triangles;
                    if (verts.Length == 0 || indices.Length < 3) continue;
                    Vector2[] uv = new Vector2[verts.Length];
                    for (int v = 0; v < verts.Length; v++)
                    {
                        Vector3 local = box.transform.InverseTransformPoint(
                            filter.transform.TransformPoint(verts[v]));
                        uv[v] = new Vector2((Component(local, axisU)-minU)*invU,
                                            (Component(local, axisV)-minV)*invV);
                    }
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector2 a = uv[indices[i]], b = uv[indices[i+1]], d = uv[indices[i+2]];
                        float determinant = Cross(b-a, d-a);
                        if (Mathf.Abs(determinant) < 1e-9f) continue;
                        int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x,d.x))*Grid),0,Grid-1);
                        int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x,d.x))*Grid),0,Grid-1);
                        int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y,d.y))*Grid),0,Grid-1);
                        int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y,d.y))*Grid),0,Grid-1);
                        for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int pixel = y * Grid + x;
                            if (filled[pixel]) continue;
                            Vector2 p = new Vector2((x+.5f)/Grid,(y+.5f)/Grid);
                            float u = Cross(b-a, p-a)/determinant;
                            float v = Cross(p-a, d-a)/determinant;
                            if (u >= 0f && v >= 0f && u+v <= 1f)
                            { filled[pixel] = true; covered++; }
                        }
                    }
                    seen++;
                }
                if (seen == 0) return -1f;
                return 1f - (float)covered / (Grid * Grid);
            }
            catch (Exception e)
            {
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("[acoustics] panel mesh unavailable: " + e.GetType().Name);
                return -1f;
            }
        }

        private static float Component(Vector3 v, int i) =>
            i == 0 ? v.x : i == 1 ? v.y : v.z;
        private static float Cross(Vector2 a, Vector2 b) =>
            a.x*b.y-a.y*b.x;
    }
}
