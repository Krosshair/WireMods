using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.UI;

namespace WiresUpscalers
{
    // On the game's camera. The game renders at its own render scale (graphics settings); the chosen
    // upscaler takes that image, its depth and motion, and makes the screen-size picture, which then
    // stands in for the game's own stretched one. At 100% render scale it's the upscaler's native
    // anti-aliasing (DLAA and the like).
    [RequireComponent(typeof(Camera))]
    internal sealed class UpscaleRenderer : MonoBehaviour
    {
        private static readonly List<UpscaleRenderer> All = new List<UpscaleRenderer>(2);
        private Camera _cam;
        private PostProcessingBehaviour _post;
        private RenderTexture _color, _depth, _motion, _output;
        private System.IntPtr _colorPtr, _depthPtr, _motionPtr, _outputPtr;
        private int _inW, _inH, _outW, _outH;
        private bool _reset = true, _jittered, _oldTransparentJitter, _aaOff, _oldAa;
        private Vector2 _jitter;
        private int _jitterIndex;
        private readonly List<RawImage> _shown = new List<RawImage>(2);
        private Upscaler _last;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _post = GetComponent<PostProcessingBehaviour>();
            All.Add(this);
        }

        private void OnDestroy() { Release(); All.Remove(this); }

        internal static void SettingsChanged()
        {
            foreach (UpscaleRenderer r in All) if (r != null) r.Release();
            Native.Release();
        }

        private bool Active => Plugin.Current != Upscaler.None && Native.Has(Plugin.Current);

        private static float Halton(int i, int b)
        {
            float f = 1f, r = 0f;
            for (; i > 0; i /= b) { f /= b; r += f * (i % b); }
            return r;
        }

        // A sub-pixel shift of the view each frame (the upscaler gathers detail from these over time).
        private void OnPreCull()
        {
            _jittered = false;
            if (!Active || _inW < 1) return;
            _cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            float ratio = _outW / (float)_inW;
            int phases = Mathf.Clamp(Mathf.CeilToInt(8f * ratio * ratio), 8, 128);
            _jitterIndex = (_jitterIndex + 1) % phases;
            _jitter = new Vector2(Halton(_jitterIndex + 1, 2) - .5f, Halton(_jitterIndex + 1, 3) - .5f);
            _cam.ResetProjectionMatrix();
            Matrix4x4 p = _cam.projectionMatrix;
            _cam.nonJitteredProjectionMatrix = p;
            p.m02 += 2f * _jitter.x / _inW;
            p.m12 += 2f * _jitter.y / _inH;
            _oldTransparentJitter = _cam.useJitteredProjectionMatrixForTransparentRendering;
            _cam.projectionMatrix = p;
            _cam.useJitteredProjectionMatrixForTransparentRendering = true;
            _jittered = true;
        }

        private void OnPostRender()
        {
            if (!_jittered) return;
            _cam.ResetProjectionMatrix();
            _cam.nonJitteredProjectionMatrix = _cam.projectionMatrix;
            _cam.useJitteredProjectionMatrixForTransparentRendering = _oldTransparentJitter;
            _jittered = false;
        }

        private static RenderTexture Make(int w, int h, RenderTextureFormat f, bool uav, string name)
        {
            var rt = new RenderTexture(w, h, 0, f, RenderTextureReadWrite.Linear)
            { name = name, enableRandomWrite = uav, antiAliasing = 1, filterMode = FilterMode.Bilinear };
            rt.Create();
            return rt;
        }

