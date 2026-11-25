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
        private Camera _tCam, _capCam;
        private Texture2D _tLut;
        private bool _tHaveLut, _tReading, _tAddedDepth;
        private readonly int[] _tWhy = new int[7]; // the one-time check: lights left out, by reason
        private float _tNextRead;
        private int _tTilesX, _tTilesY, _tKernel = -1, _tCount;
        private readonly Vector4[] _tPos = new Vector4[MaxTiled], _tColor = new Vector4[MaxTiled], _tRect = new Vector4[MaxTiled], _tDepth = new Vector4[MaxTiled];


        private static readonly int TiledPosId = Shader.PropertyToID("_WireTiledPos"), TiledCountId = Shader.PropertyToID("_WireTiledCount"),
            TiledColorId = Shader.PropertyToID("_WireTiledColor"), TileLightsId = Shader.PropertyToID("_WireTileLights"),
            TiledRectId = Shader.PropertyToID("_WireTiledRect"), TiledDepthId = Shader.PropertyToID("_WireTiledDepth"),
            WireScreenId = Shader.PropertyToID("_WireScreen"), WireZId = Shader.PropertyToID("_WireZ"), WireDepthId = Shader.PropertyToID("_WireDepth"),
            AttenId = Shader.PropertyToID("_WireAtten"), CapturingId = Shader.PropertyToID("_WireCapturing"),
            CaptureId = Shader.PropertyToID("_WireAttenCapture"), CameraDepthId = Shader.PropertyToID("_CameraDepthTexture"),
            SrcId = Shader.PropertyToID("_WireSrc"), DstId = Shader.PropertyToID("_WireDst"),
            CamToWorldId = Shader.PropertyToID("_WireCamToWorld"), InvProjId = Shader.PropertyToID("_WireInvProj"), CamPosId = Shader.PropertyToID("_WireCamPos");

        private bool TiledWanted =>
            Plugin.TiledLights != null && Plugin.TiledLights.Value && Plugin.UseShader.Value &&
            TiledShader != null && TiledShader.isSupported && TiledCompute != null && SystemInfo.supportsComputeShaders;

        // From RunFrame, for the main camera, after the lights have moved for the frame.
        private void TiledFrame(Camera cam)
        {
            if (!TiledWanted) { TiledOff(LitWanted); return; } // the falloff copy carries on for the weather particles
            if (_tCam != cam) { TiledOff(); _tCam = cam; }
            // The tiles' depth: the camera's depth texture, which the game's camera doesn't ask
            // Unity for (without it the tiles all read as sky and nothing was lit). One depth copy a
            // frame, Unity's own.
            if ((cam.depthTextureMode & DepthTextureMode.Depth) == 0)
            {
                cam.depthTextureMode |= DepthTextureMode.Depth;
                _tAddedDepth = true;
            }
            if (_tPosBuf == null) TiledMake();
            if (!_tHaveLut) { TiledCapture(cam); return; }
            int n = _tCount = TiledGather(cam);
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
                _tCb.SetComputeVectorParam(TiledCompute, WireScreenId, screen);
                _tCb.SetComputeBufferParam(TiledCompute, _tKernel, TiledRectId, _tRectBuf);
                _tCb.SetComputeBufferParam(TiledCompute, _tKernel, TileLightsId, _tTileBuf);
                _tCb.DispatchCompute(TiledCompute, _tKernel, tx, ty, 1);
                _tCb.SetGlobalBuffer(TileLightsId, _tTileBuf);
                _tCb.SetGlobalBuffer(TiledColorId, _tColorBuf);
                _tCb.SetGlobalVector(WireScreenId, screen);
                // the camera itself: Unity's built-in camera matrices aren't this camera's here (the lights slid and
                // turned with the view)
                _tCb.SetGlobalMatrix(CamToWorldId, cam.cameraToWorldMatrix);
                _tCb.SetGlobalMatrix(InvProjId, cam.projectionMatrix.inverse);
                _tCb.SetGlobalVector(WireZId, new Vector4(zx / far, zy / far, reversed ? 1f : 0f, 0f));
                _tCb.SetGlobalVector(CamPosId, cam.transform.position);
                _tCb.SetGlobalTexture(AttenId, _tLut);
                // the light buffer Unity just lit into is still the target here
                _tCb.DrawProcedural(Matrix4x4.identity, _tMat, 0, MeshTopology.Triangles, 3);
            }
            TiledCheckOnce(n, w, h);
        }

        // The lights for this frame: every one the mod knows that Unity would light this way (a point
        // light, on, no cookie, no Unity shadows), with its box on screen and its depth range.
        private int TiledGather(Camera cam)
        {
            Matrix4x4 view = cam.worldToCameraMatrix, proj = cam.projectionMatrix;
            float w = cam.pixelWidth, h = cam.pixelHeight, near = cam.nearClipPlane;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            bool temperature = GraphicsSettings.lightsUseColorTemperature, linearIntensity = GraphicsSettings.lightsUseLinearIntensity;
            int n = 0;
            System.Array.Clear(_tWhy, 0, _tWhy.Length);
            foreach (Tracked t in Lights)
            {
                if (n >= MaxTiled) break;
                Light l = t != null ? t.Light : null;
                if (l == null) { _tWhy[0]++; continue; }
                if (!l.isActiveAndEnabled) { _tWhy[1]++; continue; }
                // where it is first: most lights that are on but not lit this frame are behind or off screen
                Vector3 p = l.transform.position;
                float r = l.range;
                if (r <= 0f) { _tWhy[4]++; continue; }
                Vector3 c = view.MultiplyPoint3x4(p);
                float depth = -c.z;
                if (depth + r < near) { _tWhy[5]++; continue; } // behind the camera
                Vector4 rect;
                if (depth - r <= near) rect = new Vector4(0f, 0f, w, h); // the camera inside its reach
                else
                {
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    // The eight projected light-box corners share a centre and
                    // three scaled axes. Only clip X/Y/W reach the screen box.
                    Vector4 pc = proj * new Vector4(c.x, c.y, c.z, 1f);
                    Vector4 ax = proj.GetColumn(0) * r, ay = proj.GetColumn(1) * r, az = proj.GetColumn(2) * r;
                    for (int k = 0; k < 8; k++)
                    {
                        float sx0 = (k & 1) != 0 ? 1f : -1f;
                        float sy0 = (k & 2) != 0 ? 1f : -1f;
                        float sz0 = (k & 4) != 0 ? 1f : -1f;
                        float cw = pc.w + sx0 * ax.w + sy0 * ay.w + sz0 * az.w;
                        float sx = ((pc.x + sx0 * ax.x + sy0 * ay.x + sz0 * az.x) / cw * .5f + .5f) * w;
                        float sy = ((pc.y + sx0 * ax.y + sy0 * ay.y + sz0 * az.y) / cw * .5f + .5f) * h;
                        x0 = Mathf.Min(x0, sx); x1 = Mathf.Max(x1, sx); y0 = Mathf.Min(y0, sy); y1 = Mathf.Max(y1, sy);
                    }
                    rect = new Vector4(x0 - 1f, y0 - 1f, x1 + 1f, y1 + 1f);
                    if (rect.z < 0f || rect.x > w || rect.w < 0f || rect.y > h) { _tWhy[6]++; continue; } // off screen
                }
                if (l.type != LightType.Point || l.cookie != null) { _tWhy[2]++; continue; }
                if (l.shadows != LightShadows.None) { _tWhy[3]++; continue; }
                if (l.intensity <= 0f) { _tWhy[4]++; continue; }
                // The tracked light already knows its slot. Check ownership to
                // preserve the exact readiness/membership rules of the old map.
                int sl = t.Slot;
                int slot = t.Ready && sl >= 0 && sl < _slots.Length &&
                    ReferenceEquals(_slots[sl], t) ? sl : -1;
                _tPos[n] = new Vector4(p.x, p.y, p.z, 1f / (r * r));
                _tColor[n] = UnityLightColor(l, linear, temperature, linearIntensity);
                _tColor[n].w = slot;
                _tRect[n] = rect;
                _tDepth[n] = new Vector4(Mathf.Max(0f, depth - r), depth + r, 0f, 0f);
                n++;
            }
            return n;
        }

        // Unity's _LightColor: colour times intensity, made linear as Unity does it
        private static Vector4 UnityLightColor(Light l, bool linear, bool temperature, bool linearIntensity)
        {
            Color col = l.color;
            if (temperature && l.useColorTemperature) col *= Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature);
            float intensity = l.intensity;
            Color lc = !linear ? col * intensity : linearIntensity ? col.linear * intensity : (col * intensity).linear;
            return new Vector4(lc.r, lc.g, lc.b, -1f);
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

        // Once, a few seconds in: what the tiles hold (for the log: whether the sorting finds the lights).
        private float _tCheckAt = -1f;
        private bool _tChecked;
        private void TiledCheckOnce(int n, int w, int h)
        {
            if (_tChecked || _tTileBuf == null) return;
            if (_tCheckAt < 0f) { if (Player.m_localPlayer != null && Lights.Count > 0) _tCheckAt = Time.unscaledTime + 3f; return; } // armed once you're in the world with lights known
            if (Time.unscaledTime < _tCheckAt) return;
            _tChecked = true;
            int tiles = _tTilesX * _tTilesY;
            Rect first = n > 0 ? new Rect(_tRect[0].x, _tRect[0].y, _tRect[0].z - _tRect[0].x, _tRect[0].w - _tRect[0].y) : default;
            Vector4 z0 = n > 0 ? _tDepth[0] : default;
            string why = "known " + Lights.Count + ", left out: no light " + _tWhy[0] + ", off " + _tWhy[1] + ", not a plain point light " + _tWhy[2] +
                ", Unity shadows on " + _tWhy[3] + ", no range or intensity " + _tWhy[4] + ", behind " + _tWhy[5] + ", off screen " + _tWhy[6] +
                "; depth texture " + (_tAddedDepth ? "turned on by the mod" : "already on");
            Plugin.Log.LogInfo("[tiled] lights: " + why);
            AsyncGPUReadback.Request(_tTileBuf, req =>
            {
                if (req.hasError) { Plugin.Log.LogInfo("[tiled] check: tile buffer couldn't be read"); return; }
                var v = req.GetData<uint>();
                int lit = 0, most = 0;
                for (int t = 0; t < tiles; t++) { int c = (int)v[t * PerTile]; if (c > 0) lit++; if (c > most) most = c; }
                Plugin.Log.LogInfo("[tiled] check: " + n + " lights gathered, " + w + "x" + h + ", " + lit + " of " + tiles + " tiles have lights (most " + most + "); first light's box " + first + ", depth " + z0.x.ToString("F1") + "-" + z0.y.ToString("F1") + " m, hdr " + (_tCam != null && _tCam.allowHDR) + ", path " + (_tCam != null ? _tCam.actualRenderingPath.ToString() : "?"));
            });
        }

        // Unity's falloff, copied from its own light pass (see the top).
        private const string CaptureKeyword = "WIRE_CAPTURE";
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
                _tCaptureOn.EnableShaderKeyword(CaptureKeyword); // the light pass variant that can write it
                _tCaptureOff = new CommandBuffer { name = "Wire's Shadows falloff copy done" };
                _tCaptureOff.ClearRandomWriteTargets();
                _tCaptureOff.SetGlobalFloat(CapturingId, 0f);
                _tCaptureOff.DisableShaderKeyword(CaptureKeyword);
                cam.AddCommandBuffer(CameraEvent.BeforeLighting, _tCaptureOn);
                cam.AddCommandBuffer(CameraEvent.AfterLighting, _tCaptureOff);
                _capCam = cam;
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
                Plugin.Log.LogInfo("[tiled] Unity's point light falloff copied (" + v[0].ToString("F3") + " at the light, " + v[LutSize / 2].ToString("F3") + " half way): every known point light is now lit in one pass.");
            });
        }

        private void TiledCaptureDone()
        {
            if (_capCam != null)
            {
                if (_tCaptureOn != null) _capCam.RemoveCommandBuffer(CameraEvent.BeforeLighting, _tCaptureOn);
                if (_tCaptureOff != null) _capCam.RemoveCommandBuffer(CameraEvent.AfterLighting, _tCaptureOff);
            }
            _capCam = null;
            _tCaptureOn?.Release(); _tCaptureOn = null;
            _tCaptureOff?.Release(); _tCaptureOff = null;
            _tCapture?.Release(); _tCapture = null;
            Shader.SetGlobalFloat(CapturingId, 0f);
        }

        // Off (or a new camera): Unity draws every light itself again.
        private void TiledOff(bool keepCapture = false)
        {
            if (_tPosBuf == null && (keepCapture || _tCapture == null)) { if (_tDummy == null) EnsureTiledDummy(); return; } // already off: nothing to do each frame
            if (!keepCapture) TiledCaptureDone();
            if (_tCam != null && _tCb != null) _tCam.RemoveCommandBuffer(CameraEvent.AfterLighting, _tCb);
            _tCb?.Release(); _tCb = null;
            _tPosBuf?.Release(); _tPosBuf = null;
            _tColorBuf?.Release(); _tColorBuf = null;
            _tRectBuf?.Release(); _tRectBuf = null;
            _tDepthBuf?.Release(); _tDepthBuf = null;
            _tTileBuf?.Release(); _tTileBuf = null;
            _tTilesX = _tTilesY = 0;
            _tChecked = false; _tCheckAt = -1f; // turned on again: checked again
            if (_tMat != null) Destroy(_tMat);
            _tMat = null;
            if (_tCam != null && _tAddedDepth) _tCam.depthTextureMode &= ~DepthTextureMode.Depth;
            _tAddedDepth = false;
            _tCam = null;
            _tCount = 0;
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
