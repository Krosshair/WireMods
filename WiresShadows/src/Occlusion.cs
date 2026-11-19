using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Build pieces that can't show on screen at all (the furniture upstairs while you're in the
    // basement, the inside of a house from outside) aren't drawn. Each frame the GPU tests every
    // piece's box against a depth pyramid of what was on screen (WireOcclusion.compute) and the
    // answer comes back a few frames later (async readback, no stall). A hidden piece is set to
    // "shadows only": Unity skips it on screen but still draws its sun and moon shadow, and the
    // mod's point-light maps draw it as before (ShowFor). Nothing else about it changes.
    // Because the answer is late, everything that could open a view in the meantime is covered:
    // - the camera moving: each box's screen area is widened by how far the camera could move
    //   until the answer is used, over the distance to the nearest thing in front (parallax); if it
    //   moved more than that anyway (a teleport, a sudden dash), the answer is thrown away and every
    //   piece shown. Turning on the spot can't uncover anything (same eye point); what was off
    //   screen or partly off screen is never hidden.
    // - things that move: characters, ships and carts never count as hiding anything.
    // - a door opening or closing, a piece removed, a piece's look changing (wear): every piece
    //   shown, and nothing hidden again until the door has stopped.
    // - a piece is only hidden after two answers in a row say so; shown again at the first that doesn't.
    // - boxes reaching the camera are never hidden. Doors, ships, carts, windmills, turrets and
    //   anything animated are never hidden (they move).
    internal static class Occlusion
    {
        internal static ComputeShader Compute;
        internal static Shader MaskShader;

        private static bool Wanted =>
            Plugin.OcclusionCulling != null && Plugin.OcclusionCulling.Value && Plugin.UseShader.Value &&
            Compute != null && MaskShader != null && MaskShader.isSupported &&
            SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback;

        // Kept cheap where little is hidden (outside, looking at a house): the depth pyramid is a
        // quarter of the screen's size, the test runs every other frame (picked), and the GPU sends
        // back only the pieces whose answer changed, so nothing here goes through every piece.
        private const int TestEvery = 2;
        private static bool s_gpuReset = true;
        private static ComputeBuffer s_stateBuf;
        private static int s_kClear;

        private sealed class Slot
        {
            internal Piece Piece;
            internal Renderer[] Renderers;
            internal bool Hidden;
            internal int Since; // answers from tests before this frame belong to whatever was here before
        }

        private static Slot[] s_slots = new Slot[0];
        private static Vector4[] s_boxes = new Vector4[0];
        private static int s_count;
        private static readonly Stack<int> s_free = new Stack<int>();
        private static readonly Dictionary<Piece, int> s_index = new Dictionary<Piece, int>();
        private static readonly List<Piece> s_pending = new List<Piece>();
        private static readonly HashSet<int> s_hidden = new HashSet<int>();
        private static bool s_boxDirty;

        private static bool s_active;
        private static Camera s_cam;
        private static CommandBuffer s_cb;
        private static Material s_mask;
        private static ComputeBuffer s_boxBuf, s_resBuf;
        private static RenderTexture[] s_levels;
        private static RenderTexture s_hiz;
        private static int s_w, s_h, s_kReduce, s_kTest;
        private static readonly Plane[] s_planes = new Plane[6];

        // what each test was sent with, by frame
        private struct Sent { internal int Frame; internal Vector3 Cam; internal float Move; }
        private static readonly Sent[] s_sent = new Sent[32];
        private static uint[] s_answer = new uint[0];
        private static int s_answerLength, s_answerFrame = -1;
        private static int s_resetFrame;

        // the camera's moves over the last frames, and how late answers arrive
        private static readonly float[] s_moves = new float[8];
        private static Vector3 s_lastCam;
        private static int s_latency = 3;
        private static readonly int[] s_lateness = new int[32];
        private static int s_latenessAt;

        private static readonly List<Door> s_doors = new List<Door>();
        private static readonly AccessTools.FieldRef<Door, Animator> DoorAnimator = AccessTools.FieldRefAccess<Door, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<Door, ZNetView> DoorView = AccessTools.FieldRefAccess<Door, ZNetView>("m_nview");
        private static readonly System.Reflection.FieldInfo AllPieces = AccessTools.Field(typeof(Piece), "s_allPieces");
        private static readonly System.Reflection.FieldInfo AllVagons = AccessTools.Field(typeof(Vagon), "m_instances");

        private static readonly Dictionary<Component, Renderer[]> s_movers = new Dictionary<Component, Renderer[]>();
        private static readonly Dictionary<Component, int> s_moverFrame = new Dictionary<Component, int>();
        private static readonly List<Component> s_moverScratch = new List<Component>(64);
        private static readonly List<Material> s_matScratch = new List<Material>(4);

        private static readonly System.Action<AsyncGPUReadbackRequest> s_onRead = OnRead;

        // ---- Each frame (ShadowCache.RunFrame, main camera, before it's culled) ----
        internal static void Frame(Camera cam)
        {
            if (!Wanted) { if (s_active) Disable(); return; }
            if (!s_active) Enable();
            if (s_cam != cam) Attach(cam);

            if (s_pending.Count > 0) RegisterPending();

            // the camera's move this frame, and how far it could go until an answer is used
            Vector3 pos = cam.transform.position;
            for (int i = s_moves.Length - 1; i > 0; i--) s_moves[i] = s_moves[i - 1];
            s_moves[0] = (pos - s_lastCam).magnitude;
            s_lastCam = pos;
            float fastest = 0f;
            foreach (float m in s_moves) fastest = Mathf.Max(fastest, m);
            float move = fastest * (s_latency + 1);

            // a door still swinging: nothing gets hidden until it's done
            if (s_doors.Count > 0)
            {
                for (int i = s_doors.Count - 1; i >= 0; i--)
                {
                    Door d = s_doors[i];
                    Animator a = d != null ? DoorAnimator(d) : null;
                    if (a == null || (!a.IsInTransition(0) && a.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f))
                        s_doors.RemoveAt(i);
                }
                if (s_doors.Count > 0) { s_resetFrame = Time.frameCount; s_gpuReset = true; }
            }

            Apply(pos);
            GeometryUtility.CalculateFrustumPlanes(cam, s_planes);
            Record(cam, move);
            s_sent[Time.frameCount & 31] = new Sent { Frame = Time.frameCount, Cam = pos, Move = move };
        }

        // The latest answer, if it's still good for where the camera is now.
        private static void Apply(Vector3 pos)
        {
            int f = s_answerFrame;
            if (f < 0) return;
            s_answerFrame = -1;
            int late = Time.frameCount - f;
            s_lateness[s_latenessAt++ & 31] = late;
            int worst = 1;
            foreach (int l in s_lateness) worst = Mathf.Max(worst, l);
            s_latency = worst;
            if (f < s_resetFrame) return;
            Sent sent = s_sent[f & 31];
            if (sent.Frame != f) return;
            if ((pos - sent.Cam).magnitude > sent.Move + 0.001f) { ShowAll(); return; } // moved more than it allowed for

            // only the pieces whose answer changed (the GPU keeps each one's run of answers)
            int n = Mathf.Min((int)s_answer[1], s_answerLength - 2);
            for (int k = 0; k < n; k++)
            {
                uint v = s_answer[2 + k];
                int i = (int)(v & 0x7FFFFFFFu);
                if (i >= s_count) continue;
                Slot s = s_slots[i];
                if (s == null || s.Since > f) continue;
                if ((v & 0x80000000u) != 0) { if (!s.Hidden) Hide(i); }
                else if (s.Hidden) Show(i);
            }
        }

        private static void OnRead(AsyncGPUReadbackRequest req)
        {
            if (!s_active || req.hasError) return;
            var data = req.GetData<uint>();
            if (data.Length == 0) return;
            if (data.Length < 2) return;
            int len = Mathf.Min(data.Length, 2 + (int)data[1]);
            if (s_answer.Length < len) s_answer = new uint[Mathf.NextPowerOfTwo(len)];
            Unity.Collections.NativeArray<uint>.Copy(data, s_answer, len);
            s_answerLength = len;
            s_answerFrame = (int)s_answer[0];
        }

        private static void Hide(int i)
        {
            Slot s = s_slots[i];
            foreach (Renderer r in s.Renderers)
                if (r != null && r.shadowCastingMode == ShadowCastingMode.On)
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            s.Hidden = true;
            s_hidden.Add(i);
        }

        private static void Show(int i)
        {
            Slot s = s_slots[i];
            foreach (Renderer r in s.Renderers)
                if (r != null && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    r.shadowCastingMode = ShadowCastingMode.On;
            s.Hidden = false;
            s_hidden.Remove(i);
        }

        // Every piece shown, and answers from tests sent before now ignored.
        internal static void ShowAll()
        {
            s_resetFrame = Time.frameCount + 1;
            s_gpuReset = true; // the GPU's per-piece state starts over with it
            if (s_hidden.Count == 0) return;
            foreach (int i in new List<int>(s_hidden))
                Show(i);
        }

        // The mod's point-light map render (ShadowCache): hidden pieces in the light's reach drawn as usual.
        internal static void ShowFor(Vector3 c, float r, List<Renderer> changed, List<ShadowCastingMode> prior)
        {
            if (s_hidden.Count == 0) return;
            float r2 = r * r;
            foreach (int i in s_hidden)
            {
                Vector3 mn = s_boxes[i * 2], mx = s_boxes[i * 2 + 1];
                Vector3 q = new Vector3(Mathf.Clamp(c.x, mn.x, mx.x), Mathf.Clamp(c.y, mn.y, mx.y), Mathf.Clamp(c.z, mn.z, mx.z));
                if ((q - c).sqrMagnitude > r2) continue;
                foreach (Renderer rd in s_slots[i].Renderers)
                    if (rd != null && rd.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    {
                        changed.Add(rd);
                        prior.Add(ShadowCastingMode.ShadowsOnly);
                        rd.shadowCastingMode = ShadowCastingMode.On;
                    }
            }
        }

        // ---- Pieces ----
        private static void RegisterPending()
        {
            for (int i = 0; i < s_pending.Count; i++) Register(s_pending[i]);
            s_pending.Clear();
        }

        private static void Register(Piece p)
        {
            if (p == null || s_index.ContainsKey(p)) return;
            ZNetView nv = p.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return; // the build preview, or not a real piece yet
            if (p.GetComponentInChildren<Door>(true) || p.GetComponentInChildren<Ship>(true) || p.GetComponentInChildren<Vagon>(true) ||
                p.GetComponentInChildren<Windmill>(true) || p.GetComponentInChildren<Turret>(true) || p.GetComponentInChildren<Animator>(true))
                return; // parts that move
            var list = new List<Renderer>();
            foreach (MeshRenderer r in p.GetComponentsInChildren<MeshRenderer>(true))
                if (r.shadowCastingMode == ShadowCastingMode.On) list.Add(r);
            if (list.Count == 0) return;
            int i = s_free.Count > 0 ? s_free.Pop() : s_count++;
            if (i >= s_slots.Length)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, i + 1));
                System.Array.Resize(ref s_slots, cap);
                System.Array.Resize(ref s_boxes, cap * 2);
            }
            s_slots[i] = new Slot { Piece = p, Renderers = list.ToArray(), Since = Time.frameCount + 1 };
            s_index[p] = i;
            SetBox(i);
        }

        private static void SetBox(int i)
        {
            Slot s = s_slots[i];
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in s.Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any)
            {
                Vector3 mn = b.min, mx = b.max;
                s_boxes[i * 2] = new Vector4(mn.x, mn.y, mn.z, 0f);
                s_boxes[i * 2 + 1] = new Vector4(mx.x, mx.y, mx.z, 0f);
            }
            else s_boxes[i * 2] = new Vector4(0f, 0f, 0f, 1f); // nothing showing to test
            s_boxDirty = true;
        }

        private static void Unregister(Piece p)
        {
            if (!s_index.TryGetValue(p, out int i)) return;
            s_index.Remove(p);
            Slot s = s_slots[i];
            // a piece in view going away (broken, removed) can uncover what's behind it
            if (!s.Hidden && s_boxes[i * 2].w < 0.5f &&
                GeometryUtility.TestPlanesAABB(s_planes, new Bounds((Vector3)(s_boxes[i * 2] + s_boxes[i * 2 + 1]) * 0.5f, s_boxes[i * 2 + 1] - s_boxes[i * 2])))
                ShowAll();
            if (s.Hidden) Show(i);
            s_slots[i] = null;
            s_boxes[i * 2] = new Vector4(0f, 0f, 0f, 1f);
            s_boxDirty = true;
            s_free.Push(i);
        }

        // ---- The GPU side ----
        private static void Enable()
        {
            s_active = true;
            s_kReduce = Compute.FindKernel("Reduce");
            s_kTest = Compute.FindKernel("Test");
            s_kClear = Compute.FindKernel("Clear");
            s_gpuReset = true;
            s_mask = new Material(MaskShader) { hideFlags = HideFlags.HideAndDontSave };
            s_mask.SetFloat("_WireFar", SystemInfo.usesReversedZBuffer ? 0f : 1f);
            s_mask.SetFloat("_WireReversed", SystemInfo.usesReversedZBuffer ? 1f : 0f);
            Compute.SetFloat("_Reversed", SystemInfo.usesReversedZBuffer ? 1f : 0f);
            s_cb = new CommandBuffer { name = "Wire's Shadows occlusion" };
            s_resetFrame = Time.frameCount + 1;
            if (AllPieces?.GetValue(null) is List<Piece> all)
                s_pending.AddRange(all);
        }

        private static void Attach(Camera cam)
        {
            if (s_cam != null) s_cam.RemoveCommandBuffer(CameraEvent.AfterEverything, s_cb);
            s_cam = cam;
            s_lastCam = cam.transform.position;
            cam.AddCommandBuffer(CameraEvent.AfterEverything, s_cb);
            ShowAll();
        }

        internal static void Disable()
        {
            if (!s_active) return;
            ShowAll();
            s_active = false;
            if (s_cam != null && s_cb != null) s_cam.RemoveCommandBuffer(CameraEvent.AfterEverything, s_cb);
            s_cam = null;
            s_cb?.Release(); s_cb = null;
            s_boxBuf?.Release(); s_boxBuf = null;
            s_resBuf?.Release(); s_resBuf = null;
            s_stateBuf?.Release(); s_stateBuf = null;
            ReleaseTargets();
            if (s_mask != null) Object.Destroy(s_mask);
            s_mask = null;
            s_slots = new Slot[0]; s_boxes = new Vector4[0]; s_count = 0;
            s_free.Clear(); s_index.Clear(); s_pending.Clear(); s_hidden.Clear(); s_doors.Clear();
            s_movers.Clear(); s_moverFrame.Clear();
            s_answerFrame = -1;
        }

        private static void ReleaseTargets()
        {
            if (s_levels != null) foreach (RenderTexture rt in s_levels) if (rt != null) rt.Release();
            s_levels = null;
            if (s_hiz != null) s_hiz.Release();
            s_hiz = null;
        }

        private static void EnsureTargets(int w, int h)
        {
            if (s_hiz != null && s_w == w && s_h == h) return;
            ReleaseTargets();
            s_w = w; s_h = h;
            int levels = 1;
            while ((Mathf.Max(w, h) >> levels) > 0) levels++;
            s_levels = new RenderTexture[levels];
            for (int m = 0; m < levels; m++)
            {
                s_levels[m] = new RenderTexture(Mathf.Max(1, w >> m), Mathf.Max(1, h >> m), 0, RenderTextureFormat.RGFloat)
                { enableRandomWrite = true, filterMode = FilterMode.Point, name = "Wire occlusion " + m };
                s_levels[m].Create();
            }
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.RGFloat, 0)
            { useMipMap = true, autoGenerateMips = false, mipCount = levels };
            s_hiz = new RenderTexture(desc) { filterMode = FilterMode.Point, name = "Wire occlusion pyramid" };
            s_hiz.Create();
        }

        private static void Record(Camera cam, float move)
        {
            CommandBuffer cb = s_cb;
            cb.Clear();
            if (s_count == 0 || Time.frameCount % TestEvery != 0) return;
            if (cam.pixelWidth <= 0 || cam.pixelHeight <= 0) return;
            int w = Mathf.Max(1, (cam.pixelWidth + 3) / 4), h = Mathf.Max(1, (cam.pixelHeight + 3) / 4);
            EnsureTargets(w, h);
            int cap = Mathf.Max(1, s_slots.Length);
            if (s_boxBuf == null || s_boxBuf.count < cap * 2)
            {
                s_boxBuf?.Release(); s_resBuf?.Release(); s_stateBuf?.Release();
                s_boxBuf = new ComputeBuffer(cap * 2, 16);
                s_resBuf = new ComputeBuffer(cap + 2, 4);
                s_stateBuf = new ComputeBuffer(cap, 4);
                s_boxDirty = true;
                ShowAll(); // the new state starts empty: nothing hidden
            }
            if (s_boxDirty) { s_boxBuf.SetData(s_boxes, 0, 0, Mathf.Max(1, s_count) * 2); s_boxDirty = false; }
            // level 0: the depth, with the moving things painted out
            cb.Blit(BuiltinRenderTextureType.CameraTarget, s_levels[0], s_mask, 1);
            cb.SetRenderTarget(s_levels[0]);
            cb.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            DrawMovers(cb);
            // the rest of the pyramid
            for (int m = 1; m < s_levels.Length; m++)
            {
                RenderTexture src = s_levels[m - 1], dst = s_levels[m];
                cb.SetComputeTextureParam(Compute, s_kReduce, "_Src", src);
                cb.SetComputeTextureParam(Compute, s_kReduce, "_Dst", dst);
                cb.SetComputeIntParams(Compute, "_Size", src.width, src.height, dst.width, dst.height);
                cb.DispatchCompute(Compute, s_kReduce, (dst.width + 7) / 8, (dst.height + 7) / 8, 1);
            }
            for (int m = 0; m < s_levels.Length; m++)
                cb.CopyTexture(s_levels[m], 0, 0, s_hiz, 0, m);
            // the test
            Matrix4x4 proj = cam.projectionMatrix;
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(proj, false) * cam.worldToCameraMatrix;
            float near = cam.nearClipPlane, far = cam.farClipPlane;
            Vector4 zb = SystemInfo.usesReversedZBuffer
                ? new Vector4(-1f + far / near, 1f, (-1f + far / near) / far, 1f / far)
                : new Vector4(1f - far / near, far / near, (1f - far / near) / far, (far / near) / far);
            cb.SetComputeTextureParam(Compute, s_kTest, "_HiZ", s_hiz);
            cb.SetComputeBufferParam(Compute, s_kTest, "_Boxes", s_boxBuf);
            cb.SetComputeBufferParam(Compute, s_kTest, "_Result", s_resBuf);
            cb.SetComputeMatrixParam(Compute, "_VP", vp);
            cb.SetComputeVectorParam(Compute, "_Cam", new Vector4(proj[0, 0], proj[1, 1], move, s_count));
            cb.SetComputeVectorParam(Compute, "_ZB", zb);
            cb.SetComputeIntParams(Compute, "_HiZInfo", w, h, s_levels.Length, Time.frameCount);
            cb.SetComputeIntParam(Compute, "_Reset", s_gpuReset ? 1 : 0);
            s_gpuReset = false;
            cb.SetComputeBufferParam(Compute, s_kClear, "_Result", s_resBuf);
            cb.DispatchCompute(Compute, s_kClear, 1, 1, 1);
            cb.SetComputeBufferParam(Compute, s_kTest, "_State", s_stateBuf);
            cb.DispatchCompute(Compute, s_kTest, (s_count + 63) / 64, 1, 1);
            cb.RequestAsyncReadback(s_resBuf, (s_count + 2) * 4, 0, s_onRead);
        }

        // Characters, ships and carts on screen: their renderers, looked up again every second or so
        // (equipment changes).
        private static void DrawMovers(CommandBuffer cb)
        {
            s_moverScratch.Clear();
            foreach (Character c in Character.GetAllCharacters()) if (c != null) s_moverScratch.Add(c);
            foreach (IMonoUpdater u in Ship.Instances) if (u is Ship sh && sh != null) s_moverScratch.Add(sh);
            if (AllVagons?.GetValue(null) is List<Vagon> carts) foreach (Vagon v in carts) if (v != null) s_moverScratch.Add(v);
            int now = Time.frameCount;
            foreach (Component c in s_moverScratch)
            {
                if (!s_movers.TryGetValue(c, out Renderer[] rs) || now - s_moverFrame[c] > 60)
                {
                    rs = c.GetComponentsInChildren<Renderer>();
                    s_movers[c] = rs;
                    s_moverFrame[c] = now + (c.GetInstanceID() & 15); // spread the lookups over frames
                }
                foreach (Renderer r in rs)
                {
                    if (r == null || !r.isVisible || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    r.GetSharedMaterials(s_matScratch);
                    for (int sub = 0; sub < s_matScratch.Count; sub++)
                        cb.DrawRenderer(r, s_mask, sub, 0);
                }
            }
            if (s_movers.Count > s_moverScratch.Count * 2 + 64) // gone ones
            {
                var gone = new List<Component>();
                foreach (Component c in s_movers.Keys) if (c == null) gone.Add(c);
                foreach (Component c in gone) { s_movers.Remove(c); s_moverFrame.Remove(c); }
            }
        }

        // ---- What tells it ----
        [HarmonyPatch(typeof(Piece), "Awake")]
        private static class PieceAwake
        {
            private static void Postfix(Piece __instance) { if (s_active) s_pending.Add(__instance); }
        }

        [HarmonyPatch(typeof(Piece), "OnDestroy")]
        private static class PieceGone
        {
            private static void Prefix(Piece __instance) { if (s_active) Unregister(__instance); }
        }

        [HarmonyPatch(typeof(Door), "SetState")]
        private static class DoorMoves
        {
            private static void Prefix(Door __instance, int state)
            {
                if (!s_active) return;
                Animator a = DoorAnimator(__instance);
                ZNetView nv = DoorView(__instance);
                if (a == null || nv == null || !nv.IsValid() || a.GetInteger("state") == state) return;
                if (!s_doors.Contains(__instance)) s_doors.Add(__instance);
                ShowAll();
            }
        }

        [HarmonyPatch(typeof(WearNTear), "SetHealthVisual")]
        private static class WearChanges
        {
            private static void Prefix(WearNTear __instance, out int __state) => __state = Look(__instance);

            private static void Postfix(WearNTear __instance, int __state)
            {
                if (!s_active || Look(__instance) == __state) return;
                Piece p = __instance.GetComponent<Piece>();
                if (p == null || !s_index.TryGetValue(p, out int i)) return;
                SetBox(i);
                ShowAll();
            }

            private static int Look(WearNTear w) =>
                (w.m_new != null && w.m_new.activeSelf ? 1 : 0) | (w.m_worn != null && w.m_worn.activeSelf ? 2 : 0) |
                (w.m_broken != null && w.m_broken.activeSelf ? 4 : 0);
        }
    }
}