        // Our own copies of the inputs: the image effect's source changes from frame to frame, and the
        // native side wants the same textures each time.
        private void Ensure(int inW, int inH)
        {
            int outW = Screen.width, outH = Screen.height;
            if (_color != null && inW == _inW && inH == _inH && outW == _outW && outH == _outH) return;
            Release();
            _inW = inW; _inH = inH; _outW = outW; _outH = outH;
            _color = Make(inW, inH, RenderTextureFormat.ARGBHalf, false, "Wire upscale color");
            _depth = Make(inW, inH, RenderTextureFormat.RFloat, false, "Wire upscale depth");
            _motion = Make(inW, inH, RenderTextureFormat.RGHalf, false, "Wire upscale motion");
            _output = Make(outW, outH, RenderTextureFormat.ARGBHalf, true, "Wire upscale output");
            // asked once per texture: it waits for the render thread
            _colorPtr = _color.GetNativeTexturePtr(); _depthPtr = _depth.GetNativeTexturePtr();
            _motionPtr = _motion.GetNativeTexturePtr(); _outputPtr = _output.GetNativeTexturePtr();
            _reset = true;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Upscaler u = Plugin.Current;
            if (u != _last) { _last = u; _reset = true; }
            Texture depth = Shader.GetGlobalTexture("_CameraDepthTexture");
            Texture motion = Shader.GetGlobalTexture("_CameraMotionVectorsTexture");
            if (!Active || source == null || depth == null || motion == null)
            {
                Restore();
                Graphics.Blit(source, destination);
                return;
            }
            Ensure(source.width, source.height);
            Graphics.Blit(source, _color);
            Graphics.Blit(depth, _depth);
            Graphics.Blit(motion, _motion);

            bool fsr = u == Upscaler.FSR3 || u == Upscaler.FSR4;
            var p = new Native.Params
            {
                backend = (int)u,
                model = Plugin.DlssModel.Value,
                inW = _inW, inH = _inH, outW = _outW, outH = _outH,
                color = _colorPtr, depth = _depthPtr, motion = _motionPtr, output = _outputPtr,
                // DLSS takes the shift the other way round from the projection; FSR and XeSS as applied
                jitterX = u == Upscaler.DLSS ? -_jitter.x : _jitter.x,
                jitterY = u == Upscaler.DLSS ? -_jitter.y : _jitter.y,
                mvScaleX = -_inW, mvScaleY = -_inH,
                sharpness = fsr ? Mathf.Clamp01(Plugin.Sharpness.Value) : 0f,
                frameTimeMs = Time.unscaledDeltaTime * 1000f,
                cameraNear = _cam.nearClipPlane, cameraFar = _cam.farClipPlane,
                fovY = _cam.fieldOfView * Mathf.Deg2Rad,
                reset = _reset ? 1 : 0,
                hdr = _cam.allowHDR ? 1 : 0,
                depthInverted = SystemInfo.usesReversedZBuffer ? 1 : 0
            };
            Native.Evaluate(ref p);
            _reset = false;
            if (Time.frameCount % 120 == 0) Native.LogMessage();

            // the game's own anti-aliasing would blur what the upscaler resolved
            if (!_aaOff && _post != null && _post.profile != null)
            {
                _oldAa = _post.profile.antialiasing.enabled;
                _post.profile.antialiasing.enabled = false;
                _aaOff = true;
            }
            if (_cam.targetTexture != null)
            {
                // scaled rendering: the game stretches its target onto the screen; ours goes there instead
                Show();
                Graphics.Blit(source, destination);
            }
            else Graphics.Blit(_output, destination);
        }

        private void Show()
        {
            foreach (FrameBufferScaler s in FindObjectsByType<FrameBufferScaler>(FindObjectsSortMode.None))
            {
                RawImage raw = s != null ? s.GetComponent<RawImage>() : null;
                if (raw == null || (raw.texture != _cam.targetTexture && raw.texture != _output)) continue;
                raw.texture = _output;
                if (!_shown.Contains(raw)) _shown.Add(raw);
            }
        }

        // The game's own picture back.
        private void Restore()
        {
            foreach (RawImage raw in _shown)
                if (raw != null && raw.texture == _output) raw.texture = _cam.targetTexture;
            _shown.Clear();
            if (_aaOff && _post != null && _post.profile != null) _post.profile.antialiasing.enabled = _oldAa;
            _aaOff = false;
        }

        private void Release()
        {
            Restore();
            foreach (RenderTexture rt in new[] { _color, _depth, _motion, _output })
                if (rt != null) { rt.Release(); Destroy(rt); }
            _color = _depth = _motion = _output = null;
            _inW = _inH = _outW = _outH = 0;
            _reset = true;
        }
    }
}
