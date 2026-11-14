using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Every point light in one pass (WireTiledLights). Unity lights a deferred scene one light at a
    // time: each light a sphere drawn over the screen that reads the G-buffer again and adds itself
    // on top, so where lights overlap (a hearth, sconces and torches in one room) every pixel is lit
    // over and over. Here, after Unity's lighting, a compute shader sorts the lights into 16 x 16
    // pixel tiles (each tile's depth range against each light's box on screen and depth range), and
    // one full-screen pass lights each pixel with the lights of its tile, reading the G-buffer once.
    // Unity still issues its own draw for each of those lights, collapsed to nothing in its vertex
    // shader (WireIsTiled), so nothing is lit twice; forward-drawn things (water, glass) are still
    // lit by Unity as before, and lights the mod doesn't know (an effect's flash, a cookie or a
    // light with Unity's own shadows) stay Unity's entirely.
    // The falloff is Unity's own: its texture is only bound in Unity's light pass, so for the first
    // frames lights are drawn the normal way and that pass writes the texture's values into a
    // buffer (_WireAttenCapture), read back once; then the tiled pass takes over.
    // Per frame: the lights' positions, colours and boxes (they flicker), one dispatch, one draw.
    internal sealed partial class ShadowCache
    {
        internal static Shader TiledShader;
        internal static ComputeShader TiledCompute;
        private const int TileSize = 16, PerTile = 64, MaxTiled = 256, LutSize = 256;

        private ComputeBuffer _tPosBuf, _tColorBuf, _tRectBuf, _tDepthBuf, _tTileBuf, _tCapture, _tDummy;
        private Material _tMat;
        private CommandBuffer _tCb, _tCaptureOn, _tCaptureOff;
        private Camera _tCam;
        private Texture2D _tLut;
        private bool _tHaveLut, _tReading;
        private float _tNextRead;
        private int _tTilesX, _tTilesY, _tKernel = -1;
        private readonly Vector4[] _tPos = new Vector4[MaxTiled], _tColor = new Vector4[MaxTiled], _tRect = new Vector4[MaxTiled], _tDepth = new Vector4[MaxTiled];
        private readonly System.Collections.Generic.Dictionary<Tracked, int> _tSlotOf = new System.Collections.Generic.Dictionary<Tracked, int>();

        private static readonly int TiledPosId = Shader.PropertyToID("_WireTiledPos"), TiledCountId = Shader.PropertyToID("_WireTiledCount"),
            TiledColorId = Shader.PropertyToID("_WireTiledColor"), TileLightsId = Shader.PropertyToID("_WireTileLights"),
            TiledRectId = Shader.PropertyToID("_WireTiledRect"), TiledDepthId = Shader.PropertyToID("_WireTiledDepth"),
            WireScreenId = Shader.PropertyToID("_WireScreen"), WireZId = Shader.PropertyToID("_WireZ"), WireDepthId = Shader.PropertyToID("_WireDepth"),
            AttenId = Shader.PropertyToID("_WireAtten"), CapturingId = Shader.PropertyToID("_WireCapturing"),
            CaptureId = Shader.PropertyToID("_WireAttenCapture"), CameraDepthId = Shader.PropertyToID("_CameraDepthTexture"),
            SrcId = Shader.PropertyToID("_WireSrc"), DstId = Shader.PropertyToID("_WireDst");

        private bool TiledWanted =>
            Plugin.TiledLights != null && Plugin.TiledLights.Value && Plugin.UseShader.Value &&
            TiledShader != null && TiledShader.isSupported && TiledCompute != null && SystemInfo.supportsComputeShaders;

        // From RunFrame, for the main camera, after the lights have moved for the frame.
        private void TiledFrame(Camera cam)
        {
            if (!TiledWanted) { TiledOff(); return; }
            if (_tCam != cam) { TiledOff(); _tCam = cam; }
            if (_tPosBuf == null) TiledMake();
            if (!_tHaveLut) { TiledCapture(cam); return; }
            int n = TiledGather(cam);
            int w = cam.pixelWidth, h = cam.pixelHeight;
            int tx = (w + TileSize - 1) / TileSize, ty = (h + TileSize - 1) / TileSize;
            if (_tTileBuf == null || tx != _tTilesX || ty != _tTilesY)
            {
                _tTileBuf?.Release();
                _tTileBuf = new ComputeBuffer(tx * ty * PerTile, 4);
                _tTilesX = tx; _tTilesY = ty;
            }
            _tPosBuf.SetData(_tPos, 0, 0, Mathf.Max(1, n));
            _tColorBuf.SetData(_tColor, 0, 0, Mathf.Max(1, n));
            _tRectBuf.SetData(_tRect, 0, 0, Mathf.Max(1, n));
            _tDepthBuf.SetData(_tDepth, 0, 0, Mathf.Max(1, n));
            Shader.SetGlobalBuffer(TiledPosId, _tPosBuf);
            Shader.SetGlobalFloat(TiledCountId, n);

            // raw depth to eye depth, as Unity's _ZBufferParams (z, w)
            float near = cam.nearClipPlane, far = cam.farClipPlane;
            bool reversed = SystemInfo.usesReversedZBuffer;
            float zx = reversed ? -1f + far / near : 1f - far / near, zy = reversed ? 1f : far / near;
            var screen = new Vector4(w, h, tx, n);
            bool hdr = cam.allowHDR;
            _tMat.SetFloat(SrcId, hdr ? (float)BlendMode.One : (float)BlendMode.DstColor);
            _tMat.SetFloat(DstId, hdr ? (float)BlendMode.One : (float)BlendMode.Zero);
            if (hdr) _tMat.DisableKeyword("WIRE_LDR"); else _tMat.EnableKeyword("WIRE_LDR");

            _tCb.Clear();
            if (n > 0)
            {
                _tCb.SetComputeTextureParam(TiledCompute, _tKernel, WireDepthId, new RenderTargetIdentifier(CameraDepthId));
                _tCb.SetComputeVectorParam(TiledCompute, WireScreenId, screen);
                _tCb.SetComputeVectorParam(TiledCompute, WireZId, new Vector4(zx / far, zy / far, reversed ? 1f : 0f, 0f));
                _tCb.SetComputeBufferParam(TiledCompute, _tKernel, TiledRectId, _tRectBuf);
                _tCb.SetComputeBufferParam(TiledCompute, _tKernel, TiledDepthId, _tDepthBuf);
                _tCb.SetComputeBufferParam(TiledCompute, _tKernel, TileLightsId, _tTileBuf);
                _tCb.DispatchCompute(TiledCompute, _tKernel, tx, ty, 1);
                _tCb.SetGlobalBuffer(TileLightsId, _tTileBuf);
                _tCb.SetGlobalBuffer(TiledColorId, _tColorBuf);
                _tCb.SetGlobalVector(WireScreenId, screen);
                _tCb.SetGlobalTexture(AttenId, _tLut);
                // the light buffer Unity just lit into is still the target here
                _tCb.DrawProcedural(Matrix4x4.identity, _tMat, 0, MeshTopology.Triangles, 3);
            }
        }

        // The lights for this frame: every one the mod knows that Unity would light this way (a point
        // light, on, no cookie, no Unity shadows), with its box on screen and its depth range.
        private int TiledGather(Camera cam)
        {
            _tSlotOf.Clear();
            for (int s = 0; s < _slots.Length; s++)
            {
                Tracked st = _slots[s];
                if (st != null && st.Ready && st.Light != null) _tSlotOf[st] = s;
            }
            Matrix4x4 view = cam.worldToCameraMatrix, proj = cam.projectionMatrix;
            float w = cam.pixelWidth, h = cam.pixelHeight, near = cam.nearClipPlane;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            int n = 0;
            foreach (Tracked t in Lights)
            {
                if (n >= MaxTiled) break;
                Light l = t != null ? t.Light : null;
                if (l == null || !l.isActiveAndEnabled || l.type != LightType.Point || l.cookie != null ||
                    l.shadows != LightShadows.None || l.range <= 0f || l.intensity <= 0f) continue;
                Vector3 p = l.transform.position;
                float r = l.range;
                Vector3 c = view.MultiplyPoint3x4(p);
                float depth = -c.z;
                if (depth + r < near) continue; // behind the camera
                Vector4 rect;
                if (depth - r <= near) rect = new Vector4(0f, 0f, w, h); // the camera inside its reach
                else
                {
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 v = c + new Vector3((k & 1) != 0 ? r : -r, (k & 2) != 0 ? r : -r, (k & 4) != 0 ? r : -r);
                        Vector4 clip = proj * new Vector4(v.x, v.y, v.z, 1f);
                        float sx = (clip.x / clip.w * .5f + .5f) * w, sy = (1f - (clip.y / clip.w * .5f + .5f)) * h; // pixels from the top
                        x0 = Mathf.Min(x0, sx); x1 = Mathf.Max(x1, sx); y0 = Mathf.Min(y0, sy); y1 = Mathf.Max(y1, sy);
                    }
                    rect = new Vector4(x0 - 1f, y0 - 1f, x1 + 1f, y1 + 1f);
                    if (rect.z < 0f || rect.x > w || rect.w < 0f || rect.y > h) continue; // off screen
                }
                // Unity's _LightColor: colour times intensity, made linear as Unity does it
                Color col = l.color;
                if (GraphicsSettings.lightsUseColorTemperature && l.useColorTemperature) col *= Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature);
                Color lc = !linear ? col * l.intensity : GraphicsSettings.lightsUseLinearIntensity ? col.linear * l.intensity : (col * l.intensity).linear;
                int slot = _tSlotOf.TryGetValue(t, out int sl) ? sl : -1;
                _tPos[n] = new Vector4(p.x, p.y, p.z, 1f / (r * r));
                _tColor[n] = new Vector4(lc.r, lc.g, lc.b, slot);
                _tRect[n] = rect;
                _tDepth[n] = new Vector4(Mathf.Max(0f, depth - r), depth + r, 0f, 0f);
                n++;
            }
            return n;
        }

        private void TiledMake()
        {
            _tPosBuf = new ComputeBuffer(MaxTiled, 16);
            _tColorBuf = new ComputeBuffer(MaxTiled, 16);
            _tRectBuf = new ComputeBuffer(MaxTiled, 16);
            _tDepthBuf = new ComputeBuffer(MaxTiled, 16);
            _tMat = new Material(TiledShader) { hideFlags = HideFlags.HideAndDontSave };
            _tKernel = TiledCompute.FindKernel("Cull");
            _tCb = new CommandBuffer { name = "Wire's Shadows tiled lights" };
            _tCam.AddCommandBuffer(CameraEvent.AfterLighting, _tCb);
        }

        // Unity's falloff, copied from its own light pass (see the top).
        private void TiledCapture(Camera cam)
        {
            Shader.SetGlobalFloat(TiledCountId, 0f);
            if (_tCapture == null)
            {
                _tCapture = new ComputeBuffer(LutSize, 4);
                var none = new float[LutSize];
                for (int i = 0; i < LutSize; i++) none[i] = -1f;
                _tCapture.SetData(none);
                _tCaptureOn = new CommandBuffer { name = "Wire's Shadows falloff copy" };
                _tCaptureOn.SetRandomWriteTarget(1, _tCapture);
                _tCaptureOn.SetGlobalFloat(CapturingId, 1f);
                _tCaptureOff = new CommandBuffer { name = "Wire's Shadows falloff copy done" };
                _tCaptureOff.ClearRandomWriteTargets();
                _tCaptureOff.SetGlobalFloat(CapturingId, 0f);
                cam.AddCommandBuffer(CameraEvent.BeforeLighting, _tCaptureOn);
                cam.AddCommandBuffer(CameraEvent.AfterLighting, _tCaptureOff);
            }
            if (_tReading || Time.unscaledTime < _tNextRead) return;
            _tNextRead = Time.unscaledTime + .5f;
            _tReading = true;
            AsyncGPUReadback.Request(_tCapture, req =>
            {
                _tReading = false;
                if (req.hasError || _tCapture == null) return;
                var v = req.GetData<float>();
                for (int i = 0; i < LutSize; i++) if (v[i] < 0f) return; // not every column lit yet
                _tLut = new Texture2D(LutSize, 1, TextureFormat.RFloat, false, true)
                {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
                };
                for (int i = 0; i < LutSize; i++) _tLut.SetPixel(i, 0, new Color(v[i], 0f, 0f, 0f));
                _tLut.Apply(false, true);
                _tHaveLut = true;
                TiledCaptureDone();
                Plugin.Note("[tiled] Unity's point light falloff copied (" + v[0].ToString("F3") + " at the light, " + v[LutSize / 2].ToString("F3") + " half way): every known point light is now lit in one pass.");
            });
        }

        private void TiledCaptureDone()
        {
            if (_tCam != null)
            {
                if (_tCaptureOn != null) _tCam.RemoveCommandBuffer(CameraEvent.BeforeLighting, _tCaptureOn);
                if (_tCaptureOff != null) _tCam.RemoveCommandBuffer(CameraEvent.AfterLighting, _tCaptureOff);
            }
            _tCaptureOn?.Release(); _tCaptureOn = null;
            _tCaptureOff?.Release(); _tCaptureOff = null;
            _tCapture?.Release(); _tCapture = null;
            Shader.SetGlobalFloat(CapturingId, 0f);
        }

        // Off (or a new camera): Unity draws every light itself again.
        private void TiledOff()
        {
            if (_tPosBuf == null && _tCapture == null) { EnsureTiledDummy(); return; }
            TiledCaptureDone();
            if (_tCam != null && _tCb != null) _tCam.RemoveCommandBuffer(CameraEvent.AfterLighting, _tCb);
            _tCb?.Release(); _tCb = null;
            _tPosBuf?.Release(); _tPosBuf = null;
            _tColorBuf?.Release(); _tColorBuf = null;
            _tRectBuf?.Release(); _tRectBuf = null;
            _tDepthBuf?.Release(); _tDepthBuf = null;
            _tTileBuf?.Release(); _tTileBuf = null;
            _tTilesX = _tTilesY = 0;
            if (_tMat != null) Destroy(_tMat);
            _tMat = null;
            _tCam = null;
            EnsureTiledDummy();
        }

        // The lighting shader always reads _WireTiledPos: a one-entry buffer while nothing is tiled.
        private void EnsureTiledDummy()
        {
            if (_tDummy == null) _tDummy = new ComputeBuffer(1, 16);
            Shader.SetGlobalBuffer(TiledPosId, _tDummy);
            Shader.SetGlobalFloat(TiledCountId, 0f);
        }

        private void TiledRelease()
        {
            TiledOff();
            _tDummy?.Release(); _tDummy = null;
            if (_tLut != null) Destroy(_tLut);
            _tLut = null;
            _tHaveLut = false;
        }
    }
}
