using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // A cached face in one camera render instead of two. The surfaces facing the light and the ones
    // facing away used to be two renders (WireShadowFront, WireShadowBack), each culling the scene
    // again; WireShadowFrontBack draws both in one: its first pass the front faces into red, its
    // second the back faces into green, each keeping the nearest (min blending, as the moving maps
    // already do). Combine pass 4 takes the same midpoint rule from there into the depth map.
    // Sized maps only, and only with a bundle that has the shader; otherwise two renders as before.
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

        // What DrawUntagged did in each of the two renders, for the one: renderers without a
        // RenderType tag both ways (passes 0 and 1), two-sided sheets into both channels (pass 2),
        // colliders only as backs (pass 1).
        private void DrawUntaggedOnePass(Tracked t)
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
                            _staticCb.DrawMesh(clothMesh, pushed, ClipMaterial(Materials[sub], FrontBackShader), sub, 2);
                        else
                            _staticCb.DrawRenderer(r, ClipMaterial(Materials[sub], FrontBackShader), sub, 2);
                    }
            }
            for (int i = 0; i < t.ProxyMeshes.Count; i++)
                _staticCb.DrawMesh(t.ProxyMeshes[i], t.ProxyMatrices[i], _frontBackMat, 0, 1, i < t.NearProxies ? _proxyProps : _structProps);
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
                _staticCb.DrawRenderer(r, m, sub, 0);
                _staticCb.DrawRenderer(r, m, sub, 1);
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
