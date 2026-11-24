using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
{
    // Contact shadows for the sun: short shadows its shadow map is too coarse for (stones on the
    // ground, plank edges, the foot of a post), traced along the depth buffer toward the sun by
    // Bend Studio's screen-space shadows (WireContactShadows.compute, Apache 2.0). Laid over the
    // sun's own screen-space shadow mask right after Unity makes it (LightEvent.AfterScreenspaceMask),
    // so only direct sunlight is darkened. GPU work only; on the CPU, the handful of dispatches is
    // rebuilt each frame for where the sun is on screen (BuildDispatchList, ported below).
    // Idea from ValheimVisuals (Ivvty), which ports the same code.
    internal static class ContactShadows
    {
        internal static ComputeShader Compute;
        internal static Shader ApplyShader;

        private const int WaveSize = 64;
        private static Material s_apply;
        private static CommandBuffer s_cb;
        private static Light s_sun;
        private static RenderTexture s_rt;
        private static int s_main = -1, s_clear = -1;
        private static bool s_on;

        private static readonly int DepthId = Shader.PropertyToID("_WireContactDepth"), OutId = Shader.PropertyToID("_WireContactOut"),
            LightId = Shader.PropertyToID("_WireContactLight"), WaveId = Shader.PropertyToID("_WireContactWave"),
            DepthValuesId = Shader.PropertyToID("_WireContactDepthValues"), NormalId = Shader.PropertyToID("_WireContactNormal"),
            SunId = Shader.PropertyToID("_WireContactSun"), EyeId = Shader.PropertyToID("_WireContactEye");
        private static readonly int SunStrengthId = Shader.PropertyToID("_WireSunShadowStrength");
        private static readonly int[] Wave = new int[4];

        private static bool Wanted =>
            Plugin.ContactShadowsOn != null && Plugin.ContactShadowsOn.Value && Plugin.UseShader.Value &&
            Compute != null && ApplyShader != null && ApplyShader.isSupported && SystemInfo.supportsComputeShaders;

        // Setting changed, or the mod started: follow each frame only while it's on.
        internal static void Apply()
        {
            if (Wanted == s_on) return;
            s_on = Wanted;
            if (s_on)
            {
                if (s_apply == null) s_apply = new Material(ApplyShader) { hideFlags = HideFlags.HideAndDontSave };
                // a bundle whose shader didn't compile has no kernels: stay off rather than throw
                if (!Compute.HasKernel("CSMain") || !Compute.HasKernel("Clear")) { s_on = false; Plugin.Log.LogWarning("Contact shadows: shader not available, staying off."); return; }
                s_main = Compute.FindKernel("CSMain");
                s_clear = Compute.FindKernel("Clear");
                if (!Compute.IsSupported(s_main) || !Compute.IsSupported(s_clear)) { s_on = false; Plugin.Log.LogWarning("Contact shadows: shader not supported here, staying off."); return; }
                Camera.onPreRender += Frame;
            }
            else
            {
                Camera.onPreRender -= Frame;
                Detach();
                if (s_rt != null) { s_rt.Release(); Object.Destroy(s_rt); s_rt = null; }
            }
        }

        private static void Detach()
        {
            if (s_sun != null && s_cb != null) s_sun.RemoveCommandBuffer(LightEvent.AfterScreenspaceMask, s_cb);
            s_sun = null;
        }

        private static void Frame(Camera cam)
        {
            Camera main = Utils.GetMainCamera();
            if (cam != main) return;
            Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
            // not while the benchmark measures vanilla (Plugin.CachedShadows off)
            if (!Plugin.CachedShadows || sun == null || !sun.isActiveAndEnabled || sun.shadows == LightShadows.None || cam.actualRenderingPath != RenderingPath.DeferredShading)
            {
                Detach();
                return;
            }
            if (s_cb == null) s_cb = new CommandBuffer { name = "Wire's Shadows: contact shadows" };
            if (sun != s_sun)
            {
                Detach();
                s_sun = sun;
                sun.AddCommandBuffer(LightEvent.AfterScreenspaceMask, s_cb);
            }

            int w = cam.pixelWidth, h = cam.pixelHeight;
            if (s_rt == null || s_rt.width != w || s_rt.height != h)
            {
                if (s_rt != null) { s_rt.Release(); Object.Destroy(s_rt); }
                s_rt = new RenderTexture(w, h, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
                { name = "Wire contact shadows", enableRandomWrite = true, filterMode = FilterMode.Point };
                s_rt.Create();
            }

            // Where the sun is on screen: the direction toward it through the view and the projection
            // the depth buffer was drawn with (Bend's inLightProjection, a direction so w = 0), as
            // Unity draws into a texture (flipped on Direct3D). Checked in game: shadows fall the right way.
            Vector3 toSun = -sun.transform.forward;
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            Vector4 lp = vp * new Vector4(toSun.x, toSun.y, toSun.z, 0f);
            bool reversed = SystemInfo.usesReversedZBuffer;

            s_cb.Clear();
            Wave[0] = 0; Wave[1] = 0; Wave[2] = w; Wave[3] = h;
            s_cb.SetComputeIntParams(Compute, WaveId, Wave);
            s_cb.SetComputeTextureParam(Compute, s_clear, OutId, s_rt);
            s_cb.DispatchCompute(Compute, s_clear, (w + 7) / 8, (h + 7) / 8, 1);

            s_cb.SetComputeTextureParam(Compute, s_main, DepthId, BuiltinRenderTextureType.ResolvedDepth);
            s_cb.SetComputeTextureParam(Compute, s_main, OutId, s_rt);
            s_cb.SetComputeVectorParam(Compute, DepthValuesId, new Vector4(reversed ? 0f : 1f, reversed ? 1f : 0f, 0f, 0f));
            // for skipping what the sun can't shadow: its direction, the game's sun shadow distance, raw depth to eye depth
            s_cb.SetComputeTextureParam(Compute, s_main, NormalId, BuiltinRenderTextureType.GBuffer2);
            s_cb.SetComputeVectorParam(Compute, SunId, new Vector4(toSun.x, toSun.y, toSun.z, QualitySettings.shadowDistance));
            float near = cam.nearClipPlane, far = cam.farClipPlane;
            float zx = reversed ? -1f + far / near : 1f - far / near, zy = reversed ? 1f : far / near;
            s_cb.SetComputeVectorParam(Compute, EyeId, new Vector4(zx / far, zy / far, 0f, 0f));
            BuildDispatches(lp, w, h);
            s_cb.SetComputeVectorParam(Compute, LightId, s_light);
            for (int i = 0; i < s_count; i++)
            {
                Wave[0] = s_offset[i * 2]; Wave[1] = s_offset[i * 2 + 1];
                s_cb.SetComputeIntParams(Compute, WaveId, Wave);
                s_cb.DispatchCompute(Compute, s_main, s_waves[i * 3], s_waves[i * 3 + 1], s_waves[i * 3 + 2]);
            }
            s_apply.SetFloat(SunStrengthId, sun.shadowStrength); // as dark as the sun's own shadows, no darker
            s_cb.Blit(s_rt, BuiltinRenderTextureType.CurrentActive, s_apply);
        }

        // ---- Bend's BuildDispatchList (bend_sss_cpu.h, Apache 2.0, Sony Interactive Entertainment 2023),
        // ported to C# as is: the dispatches needed to cover the screen, the light's pixel position. ----
        private static Vector4 s_light;
        private static readonly int[] s_waves = new int[8 * 3], s_offset = new int[8 * 2];
        private static int s_count;
        private static readonly int[] s_bb = new int[4], s_cur = new int[5], s_nxt = new int[5];

        private static void BuildDispatches(Vector4 lp, int width, int height)
        {
            s_count = 0;
            float xyW = lp.w, fpLimit = .000002f * WaveSize;
            if (xyW >= 0 && xyW < fpLimit) xyW = fpLimit;
            else if (xyW < 0 && xyW > -fpLimit) xyW = -fpLimit;
            s_light = new Vector4(
                (lp.x / xyW * .5f + .5f) * width,
                (lp.y / xyW * -.5f + .5f) * height,
                lp.w == 0 ? 0 : lp.z / lp.w,
                lp.w > 0 ? 1 : -1);

            int lx = (int)(s_light.x + .5f), ly = (int)(s_light.y + .5f);
            // the whole screen, inclusive
            int[] bb = s_bb, cur = s_cur, nxt = s_nxt; // cur, nxt: waves x,y,z, offset x,y
            bb[0] = 0 - lx; bb[1] = -(height - ly); bb[2] = width - lx; bb[3] = -(0 - ly);

            for (int q = 0; q < 4; q++)
            {
                bool vertical = q == 0 || q == 3;
                int b0 = Mathf.Max(0, (q & 1) != 0 ? bb[0] : -bb[2]) / WaveSize;
                int b1 = Mathf.Max(0, (q & 2) != 0 ? bb[1] : -bb[3]) / WaveSize;
                int b2 = Mathf.Max(0, ((q & 1) != 0 ? bb[2] : -bb[0]) + WaveSize * (vertical ? 1 : 2) - 1) / WaveSize;
                int b3 = Mathf.Max(0, ((q & 2) != 0 ? bb[3] : -bb[1]) + WaveSize * (vertical ? 2 : 1) - 1) / WaveSize;
                if (b2 - b0 <= 0 || b3 - b1 <= 0) continue;

                int biasX = (q == 2 || q == 3) ? 1 : 0, biasY = (q == 1 || q == 3) ? 1 : 0;
                cur[0] = WaveSize; cur[1] = b2 - b0; cur[2] = b3 - b1;
                cur[3] = ((q & 1) != 0 ? b0 : -b2) + biasX;
                cur[4] = ((q & 2) != 0 ? -b3 : b1) + biasY;
                bool keepCur = true, keepNxt = false;

                int axisDelta = +bb[0] - bb[1];
                if (q == 1) axisDelta = +bb[2] + bb[1];
                if (q == 2) axisDelta = -bb[0] - bb[3];
                if (q == 3) axisDelta = -bb[2] + bb[3];
                axisDelta = (axisDelta + WaveSize - 1) / WaveSize;

                if (axisDelta > 0)
                {
                    System.Array.Copy(cur, nxt, 5);
                    keepNxt = true;
                    if (q == 0) { nxt[2] = Mathf.Min(cur[2], axisDelta); cur[2] -= nxt[2]; nxt[4] = cur[4] + cur[2]; nxt[3]--; nxt[1]++; }
                    if (q == 1) { nxt[1] = Mathf.Min(cur[1], axisDelta); cur[1] -= nxt[1]; nxt[3] = cur[3] + cur[1]; nxt[2]++; }
                    if (q == 2) { nxt[1] = Mathf.Min(cur[1], axisDelta); cur[1] -= nxt[1]; cur[3] += nxt[1]; nxt[2]++; nxt[4]--; }
                    if (q == 3) { nxt[2] = Mathf.Min(cur[2], axisDelta); cur[2] -= nxt[2]; cur[4] += nxt[2]; nxt[1]++; }
                    if (nxt[1] <= 0 || nxt[2] <= 0) keepNxt = false;
                    if (cur[1] <= 0 || cur[2] <= 0) keepCur = false;
                }
                if (keepCur) Add(cur);
                if (keepNxt) Add(nxt);
            }
        }

        private static void Add(int[] d)
        {
            if (s_count >= 8) return;
            s_waves[s_count * 3] = d[0]; s_waves[s_count * 3 + 1] = d[1]; s_waves[s_count * 3 + 2] = d[2];
            s_offset[s_count * 2] = d[3] * WaveSize; s_offset[s_count * 2 + 1] = d[4] * WaveSize;
            s_count++;
        }
    }
}
