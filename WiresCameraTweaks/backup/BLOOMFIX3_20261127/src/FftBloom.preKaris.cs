using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace WiresShakeItUp
{
    // Replaces the blur chain of Valheim's bloom with a convolution by a
    // physical glare kernel, done through an FFT on the GPU. Valheim's uber
    // shader still adds it (with lens dirt) before tone mapping, so the Bloom
    // graphics setting and HDR tonemappers that replace the uber shader keep working.
    internal static class FftBloom
    {
        // Must match WIDTH/HEIGHT in WireBloom.compute.
        private const int Width = 2048;
        private const int Height = 1024;

        private static RenderTexture _buffer;
        private static RenderTexture _kernel;
        private static RenderTexture _result;
        private static Vector2Int _size;
        private static int _kernelLevels = -1;
        private static float _kernelTexelPx = -1f;

        private static int _pad, _kernelGen, _rowF, _rowI, _colF, _colI, _multiply, _output;
        private static bool _found;

        private static readonly int SourceId = Shader.PropertyToID("_Source");
        private static readonly int ExposureLensId = Shader.PropertyToID("_WireExposure");
        private static readonly int CurveId = Shader.PropertyToID("_WireBloomCurve");
        private static readonly int BufferId = Shader.PropertyToID("_Buffer");
        private static readonly int KernelId = Shader.PropertyToID("_Kernel");
        private static readonly int ResultId = Shader.PropertyToID("_Result");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int LevelsId = Shader.PropertyToID("_Levels");
        private static readonly int TexelPxId = Shader.PropertyToID("_TexelPx");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");

        internal static bool Active
        {
            get
            {
                if (!ModConfig.BloomEnabled.Value) return false;
                LensEffect.LoadMaterial();
                return LensEffect.BloomCompute != null;
            }
        }

        private static void Find(ComputeShader cs)
        {
            if (_found) return;
            _found = true;
            _pad = cs.FindKernel("Pad");
            _kernelGen = cs.FindKernel("KernelGen");
            _rowF = cs.FindKernel("RowForward");
            _rowI = cs.FindKernel("RowInverse");
            _colF = cs.FindKernel("ColForward");
            _colI = cs.FindKernel("ColInverse");
            _multiply = cs.FindKernel("Multiply");
            _output = cs.FindKernel("Output");
        }

        private static RenderTexture Make(RenderTexture rt, int w, int h, RenderTextureFormat format)
        {
            if (rt != null && rt.width == w && rt.height == h && rt.IsCreated()) return rt;
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }
            rt = new RenderTexture(w, h, 0, format)
            {
                enableRandomWrite = true,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        private static void Transform(ComputeShader cs, RenderTexture target, bool forward)
        {
            int row = forward ? _rowF : _rowI;
            int col = forward ? _colF : _colI;
            cs.SetTexture(row, BufferId, target);
            cs.Dispatch(row, Height, 1, 1);
            cs.SetTexture(col, BufferId, target);
            cs.Dispatch(col, Width, 1, 1);
        }

        // Scene in, bloom texture out (owned here, not by Valheim's texture factory).
        internal static RenderTexture Render(RenderTexture source, Texture exposure, Camera camera, BloomModel.BloomSettings settings)
        {
            ComputeShader cs = LensEffect.BloomCompute;
            Find(cs);

            // Largest size that fits in half the buffer each way: the black
            // other half keeps the kernel from wrapping light across the screen.
            float scale = Mathf.Min((Width / 2f) / source.width, (Height / 2f) / source.height);
            _size = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(source.width * scale)),
                                   Mathf.Max(1, Mathf.RoundToInt(source.height * scale)));

            _buffer = Make(_buffer, Width, Height, RenderTextureFormat.ARGBFloat);
            _kernel = Make(_kernel, Width, Height, RenderTextureFormat.ARGBFloat);
            _result = Make(_result, _size.x, _size.y, RenderTextureFormat.ARGBHalf);

            // Valheim's level count: its blur adds one copy of the light per level,
            // so the kernel gets that many levels and carries that many times the light.
            int levels = Mathf.Clamp((int)(Mathf.Log(camera.pixelHeight / 2, 2f) + settings.radius - 8f), 1, 16);
            float texelPx = (float)source.height / _size.y;
            if (levels != _kernelLevels || Mathf.Abs(texelPx - _kernelTexelPx) > 1e-4f)
            {
                _kernelLevels = levels;
                _kernelTexelPx = texelPx;
                cs.SetInt(LevelsId, levels);
                cs.SetFloat(TexelPxId, texelPx);
                cs.SetTexture(_kernelGen, BufferId, _kernel);
                cs.Dispatch(_kernelGen, Width / 8, Height / 8, 1);
                Transform(cs, _kernel, true);
            }

            // First step is Valheim's prefilter (exposure, threshold) into half res.
            Material lens = LensEffect.LoadMaterial();
            lens.SetTexture(ExposureLensId, exposure != null ? exposure : Texture2D.whiteTexture);
            float threshold = settings.thresholdLinear;
            float knee = threshold * settings.softKnee + 1e-5f;
            lens.SetVector(CurveId, new Vector4(threshold - knee, knee * 2f, 0.25f / knee, threshold));
            RenderTexture owned = RenderTexture.GetTemporary(source.width / 2, source.height / 2, 0, RenderTextureFormat.ARGBHalf);
            owned.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, owned, lens, 5);
            RenderTexture current = owned;
            // carries that many times the thresholded light. Match it.
            // Halve with bilinear (an exact 2x2 average) while it stays above the target, then one last step.
            // Valheim's anti-flicker also weights the first downsample by brightness.
            bool karis = settings.antiFlicker;
            while (current.width / 2 >= _size.x && current.height / 2 >= _size.y)
            {
                RenderTexture next = RenderTexture.GetTemporary(current.width / 2, current.height / 2, 0, RenderTextureFormat.ARGBHalf);
                next.filterMode = FilterMode.Bilinear;
                if (karis) Graphics.Blit(current, next, lens, 6);
                else Graphics.Blit(current, next);
                karis = false;
                if (owned != null) RenderTexture.ReleaseTemporary(owned);
                owned = next;
                current = next;
            }
            RenderTexture small = RenderTexture.GetTemporary(_size.x, _size.y, 0, RenderTextureFormat.ARGBHalf);
            Graphics.Blit(current, small);
            if (owned != null) RenderTexture.ReleaseTemporary(owned);

            cs.SetInts(SizeId, _size.x, _size.y);
            cs.SetFloat(StrengthId, levels);
            cs.SetTexture(_pad, SourceId, small);
            cs.SetTexture(_pad, BufferId, _buffer);
            cs.Dispatch(_pad, Width / 8, Height / 8, 1);
            RenderTexture.ReleaseTemporary(small);

            Transform(cs, _buffer, true);
            cs.SetTexture(_multiply, BufferId, _buffer);
            cs.SetTexture(_multiply, KernelId, _kernel);
            cs.Dispatch(_multiply, Width / 8, Height / 8, 1);
            Transform(cs, _buffer, false);

            cs.SetTexture(_output, BufferId, _buffer);
            cs.SetTexture(_output, ResultId, _result);
            cs.Dispatch(_output, (_size.x + 7) / 8, (_size.y + 7) / 8, 1);
            return _result;
        }
    }

    [HarmonyPatch(typeof(BloomComponent), nameof(BloomComponent.Prepare))]
    internal static class BloomPreparePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BloomComponent __instance, RenderTexture source, Material uberMaterial, Texture autoExposure)
        {
            if (!FftBloom.Active) return true;
            Camera camera = __instance.context.camera;
            if (camera == null) return true;

            RenderTexture bloom = FftBloom.Render(source, autoExposure, camera, __instance.model.settings.bloom);
            uberMaterial.SetTexture("_BloomTex", bloom);
            // Sample scale 1: Valheim's upsample tent stays one texel wide. Its
            // intensity scales ours as it did its own (and RenoDX's bloom slider with it).
            uberMaterial.SetVector("_Bloom_Settings", new Vector2(1f, __instance.model.settings.bloom.intensity * ModConfig.BloomIntensity.Value));
            BloomModel.LensDirtSettings dirt = __instance.model.settings.lensDirt;
            if (dirt.intensity > 0f && dirt.texture != null)
            {
                uberMaterial.SetTexture("_Bloom_DirtTex", dirt.texture);
                uberMaterial.SetFloat("_Bloom_DirtIntensity", dirt.intensity);
                uberMaterial.EnableKeyword("BLOOM_LENS_DIRT");
            }
            else
            {
                uberMaterial.EnableKeyword("BLOOM");
            }
            return false;
        }
    }
}
