
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.NVIDIA;
using UnityEngine.PostProcessing;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WiresDLSS
{
    // Existing camera image callbacks only; no new per-frame Update or polling.
    // No DXGI interposer and no changes to RenoDX shaders, menus or profiles.
    [DefaultExecutionOrder(-2200)]
    [RequireComponent(typeof(Camera))]
    internal sealed class DLSSRenderer : MonoBehaviour
    {
        private static readonly List<DLSSRenderer> Active = new List<DLSSRenderer>(2);
        private static bool scaleSaved;
        private static bool previousAuto;
        private static uint previousVertical;

        private Camera cameraComponent;
        private PostProcessingBehaviour post;
        private GraphicsDevice device;
        private DLSSContext context;
        private CommandBuffer commands;
        private RenderTexture output;
        private RenderTexture downscaled;
        private RenderTexture depthScaled, motionScaled;
        private readonly List<RawImage> visualOutputs = new List<RawImage>(3);
        private int width, height, finalWidth, finalHeight;
        private Mode contextMode;
        private bool runtimeFailed, loggedBackend, verified, warnedMotion;
        private int verifyAttempts, jitterIndex;
        private bool jitterApplied;
        private Vector2 jitter;
        private int jitterPhases;
        private bool oldTransparentJitter;
        private bool requireReset = true;
        private bool aaOverridden, oldAa;
        private bool loggedScaleFallback;

        private void Awake()
        {
            cameraComponent = GetComponent<Camera>();
            post = GetComponent<PostProcessingBehaviour>();
            if (cameraComponent != null)
                cameraComponent.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            Active.Add(this);
        }

        internal static void InvalidateAll()
        {
            foreach (DLSSRenderer renderer in Active.ToArray())
                if (renderer != null) renderer.ResetRenderer();
            ResetRenderScale();
        }

        private static float Halton(int number, int @base)
        {
            float f = 1f, result = 0f;
            while (number > 0)
            {
                f /= @base;
                result += f * (number % @base);
                number /= @base;
            }
            return result;
        }

        private static float RenderFactor(Mode mode)
        {
            switch (mode)
            {
                case Mode.Quality: return 0.67f;
                case Mode.Balanced: return 0.58f;
                case Mode.Performance: return 0.50f;
                default: return 1f;
            }
        }

        private static void RequestRenderScale(Mode mode)
        {
            if (!scaleSaved)
            {
                scaleSaved = true;
                previousAuto = UpscaledFrameBuffer.m_autoTargetResolution;
                previousVertical = UpscaledFrameBuffer.m_targetResolutionVertical;
            }
            UpscaledFrameBuffer.m_autoTargetResolution = false;
            UpscaledFrameBuffer.m_targetResolutionVertical =
                mode == Mode.DLAA ? uint.MaxValue :
                (uint)Mathf.Max(8, Mathf.RoundToInt(Screen.height * RenderFactor(mode)));
        }

        private static void ResetRenderScale()
        {
            if (!scaleSaved) return;
            UpscaledFrameBuffer.m_autoTargetResolution = previousAuto;
            UpscaledFrameBuffer.m_targetResolutionVertical = previousVertical;
            scaleSaved = false;
        }

        private bool EnsureBackend()
        {
            if (runtimeFailed) return false;
            if (device != null) return true;
            try
            {
                if (!SystemInfo.graphicsDeviceVendor.ToLowerInvariant().Contains("nvidia"))
                    throw new NotSupportedException("NVIDIA GPU required");
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 &&
                    SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
                    throw new NotSupportedException("Direct3D 11 or 12 required");
                if (!NVUnityPlugin.IsLoaded() && !NVUnityPlugin.Load())
                    throw new InvalidOperationException("NVUnityPlugin runtime not found or failed");
                device = GraphicsDevice.device ?? GraphicsDevice.CreateGraphicsDevice();
                if (device == null || !device.IsFeatureAvailable(GraphicsDeviceFeature.DLSS))
                    throw new InvalidOperationException("Unity reports NVIDIA DLSS unavailable");
                if (!loggedBackend)
                {
                    loggedBackend = true;
                    Plugin.Report("DLSS device available on " + SystemInfo.graphicsDeviceType +
                        ". RenoDX and ReShade DLLs not modified.");
                }
                return true;
            }
            catch (Exception ex)
            {
                runtimeFailed = true;
                Plugin.Warn("DLSS inactive (vanilla fallback): " + ex.Message);
                return false;
            }
        }

        private void OnPreCull()
        {
            jitterApplied = false;
            if (Plugin.Current == Mode.Off || runtimeFailed || cameraComponent == null ||
                !cameraComponent.isActiveAndEnabled) return;

            cameraComponent.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            if (context == null || width < 1 || height < 1) return;

            // ValheimUpscaler Light 3.0 temporal input policy:
            // phaseCount = clamp(ceil(8 * (outputWidth / renderWidth)^2), 8, 128)
            // The NVIDIA reported jitter uses auto sign mode 3: negative X and Y.
            int phases = Mathf.Clamp(Mathf.CeilToInt(8f *
                (finalWidth / (float)Mathf.Max(1, width)) *
                (finalWidth / (float)Mathf.Max(1, width))), 8, 128);
            if (phases != jitterPhases || requireReset)
            {
                jitterIndex = 0;
                jitterPhases = phases;
            }
            jitter = new Vector2(Halton(jitterIndex + 1, 2) - .5f,
                                 Halton(jitterIndex + 1, 3) - .5f);
            jitterIndex = (jitterIndex + 1) % phases;
            cameraComponent.ResetProjectionMatrix();
            Matrix4x4 original = cameraComponent.projectionMatrix;
            cameraComponent.nonJitteredProjectionMatrix = original;
            Matrix4x4 projection = original;
            projection.m02 += (2f * jitter.x) / width;
            projection.m12 += (2f * jitter.y) / height;
            oldTransparentJitter = cameraComponent.useJitteredProjectionMatrixForTransparentRendering;
            cameraComponent.projectionMatrix = projection;
            cameraComponent.useJitteredProjectionMatrixForTransparentRendering = true;
            jitterApplied = true;
        }

        private void OnPostRender()
        {
            if (jitterApplied && cameraComponent != null)
            {
                cameraComponent.ResetProjectionMatrix();
                cameraComponent.nonJitteredProjectionMatrix = cameraComponent.projectionMatrix;
                cameraComponent.useJitteredProjectionMatrixForTransparentRendering = oldTransparentJitter;
                jitterApplied = false;
            }
        }

        private bool EnsureContext(RenderTexture source, Mode mode, out RenderTexture input)
        {
            input = source;
            int outputWidth = Screen.width, outputHeight = Screen.height;
            if (outputWidth < 8 || outputHeight < 8 || source == null) return false;
            int desiredWidth = mode == Mode.DLAA ? outputWidth :
                Mathf.Max(8, Mathf.RoundToInt(outputWidth * RenderFactor(mode)));
            int desiredHeight = mode == Mode.DLAA ? outputHeight :
                Mathf.Max(8, Mathf.RoundToInt(outputHeight * RenderFactor(mode)));

            bool nativeLowResolution =
                mode != Mode.DLAA && source.width < outputWidth &&
                source.height < outputHeight;

            // In-game FrameBufferScaler allows real low-resolution scene
            // rendering. Menu scenes may not contain a scaler, so those
            // downsample the full scene only for visual quality, not FPS.
            if (mode != Mode.DLAA && !nativeLowResolution)
            {
                if (!loggedScaleFallback)
                {
                    loggedScaleFallback = true;
                    Plugin.Warn("DLSS camera has no native low-resolution target: input downsampled; this scene does not gain render-resolution FPS.");
                }
                if (downscaled == null || downscaled.width != desiredWidth ||
                    downscaled.height != desiredHeight)
                {
                    if (downscaled != null) { downscaled.Release(); Destroy(downscaled); }
                    downscaled = new RenderTexture(desiredWidth, desiredHeight, 0,
                        RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { name = "Wire DLSS Input", antiAliasing = 1 };
                    downscaled.Create();
                }
                Graphics.Blit(source, downscaled);
                input = downscaled;
            }
            else if (mode == Mode.DLAA && (source.width != outputWidth || source.height != outputHeight))
                return false;

            if (context != null && width == input.width && height == input.height &&
                finalWidth == outputWidth && finalHeight == outputHeight && contextMode == mode)
                return true;

            ReleaseFeature();
            if (!EnsureBackend()) return false;

            width = input.width; height = input.height;
            finalWidth = outputWidth; finalHeight = outputHeight;
            contextMode = mode;
            try
            {
                commands = new CommandBuffer { name = "Wire DLAA/DLSS" };
                output = new RenderTexture(finalWidth, finalHeight, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Wire DLSS Output HDR",
                    enableRandomWrite = true,
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear
                };
                if (!output.Create()) throw new InvalidOperationException("Cannot allocate HDR output");

                DLSSCommandInitializationData init = new DLSSCommandInitializationData
                {
                    inputRTWidth = (uint)width, inputRTHeight = (uint)height,
                    outputRTWidth = (uint)finalWidth, outputRTHeight = (uint)finalHeight,
                    quality = mode == Mode.DLAA ? (DLSSQuality)5 :
                        mode == Mode.Quality ? DLSSQuality.MaximumQuality :
                        mode == Mode.Balanced ? DLSSQuality.Balanced :
                        DLSSQuality.MaximumPerformance
                };

                bool isHdr = cameraComponent.allowHDR &&
                    (source.format == RenderTextureFormat.ARGBHalf ||
                     source.format == RenderTextureFormat.ARGBFloat ||
                     source.format == RenderTextureFormat.DefaultHDR);
                init.SetFlag(DLSSFeatureFlags.IsHDR, isHdr);
                init.SetFlag(DLSSFeatureFlags.DepthInverted, SystemInfo.usesReversedZBuffer);
                init.SetFlag(DLSSFeatureFlags.MVLowRes, false);
                init.SetFlag(DLSSFeatureFlags.MVJittered, false);
                init.SetFlag(DLSSFeatureFlags.DoSharpening, false);
                context = device.CreateFeature(commands, in init);
                if (context == null) throw new InvalidOperationException("NVIDIA DLSS feature context unavailable");
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
                requireReset = true;
                verifyAttempts = 0;
                verified = false;
                warnedMotion = false;
                Plugin.Report("Requested " + mode + ": input " + width + "x" + height +
                    ", output " + finalWidth + "x" + finalHeight +
                    ", HDR=" + isHdr + ", sharpening=off.");
                return true;
            }
            catch (Exception e)
            {
                runtimeFailed = true;
                Plugin.Warn("NVIDIA refused DLSS mode " + mode + ": " + e.Message);
                ReleaseFeature();
                return false;
            }
        }

        private static readonly System.Reflection.PropertyInfo NativeFeatureSlot = typeof(DLSSContext).GetProperty("featureSlot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        private static uint GetContextSlot(DLSSContext ctx) => NativeFeatureSlot != null ? Convert.ToUInt32(NativeFeatureSlot.GetValue(ctx, null)) : uint.MaxValue;

        private bool VerifyNativeFeature()
        {
            if (verified) return true;
            if (context == null || device == null) return false;
            GraphicsDeviceDebugView debug = null;
            try
            {
                debug = device.CreateDebugView();
                device.UpdateDebugView(debug);
                foreach (DLSSDebugFeatureInfos feature in debug.dlssFeatureInfos)
                    if (feature.featureSlot == GetContextSlot(context) && feature.validFeature)
                    {
                        verified = true;
                        Plugin.Report("NVIDIA confirmed " + contextMode + " native DLSS feature (slot " +
                            GetContextSlot(context) + ").");
                        return true;
                    }
            }
            finally
            {
                if (debug != null) device.DeleteDebugView(debug);
            }
            if (++verifyAttempts >= 8)
                throw new InvalidOperationException("NVIDIA never reported a valid DLSS feature");
            return false;
        }

        private void SyncOutput()
        {
            if (output == null || cameraComponent == null ||
                cameraComponent.targetTexture == null) return;
            FrameBufferScaler[] scalers =
                UnityEngine.Object.FindObjectsByType<FrameBufferScaler>(FindObjectsSortMode.None);
            foreach (FrameBufferScaler scaler in scalers)
            {
                if (scaler == null) continue;
                RawImage raw = scaler.GetComponent<RawImage>();
                if (raw == null || (raw.texture != cameraComponent.targetTexture &&
                    raw.texture != output)) continue;
                raw.texture = output;
                if (!visualOutputs.Contains(raw)) visualOutputs.Add(raw);
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (source == null) { Graphics.Blit(source, destination); return; }
            Mode mode = Plugin.Current;
            if (mode == Mode.Off || runtimeFailed)
            {
                Graphics.Blit(source, destination);
                return;
            }
            try
            {
                RequestRenderScale(mode);
                if (!EnsureBackend() || !EnsureContext(source, mode, out RenderTexture input))
                {
                    Graphics.Blit(source, destination);
                    return;
                }
                Texture depth = Shader.GetGlobalTexture("_CameraDepthTexture");
                Texture motion = Shader.GetGlobalTexture("_CameraMotionVectorsTexture");
                // In a menu with no scaled framebuffer the scene renders at
                // native size. Downsample its depth/motion alongside color
                // so NVIDIA receives coherent same-resolution inputs.
                if (input == downscaled && depth != null && motion != null)
                {
                    if (depthScaled == null || depthScaled.width != width || depthScaled.height != height)
                    {
                        if (depthScaled != null) { depthScaled.Release(); Destroy(depthScaled); }
                        if (motionScaled != null) { motionScaled.Release(); Destroy(motionScaled); }
                        depthScaled = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat,
                            RenderTextureReadWrite.Linear) { name = "Wire DLSS Menu Depth" };
                        motionScaled = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf,
                            RenderTextureReadWrite.Linear) { name = "Wire DLSS Menu Motion" };
                        depthScaled.Create(); motionScaled.Create();
                    }
                    Graphics.Blit(depth, depthScaled);
                    Graphics.Blit(motion, motionScaled);
                    depth = depthScaled;
                    motion = motionScaled;
                }
                if (depth == null || motion == null ||
                    depth.width != width || depth.height != height ||
                    motion.width != width || motion.height != height)
                {
                    if (!warnedMotion)
                    {
                        warnedMotion = true;
                        Plugin.Warn("Waiting for camera depth and motion vectors " + width + "x" + height);
                    }
                    Graphics.Blit(source, destination);
                    return;
                }

                ref DLSSCommandExecutionData exec = ref context.executeData;
                exec.reset = requireReset ? 1 : 0;
                exec.sharpness = 0f;
                exec.jitterOffsetX = -jitter.x;
                exec.jitterOffsetY = -jitter.y;
                exec.mvScaleX = -width;
                exec.mvScaleY = -height;
                exec.invertYAxis = 1;
                exec.invertXAxis = 0;
                exec.preExposure = 1f;
                exec.subrectOffsetX = 0;
                exec.subrectOffsetY = 0;
                exec.subrectWidth = (uint)width;
                exec.subrectHeight = (uint)height;

                var textures = new DLSSTextureTable {
                    colorInput = input, colorOutput = output,
                    depth = depth, motionVectors = motion
                };
                device.ExecuteDLSS(commands, context, in textures);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
                requireReset = false;
                if (!VerifyNativeFeature())
                {
                    Graphics.Blit(source, destination);
                    return;
                }

                if (!aaOverridden && post != null && post.profile != null)
                {
                    oldAa = post.profile.antialiasing.enabled;
                    post.profile.antialiasing.enabled = false;
                    aaOverridden = true;
                }

                if (cameraComponent.targetTexture != null && mode != Mode.DLAA)
                {
                    SyncOutput();
                    Graphics.Blit(source, destination);
                }
                else
                    Graphics.Blit(output, destination);
            }
            catch (Exception ex)
            {
                runtimeFailed = true;
                Plugin.Warn("DLSS rendering failed; restoring vanilla image: " +
                    ex.GetType().Name + " " + ex.Message);
                ResetRenderer();
                Graphics.Blit(source, destination);
            }
        }

        private void ReleaseFeature()
        {
            foreach (RawImage raw in visualOutputs)
                if (raw != null && raw.texture == output)
                    raw.texture = cameraComponent != null ? cameraComponent.targetTexture : null;
            visualOutputs.Clear();

            if (context != null && device != null)
            {
                try
                {
                    CommandBuffer destroy = new CommandBuffer { name = "Release Wire DLSS" };
                    device.DestroyFeature(destroy, context);
                    Graphics.ExecuteCommandBuffer(destroy);
                    destroy.Release();
                }
                catch (Exception e) { Plugin.Warn("DLSS cleanup: " + e.Message); }
            }
            context = null;
            if (commands != null) { commands.Release(); commands = null; }
            if (output != null) { output.Release(); Destroy(output); output = null; }
            verified = false;
            requireReset = true;
        }

        private void ResetRenderer()
        {
            ReleaseFeature();
            if (downscaled != null) { downscaled.Release(); Destroy(downscaled); downscaled = null; }
            if (depthScaled != null) { depthScaled.Release(); Destroy(depthScaled); depthScaled = null; }
            if (motionScaled != null) { motionScaled.Release(); Destroy(motionScaled); motionScaled = null; }
            if (aaOverridden && post != null && post.profile != null)
                post.profile.antialiasing.enabled = oldAa;
            aaOverridden = false;
            width = height = finalWidth = finalHeight = 0;
            loggedScaleFallback = false;
            runtimeFailed = false;
            if (cameraComponent != null) { cameraComponent.ResetProjectionMatrix(); cameraComponent.nonJitteredProjectionMatrix = cameraComponent.projectionMatrix; }
        }

        private void OnDisable()
        {
            ResetRenderer();
            ResetRenderScale();
        }
        private void OnDestroy()
        {
            ResetRenderer();
            Active.Remove(this);
            if (Active.Count == 0) ResetRenderScale();
        }
    }
}
