using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // A cached face: the light's casters drawn straight into it (no camera), the surfaces facing the
    // light into red and the ones facing away into green, each keeping the nearest (min blending, as
    // the moving maps do), by WireShadowFrontBack. Combine pass 4 takes the midpoint rule from there
    // into the depth map.
    internal sealed partial class ShadowCache
    {
        internal static Shader FrontBackShader;
        private static bool OnePass => FrontBackShader != null && FrontBackShader.isSupported &&
                                       CombineShader != null && CombineShader.passCount >= 5;
        private readonly RenderTexture[] _tierRG = new RenderTexture[TierCount], _tierFlatRG = new RenderTexture[TierCount];
        private Material _frontBackMat;
        private static readonly int FrontBackId = Shader.PropertyToID("_WireFrontBack");

        private void MakeOnePassFaces()
        {
            ReleaseOnePassFaces();
            if (!OnePass) return;
            for (int k = 0; k < TierCount; k++)
            {
                int r = TierRes(k);
                _tierRG[k] = new RenderTexture(r, r, 16, RenderTextureFormat.RGHalf)
                { dimension = TextureDimension.Cube, useMipMap = false, filterMode = FilterMode.Point, name = "WireTierFaceRG" + k };
                _tierRG[k].Create();
                _tierFlatRG[k] = new RenderTexture(r, r, 0, RenderTextureFormat.RGHalf)
                { dimension = TextureDimension.Tex2D, useMipMap = false, filterMode = FilterMode.Point, name = "WireTierFlatRG" + k };
                _tierFlatRG[k].Create();
            }
        }

        private void ReleaseOnePassFaces()
        {
            for (int k = 0; k < TierCount; k++)
            {
                if (_tierRG[k] != null) { _tierRG[k].Release(); _tierRG[k] = null; }
                if (_tierFlatRG[k] != null) { _tierFlatRG[k].Release(); _tierFlatRG[k] = null; }
            }
        }

        // Everything a camera with the replacement shader drew, from the light's own list: each
        // renderer still on and reaching this face, one draw per part (pass 1). The game's passing
        // effects (dust, chips) are never part of a stored map.
        private static readonly HashSet<Renderer> s_effectSkip = new HashSet<Renderer>();
        private static void DrawDirect(CommandBuffer cb, Tracked t, float range)
        {
            s_effectSkip.Clear();
            foreach (Renderer r in Effects) if (r != null) s_effectSkip.Add(r);
            int bit = 1 << t.Face;
            float range2 = range * range;
            for (int i = 0; i < t.Direct.Count; i++)
            {
                Renderer r = t.Direct[i];
                if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy || s_effectSkip.Contains(r))
                    continue;
                Bounds b = r.bounds;
                if (b.SqrDistance(t.Center) > range2 || (FacesFor(b.center - t.Center, b.extents.magnitude) & bit) == 0)
                    continue;
                r.GetSharedMaterials(Materials);
                int sub = t.DirectSub[i];
                if (sub >= Materials.Count || Materials[sub] == null) continue;
                cb.DrawRenderer(r, ClipMaterial(Materials[sub], FrontBackShader), sub, 1);
            }
        }

        // The rest of a face: renderers without a RenderType tag (pass 1, front or back by which way each
        // surface faces the light), two-sided sheets into both channels (pass 0), colliders only as
        // backs (pass 2).
        private void DrawUntaggedOnePass(CommandBuffer cb, Tracked t)
        {
            if (t.Explicit.Count == 0 && t.ProxyMeshes.Count == 0 && t.Sheets.Count == 0)
                return;
            if (_frontBackMat == null)
                _frontBackMat = new Material(FrontBackShader) { hideFlags = HideFlags.HideAndDontSave };
            if (_proxyProps == null)
            {
                _proxyProps = new MaterialPropertyBlock();
                _proxyProps.SetFloat("_WireProxy", 1f);
                _structProps = new MaterialPropertyBlock();
                _structProps.SetFloat("_WireProxy", 2f);
            }
            foreach (Renderer r in t.Sheets)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                r.GetSharedMaterials(Materials);
                Mesh clothMesh = IsClothRenderer(r) && !r.isPartOfStaticBatch && r.GetComponent<MeshFilter>() is MeshFilter mf
                    ? mf.sharedMesh : null;
                Matrix4x4 pushed = clothMesh != null
                    ? Matrix4x4.Translate((r.bounds.center - t.Center).normalized * ClothPush) * r.localToWorldMatrix
                    : default;
                for (int sub = 0; sub < Materials.Count; sub++)
                    if (Materials[sub] != null)
                    {
                        if (clothMesh != null && sub < clothMesh.subMeshCount)
                            cb.DrawMesh(clothMesh, pushed, ClipMaterial(Materials[sub], FrontBackShader), sub, 0);
                        else
                            cb.DrawRenderer(r, ClipMaterial(Materials[sub], FrontBackShader), sub, 0);
                    }
            }
            for (int i = 0; i < t.ProxyMeshes.Count; i++)
                cb.DrawMesh(t.ProxyMeshes[i], t.ProxyMatrices[i], _frontBackMat, 0, 2, i < t.NearProxies ? _proxyProps : _structProps);
            for (int i = 0; i < t.Explicit.Count; i++)
            {
                Renderer r = t.Explicit[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                r.GetSharedMaterials(Materials);
                int sub = t.ExplicitSub[i];
                Material orig = sub < Materials.Count ? Materials[sub] : null;
                bool item = StandVisuals.Contains(r) || DropVisuals.Contains(r);
                Material m = orig != null ? ClipMaterial(orig, FrontBackShader, item) : _frontBackMat;
                cb.DrawRenderer(r, m, sub, 1);
            }
        }

        // The face from the one render into the light's cube (combine pass 4: red front, green back).
        private void TierFaceOnePass(Tracked t, int face, bool rendering)
        {
            int tier = rendering ? t.RenderTier : (int)_mapTier[t.Slot];
            int cube = rendering ? t.RenderCube : (int)_mapCube[t.Slot];
            if (tier < 0 || cube < 0) return;
            if (_combine == null)
                _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
            if (_nativeCopyCb == null)
                _nativeCopyCb = new CommandBuffer { name = "Wire's Shadows native depth conversion" };
            _nativeCopyCb.Clear();
            _nativeCopyCb.SetRenderTarget(new RenderTargetIdentifier(_pool[tier], 0, CubemapFace.Unknown, cube * 6 + face));
            _nativeCopyCb.SetGlobalFloat(NativeInvRangeId, 1f / (rendering ? t.RenderRange : t.NativeCacheRange));
            _nativeCopyCb.SetGlobalTexture(FrontBackId, _tierFlatRG[tier]);
            _nativeCopyCb.DrawProcedural(Matrix4x4.identity, _combine, 4, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_nativeCopyCb);
        }
    }
}
