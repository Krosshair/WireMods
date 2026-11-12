using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresLightOcclusion
{
    // Pointlight Rework: cached maps drawn the way Unity draws point shadows.
    // Every caster with its own material's ShadowCaster pass (wind-bent cloth,
    // cutouts, displacement, exactly what casts), into a depth image with the
    // light's own 90 degree projection and near plane, then turned into the
    // distance the rest of the pipeline stores. Only when a face is (re)cached.
    internal sealed partial class ShadowCache
    {
        private struct OwnCaster
        {
            internal Renderer R;
            internal Material[] Mats;
            internal int[] Passes;
        }

        private static readonly Dictionary<Material, int> ShadowPassOf = new Dictionary<Material, int>();
        private static readonly Dictionary<Renderer, bool> LowerLod = new Dictionary<Renderer, bool>();
        private readonly Dictionary<int, RenderTexture> _ownDepth = new Dictionary<int, RenderTexture>();
        private CommandBuffer _ownCb;

        private static int ShadowPass(Material m)
        {
            if (m == null) return -1;
            if (!ShadowPassOf.TryGetValue(m, out int pass))
            {
                if (ShadowPassOf.Count > 8192) ShadowPassOf.Clear();
                ShadowPassOf[m] = pass = m.FindPass("SHADOWCASTER");
            }
            return pass;
        }

        // A renderer of LOD1+ (the camera would have drawn LOD0 instead).
        private static bool IsLowerLod(Renderer r)
        {
            if (!LowerLod.TryGetValue(r, out bool lower))
            {
                LODGroup g = r.GetComponentInParent<LODGroup>();
                if (g != null)
                {
                    LOD[] lods = g.GetLODs();
                    lower = true;
                    if (lods.Length > 0)
                        foreach (Renderer x in lods[0].renderers)
                            if (x == r) { lower = false; break; }
                    // Not listed in any LOD: drawn as normal.
                    bool listed = false;
                    foreach (LOD l in lods)
                        foreach (Renderer x in l.renderers)
                            if (x == r) listed = true;
                    if (!listed) lower = false;
                }
                if (LowerLod.Count > 16384) LowerLod.Clear();
                LowerLod[r] = lower;
            }
            return lower;
        }

        // Called from FindCasters for every renderer the old path would cast with.
        private static void AddOwnCaster(Tracked t, Renderer r)
        {
            if ((StaticLayerMask & (1 << r.gameObject.layer)) == 0 || IsLowerLod(r))
                return;
            Material[] mats = r.sharedMaterials;
            var passes = new int[mats.Length];
            bool any = false;
            for (int i = 0; i < mats.Length; i++)
            {
                passes[i] = ShadowPass(mats[i]);
                any |= passes[i] >= 0;
            }
            if (any)
                t.Own.Add(new OwnCaster { R = r, Mats = mats, Passes = passes });
        }

        private RenderTexture OwnDepth(int size)
        {
            if (!_ownDepth.TryGetValue(size, out RenderTexture rt) || rt == null)
            {
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.Depth)
                { name = "WirePointlightDepth" + size, filterMode = FilterMode.Point, useMipMap = false, autoGenerateMips = false };
                rt.Create();
                _ownDepth[size] = rt;
            }
            return rt;
        }

        // Same directions as the projection, but every point's depth is the near plane:
        // clip z = clip w (reversed Z: depth 1 = near), or 0 without reversed Z.
        private static Matrix4x4 AtNearPlane(Matrix4x4 gpuProj)
        {
            bool reversed = SystemInfo.usesReversedZBuffer;
            gpuProj.m20 = reversed ? gpuProj.m30 : 0f;
            gpuProj.m21 = reversed ? gpuProj.m31 : 0f;
            gpuProj.m22 = reversed ? gpuProj.m32 : 0f;
            gpuProj.m23 = reversed ? gpuProj.m33 : 0f;
            return gpuProj;
        }

        private static Matrix4x4 OwnProj(float near, float far) =>
            GL.GetGPUProjectionMatrix(Matrix4x4.Scale(new Vector3(-1f, -1f, 1f)) * Matrix4x4.Perspective(90f, 1f, near, far), true);

        private static readonly int LightPosRangeId = Shader.PropertyToID("_LightPositionRange");
        private static readonly int WorldLightPosId = Shader.PropertyToID("_WorldSpaceLightPos0");
        private Material _pancakeMat;
        private int _pancakePass = -2;

        // What Unity sets before a point light's shadow casters draw.
        private static void BeginPointShadow(CommandBuffer cb, Vector3 center, float far, bool pancake)
        {
            cb.SetGlobalVector(LightPosRangeId, new Vector4(center.x, center.y, center.z, 1f / far));
            cb.SetGlobalVector(WorldLightPosId, new Vector4(center.x, center.y, center.z, 1f));
            // Bias lives on the receiving side (lighting shader). y = pancaking: only for
            // the near-plane walls, which must block at the near plane instead of vanishing.
            cb.SetGlobalVector(ShadowBiasGlobalId, new Vector4(0f, pancake ? 1f : 0f, 0f, 0f));
        }

        // A material with a real shadow pass to draw collider meshes with.
        private bool PancakeMaterial(out Material mat, out int pass)
        {
            if (_pancakePass == -2)
            {
                Shader sh = Shader.Find("Custom/Piece");
                _pancakeMat = sh != null ? new Material(sh) { hideFlags = HideFlags.HideAndDontSave } : null;
                _pancakePass = _pancakeMat != null ? _pancakeMat.FindPass("SHADOWCASTER") : -1;
                if (_pancakePass < 0) Plugin.Log.LogWarning("[rework] no shadow pass for near-plane walls (Custom/Piece missing)");
            }
            mat = _pancakeMat; pass = _pancakePass;
            return pass >= 0;
        }

        // Vanilla point shadow face, written straight into the comparison cube the
        // lighting reads: casters with their own shadow pass, the light's 90 degree
        // projection and vanilla near plane. Vanilla's drawback fixed: walls the
        // light sits against (inside the near plane) still block, flattened onto it.
        private bool DrawFaceVanilla(Tracked t)
        {
            bool near = t.Slot < _near;
            RenderTexture src = near ? _maps : _farMaps;
            if (src == null || !Plugin.NativeDepthPcf.Value || !EnsureNativeTier(near, src.width))
                return false;
            int sl = t.Slot;
            RenderTexture dst = near ? _nativeNearMaps : _nativeFarMaps;
            int cube = near ? sl : sl - _near;
            if (_nativeOwners[sl] != t) { _nativeOwners[sl] = t; _nativeFaceMasks[sl] = 0; }
            if (t.Face == 0 && !t.Ready) _nativeFaceMasks[sl] = 0;

            float nearP = NearPlane(t), far = Mathf.Max(1f, Range(t));
            Matrix4x4 view = FaceViewBasis[t.Face] * Matrix4x4.Translate(-t.Center);
            int bit = 1 << t.Face;
            if (_ownCb == null) _ownCb = new CommandBuffer { name = "WirePointlightRework" };
            CommandBuffer cb = _ownCb;
            cb.Clear();
            cb.SetRenderTarget(new RenderTargetIdentifier(dst, 0, CubemapFace.Unknown, cube * 6 + t.Face));
            cb.ClearRenderTarget(true, false, Color.clear, 1f); // far; Unity flips it for reversed Z
            cb.SetViewProjectionMatrices(view, OwnProj(nearP, far));
            BeginPointShadow(cb, t.Center, far, false);
            int draws = 0;
            foreach (OwnCaster c in t.Own)
            {
                if (c.R == null || !c.R.enabled || !c.R.gameObject.activeInHierarchy) continue;
                if ((FacesFor(c.R.bounds.center - t.Center, c.R.bounds.extents.magnitude) & bit) == 0) continue;
                for (int sub = 0; sub < c.Passes.Length; sub++)
                    if (c.Passes[sub] >= 0 && c.Mats[sub] != null)
                    {
                        cb.DrawRenderer(c.R, c.Mats[sub], sub, c.Passes[sub]);
                        draws++;
                    }
            }
            if (t.NearProxies > 0)
            {
                // Walls the light sits against (inside the near plane): only their BACK
                // faces, only the part inside the near sphere (the Back shader's proxy
                // discard), written at exactly the near-plane depth. They block what is
                // behind the wall; the wall's own lit surface is never covered.
                if (_backMat == null)
                    _backMat = new Material(BackShader) { hideFlags = HideFlags.HideAndDontSave };
                if (_proxyProps == null)
                {
                    _proxyProps = new MaterialPropertyBlock(); _proxyProps.SetFloat("_WireProxy", 1f);
                    _structProps = new MaterialPropertyBlock(); _structProps.SetFloat("_WireProxy", 2f);
                }
                cb.SetViewProjectionMatrices(view, AtNearPlane(OwnProj(nearP, far)));
                cb.SetGlobalVector(CenterId, t.Center);
                cb.SetGlobalFloat(NearCutId, nearP);
                for (int i = 0; i < t.NearProxies && i < t.ProxyMeshes.Count; i++)
                    cb.DrawMesh(t.ProxyMeshes[i], t.ProxyMatrices[i], _backMat, 0, 0, _proxyProps);
                cb.SetGlobalFloat(NearCutId, 0f);
            }
            cb.SetGlobalVector(ShadowBiasGlobalId, Vector4.zero);
            Graphics.ExecuteCommandBuffer(cb);
            _statDraws += draws;
            t.NativeCacheRange = far;
            _nativeNears[sl] = nearP;
            _nativeFaceMasks[sl] |= bit;
            UpdateGlobals();
            return true;
        }

        private static int _ownProbes;

        // One-time readback of a whole cached face: nearest distance and how many
        // texels hold a caster. Tells which stage writes what.
        private static void OwnProbe(CommandBuffer cb, RenderTexture target, int slice, string what)
        {
            int size = target.width;
            cb.RequestAsyncReadback(target, 0, 0, size, 0, size, slice, 1, req =>
            {
                if (req.hasError) { Plugin.Log.LogInfo("[rework probe] " + what + ": readback failed"); return; }
                var data = req.GetData<ushort>();
                float lo = float.MaxValue; int hit = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    float v = Mathf.HalfToFloat(data[i]);
                    if (v < lo) lo = v;
                    if (v < 1000f) hit++;
                }
                Plugin.Log.LogInfo("[rework probe] " + what + ": nearest " + lo.ToString("F2") + ", texels " + hit + " of " + data.Length);
            });
        }

        private void Merge(CommandBuffer cb, RenderTargetIdentifier faceTarget, RenderTexture depth, int size, float near, float far, Rect? scissor = null)
        {
            cb.SetRenderTarget(faceTarget);
            if (scissor.HasValue) cb.EnableScissorRect(scissor.Value);
            cb.SetGlobalTexture(FabricDepthId, depth, RenderTextureSubElement.Depth);
            cb.SetGlobalFloat(FabricSizeId, size);
            cb.SetGlobalFloat(FabricNearId, near);
            cb.SetGlobalFloat(FabricFarId, far);
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawProcedural(Matrix4x4.identity, _fabricMerge, 0, MeshTopology.Triangles, 3);
        }

        private static readonly Dictionary<Renderer, OwnCaster> MovingOwn = new Dictionary<Renderer, OwnCaster>();

        // Cached per renderer; refreshed when its materials change (equipment swaps).
        private static bool MovingCaster(Renderer r, out OwnCaster c)
        {
            if (!MovingOwn.TryGetValue(r, out c) || c.Mats.Length == 0 || c.Mats[0] != r.sharedMaterial)
            {
                Material[] mats = r.sharedMaterials;
                var passes = new int[mats.Length];
                for (int i = 0; i < mats.Length; i++) passes[i] = ShadowPass(mats[i]);
                c = new OwnCaster { R = r, Mats = mats, Passes = passes };
                if (MovingOwn.Count > 8192) MovingOwn.Clear();
                MovingOwn[r] = c;
            }
            foreach (int p in c.Passes) if (p >= 0) return true;
            return false;
        }

        // A moving face, vanilla style, straight into the moving comparison cube.
        // Returns false (old path) when the native moving array isn't there.
        private bool DrawMovingVanilla(Tracked t, int cube, int face, Matrix4x4 view, float near, float far, Rect sc)
        {
            RenderTexture dst = t.Slot < _near ? _nativeMovingNear : _nativeMovingFar;
            if (dst == null || _nativeMovingFailed || !Plugin.NativeMovingDepthPcf.Value)
                return false;
            _frameCb.SetRenderTarget(new RenderTargetIdentifier(dst, 0, CubemapFace.Unknown, cube * 6 + face));
            _frameCb.ClearRenderTarget(true, false, Color.clear, 1f); // whole face: all its casters are redrawn
            _frameCb.EnableScissorRect(sc);
            _frameCb.SetViewProjectionMatrices(view, OwnProj(near, far));
            BeginPointShadow(_frameCb, t.Center, far, false);
            int draws = 0;
            foreach (int k in FaceRenderers)
            {
                Caster c = Casters[k];
                if (c.R == null || !MovingCaster(c.R, out OwnCaster oc)) continue;
                for (int sub = 0; sub < oc.Passes.Length && sub < c.Subs; sub++)
                    if (oc.Passes[sub] >= 0 && oc.Mats[sub] != null)
                    {
                        _frameCb.DrawRenderer(c.R, oc.Mats[sub], sub, oc.Passes[sub]);
                        draws++;
                    }
            }
            _frameCb.SetGlobalVector(ShadowBiasGlobalId, Vector4.zero);
            _nativeNears[t.Slot] = near;
            _statDraws += draws;
            return true;
        }

        private void ClearMovingVanilla(Tracked t, int cube, int face)
        {
            RenderTexture dst = t.Slot < _near ? _nativeMovingNear : _nativeMovingFar;
            if (dst == null) return;
            _frameCb.SetRenderTarget(new RenderTargetIdentifier(dst, 0, CubemapFace.Unknown, cube * 6 + face));
            _frameCb.ClearRenderTarget(true, false, Color.clear, 1f);
        }

        private static readonly List<int> OwnFallback = new List<int>();

        internal static void ReRenderAll()
        {
            if (Instance == null) return;
            foreach (Tracked t in Instance._slots)
                if (t != null && t.Light != null) { t.Dirty = true; t.DirtyAt = Time.time; }
            Instance.WakeRenders();
        }

        private void ReleaseOwnPass()
        {
            foreach (RenderTexture rt in _ownDepth.Values)
                if (rt != null) { rt.Release(); Destroy(rt); }
            _ownDepth.Clear();
            _ownCb?.Release();
            if (_pancakeMat != null) Destroy(_pancakeMat);
            _ownCb = null;
        }
    }
}
