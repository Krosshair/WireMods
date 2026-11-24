using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresAcoustics
{
    // How open a built piece's face is: a grate, a lattice divider, a window, a fence gate. These
    // pieces have one solid box collider over their holes, so rays see a wall; only what's drawn
    // shows the holes. The first time a piece type's box is met, its meshes are drawn flat onto
    // that box's face (128 x 128, the texture cutout counting as the game draws it) and the open
    // part is counted on the GPU, read back without waiting. Kept for the session per piece type
    // and box, so every vanilla and modded piece is measured once, no names. Until its answer
    // arrives (a frame or two) a box counts as solid, as every box did before.
    internal static class OpenPanelArea
    {
        private const int Size = 128;
        private const float Inset = .10f;    // m left off each edge of the face: bevels and trim, not openings (picked)
        private const float MinOpen = .02f;  // under 2% of the face open: slivers, not an opening (picked)
        private const float DepthMargin = .05f; // m past the box's front and back that still counts (picked)
        private const int PerFrame = 4;      // measurements started per frame while any wait (picked)

        private static Shader s_shader;
        private static Material s_material;
        private static readonly Dictionary<string, float> Measured = new Dictionary<string, float>();
        private static readonly HashSet<string> Asked = new HashSet<string>();
        private static readonly Queue<Job> Waiting = new Queue<Job>();
        private static readonly List<BoxCollider> Boxes = new List<BoxCollider>();
        private static readonly List<Renderer> Drawn = new List<Renderer>();
        private static CommandBuffer s_cb;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), MainTexStId = Shader.PropertyToID("_MainTex_ST"), CutoffId = Shader.PropertyToID("_Cutoff");

        private struct Job { internal Piece Piece; internal BoxCollider Box; internal string Key; }

        internal static void Load()
        {
            using (Stream stream = typeof(OpenPanelArea).Assembly.GetManifestResourceStream("wireacoustics.bundle"))
            {
                if (stream == null) return;
                var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                AssetBundle bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                if (bundle == null) return;
                foreach (Shader s in bundle.LoadAllAssets<Shader>())
                    if (s.name == "Hidden/WireAcousticCoverage") s_shader = s;
            }
            if (s_shader != null && s_shader.isSupported && SystemInfo.supportsAsyncGPUReadback)
                s_material = new Material(s_shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        // The open part of this box's face (0 solid, up to 1). Unknown yet: 0, and it's measured.
        internal static float Of(Piece p, Collider c)
        {
            if (s_material == null || p == null || !(c is BoxCollider box)) return 0f;
            string key = KeyOf(p, box);
            if (key == null) return 0f;
            if (Measured.TryGetValue(key, out float open)) return open;
            if (Asked.Add(key)) Waiting.Enqueue(new Job { Piece = p, Box = box, Key = key });
            return 0f;
        }

        private static string KeyOf(Piece p, BoxCollider box)
        {
            p.GetComponentsInChildren(true, Boxes);
            int i = Boxes.IndexOf(box);
            Boxes.Clear();
            if (i < 0) return null;
            string name = p.gameObject.name;
            if (name.EndsWith("(Clone)", System.StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7);
            return name + "#" + i;
        }

        // From Room's tick; nothing while nothing waits.
        internal static void Step()
        {
            if (Waiting.Count == 0) return;
            for (int n = 0; n < PerFrame && Waiting.Count > 0; n++)
            {
                Job j = Waiting.Dequeue();
                if (j.Piece == null || j.Box == null || !Measure(j)) { Asked.Remove(j.Key); }
            }
        }

        private static bool Measure(Job j)
        {
            Transform bt = j.Box.transform;
            Vector3 scale = bt.lossyScale;
            Vector3 size = Vector3.Scale(j.Box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            // looked at along the box's thinnest side
            int k = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            int u = (k + 1) % 3, v = (k + 2) % 3;
            Vector3 axisU = bt.rotation * Axis(u), axisV = bt.rotation * Axis(v), axisK = bt.rotation * Axis(k);
            float hu = size[u] * .5f - Inset, hv = size[v] * .5f - Inset, hk = size[k] * .5f + DepthMargin;
            if (hu <= 0f || hv <= 0f) { Measured[j.Key] = 0f; return true; }

            var frame = new Matrix4x4(axisU, axisV, axisK, new Vector4(0, 0, 0, 1));
            frame.SetColumn(3, new Vector4(0, 0, 0, 1));
            Vector3 centre = bt.TransformPoint(j.Box.center);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * frame.transpose * Matrix4x4.Translate(-centre);
            Matrix4x4 proj = Matrix4x4.Ortho(-hu, hu, -hv, hv, -hk, hk);

            Drawn.Clear();
            LODGroup lod = j.Piece.GetComponentInChildren<LODGroup>();
            if (lod != null && lod.lodCount > 0)
            {
                foreach (Renderer r in lod.GetLODs()[0].renderers) if (r != null) Drawn.Add(r);
            }
            else j.Piece.GetComponentsInChildren(false, Drawn);

            RenderTexture rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.R8);
            if (s_cb == null) s_cb = new CommandBuffer { name = "Wire's Acoustics: panel openness" };
            s_cb.Clear();
            s_cb.SetRenderTarget(rt);
            s_cb.ClearRenderTarget(false, true, Color.clear);
            s_cb.SetViewProjectionMatrices(view, proj);
            int draws = 0;
            foreach (Renderer r in Drawn)
            {
                if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                Material[] mats = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    Material m = mats[s];
                    var props = new MaterialPropertyBlock();
                    Texture tex = m != null && m.HasProperty(MainTexId) ? m.GetTexture(MainTexId) : null;
                    props.SetTexture(MainTexId, tex != null ? tex : Texture2D.whiteTexture);
                    if (m != null && m.HasProperty(MainTexId))
                    {
                        Vector2 sc = m.GetTextureScale(MainTexId), of = m.GetTextureOffset(MainTexId);
                        props.SetVector(MainTexStId, new Vector4(sc.x, sc.y, of.x, of.y));
                    }
                    else props.SetVector(MainTexStId, new Vector4(1, 1, 0, 0));
                    props.SetFloat(CutoffId, m != null && m.HasProperty(CutoffId) ? m.GetFloat(CutoffId) : 0f);
                    s_cb.DrawMesh(mesh, r.localToWorldMatrix, s_material, s, 0, props);
                    draws++;
                }
            }
            Drawn.Clear();
            if (draws == 0) { RenderTexture.ReleaseTemporary(rt); Measured[j.Key] = 0f; return true; }
            Graphics.ExecuteCommandBuffer(s_cb);
            string key = j.Key;
            AsyncGPUReadback.Request(rt, 0, req => Read(req, rt, key));
            return true;
        }

        private static void Read(AsyncGPUReadbackRequest req, RenderTexture rt, string key)
        {
            RenderTexture.ReleaseTemporary(rt);
            if (req.hasError) { Asked.Remove(key); return; }
            var data = req.GetData<byte>();
            int covered = 0;
            for (int i = 0; i < data.Length; i++) if (data[i] > 127) covered++;
            float open = 1f - covered / (float)data.Length;
            if (open < MinOpen) open = 0f;
            Measured[key] = open;
            // An open one: the surfaces already looked at are looked at again, with it.
            if (open > 0f) { Solids.Forget(); Room.Invalidate(); }
        }

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    }
}
