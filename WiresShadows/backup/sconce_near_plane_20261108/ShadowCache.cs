using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresLightOcclusion
{
    // Cached point light shadows. Lights near or in view get a slot: their
    // Unity shadows are turned off (no per-frame shadow rendering) and a
    // shadow cube is rendered from the real meshes into a cube map array the
    // lighting shader reads, only again when something near it changes.
    // Full-size slots for close lights, low-res ones for distant lights.
    // Characters, swinging doors and the building preview go into separate
    // moving maps, redrawn every frame for lights they're near.
    internal sealed partial class ShadowCache : MonoBehaviour
    {
        private sealed class Tracked
        {
            internal Light Light;
            internal LightLod Lod;
            internal float NativeCacheRange = 1f; // normalized range of the actually rendered native-depth map
            internal int Slot = -1;
            internal float Reach, Priority;
            internal bool Tiny;          // an item's glow: only a spare slot
            internal float LitAt = -10f;  // when it was last switched on
            internal bool Instant;       // next render shows at full strength
            internal int ReleasedFrame = -1;
            internal Rigidbody Body;     // a loose item's physics body
            internal bool Rolling;       // using the game's own shadows while it moves
            internal bool Staged;        // re-rendering into the staging cube, swapped in when done
            internal bool NativeStaged;  // comparison-depth faces also rendered to the spare cube
            internal readonly List<Renderer> Explicit = new List<Renderer>();  // untagged shaders, drawn directly
            internal readonly List<Renderer> Sheets = new List<Renderer>();     // two-sided flat ones, both passes
            internal readonly List<Mesh> ProxyMeshes = new List<Mesh>();       // colliders inside the near plane
            internal readonly List<Matrix4x4> ProxyMatrices = new List<Matrix4x4>();
            internal int NearProxies;
            internal readonly List<Mesh> StructMeshes = new List<Mesh>();      // null: a box
            internal readonly List<Matrix4x4> StructMatrices = new List<Matrix4x4>();   // the first ones are inside the near plane only
            internal readonly List<int> ExplicitSub = new List<int>();
            internal Vector3 Center;
            internal Vector3 PublishedCenter; // centre of the six faces currently visible to the shader
            internal bool Dirty;         // waiting for a full re-render
            internal float DirtyAt;      // not before this time (bursts of changes coalesce)
            internal int Face = -1;      // face being rendered, -1 when idle
            internal int DoorFaces;      // affected static faces queued on a gate/shutter change
            internal bool DoorPrepared;  // casters gathered once for this queued face batch
            internal bool Ready;         // has a complete map
            internal float Fade;         // shadow strength fading in, as LightLod does (1 s)
            internal readonly List<Renderer> Hide = new List<Renderer>();
            internal readonly List<OwnCaster> Own = new List<OwnCaster>(); // Pointlight Rework casters
            internal readonly List<Renderer> Show = new List<Renderer>();
            internal LightShadows OriginalShadows;
            internal bool OriginalShadowLod;
            internal float OriginalMovement = -1f;
            internal bool Suppressed;    // Unity's own shadows off for this light
            internal bool Sconce;        // detected once per registered light
            internal bool NativeSconceShadows; // true only while in native shadow A/B
            internal float OriginalShadowStrength;
            internal float OriginalShadowNearPlane;
            internal Vector3 RootPos;    // where the light's object stood when its map was made
            internal Quaternion RootRot;
            internal readonly List<Animator> Animators = new List<Animator>();
            internal readonly List<AnimatorCullingMode> AnimatorModes = new List<AnimatorCullingMode>();
        }

        internal static ShadowCache Instance;

        // Debug log: one line per event, never per frame. Account for the
        // event formatting + logging CPU separately from WLO rendering.
        internal static void Check(string what)
        {
            if (!Plugin.DebugLog.Value) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Plugin.Log.LogInfo("[check] " + what);
            if (Instance != null)
            {
                Instance._perfEventLogTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                Instance._perfProbeCalls += 2;
            }
        }

        private static string NameOf(Light l) => l == null ? "?" : l.transform.root.name.Replace("(Clone)", "") + "/" + l.name;

        // Record prefab class once; no additional work in the rendering loop.
        private static bool IsSconce(Light light)
        {
            if (light == null) return false;
            Piece piece = light.GetComponentInParent<Piece>();
            string n = (piece != null ? piece.name : light.transform.root.name).ToLowerInvariant();
            return n.Contains("sconce") || n.Contains("walltorch") ||
                n.Contains("wall_torch") || n.Contains("walllantern") ||
                n.Contains("wall_lantern") || n.Contains("walllight") ||
                n.Contains("wall_light");
        }
        internal static Shader DistanceShader;

        private static readonly List<Tracked> Lights = new List<Tracked>();
        private static readonly Dictionary<Light, Tracked> ByLight = new Dictionary<Light, Tracked>();
        private static readonly AccessTools.FieldRef<LightLod, bool> ShadowLod =
            AccessTools.FieldRefAccess<LightLod, bool>("m_shadowLod");

        private static readonly int MapsId = Shader.PropertyToID("_WireShadowMaps");
        private static readonly int LightsId = Shader.PropertyToID("_WireShadowLights");
        private static readonly int CountId = Shader.PropertyToID("_WireShadowCount");
        private static readonly int CenterId = Shader.PropertyToID("_WireLightCenter");

        private const int NearMax = 32, FarMax = 64, MaxSlots = NearMax + FarMax;
        private const float SettleSeconds = 0.15f;
        private const float UrgentMargin = 12f;
        private const float MovedDistance = 0.25f;
        private const float FireSway = 0.1f; // LightFlicker's default, as on fire pits
        private const int LoadingFacesPerFrame = 6;
        private const float KeepMargin = 15f;
        private const float SwapMargin = 10f;
        // Near slots need a tighter hysteresis: nearby lights must not remain
        // trapped in the 256px tier because an old light owns a near slot.
        private const float NearSwapMargin = 2.5f;

        // Moving casters (characters, doors, the preview): a second map per
        // full-size slot, redrawn every frame, only for the cube faces they're
        // in and only for lights whose reach is on screen.
        private RenderTexture _dynMaps;
        private RenderTexture _dynFace;
        private Camera _dynCamera;
        private readonly float[] _dynActive = new float[MaxSlots]; // 1 = the slot's moving map is in use
        private RenderTexture _farDynMaps;
        private static readonly int FarDynMapsId = Shader.PropertyToID("_WireFarDynMaps");

        // Static shadow slots are logical. Moving-shadow faces occupy a compact,
        // lossless physical cube pool, growing only when another light really
        // needs moving shadows. Resolution and exact shader comparisons unchanged.
        private const int InitialMovingCubes = 4;
        private int _dynNearCapacity, _dynFarCapacity;
        private readonly int[] _dynPhysical = new int[MaxSlots];
        private readonly int[] _dynNearOwners = new int[NearMax];
        private readonly int[] _dynFarOwners = new int[FarMax];
        private readonly float[] _dynPhysicalUniform = new float[MaxSlots];
        private static readonly int DynPhysicalId = Shader.PropertyToID("_WireDynPhysicalSlot");
        // Slot changes are often bursty (teleport, fires, large builds).
        // Upload this complete 96-float shader array only once at the next
        // main-camera pre-cull, AFTER all additions/removals have completed.
        // Never gate or delay shadow renders; command buffers consume their
        // physical map indices directly, and the shader sees final indices.
        private bool _dynPhysicalDirty;
        private void UploadDynPhysical()
        {
            if (!_dynPhysicalDirty) return;
            _dynPhysicalDirty = false;
            Shader.SetGlobalFloatArray(DynPhysicalId, _dynPhysicalUniform);
        }
        private void ResetDynPool()
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                _dynPhysical[i] = -1;
                _dynPhysicalUniform[i] = -1f;
            }
            for (int i = 0; i < NearMax; i++) _dynNearOwners[i] = -1;
            for (int i = 0; i < FarMax; i++) _dynFarOwners[i] = -1;
            _dynPhysicalDirty = false;
            Shader.SetGlobalFloatArray(DynPhysicalId, _dynPhysicalUniform);
        }
        private void FreeDynSlot(int sl)
        {
            if (sl < 0 || sl >= MaxSlots) return;
            int physical = _dynPhysical[sl];
            if (physical < 0) return;
            int[] owners = sl < _near ? _dynNearOwners : _dynFarOwners;
            owners[physical] = -1;
            _dynPhysical[sl] = -1;
            _dynPhysicalUniform[sl] = -1f;
            _dynPhysicalDirty = true;
        }
        private bool GrowDynPool(bool near, int required)
        {
            int oldCapacity = near ? _dynNearCapacity : _dynFarCapacity;
            int limit = near ? _near : Mathf.Max(1, _slots.Length - _near);
            int capacity = Mathf.Min(limit, Mathf.Max(required, oldCapacity * 2));
            if (capacity <= oldCapacity) return false;
            RenderTexture old = near ? _dynMaps : _farDynMaps;
            var next = new RenderTexture(old.width, old.height, 0, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * capacity,
                useMipMap = false, filterMode = FilterMode.Point,
                name = near ? "WireDynShadowMapsSparse" : "WireFarDynShadowMapsSparse"
            };
            if (!next.Create())
            {
                next.Release();
                Plugin.Log.LogWarning("Could not grow sparse moving-shadow pool.");
                return false;
            }
            // Whole-face copies retain all cached silhouettes exactly. Growth
            // occurs only on an actual new moving-shadow light, not every frame.
            int[] owners = near ? _dynNearOwners : _dynFarOwners;
            for (int i = 0; i < oldCapacity; i++)
            {
                int logical = owners[i];
                if (logical < 0 || _dynFacesLast[logical] == 0) continue;
                for (int face = 0; face < 6; face++)
                    Graphics.CopyTexture(old, i * 6 + face, 0, next, i * 6 + face, 0);
            }
            if (near)
            {
                _dynMaps = next;
                _dynNearCapacity = capacity;
                Shader.SetGlobalTexture(DynMapsId, next);
            }
            else
            {
                _farDynMaps = next;
                _dynFarCapacity = capacity;
                Shader.SetGlobalTexture(FarDynMapsId, next);
            }
            old.Release();
            Destroy(old);
            Plugin.Log.LogInfo("[sparse-moving] " + (near ? "near" : "far") +
                " moving pool grew to " + capacity + " cubes.");
            return true;
        }
        private void ReserveNewMovingCubes(int requestedNear, int requestedFar)
        {
            // This is reached only when an EXISTING moving-caster update
            // actually needs a cube. No timer or independent update loop.
            if (requestedNear > 0)
            {
                int free = 0;
                for (int i = 0; i < _dynNearCapacity; i++)
                    if (_dynNearOwners[i] < 0) free++;
                if (requestedNear > free)
                    GrowDynPool(true, _dynNearCapacity + requestedNear - free);
            }
            if (requestedFar > 0)
            {
                int free = 0;
                for (int i = 0; i < _dynFarCapacity; i++)
                    if (_dynFarOwners[i] < 0) free++;
                if (requestedFar > free)
                    GrowDynPool(false, _dynFarCapacity + requestedFar - free);
            }
        }
        private bool EnsureDynSlot(int sl)
        {
            if (_dynPhysical[sl] >= 0) return true;
            bool near = sl < _near;
            int[] owners = near ? _dynNearOwners : _dynFarOwners;
            int capacity = near ? _dynNearCapacity : _dynFarCapacity;
            int free = -1;
            for (int i = 0; i < capacity; i++)
                if (owners[i] < 0) { free = i; break; }
            if (free < 0)
            {
                if (!GrowDynPool(near, capacity + 1)) return false;
                free = capacity;
            }
            owners[free] = sl;
            _dynPhysical[sl] = free;
            _dynPhysicalUniform[sl] = free;
            _dynPhysicalDirty = true;
            return true;
        }
        private void DynTarget(int sl, out RenderTexture maps, out int cube)
        {
            maps = sl < _near ? _dynMaps : _farDynMaps;
            cube = _dynPhysical[sl];
        }
        private readonly int[] _dynFacesLast = new int[MaxSlots];
        private readonly RectInt[] _dynRect = new RectInt[MaxSlots * 6]; // what each moving face drew last
        // Exactly the rect actually touched by the moving-face scissor.
        // Used ONLY while copying/converting an already dirty native face.
        private readonly RectInt[] _nativeMovingTouched = new RectInt[MaxSlots * 6];
        internal void InvalidateMovingFaces()
        {
            // One-shot invalidation on debug toggle, no additional per-frame
            // work when the scissor experiment is disabled.
            for (int f = 0; f < _faceHash.Length; f++)
                _faceHash[f] = int.MinValue;
        }
        private static readonly int DynMapsId = Shader.PropertyToID("_WireDynMaps");
        private static readonly int DynActiveId = Shader.PropertyToID("_WireDynActive");
        private bool _active;

        private Tracked[] _slots = new Tracked[0];
        private readonly Vector4[] _slotData = new Vector4[MaxSlots];
        private RenderTexture _maps;
        private RenderTexture _face;
        private RenderTexture _farMaps, _farFace;
        private RenderTexture _faceBack, _farFaceBack, _flatFront, _flatBack, _farFlatFront, _farFlatBack;
        // Re-renders of a light that already has a map go into a spare cube
        // (one per tier), a face per frame, and are copied in whole when done:
        // no hitch, and never a half-updated map.
        private RenderTexture _stage, _farStage;
        private readonly Tracked[] _stageOwner = new Tracked[2];
        private Material _combine, _frontMat, _backMat;
        private CommandBuffer _staticCb;

        // Untagged-shader parts, drawn with the pass's own shader during the
        // shadow camera's render (same face matrices).
        private void DrawUntagged(Tracked t, ref Material mat, Shader shader, bool front)
        {
            _staticCb.Clear();
            if (t.Explicit.Count == 0 && t.ProxyMeshes.Count == 0 && t.Sheets.Count == 0)
                return;
            if (mat == null)
                mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
                // Wind-bent cloth: the stored sheet is its rest pose, and the bent
                // visible banner keeps crossing it (blocky self-shadow). Push its
                // shadow 15 cm away from the light so it never shades itself.
                Mesh clothMesh = IsClothRenderer(r) && !r.isPartOfStaticBatch && r.GetComponent<MeshFilter>() is MeshFilter mf
                    ? mf.sharedMesh : null;
                Matrix4x4 pushed = clothMesh != null
                    ? Matrix4x4.Translate((r.bounds.center - t.Center).normalized * ClothPush) * r.localToWorldMatrix
                    : default;
                for (int sub = 0; sub < Materials.Count; sub++)
                    if (Materials[sub] != null)
                    {
                        if (clothMesh != null && sub < clothMesh.subMeshCount)
                            _staticCb.DrawMesh(clothMesh, pushed, ClipMaterial(Materials[sub], shader), sub, 1);
                        else
                            _staticCb.DrawRenderer(r, ClipMaterial(Materials[sub], shader), sub, 1);
                    }
            }
            // Colliders only by their backs: they block what's behind a wall,
            // but never shade the wall's own surface inside them.
            if (!front)
                for (int i = 0; i < t.ProxyMeshes.Count; i++)
                    _staticCb.DrawMesh(t.ProxyMeshes[i], t.ProxyMatrices[i], mat, 0, 0, i < t.NearProxies ? _proxyProps : _structProps);
            for (int i = 0; i < t.Explicit.Count; i++)
            {
                Renderer r = t.Explicit[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                r.GetSharedMaterials(Materials);
                int sub = t.ExplicitSub[i];
                Material orig = sub < Materials.Count ? Materials[sub] : null;
                _staticCb.DrawRenderer(r, orig != null ? ClipMaterial(orig, shader) : mat, sub, 0);
            }
        }
        private CommandBuffer _combineCb;

        private const float ClothPush = 0.15f;
        private static readonly Dictionary<Renderer, bool> ClothRenderers = new Dictionary<Renderer, bool>();

        private static bool IsClothRenderer(Renderer r)
        {
            if (!ClothRenderers.TryGetValue(r, out bool cloth))
            {
                r.GetSharedMaterials(ClothMats);
                foreach (Material m in ClothMats)
                    if (m != null && m.shader != null && m.shader.name == "Custom/Vegetation" &&
                        m.HasProperty("_PushClothMode") && m.GetFloat("_PushClothMode") > 0.5f)
                    { cloth = true; break; }
                if (ClothRenderers.Count > 4096) ClothRenderers.Clear();
                ClothRenderers[r] = cloth;
            }
            return cloth;
        }
        private static readonly List<Material> ClothMats = new List<Material>();

        // The pass's shader with a material's texture and cutoff, so drawn
        // directly it keeps its holes (leaves, a banner's edge).
        private static readonly Dictionary<(Material, Shader), Material> ClipMaterials = new Dictionary<(Material, Shader), Material>();

        private static Material ClipMaterial(Material orig, Shader shader)
        {
            if (!ClipMaterials.TryGetValue((orig, shader), out Material m) || m == null)
            {
                m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                Texture tex = orig.HasProperty("_MainTex") ? orig.mainTexture : null;
                if (tex != null)
                {
                    m.mainTexture = tex;
                    m.mainTextureScale = orig.mainTextureScale;
                    m.mainTextureOffset = orig.mainTextureOffset;
                }
                m.SetFloat("_Cutoff", orig.HasProperty("_Cutoff") ? orig.GetFloat("_Cutoff") : 0.5f);
                // Holes only where the material has a cutoff: an opaque one's
                // texture alpha is often something else (gloss).
                m.SetFloat("_WireClip", tex != null && orig.HasProperty("_Cutoff") ? 1f : 0f);
                ClipMaterials[(orig, shader)] = m;
            }
            return m;
        }
        private static MaterialPropertyBlock _proxyProps, _structProps;
        private static readonly int NearCutId = Shader.PropertyToID("_WireNearCut");
        internal static Shader FrontShader, BackShader, CombineShader;
        internal static Shader NativeCopyShader;
        // Extra depth-only cube ARRAYS: same slots/resolutions as existing WLO
        // cached static maps. Conversion runs on face refresh, not per frame.
        private RenderTexture _nativeNearMaps, _nativeFarMaps;
        // One spare native-depth cube per tier. A refresh must not modify the
        // visible six faces (or unset their mask) until the whole map is ready.
        private RenderTexture _nativeStageNear, _nativeStageFar;
        private RenderTexture _nativeNearFlat, _nativeFarFlat;
        private bool EnsureNativeStage(bool near)
        {
            RenderTexture source = near ? _nativeNearMaps : _nativeFarMaps;
            if (source == null || !source.IsCreated()) return false;
            RenderTexture stage = near ? _nativeStageNear : _nativeStageFar;
            if (stage != null && stage.IsCreated() && stage.width == source.width) return true;
            if (stage != null) { stage.Release(); Destroy(stage); }
            stage = new RenderTexture(source.width, source.width, 24, RenderTextureFormat.Shadowmap)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6,
                useMipMap = false, filterMode = FilterMode.Bilinear,
                name = near ? "WireNativeNearStagingCube" : "WireNativeFarStagingCube"
            };
            if (!stage.Create())
            {
                stage.Release(); Destroy(stage);
                Plugin.Log.LogWarning("[native-stage] staging cube unavailable; using direct refresh.");
                return false;
            }
            if (near) _nativeStageNear = stage; else _nativeStageFar = stage;
            return true;
        }
        private RenderTexture NativeRenderTarget(Tracked t, bool near)
        {
            if (t.Staged && EnsureNativeStage(near))
            {
                t.NativeStaged = true;
                return near ? _nativeStageNear : _nativeStageFar;
            }
            return near ? _nativeNearMaps : _nativeFarMaps;
        }
        private Material _nativeCopyMat;
        private CommandBuffer _nativeCopyCb;
        private readonly int[] _nativeFaceMasks = new int[MaxSlots];
        private readonly Tracked[] _nativeOwners = new Tracked[MaxSlots];
        private readonly float[] _nativeActive = new float[MaxSlots];
        private readonly float[] _nativeInvRanges = new float[MaxSlots];
        private bool _nativeFailed;
        private bool _nativeReportedReady;
        // Native moving depth is a second representation of the same existing
        // RGHalf faces. No new caster traversal and no new face refreshes.
        private RenderTexture _nativeMovingNear, _nativeMovingFar, _nativeMovingFlat;
        private Material _nativeMovingCopyMat;
        private readonly float[] _nativeMovingActive = new float[MaxSlots];
        private readonly float[] _nativeMovingInvRanges = new float[MaxSlots];
        private bool _nativeMovingFailed;
        private static readonly int NativeMovingNearId = Shader.PropertyToID("_WireNativeMovingNearMaps");
        private static readonly int NativeMovingFarId = Shader.PropertyToID("_WireNativeMovingFarMaps");
        private static readonly int NativeMovingSourceId = Shader.PropertyToID("_WireNativeMovingSource");
        private static readonly int NativeMovingActiveId = Shader.PropertyToID("_WireNativeMovingActive");
        private static readonly int NativeMovingRangesId = Shader.PropertyToID("_WireNativeMovingInvRanges");

        // Graphics-format support is immutable for a running graphics device.
        // Both static and moving native-depth paths previously crossed the
        // Unity native boundary once for each active light/refresh.
        private static bool? _supportsShadowmap;
        private static bool SupportsShadowmap()
        {
            if (!_supportsShadowmap.HasValue)
                _supportsShadowmap = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap);
            return _supportsShadowmap.Value;
        }
        // Avoid repeated 96-float Unity global-array uploads when the state
        // did not change. Compare exact floats so every real status/range
        // transition, including tiny ones, reaches the GPU unchanged.
        // One retained snapshot per array; no new work loop or GC each frame.
        private static void UploadArrayIfChanged(int shaderId, float[] values,
            float[] previous, ref bool uploaded)
        {
            bool changed = !uploaded;
            if (!changed)
                for (int i = 0; i < values.Length; i++)
                    if (values[i] != previous[i])
                    {
                        changed = true;
                        break;
                    }
            if (!changed) return;
            System.Array.Copy(values, previous, values.Length);
            Shader.SetGlobalFloatArray(shaderId, values);
            uploaded = true;
        }

        private readonly float[] _uploadedDynActive = new float[MaxSlots];
        private readonly float[] _uploadedNativeMovingActive = new float[MaxSlots];
        private readonly float[] _uploadedNativeMovingRanges = new float[MaxSlots];
        private bool _hasUploadedDynActive, _hasUploadedNativeMovingActive, _hasUploadedNativeMovingRanges;

        private void UploadDynActive() =>
            UploadArrayIfChanged(DynActiveId, _dynActive, _uploadedDynActive, ref _hasUploadedDynActive);
        private void UploadNativeMovingActive() =>
            UploadArrayIfChanged(NativeMovingActiveId, _nativeMovingActive,
                _uploadedNativeMovingActive, ref _hasUploadedNativeMovingActive);
        private void UploadNativeMovingRanges() =>
            UploadArrayIfChanged(NativeMovingRangesId, _nativeMovingInvRanges,
                _uploadedNativeMovingRanges, ref _hasUploadedNativeMovingRanges);
        private void ReleaseNativeMovingResources()
        {
            if (_nativeMovingNear != null) _nativeMovingNear.Release();
            if (_nativeMovingFar != null) _nativeMovingFar.Release();
            if (_nativeMovingFlat != null) _nativeMovingFlat.Release();
            if (_nativeMovingCopyMat != null) Destroy(_nativeMovingCopyMat);
            _nativeMovingNear = _nativeMovingFar = _nativeMovingFlat = null;
            _nativeMovingCopyMat = null;
            System.Array.Clear(_nativeMovingActive, 0, MaxSlots);
            UploadNativeMovingActive();
        }
        internal static void RequestNativeMovingRefresh()
        {
            ShadowCache c = Instance;
            if (c == null) return;
            c.ReleaseNativeMovingResources();
            c._nativeMovingFailed = false;
            // Redraw only when the regular moving-shadow logic next needs a face.
            // Reset the old face signatures to guarantee valid depth data.
            System.Array.Clear(c._dynFacesLast, 0, c._dynFacesLast.Length);
            System.Array.Clear(c._faceHash, 0, c._faceHash.Length);
            c.WakeRenders();
        }
        private bool EnsureNativeMovingTier(bool near)
        {
            if (!Plugin.NativeMovingDepthPcf.Value || _nativeMovingFailed || NativeCopyShader == null) return false;
            if (!SupportsShadowmap())
            {
                _nativeMovingFailed = true;
                Plugin.Log.LogWarning("[native-moving] Shadowmap unsupported; using existing moving filter.");
                return false;
            }
            RenderTexture source = near ? _dynMaps : _farDynMaps;
            if (source == null || !source.IsCreated()) return false;
            int slices = near ? _dynNearCapacity : _dynFarCapacity;
            RenderTexture target = near ? _nativeMovingNear : _nativeMovingFar;
            if (target != null && target.IsCreated() && target.width == source.width && target.volumeDepth == 6 * slices)
                return true;
            RenderTexture oldNative = target;
            int oldCubes = oldNative != null && oldNative.IsCreated() ? oldNative.volumeDepth / 6 : 0;
            target = new RenderTexture(source.width, source.width, 24, RenderTextureFormat.Shadowmap)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = slices * 6,
                useMipMap = false, filterMode = FilterMode.Bilinear,
                name = near ? "WireNativeMovingNearDepth" : "WireNativeMovingFarDepth"
            };
            if (!target.Create())
            {
                target.Release();
                _nativeMovingFailed = true;
                Plugin.Log.LogWarning("[native-moving] Depth array allocation failed; using old moving shadow filter.");
                return false;
            }
            if (oldNative != null && oldCubes > 0)
            {
                // Preserve the native comparison depth of every populated
                // physical cube during sparse-pool growth (whole faces).
                int[] owners = near ? _dynNearOwners : _dynFarOwners;
                for (int cube = 0; cube < oldCubes; cube++)
                {
                    int logical = owners[cube];
                    if (logical < 0 || _nativeMovingActive[logical] <= 0.5f) continue;
                    for (int face = 0; face < 6; face++)
                        Graphics.CopyTexture(oldNative, cube * 6 + face, 0,
                            target, cube * 6 + face, 0);
                }
                oldNative.Release();
                Destroy(oldNative);
            }
            if (near) _nativeMovingNear = target; else _nativeMovingFar = target;
            Shader.SetGlobalTexture(near ? NativeMovingNearId : NativeMovingFarId, target);
            Plugin.Log.LogInfo("[native-moving] " + (near ? "NEAR" : "FAR") +
                " comparison depth array ready: " + slices + " slots, " + source.width + "px faces.");
            return true;
        }
        private bool QueueNativeMovingFace(Tracked t, RenderTexture src, int cube, int face, RectInt touched)
        {
            // Caller already validated the native tier once for this light.
            // Retain the cheap failure guard: the scratch texture can
            // still fail to allocate on the first face of this batch.
            bool near = t.Slot < _near;
            if (_nativeMovingFailed ||
                (near ? _nativeMovingNear == null : _nativeMovingFar == null))
                return false;
            if (_nativeMovingFlat == null || _nativeMovingFlat.width != src.width)
            {
                if (_nativeMovingFlat != null) _nativeMovingFlat.Release();
                _nativeMovingFlat = new RenderTexture(src.width, src.width, 0, RenderTextureFormat.RGHalf)
                {
                    dimension = TextureDimension.Tex2D, useMipMap = false,
                    filterMode = FilterMode.Point, name = "WireMovingDepthConversionScratch"
                };
                if (!_nativeMovingFlat.Create())
                {
                    _nativeMovingFailed = true;
                    ReleaseNativeMovingResources();
                    return false;
                }
            }
            if (_nativeMovingCopyMat == null)
                _nativeMovingCopyMat = new Material(NativeCopyShader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture dst = near ? _nativeMovingNear : _nativeMovingFar;
            int slice = cube * 6 + face;
            // The source draw and both conversion steps run in-order in the
            // SAME GPU command buffer, avoiding readback, stalls and extra CPU.
            // The direct moving-caster pass drew only a small scissored rect
            // on this 2048px face. Previously we copied and reconverted all
            // 4,194,304 pixels regardless, for EVERY changed light/face.
            // Copy and convert only pixels actually changed. Empty/full-face
            // and non-direct fallbacks stay exactly as before.
            bool partial = touched.width > 0 && touched.height > 0 &&
                touched.x >= 0 && touched.y >= 0 &&
                touched.xMax <= src.width && touched.yMax <= src.height &&
                (touched.width < src.width || touched.height < src.height);
            if (partial)
                _frameCb.CopyTexture(src, slice, 0,
                    touched.x, touched.y, touched.width, touched.height,
                    _nativeMovingFlat, 0, 0, touched.x, touched.y);
            else
                _frameCb.CopyTexture(src, slice, 0, _nativeMovingFlat, 0, 0);
            _frameCb.SetRenderTarget(new RenderTargetIdentifier(dst, 0, CubemapFace.Unknown, slice));
            _frameCb.SetGlobalTexture(NativeMovingSourceId, _nativeMovingFlat);
            _frameCb.SetGlobalFloat(NativeInvRangeId, 1f / Mathf.Max(1f, Range(t)));
            if (partial)
                _frameCb.EnableScissorRect(new Rect(touched.x, touched.y, touched.width, touched.height));
            _frameCb.DrawProcedural(Matrix4x4.identity, _nativeMovingCopyMat, 1, MeshTopology.Triangles, 3);
            if (partial)
                _frameCb.DisableScissorRect();
            return true;
        }
        private void InitNativeMovingSlot(Tracked t, int cube)
        {
            RenderTexture dst = t.Slot < _near ? _nativeMovingNear : _nativeMovingFar;
            if (dst == null) return;
            // Unity converts the clear depth for reversed Z itself: 1 is always far
            // (0 here walled every untouched face in at the near plane).
            const float far = 1f;
            // Clear all faces only when a light starts using a moving map.
            for (int f = 0; f < 6; f++)
            {
                _frameCb.SetRenderTarget(new RenderTargetIdentifier(dst, 0, CubemapFace.Unknown, cube * 6 + f));
                _frameCb.ClearRenderTarget(true, false, Color.clear, far);
            }
        }
        private static readonly int NativeNearId = Shader.PropertyToID("_WireNativeNearMaps");
        private static readonly int NativeFarId = Shader.PropertyToID("_WireNativeFarMaps");
        private static readonly int NativeSourceId = Shader.PropertyToID("_WireNativeDepthSource");
        private static readonly int NativeInvRangeId = Shader.PropertyToID("_WireNativeInvRange");
        private static readonly int NativeActiveId = Shader.PropertyToID("_WireNativeActive");
        private static readonly int NativeRangesId = Shader.PropertyToID("_WireNativeInvRanges");
        private static readonly int NativeNearsId = Shader.PropertyToID("_WireNativeNears");
        private static readonly int VanillaDepthId = Shader.PropertyToID("_WireVanillaDepth");
        private readonly float[] _nativeNears = new float[MaxSlots];

        private void ReleaseNativeResources()
        {
            if (_nativeNearMaps != null) _nativeNearMaps.Release();
            if (_nativeFarMaps != null) _nativeFarMaps.Release();
            if (_nativeStageNear != null) { _nativeStageNear.Release(); Destroy(_nativeStageNear); }
            if (_nativeStageFar != null) { _nativeStageFar.Release(); Destroy(_nativeStageFar); }
            _nativeStageNear = _nativeStageFar = null;
            if (_nativeNearFlat != null) _nativeNearFlat.Release();
            if (_nativeFarFlat != null) _nativeFarFlat.Release();
            _nativeNearMaps = _nativeFarMaps = null;
            _nativeNearFlat = _nativeFarFlat = null;
            System.Array.Clear(_nativeFaceMasks, 0, MaxSlots);
            System.Array.Clear(_nativeOwners, 0, MaxSlots);
            System.Array.Clear(_nativeActive, 0, MaxSlots);
            Shader.SetGlobalFloatArray(NativeActiveId, _nativeActive);
        }

        internal static void RequestNativeRefresh()
        {
            ShadowCache c = Instance;
            if (c == null) return;
            c.ReleaseNativeResources();
            c._nativeFailed = false;
            c._nativeReportedReady = false;
            if (Plugin.NativeDepthPcf.Value)
            {
                foreach (Tracked t in c._slots)
                {
                    if (t == null || t.Light == null) continue;
                    t.Dirty = true;
                    t.DirtyAt = Time.time;
                }
                c.WakeRenders();
            }
            c.UpdateGlobals();
        }

        private bool EnsureNativeTier(bool near, int size)
        {
            if (_nativeFailed || !Plugin.NativeDepthPcf.Value || NativeCopyShader == null) return false;
            if (!SupportsShadowmap())
            {
                _nativeFailed = true;
                Plugin.Log.LogWarning("[native-depth] Shadowmap format unsupported; original WLO retained.");
                return false;
            }
            int tierslots = near ? _near : Mathf.Max(1, _slots.Length - _near);
            RenderTexture target = near ? _nativeNearMaps : _nativeFarMaps;
            if (target != null && target.IsCreated() && target.width == size &&
                target.volumeDepth == tierslots * 6) return true;
            if (target != null) target.Release();
            RenderTexture flat = near ? _nativeNearFlat : _nativeFarFlat;
            if (flat != null) flat.Release();
            target = new RenderTexture(size, size, 24, RenderTextureFormat.Shadowmap)
            {
                dimension = TextureDimension.CubeArray,
                volumeDepth = tierslots * 6, useMipMap = false,
                filterMode = FilterMode.Bilinear,
                name = near ? "WireNativeNearComparison" : "WireNativeFarComparison"
            };
            flat = new RenderTexture(size, size, 0, RenderTextureFormat.RHalf)
            {
                dimension = TextureDimension.Tex2D,
                useMipMap = false, filterMode = FilterMode.Point,
                name = near ? "WireNativeNearCopy" : "WireNativeFarCopy"
            };
            if (!target.Create() || !flat.Create())
            {
                target.Release(); flat.Release();
                _nativeFailed = true;
                Plugin.Log.LogWarning("[native-depth] Comparison cube array allocation unsupported; original WLO retained.");
                ReleaseNativeResources();
                return false;
            }
            if (near) { _nativeNearMaps = target; _nativeNearFlat = flat; }
            else { _nativeFarMaps = target; _nativeFarFlat = flat; }
            for (int s = near ? 0 : _near; s < (near ? _near : _slots.Length); s++)
            {
                _nativeFaceMasks[s] = 0;
                _nativeOwners[s] = null;
            }
            Shader.SetGlobalTexture(near ? NativeNearId : NativeFarId, target);
            long bytes = (long)tierslots * 6 * size * size * 4;
            Plugin.Log.LogInfo("[native-depth] " + (near ? "NEAR" : "FAR") +
                " array allocated: " + tierslots + " slots, " + size +
                "px faces, ~" + (bytes / 1048576) + " MiB depth.");
            return true;
        }

        private void CacheNativeFace(Tracked t, RenderTexture combined, int slice, bool near)
        {
            if (!Plugin.NativeDepthPcf.Value || t.Slot < 0 ||
                !EnsureNativeTier(near, combined.width)) return;
            int sl = t.Slot;
            RenderTexture flat = near ? _nativeNearFlat : _nativeFarFlat;
            RenderTexture dst = NativeRenderTarget(t, near);
            int cube = t.Staged && dst != (near ? _nativeNearMaps : _nativeFarMaps) ? 0 : (near ? sl : sl - _near);
            if (_nativeOwners[sl] != t)
            {
                _nativeOwners[sl] = t;
                _nativeFaceMasks[sl] = 0;
            }
            if (t.Face == 0 && !t.Ready)
                _nativeFaceMasks[sl] = 0;
            Graphics.CopyTexture(combined, slice, 0, flat, 0, 0);
            if (_nativeCopyMat == null)
                _nativeCopyMat = new Material(NativeCopyShader) { hideFlags = HideFlags.HideAndDontSave };
            if (_nativeCopyCb == null)
                _nativeCopyCb = new CommandBuffer { name = "WLO All-Light Native Depth Conversion" };
            _nativeCopyCb.Clear();
            _nativeCopyCb.SetRenderTarget(new RenderTargetIdentifier(dst, 0, CubemapFace.Unknown, cube * 6 + t.Face));
            _nativeCopyCb.SetGlobalTexture(NativeSourceId, flat);
            t.NativeCacheRange = Mathf.Max(1f, Range(t));
            _nativeCopyCb.SetGlobalFloat(NativeInvRangeId, 1f / t.NativeCacheRange);
            _nativeCopyCb.DrawProcedural(Matrix4x4.identity, _nativeCopyMat, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_nativeCopyCb);
            if (!t.Staged) _nativeFaceMasks[sl] |= 1 << t.Face;
            UpdateGlobals();
        }
        private static readonly int FrontId = Shader.PropertyToID("_WireFront");
        private static readonly int BackId = Shader.PropertyToID("_WireBack");

        private static RenderTexture FaceRT(int res, int depth, bool cube, string name)
        {
            var rt = new RenderTexture(res, res, depth, RenderTextureFormat.RHalf)
            {
                dimension = cube ? TextureDimension.Cube : TextureDimension.Tex2D,
                useMipMap = false,
                filterMode = FilterMode.Point,
                name = name
            };
            rt.Create();
            return rt;
        }

        // A staged re-render that won't finish (slot lost, item started rolling).
        private void AbortStage(Tracked t)
        {
            if (!t.Staged)
                return;
            for (int i = 0; i < 2; i++)
                if (_stageOwner[i] == t) _stageOwner[i] = null;
            t.Staged = false;
            t.NativeStaged = false;
        }

        private static RenderTexture StageRT(int res, string name)
        {
            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.RHalf)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6, useMipMap = false,
                filterMode = FilterMode.Point, name = name
            };
            rt.Create();
            return rt;
        }

        private void ReleaseFaces()
        {
            foreach (RenderTexture rt in new[] { _faceBack, _farFaceBack, _flatFront, _flatBack, _farFlatFront, _farFlatBack, _stage, _farStage })
                if (rt != null) rt.Release();
            _stageOwner[0] = _stageOwner[1] = null;
        }
        private int _farResolution;
        private static readonly int FarMapsId = Shader.PropertyToID("_WireFarMaps");
        private static readonly int NearCountId = Shader.PropertyToID("_WireNearCount");
        private Camera _camera;
        private int _resolution;
        private bool _rendering;

        internal static int SlotCount => Mathf.Clamp(Plugin.Slots.Value, 1, NearMax);
        internal static int FarCount => Mathf.Clamp(Plugin.FarSlots.Value, 0, FarMax);
        private int _near; // slots below this use the full-size maps

        // ---- Lights ----

        internal static void Register(LightLod lod) => Register(lod != null ? lod.GetComponent<Light>() : null, lod);

        // Lights LightLod doesn't manage (fairy lights, cauldrons, the
        // artisan table, some items) come in through what holds them.
        internal static void RegisterPiece(Component piece)
        {
            piece.GetComponentsInChildren(true, PieceLights);
            foreach (Light l in PieceLights)
                if (l.GetComponent<LightLod>() == null)
                    Register(l, null);
        }

        internal static void UnregisterPiece(Piece piece)
        {
            piece.GetComponentsInChildren(true, PieceLights);
            foreach (Light l in PieceLights)
                if (l.GetComponent<LightLod>() == null)
                    Unregister(l);
        }

        private static readonly List<Light> PieceLights = new List<Light>();
        private static readonly List<Light> GoneLights = new List<Light>();

        // Drop dictionary entries of lights destroyed without an OnDestroy we hook (items).
        private static void Forget()
        {
            GoneLights.Clear();
            foreach (var kv in ByLight)
                if (kv.Key == null) GoneLights.Add(kv.Key);
            foreach (Light l in GoneLights)
                ByLight.Remove(l);
        }

        // Only the EQUIPPED Wisplight orb is excluded. Wisp Torches and
        // other constructed pieces deliberately retain all their shadows.
        // The orb can contain "Wisp" or "Demister" in its object hierarchy;
        // never rely on the shared Demister component alone (torches use it).
        private static bool IsWispLight(Light light)
        {
            if (light == null || light.GetComponentInParent<Piece>() != null)
                return false;
            for (Transform parent = light.transform; parent != null; parent = parent.parent)
            {
                string n = parent.name;
                if (n.IndexOf("wisplight", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("wisp", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("demister", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
        private static void DisableWispShadows(Light light, LightLod lod)
        {
            if (lod != null) ShadowLod(lod) = false; // Stop vanilla's LightLod from re-enabling them.
            light.shadows = LightShadows.None;
        }

        // The equipped Wisplight orb is not necessarily registered as a
        // LightLod. Its Demister component can be on a child,
        // with the visible light on an ancestor. Cover it on spawn.
        internal static void WispDemisterAwake(Demister demister)
        {
            if (demister == null) return;
            for (Transform p = demister.transform; p != null; p = p.parent)
            {
                Light light = p.GetComponent<Light>();
                if (light != null && IsWispLight(light))
                {
                    DisableWispShadows(light, light.GetComponent<LightLod>());
                    return;
                }
            }
        }

        private static void Register(Light light, LightLod lod)
        {
            // Placement ghosts are made with network init switched off.
            if (light == null || light.type != LightType.Point || ZNetView.m_forceDisableInit || ByLight.ContainsKey(light))
                return;
            if (IsWispLight(light))
            {
                // No WLO slots, cached maps, moving maps or native Unity shadows.
                // Keep the actual light fully enabled for illumination.
                DisableWispShadows(light, lod);
                return;
            }
            // Lights carried by a character or on a ship or cart would need a
            // new map every step: they stay as in vanilla. Item glows (dropped,
            // on stands) only take spare slots.
            if (light.GetComponentInParent<Character>() != null)
            {
                MovingLight(light, true, "carried");
                return;
            }
            bool tiny = light.GetComponentInParent<ItemDrop>() != null || light.GetComponentInParent<ItemStand>() != null ||
                        light.GetComponentInParent<ArmorStand>() != null;
            Rigidbody body = light.GetComponentInParent<Rigidbody>();
            if (!tiny && body != null && !body.isKinematic)
                return;
            var t = new Tracked
            {
                Light = light, Lod = lod, Tiny = tiny, Body = body != null && !body.isKinematic ? body : null,
                OriginalShadows = light.shadows,
                OriginalShadowLod = lod != null && ShadowLod(lod),
                OriginalShadowStrength = light.shadowStrength,
                OriginalShadowNearPlane = light.shadowNearPlane,
                Sconce = IsSconce(light)
            };
            Lights.Add(t);
            ByLight[light] = t;
            _reassignSoon = true;
            // No LightLod to tell us it switched on: watch it ourselves.
            if (lod == null && light.GetComponent<LightWatch>() == null)
                light.gameObject.AddComponent<LightWatch>().Light = light;
            if (Instance != null && Instance._active)
            {
                if (Plugin.CompareGame.Value && t.Sconce) EnableNativeSconceShadows(t);
                else Suppress(t);
            }
        }

        internal static void Unregister(LightLod lod) => Unregister(lod != null ? lod.GetComponent<Light>() : null);

        private static void Unregister(Light light)
        {
            if (ReferenceEquals(light, null) || !ByLight.TryGetValue(light, out Tracked t))
                return;
            if (Instance != null)
                Instance.Release(t);
            DisableNativeSconceShadows(t);
            ByLight.Remove(light);
            Lights.Remove(t);
        }

        // A light switched on (a fire lit, LightLod waking it): slot it now
        // rather than at the next Reassign, so it never shows unshadowed.
        internal static void LightEnabled(Light light)
        {
            if (Instance == null || !Instance._active || light == null)
                return;
            // Lights appearing as the destination streams in must never
            // individually trigger a full Reassign + six synchronous face
            // renders. Queue one shared reassignment on the existing frame.
            if (Loading() || TeleportStreaming())
            {
                _reassignSoon = true;
                return;
            }
            if (!ByLight.TryGetValue(light, out Tracked t))
            {
                Check("switched on, not tracked: " + NameOf(light));
                return;
            }
            Check("switched on" + (t.Lod == null ? " (no LightLod)" : "") + ": " + NameOf(light) + (t.Slot >= 0 ? " (had a slot)" : ""));
            if (Plugin.CompareGame.Value && t.Sconce)
            {
                EnableNativeSconceShadows(t);
                return;
            }
            ShadowCache c = Instance;
            if (t.Slot < 0)
            {
                Player player = Player.m_localPlayer;
                if (player == null || Reach(t, player.transform.position) > Plugin.Distance.Value)
                    return;
                // Just lit (a refilled hearth): first in line for a slot for a
                // moment, then ranked as usual.
                t.LitAt = Time.time;
                c.Reassign();
                if (t.Slot < 0)
                    return;
            }
            // Its map is there from the first lit frame. LightLod grows the
            // light's range over a second, at the same rate as it fades its
            // shadows in: these start where the range is. A light switched off
            // and on again keeps its full range (LightLod's loop stops while
            // it's off), so it snaps on, shadows included.
            t.Instant = true;
            if (!t.Ready || t.Dirty || t.Face >= 0)
                c.RenderNow(t);
            t.Instant = false;
            float baseRange = t.Lod != null && t.Lod.m_lightLod ? LodBaseRange(t.Lod) : 0f;
            t.Fade = baseRange > 0f ? Mathf.Clamp01(light.range / baseRange) : 1f;
            Check("shadows " + (t.Fade < 1f ? "fade in with the light: " : "on at once: ") + NameOf(light));
            c.UpdateGlobals();
        }

        private static readonly AccessTools.FieldRef<LightLod, float> LodBaseRange =
            AccessTools.FieldRefAccess<LightLod, float>("m_baseRange");

        // Lights that move every frame (a torch in a hand, a rolling glowing
        // item, the building preview's light): the game's own per-frame
        // shadows, as a vanilla shadowed light. A near plane keeps the light's
        // own item or bracket out of them. Carried: only torches, a weapon's
        // aura shouldn't shade the weapon.
        internal static void MovingLight(Light light, bool on, string why)
        {
            if (light == null || light.type != LightType.Point)
                return;
            // A carried/rolling/preview wisp may reach this path without
            // LightLod. Never let the moving-light handler add shadows.
            if (IsWispLight(light))
            {
                DisableWispShadows(light, light.GetComponent<LightLod>());
                return;
            }
            if (on && (!Plugin.MovingLightShadows.Value || (why == "carried" && !IsTorch(light))))
                return;
            light.shadows = on ? LightShadows.Soft : LightShadows.None;
            if (on)
            {
                // Past the light's own item (a whole axe for a rolling one).
                float own = 0.3f;
                if (why == "rolling")
                {
                    Transform root = light.transform.root;
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                        if (!(r is ParticleSystemRenderer))
                            own = Mathf.Max(own, Vector3.Distance(light.transform.position, r.bounds.center) + r.bounds.extents.magnitude);
                }
                light.shadowNearPlane = Mathf.Max(light.shadowNearPlane, own);
                light.shadowStrength = 1f;
            }
            Check("moving light " + (on ? "gets" : "drops") + " shadows (" + why + "): " + NameOf(light));
        }

        private static bool IsTorch(Light light)
        {
            for (Transform p = light.transform; p != null; p = p.parent)
                if (p.name.IndexOf("torch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // Doors and shutters: while one swings, it's left out of the cached
        // maps and drawn into the moving maps every frame, so its shadow
        // turns with it. Once it has stopped, the cached maps take it back.
        private sealed class Swing
        {
            internal Door Door;
            internal Animator Animator;
            internal Renderer[] Renderers;
            internal Vector3 Center;
            internal float Radius;
            internal float Until;
            internal bool Ending;
        }

        private static readonly List<Swing> Swings = new List<Swing>();
        private static readonly HashSet<Renderer> SwingHidden = new HashSet<Renderer>();
        private const float SwingSeconds = 0.4f;

        // Debug view: the slotted light nearest the middle of the screen.
        private Tracked _debugLight;

        private void PickDebugLight()
        {
            Camera cam = Utils.GetMainCamera();
            if (cam == null)
                return;
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            Tracked best = null;
            float bestD = 3f;
            foreach (Tracked t in _slots)
            {
                if (t == null || !t.Ready || t.Light == null)
                    continue;
                Vector3 to = t.Light.transform.position - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0.5f || along > 40f)
                    continue;
                float d = Vector3.Cross(ray.direction, to).magnitude / along; // angle-ish: off-centre per metre
                if (d < bestD) { bestD = d; best = t; }
            }
            Shader.SetGlobalFloat("_WireDebugSlot", best != null ? best.Slot : -2);
            if (best != _debugLight && best != null)
            {
                Plugin.Log.LogInfo("[debug] " + best.Light.transform.root.name + "/" + best.Light.name + " slot " + best.Slot +
                    (best.Slot < _near ? " near" : " far") + " center " + best.Center.ToString("F2") + " light " + best.Light.transform.position.ToString("F2") +
                    " range " + Range(best).ToString("F1") + " nearPlane " + NearPlane(best).ToString("F2") + " ownShades " + OwnShades(best) +
                    " tiny " + best.Tiny + " hidden " + best.Hide.Count + " moving map " + (_dynActive[best.Slot] > 0.5f ? "in use" : "idle"));
            }
            _debugLight = best;
        }

        // The building preview follows the cursor: drawn as a moving caster.
        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private GameObject _ghost;
        private Renderer[] _ghostRenderers = new Renderer[0];
        private Vector3 _ghostLocalCenter, _ghostCenter;
        private float _ghostRadius;
        private int _ghostHash;
        private bool _ghostActive;

        private void UpdateGhost()
        {
            Player p = Player.m_localPlayer;
            GameObject g = p != null && Plugin.GhostShadows.Value ? PlacementGhost(p) : null;
            _ghostActive = g != null && g.activeInHierarchy;
            if (!_ghostActive)
                return;
            if (g != _ghost)
            {
                _ghost = g;
                _ghostRenderers = System.Array.FindAll(g.GetComponentsInChildren<Renderer>(),
                    r => !(r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer));
                if (_ghostRenderers.Length == 0)
                {
                    _ghostActive = false;
                    return;
                }
                Bounds b = _ghostRenderers[0].bounds;
                foreach (Renderer r in _ghostRenderers) b.Encapsulate(r.bounds);
                _ghostLocalCenter = g.transform.InverseTransformPoint(b.center);
                _ghostRadius = b.extents.magnitude + 0.3f;
            }
            if (_ghostRenderers.Length == 0)
            {
                _ghostActive = false;
                return;
            }
            Transform gt = g.transform;
            _ghostCenter = gt.TransformPoint(_ghostLocalCenter);
            _ghostHash = Quantize(gt.position, 50f) * 31 + Quantize(gt.eulerAngles, 0.5f);
        }

        internal static void DoorMoved(Door door)
        {
            ShadowCache c = Instance;
            if (c == null || !c._active)
                return;
            if (!Plugin.MovingCasters.Value || !Plugin.DirectMovingDraw.Value)
            {
                PieceChanged(door.transform.position, 1.2f, door);
                return;
            }
            Swing w = Swings.Find(x => x.Door == door);
            if (w == null)
            {
                Renderer[] rs = System.Array.FindAll(door.GetComponentsInChildren<Renderer>(),
                    r => r.shadowCastingMode != ShadowCastingMode.Off && !SeeThrough(r));
                if (rs.Length == 0)
                    return;
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                w = new Swing { Door = door, Animator = door.GetComponentInChildren<Animator>(), Renderers = rs, Center = b.center, Radius = b.extents.magnitude + 0.5f };
                Swings.Add(w);
            }
            w.Until = Time.time + SwingSeconds;
            w.Ending = false;
            foreach (Renderer r in w.Renderers) SwingHidden.Add(r);
            // Queue only affected faces on nearby lights. A bounded render
            // budget prevents synchronous GPU stalls when a gate is opened.
            for (int sl = 0; sl < c._slots.Length; sl++)
            {
                Tracked t = c._slots[sl];
                if (t == null || t.Light == null || !Reaches(t, w))
                    continue;
                if (!t.Ready || t.Face >= 0 || t.Dirty)
                {
                    // A full map is already queued/in progress; make sure
                    // that pass accounts for the now-moving door.
                    t.DoorFaces = 0;
                    t.DoorPrepared = false;
                    t.Dirty = true;
                    t.DirtyAt = Time.time;
                    continue;
                }
                // Don't render GPU shadow maps from Door.SetState. Queue
                // affected faces and process them across subsequent frames.
                int faces = FacesFor(w.Center - RestingPosition(t.Light), w.Radius);
                t.DoorFaces |= faces;
                t.DoorPrepared = false;
                Check("door moved, queued faces: " + NameOf(t.Light) + " (" + CountBits(faces) + ")");
            }
            c.WakeRenders();
        }

        // Items lying around: in the moving maps (they get picked up, kicked
        // and roll), redrawn only when one has moved. A glowing item doesn't
        // shade its own glow.
        private const float ItemRadius = 0.8f;
        private sealed class DropCaster
        {
            internal ItemDrop Item;
            internal Rigidbody Body;
            internal Renderer[] Renderers;
            internal Vector3 StaticPosition;
            internal Quaternion StaticRotation;
            internal Vector3 CurrentPosition;
            internal Quaternion CurrentRotation;
            internal float LastMotion;
            internal bool Stable; // pose is stationary; moving-map face stays cached
            internal bool Mounted;
            internal bool Handoff; // legacy flag; dropped items now live in cached moving maps
        }
        private static readonly Dictionary<ItemDrop, DropCaster> Drops = new Dictionary<ItemDrop, DropCaster>();
        // Legacy handoff list stays empty now that settled drops use moving-map caching.
        private static readonly List<DropCaster> PendingDrops = new List<DropCaster>();
        private static readonly HashSet<Renderer> DropVisuals = new HashSet<Renderer>();
        private static readonly List<ItemDrop> LostDrops = new List<ItemDrop>();
        // A pickup changes a cached moving face NOW, not at its normal distant LOD cadence.
        private static int _dropChangedFrame = -100;
        // One-shot per-light face invalidation: robust even if two item poses
        // happen to collide in the hash. Only written on actual pickup.
        private readonly int[] _pickupInvalidFaces = new int[MaxSlots];
        private static void MarkDropFaces(Vector3 pos)
        {
            ShadowCache c = Instance;
            if (c == null || !c._active) return;
            for (int sl = 0; sl < c._slots.Length; sl++)
            {
                Tracked t = c._slots[sl];
                if (t == null || t.Light == null || !t.Ready) continue;
                float reach = Range(t) + ItemRadius;
                if ((t.Center - pos).sqrMagnitude <= reach * reach)
                    c._pickupInvalidFaces[sl] |= FacesFor(pos - t.Center, ItemRadius);
            }
        }

        internal static readonly List<ItemDrop> Items = new List<ItemDrop>();

        // Rigidbody state is inspected once per nearby item per frame, not
        // once for every light. Meshes and renderers are discovered only once.
        private static DropCaster GetDrop(ItemDrop it, Vector3 pos)
        {
            if (Drops.TryGetValue(it, out DropCaster d)) return d;
            d = new DropCaster
            {
                Item = it, Body = it.GetComponent<Rigidbody>(),
                Mounted = it.GetComponentInParent<ItemStand>() != null,
                CurrentPosition = pos, StaticPosition = pos,
                CurrentRotation = it.transform.rotation,
                StaticRotation = it.transform.rotation,
                LastMotion = Time.time
            };
            d.Renderers = System.Array.FindAll(it.GetComponentsInChildren<Renderer>(true),
                r => !(r is ParticleSystemRenderer || r is TrailRenderer ||
                    r is LineRenderer) && !SeeThrough(r));
            if (!d.Mounted)
                foreach (Renderer r in d.Renderers) DropVisuals.Add(r);
            Drops[it] = d;
            _dropChangedFrame = Time.frameCount;
            return d;
        }

        private void UpdateDropHandoffs()
        {
            for (int i = PendingDrops.Count - 1; i >= 0; i--)
            {
                DropCaster d = PendingDrops[i];
                if (!d.Handoff || d.Item == null)
                {
                    PendingDrops.RemoveAt(i);
                    continue;
                }
                bool waiting = false;
                Vector3 c = d.Stable ? d.StaticPosition : d.CurrentPosition;
                foreach (Tracked t in _slots)
                {
                    if (t == null || t.Light == null) continue;
                    float reach = Range(t) + ItemRadius;
                    if ((t.Center - c).sqrMagnitude <= reach * reach &&
                        (t.Dirty || t.Face >= 0 || t.DoorFaces != 0))
                    {
                        waiting = true;
                        break;
                    }
                }
                if (!waiting)
                {
                    d.Handoff = false;
                    PendingDrops.RemoveAt(i);
                }
            }
        }

        // Removal invalidates affected persistent moving-map faces; never
        // wait on a scheduled static-cubemap rebuild for a picked-up item.
        internal static void DropRemoved(ItemDrop item)
        {
            if (System.Object.ReferenceEquals(item, null) || !Drops.TryGetValue(item, out DropCaster d))
            {
                Items.Remove(item);
                return;
            }
            _dropChangedFrame = Time.frameCount;
            MarkDropFaces(d.CurrentPosition);
            if (d.Renderers != null)
                foreach (Renderer r in d.Renderers) DropVisuals.Remove(r);
            PendingDrops.Remove(d);
            Drops.Remove(item);
            Items.Remove(item);
        }

        private static void CleanupDroppedItems()
        {
            LostDrops.Clear();
            foreach (var pair in Drops)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled)
                {
                    DropCaster d = pair.Value;
                    // The moving shadow cache notices the removed group immediately.
                    _dropChangedFrame = Time.frameCount;
                    MarkDropFaces(d.CurrentPosition);
                    foreach (Renderer r in d.Renderers) DropVisuals.Remove(r);
                    PendingDrops.Remove(d);
                    LostDrops.Add(pair.Key);
                }
            foreach (ItemDrop it in LostDrops) Drops.Remove(it);
            LostDrops.Clear();
        }

        private static int CountBits(int v) { int n = 0; for (; v != 0; v &= v - 1) n++; return n; }

        private static bool Swinging(Animator a)
        {
            if (a == null || !a.isActiveAndEnabled)
                return false;
            return a.IsInTransition(0) || a.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f;
        }

        private static bool Reaches(Tracked t, Swing w)
        {
            float reach = Range(t) + w.Radius;
            return (t.Center - w.Center).sqrMagnitude < reach * reach;
        }

        private void UpdateSwings()
        {
            for (int i = Swings.Count - 1; i >= 0; i--)
            {
                Swing w = Swings[i];
                if (w.Door == null)
                {
                    foreach (Renderer r in w.Renderers) SwingHidden.Remove(r);
                    Swings.RemoveAt(i);
                    continue;
                }
                if (!w.Ending)
                {
                    // Lasts as long as the door's own animation (a big gate
                    // swings longer than a shutter); the timer is the minimum.
                    if (Time.time < w.Until || Swinging(w.Animator))
                        continue;
                    // Stopped: put it back into the cached maps.
                    w.Ending = true;
                    foreach (Renderer r in w.Renderers) SwingHidden.Remove(r);
                    for (int sl = 0; sl < _slots.Length; sl++)
                    {
                        Tracked t = _slots[sl];
                        if (t == null || t.Light == null || !Reaches(t, w))
                            continue;
                        // Door is stationary again; supersede any incomplete
                        // removal faces with a full stable shadow-map update.
                        t.DoorFaces = 0;
                        t.DoorPrepared = false;
                        t.Dirty = true;
                        t.DirtyAt = Time.time;
                    }
                    WakeRenders();
                    continue;
                }
                // Keep drawing it as moving until those maps have it again,
                // so there's no frame without its shadow.
                bool waiting = false;
                for (int sl = 0; sl < _slots.Length && !waiting; sl++)
                {
                    Tracked t = _slots[sl];
                    waiting = t != null && t.Light != null && (t.Dirty || t.Face >= 0) && Reaches(t, w);
                }
                if (!waiting)
                    Swings.RemoveAt(i);
            }
        }

        // Mounted items are permanent static-cache casters. For a brief
        // face-queued handoff after SetVisualItem, the moving map bridges
        // their new silhouette until the affected static faces are ready.
        private sealed class StandCaster
        {
            internal ItemStand Stand;
            internal Renderer[] Renderers;
            internal Caster[] Geometry;
            internal Vector3 Center;
            internal float Radius;
            internal int Version;
            internal bool Handoff;
            internal int ChangedFrame;
        }
        private static readonly List<StandCaster> Stands = new List<StandCaster>();
        // Event-driven: empty when all stand shadows are cached.
        private static readonly List<StandCaster> PendingStands = new List<StandCaster>();
        private static readonly HashSet<Renderer> StandVisuals = new HashSet<Renderer>();
        private static int _standChangedFrame = -100;

        internal static void RefreshStand(ItemStand stand)
        {
            if (stand == null) return;
            StandCaster entry = Stands.Find(s => s.Stand == stand);
            if (entry == null)
            {
                entry = new StandCaster { Stand = stand, Version = stand.GetInstanceID() };
                Stands.Add(entry);
            }
            Vector3 oldCenter = entry.Center;
            float oldRadius = entry.Radius;
            bool hadVisual = entry.Geometry != null && entry.Geometry.Length > 0;
            if (entry.Renderers != null)
                foreach (Renderer r in entry.Renderers) StandVisuals.Remove(r);
            GameObject visual = ItemStandVisual.CurrentItem(stand);
            if (visual == null)
            {
                entry.Renderers = new Renderer[0];
                entry.Geometry = new Caster[0];
                entry.Center = stand.transform.position;
                entry.Radius = 0.5f;
            }
            else
            {
                entry.Renderers = System.Array.FindAll(
                    visual.GetComponentsInChildren<Renderer>(true),
                    r => r != null && !(r is ParticleSystemRenderer ||
                        r is TrailRenderer || r is LineRenderer) && !SeeThrough(r));
                Bounds b = default;
                bool first = true;
                var cached = new List<Caster>(entry.Renderers.Length);
                foreach (Renderer r in entry.Renderers)
                {
                    StandVisuals.Add(r);
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                    int subs = r is SkinnedMeshRenderer smr && smr.sharedMesh != null
                        ? smr.sharedMesh.subMeshCount :
                        r.GetComponent<MeshFilter>() is MeshFilter mf &&
                            mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                    Bounds rb = r.bounds;
                    cached.Add(new Caster { R = r, B = rb, Radius = rb.extents.magnitude, Subs = subs });
                }
                entry.Geometry = cached.ToArray();
                entry.Center = first ? stand.transform.position : b.center;
                entry.Radius = first ? 0.5f : b.extents.magnitude + 0.5f;
            }
            entry.Version++;
            entry.ChangedFrame = _standChangedFrame = Time.frameCount;
            // Rebuild affected cached cube faces through the same bounded
            // face queue used for gates. Moving shadows bridge the handoff.
            if (hadVisual) QueueStaticItemChange(oldCenter, oldRadius);
            if (entry.Geometry.Length > 0)
            {
                QueueStaticItemChange(entry.Center, entry.Radius);
                entry.Handoff = true;
            }
            else entry.Handoff = false;
            if (entry.Handoff)
            {
                if (!PendingStands.Contains(entry)) PendingStands.Add(entry);
            }
            else PendingStands.Remove(entry);
        }

        private static void UnregisterStand(ItemStand stand)
        {
            for (int i = Stands.Count - 1; i >= 0; i--)
            {
                StandCaster s = Stands[i];
                if (s.Stand != stand) continue;
                if (s.Renderers != null)
                    foreach (Renderer r in s.Renderers) StandVisuals.Remove(r);
                QueueStaticItemChange(s.Center, s.Radius);
                PendingStands.Remove(s);
                Stands.RemoveAt(i);
                _standChangedFrame = Time.frameCount;
            }
        }

        // Queue only cubemap faces touched by a changed item, like a gate.
        // Never render GPU faces on the mount/drop event itself.
        private static void QueueStaticItemChange(Vector3 center, float radius)
        {
            ShadowCache c = Instance;
            if (c == null || !c._active) return;
            foreach (Tracked t in c._slots)
            {
                if (t == null || t.Light == null) continue;
                float reach = Range(t) + radius;
                if ((t.Center - center).sqrMagnitude > reach * reach) continue;
                int faces = FacesFor(center - t.Center, radius);
                if (t.Face >= 0)
                {
                    c.AbortStage(t);
                    t.Face = -1;
                    t.Dirty = true; // redo full map if the earlier pass is incomplete
                }
                else if (t.Ready && !t.Dirty)
                    t.DoorFaces |= faces;
                else t.Dirty = true;
                t.DirtyAt = Time.time;
                t.DoorPrepared = false;
            }
            c.WakeRenders();
        }

        private void UpdateStandHandoffs()
        {
            for (int i = PendingStands.Count - 1; i >= 0; i--)
            {
                StandCaster s = PendingStands[i];
                if (!s.Handoff) { PendingStands.RemoveAt(i); continue; }
                if (Time.frameCount <= s.ChangedFrame) continue;
                bool waiting = false;
                foreach (Tracked t in _slots)
                {
                    if (t == null || t.Light == null) continue;
                    float reach = Range(t) + s.Radius;
                    if ((t.Center - s.Center).sqrMagnitude <= reach * reach &&
                        (t.Dirty || t.Face >= 0 || t.DoorFaces != 0))
                    {
                        waiting = true;
                        break;
                    }
                }
                if (!waiting)
                {
                    s.Handoff = false;
                    PendingStands.RemoveAt(i);
                }
            }
        }

        // Building previews are pieces too (made with network init off): they
        // never change anyone's shadows.
        private static readonly HashSet<Piece> Ghosts = new HashSet<Piece>();

        internal static void PieceAwake(Piece piece)
        {
            if (ZNetView.m_forceDisableInit)
            {
                Ghosts.Add(piece);
                return;
            }
            RegisterPiece(piece);
            ItemStand stand = piece.GetComponentInChildren<ItemStand>(true);
            if (stand != null) RefreshStand(stand);
            PieceChanged(piece.transform.position, piece);
        }

        internal static void PieceGone(Piece piece)
        {
            if (Ghosts.Remove(piece))
                return;
            UnregisterPiece(piece);
            ItemStand stand = piece.GetComponentInChildren<ItemStand>(true);
            if (stand != null) UnregisterStand(stand);
            PieceChanged(piece.transform.position, piece);
        }

        // A building piece appeared or went away: lights whose range reaches it re-render.
        internal static void PieceChanged(Vector3 position, Object who) => PieceChanged(position, SettleSeconds, who);

        // Building pieces can be long (iron bars, beams). Their pivot may be
        // outside a light's range even while their shadow-casting mesh reaches
        // inside it. Gather the bounds only on a geometry-change event.
        private static readonly List<Renderer> ChangedPieceRenderers = new List<Renderer>(16);

        internal static void PieceChanged(Vector3 position, float settle, Object who)
        {
            ShadowCache cache = Instance;
            if (cache == null)
                return;
            Bounds affected = default;
            bool hasBounds = false;
            if (who is Component component)
            {
                ChangedPieceRenderers.Clear();
                component.GetComponentsInChildren(true, ChangedPieceRenderers);
                foreach (Renderer renderer in ChangedPieceRenderers)
                {
                    if (renderer == null || renderer.shadowCastingMode == ShadowCastingMode.Off)
                        continue;
                    if (!hasBounds) { affected = renderer.bounds; hasBounds = true; }
                    else affected.Encapsulate(renderer.bounds);
                }
                ChangedPieceRenderers.Clear();
                // Ground-only pieces have no meshes to invalidate.
                if (!hasBounds && component.GetComponentInChildren<Renderer>() == null)
                    return;
            }
            bool dirtied = false;
            foreach (Tracked t in cache._slots)
            {
                if (t == null || t.Light == null)
                    continue;
                float reach = Range(t) + 4f;
                Vector3 nearest = hasBounds ? affected.ClosestPoint(t.Center) : position;
                if ((t.Center - nearest).sqrMagnitude >= reach * reach)
                    continue;
                // A change during a partially built six-face map invalidates
                // that entire build. Never publish a mix of old/new geometry.
                if (t.Face >= 0)
                {
                    cache.AbortStage(t);
                    t.Face = -1;
                }
                t.DoorFaces = 0;
                t.DoorPrepared = false;
                t.Dirty = true;
                t.DirtyAt = Time.time + settle;
                dirtied = true;
            }
            if (dirtied) cache.WakeRenders();
        }

        // ---- Setup ----

        private void Awake()
        {
            Instance = this;
            InvokeRepeating(nameof(Reassign), 1f, 1f);
            InvokeRepeating(nameof(ForgetGoneCharacters), 10f, 10f);
        }

        private void OnDestroy()
        {
            _active = false;
            ReleaseAll();
            foreach (Tracked t in Lights)
                Unsuppress(t);
            ReleaseNativeResources();
            _nativeCopyCb?.Release();
            ReleaseNativeMovingResources();
            ReleaseFabric();
            ReleaseOwnPass();
            if (_nativeCopyMat != null) Destroy(_nativeCopyMat);            if (_maps != null) _maps.Release();
            if (_face != null) _face.Release();
            if (_farMaps != null) _farMaps.Release();
            if (_farFace != null) _farFace.Release();
            ReleaseFaces();
            if (_dynMaps != null) _dynMaps.Release();
            if (_dynFace != null) _dynFace.Release();

            if (_farDynMaps != null) _farDynMaps.Release();

            _frameCb?.Release();
            Stands.Clear();
            PendingStands.Clear();
            StandVisuals.Clear();
            Drops.Clear();
            PendingDrops.Clear();
            DropVisuals.Clear();
            LostDrops.Clear();
            if (_camera != null) Destroy(_camera.gameObject);
            if (_dynCamera != null) Destroy(_dynCamera.gameObject);
            Instance = null;
        }

        private static readonly List<Character> GoneCharacterScratch = new List<Character>(16);
        private void ForgetGoneCharacters()
        {
            // Reuse the same cleanup list instead of allocating one on each
            // existing 10-second cleanup event in long play sessions.
            if (CharacterRenderers.Count == 0) return;
            GoneCharacterScratch.Clear();
            foreach (var kv in CharacterRenderers)
                if (kv.Key == null) GoneCharacterScratch.Add(kv.Key);
            foreach (Character c in GoneCharacterScratch)
                CharacterRenderers.Remove(c);
        }

        private bool EnsureResources()
        {
            // Following the game: what Unity gives a point light near you at the
            // current shadow quality (its reach fills the screen, capped at
            // 1024; only Low comes out at 512). Distant lights a quarter.
            bool follow = Plugin.FollowGameShadows.Value;
            int game = QualitySettings.shadowResolution == ShadowResolution.Low ? 512 : 1024;
            int res = follow ? game : Mathf.ClosestPowerOfTwo(Mathf.Clamp(Plugin.Resolution.Value, 64, 1024));
            int slots = SlotCount, far = FarCount;
            int farRes = follow ? game / 4 : Mathf.ClosestPowerOfTwo(Mathf.Clamp(Plugin.FarResolution.Value, 64, 1024));
            int dynRes = Mathf.Min(2048, res * (Plugin.MovingResolutionExperiment.Value ? 2 : 1));
            if (_maps != null && _resolution == res && _farResolution == farRes && _near == slots && _slots.Length == slots + far &&
                _dynMaps != null && _dynMaps.width == dynRes &&
                _farDynMaps != null && _farDynMaps.width == dynRes)
                return true;
            ReleaseAll();
            ReleaseNativeResources();
            if (_maps != null) _maps.Release();
            if (_face != null) _face.Release();
            if (_farMaps != null) _farMaps.Release();
            if (_farFace != null) _farFace.Release();
            _resolution = res;
            _farResolution = farRes;
            _near = slots;
            _slots = new Tracked[slots + far];
            _dynNearCapacity = Mathf.Min(InitialMovingCubes, slots);
            _dynFarCapacity = Mathf.Min(InitialMovingCubes, Mathf.Max(1, far));
            ResetDynPool();

            // At least one slice so the shader always has a texture bound.
            _farMaps = new RenderTexture(farRes, farRes, 0, RenderTextureFormat.RHalf)
            {
                dimension = TextureDimension.CubeArray,
                volumeDepth = 6 * Mathf.Max(1, far),
                useMipMap = false,
                filterMode = FilterMode.Point,
                name = "WireFarShadowMaps"
            };
            _farFace = new RenderTexture(farRes, farRes, 16, RenderTextureFormat.RHalf)
            {
                dimension = TextureDimension.Cube,
                useMipMap = false,
                filterMode = FilterMode.Point,
                name = "WireFarShadowFace"
            };
            if (!_farMaps.Create() || !_farFace.Create())
            {
                Plugin.Log.LogError("Couldn't create the distant shadow textures.");
                return false;
            }
            Shader.SetGlobalTexture(FarMapsId, _farMaps);
            Shader.SetGlobalFloat("_WireNearRes", res);
            Shader.SetGlobalFloat("_WireFarRes", farRes);
            // Separate actual resolution: both dynamic tiers have the same
            // texture dimensions, independent of the distant static tier.
            Shader.SetGlobalFloat("_WireDynRes", dynRes);

            _maps = new RenderTexture(res, res, 0, RenderTextureFormat.RHalf)
            {
                dimension = TextureDimension.CubeArray,
                volumeDepth = 6 * slots,
                useMipMap = false,
                filterMode = FilterMode.Point,
                name = "WireShadowMaps"
            };
            _face = new RenderTexture(res, res, 16, RenderTextureFormat.RHalf)
            {
                dimension = TextureDimension.Cube,
                useMipMap = false,
                filterMode = FilterMode.Point,
                name = "WireShadowFace"
            };
            if (!_maps.Create() || !_face.Create())
            {
                Plugin.Log.LogError("Couldn't create the shadow cache textures.");
                return false;
            }
            Shader.SetGlobalTexture(MapsId, _maps);
            ReleaseFaces();
            _faceBack = FaceRT(res, 16, true, "WireShadowFaceBack");
            _farFaceBack = FaceRT(farRes, 16, true, "WireFarShadowFaceBack");
            _flatFront = FaceRT(res, 0, false, "WireShadowFlatFront");
            _flatBack = FaceRT(res, 0, false, "WireShadowFlatBack");
            _farFlatFront = FaceRT(farRes, 0, false, "WireFarShadowFlatFront");
            _farFlatBack = FaceRT(farRes, 0, false, "WireFarShadowFlatBack");
            _stage = StageRT(res, "WireShadowStage");
            _farStage = StageRT(farRes, "WireFarShadowStage");

            ReleaseNativeMovingResources();
            if (_dynMaps != null) _dynMaps.Release();
            if (_farDynMaps != null) _farDynMaps.Release();
            if (_dynFace != null) _dynFace.Release();
            _dynMaps = new RenderTexture(dynRes, dynRes, 0, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * _dynNearCapacity, useMipMap = false,
                filterMode = FilterMode.Point, name = "WireDynShadowMaps"
            };
            // Moving shadows always use the full-size existing map tier:
            // distant 256px source maps produced oversized silhouette texels.
            // The static distant cache remains at farRes.
            _farDynMaps = new RenderTexture(dynRes, dynRes, 0, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * _dynFarCapacity, useMipMap = false,
                filterMode = FilterMode.Point, name = "WireFarDynShadowMaps"
            };
            if (!_farDynMaps.Create())
            {
                Plugin.Log.LogError("Couldn't create distant moving-shadow maps at " + dynRes + " px.");
                return false;
            }
            Shader.SetGlobalTexture(FarDynMapsId, _farDynMaps);
            _dynFace = new RenderTexture(dynRes, dynRes, 16, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.Cube, useMipMap = false, filterMode = FilterMode.Point, name = "WireDynShadowFace"
            };
            if (!_dynMaps.Create() || !_dynFace.Create())
            {
                Plugin.Log.LogError("Couldn't create moving-shadow maps at " + dynRes + " px.");
                return false;
            }
            Shader.SetGlobalTexture(DynMapsId, _dynMaps);
            System.Array.Clear(_dynActive, 0, _dynActive.Length);
            System.Array.Clear(_dynFacesLast, 0, _dynFacesLast.Length);

            if (_camera == null)
            {
                var go = new GameObject("WireShadowCamera") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _camera = go.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(60000f, 0, 0, 0);
                _camera.renderingPath = RenderingPath.Forward;
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _camera.useOcclusionCulling = false;
                _camera.SetReplacementShader(DistanceShader, "RenderType");
                _staticCb = new CommandBuffer { name = "WireUntaggedCasters" };
                _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _staticCb);
            }
            _camera.cullingMask = StaticLayerMask = StaticMask();
            if (_dynCamera == null)
            {
                var go = new GameObject("WireDynShadowCamera") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _dynCamera = go.AddComponent<Camera>();
                _dynCamera.CopyFrom(_camera);
                // Both RGHalf channels must mean empty at the camera's clear colour.
                _dynCamera.backgroundColor = new Color(60000f, 60000f, 0f, 0f);
                _dynCamera.enabled = false;
                _dynCamera.SetReplacementShader(DistanceShader, "RenderType");
            }
            _dynCamera.cullingMask = MovingMask();
            return true;
        }

        private static int MovingMask()
        {
            int mask = 0;
            foreach (string name in new[] { "character", "character_net", "character_ghost", "character_noenv", "item", "ragdoll" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                    mask |= 1 << layer;
            }
            return mask;
        }


        private static int StaticMask()
        {
            int mask = 0;
            foreach (string name in new[] { "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "blocker" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                    mask |= 1 << layer;
            }
            return mask;
        }

        // ---- Slots ----

        internal static void RefreshSconceComparison()
        {
            if (Instance == null) return;
            Instance.Reassign(); // toggle applies now, not at the next second
            int count = 0;
            foreach (Tracked t in Lights)
                if (t.Light != null && t.Sconce) count++;
            Plugin.Log.LogInfo("[sconce A/B] " + count + " sconces " +
                (Plugin.CompareGame.Value ? "Unity soft shadows for all sconces" : "returned to WLO"));
        }

        // Reassign can evict and retake dozens of lights in one teleport.
        // Slot changes happen together: uploading all entries after EVERY
        // individual Release is quadratic in the number of changed slots.
        private bool _batchingReassign;

        private void Reassign()
        {
            if (!Plugin.CachedShadows.Value || !Plugin.UseShader.Value || DistanceShader == null || !EnsureResources())
            {
                SetActive(false);
                ReleaseAll();
                return;
            }
            SetActive(true);
            Camera main = Utils.GetMainCamera();
            Player player = Player.m_localPlayer;
            if (main == null)
                return;
            _batchingReassign = true;
            _perfReassignStart = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            try
            {
            Vector3 at = player != null ? player.transform.position : main.transform.position;
            Vector3 eye = main.transform.position;
            _lastForward = main.transform.forward;
            float maxDist = Plugin.Distance.Value;
            GeometryUtility.CalculateFrustumPlanes(main, ReassignFrustum);

            // LightLod can be destroyed during teleport/streaming before the
            // next reassignment. Evict dead slots before touching their state.
            bool evictedDestroyed = false;
            for (int sl = 0; sl < _slots.Length; sl++)
            {
                Tracked stale = _slots[sl];
                if (stale != null && stale.Light == null)
                {
                    Release(stale);
                    evictedDestroyed = true;
                }
            }
            if (Lights.RemoveAll(t => t.Light == null) > 0 || evictedDestroyed)
                Forget();

            // A light may Awake/Register before Valheim finishes parenting its
            // visual under a player, armor stand, or item stand. Registration
            // correctly excludes these, but a light registered too early
            // would otherwise retain a static slot and redraw 6 moving faces
            // continuously next to the player or mannequin.
            // Reuse the already-existing 1-second/turn Reassign, not a new
            // per-frame scan. All checks below are one-way classifications.
            for (int i = Lights.Count - 1; i >= 0; i--)
            {
                Tracked t = Lights[i];
                Light light = t.Light;
                if (light == null) continue;
                if (light.GetComponentInParent<Character>() != null)
                {
                    Release(t);
                    DisableNativeSconceShadows(t);
                    Unsuppress(t);
                    ByLight.Remove(light);
                    Lights.RemoveAt(i);
                    // Same existing behavior as a carried light registered
                    // after parenting. Only real torches get native shadows.
                    MovingLight(light, true, "carried");
                    Check("late-parented character light returned to vanilla: " +
                        NameOf(light));
                    continue;
                }
                if (!t.Tiny && (light.GetComponentInParent<ItemDrop>() != null ||
                    light.GetComponentInParent<ItemStand>() != null ||
                    light.GetComponentInParent<ArmorStand>() != null))
                {
                    // A glow belonging to a stand/item is intentionally tiny:
                    // its static cache can remain, but moving-caster maps
                    // are not needed (and are absent for vanilla item glows).
                    t.Tiny = true;
                    Check("late-parented stand/item light uses cached shadows: " +
                        NameOf(light));
                }
            }

            foreach (Tracked t in Lights)
            {
                if (t.Sconce && Plugin.CompareGame.Value)
                {
                    // No WLO slot, no WLO shadow suppression, no shader lookup.
                    if (t.Slot >= 0) Release(t);
                    Unsuppress(t);
                    EnableNativeSconceShadows(t);
                    t.Reach = float.MaxValue;
                    t.Priority = float.MaxValue;
                    continue;
                }
                if (t.NativeSconceShadows) DisableNativeSconceShadows(t);
                if (_active && !t.Suppressed) Suppress(t);
                t.Reach = Reach(t, at);
                t.Priority = Priority(t, eye);
            }
            Lights.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            // Slotted lights stay until they're well out of range, so walking
            // around doesn't keep re-rendering lights at the edge.
            // Recycle physical moving cubes that have become completely empty.
            // Reassign runs once/sec or on real camera turns, NOT every frame.
            for (int sl = 0; sl < _slots.Length; sl++)
            {
                if (!_framePending && _dynPhysical[sl] >= 0 &&
                    _dynFacesLast[sl] == 0 && _dynActive[sl] <= 0f)
                    FreeDynSlot(sl);
                if (_slots[sl] != null && _slots[sl].Reach > maxDist + KeepMargin)
                    Release(_slots[sl]);
            }
            // The existing coroutine budgets new shadow-map faces; avoid
            // synchronous 6-face builds here that bypass its frame budget.
            bool promoted = false;
            int total = _slots.Length;
            foreach (Tracked t in Lights)
            {
                if (t.Sconce && Plugin.CompareGame.Value) continue;
                if (t.Reach > maxDist)
                    continue;
                // Close or in view: a full-size map. Further: a low-res one.
                bool wantNear = !t.Tiny && t.Priority < NearCutoff;
                int slot;
                if (t.Slot >= 0)
                {
                    // Moved up into view on a distant map: switch to a full-size
                    // one (one per pass; the rest keep their distant map till then).
                    if (promoted || t.Slot < _near || !wantNear || t.Priority >= NearCutoff - TierMargin || KeepReadyVisibleSlot(t))
                        continue;
                    slot = FindSlot(0, _near, t.Priority, true);
                    if (slot < 0)
                        continue;
                    Release(t);
                }
                else if (t.Tiny)
                    slot = Mathf.Max(FindSlot(_near, total, t.Priority, false), FindSlot(0, _near, t.Priority, false));
                else if (wantNear)
                {
                    slot = FindSlot(0, _near, t.Priority, true);
                    if (slot < 0) slot = FindSlot(_near, total, t.Priority, true);
                }
                else
                {
                    slot = FindSlot(_near, total, t.Priority, true);
                    if (slot < 0) slot = FindSlot(0, _near, t.Priority, false);
                }
                if (slot < 0)
                    continue;
                // A light promoted or switched between slot tiers previously
                // re-rendered synchronously during loading; now all such changes
                // use the same bounded background rendering queue.
                bool moving = t.ReleasedFrame == Time.frameCount;
                Take(t, slot);
                if (moving && (slot >= _near || !promoted))
                {
                    if (slot < _near) promoted = true;
                    // The existing renderer prioritizes this fresh map.
                    // A portal exit must not force a synchronous six-face
                    // render outside the shared loading-frame budget.
                    continue;
                }
                // Do not bypass the RenderDirty frame budget on teleport.
                // It renders urgent nearby maps first while Loading() is true
                // and fades unfinished maps in once the world becomes visible.
            }
            }
            finally
            {
                _batchingReassign = false;
                UpdateGlobals(); // One upload for the whole reassignment.
                if (_perfReassignStart != 0L)
                {
                    _perfReassignTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _perfReassignStart;
                    _perfProbeCalls += 2;
                    _perfReassignStart = 0L;
                }
            }
            WakeRenders();
        }

        private const float OffscreenWeight = 3f;
        private const float NearCutoff = 12f;
        private const float TierMargin = 4f;

        // A free slot in [from, to), else (if allowed) one whose light
        // matters clearly less, released. -1 if none.
        // Prevent the one-second slot reshuffle from blanking a currently
        // visible, fully rendered shadow cube. Without this, Reassign's
        // Release -> Take path sets Ready=false/Fade=0 immediately, while
        // rebuilding takes multiple frames. That causes the reported
        // sudden leakage followed by a one-second shadow fade-in.
        // Existing on-screen maps stay mapped until the light is gone,
        // actually out of shadow distance, or no longer in the view.
        // Intentional tradeoff: an already-ready far-tier light can retain
        // lower-resolution shadows until it leaves view, instead of making
        // a destructive near-tier promotion while you watch.
        private static bool KeepReadyVisibleSlot(Tracked t)
        {
            if (t == null || !t.Ready || t.Light == null ||
                !t.Light.isActiveAndEnabled ||
                t.Reach > Plugin.Distance.Value + KeepMargin)
                return false;
            float range = Range(t);
            if (range <= 0f) return false;
            return GeometryUtility.TestPlanesAABB(ReassignFrustum,
                new Bounds(t.Light.transform.position,
                    Vector3.one * (range * 2f)));
        }
        private int FindSlot(int from, int to, float priority, bool evict)
        {
            int worst = -1;
            float worstPriority = priority + (from == 0 && to == _near ? NearSwapMargin : SwapMargin);
            for (int sl = from; sl < to; sl++)
            {
                if (_slots[sl] == null)
                    return sl;
                if (evict && !KeepReadyVisibleSlot(_slots[sl]) && _slots[sl].Priority > worstPriority) { worst = sl; worstPriority = _slots[sl].Priority; }
            }
            if (worst >= 0)
                Release(_slots[worst]);
            return worst;
        }
        private const float TinyRank = 1000f;
        private const float ItemSettle = 0.3f;
        private const float TurnDot = 0.9f; // about 25 degrees
        private static readonly Plane[] ReassignFrustum = new Plane[6];
        private Vector3 _lastForward;
        private float _nextTurnReassign;

        // Whether the light is near enough to get shadows at all. LightLod
        // switches lights off and shrinks their range with distance; the
        // light still counts, at its full range.
        private static float Reach(Tracked t, Vector3 at)
        {
            if (t.Light == null || !t.Light.gameObject.activeInHierarchy)
                return float.MaxValue;
            return Vector3.Distance(t.Light.transform.position, at) - Range(t) * 0.5f;
        }

        // Which lights get the slots first: how far the camera is from the
        // edge of the light's reach. Off screen counts as three times as
        // far, so a hearth right behind you still beats a torch across the
        // room, but a light in view beats one behind you at the same distance.
        // Item glows rank behind every other light: they only take a slot
        // nobody else needs.
        private static float Priority(Tracked t, Vector3 eye)
        {
            if (t.Light == null || !t.Light.gameObject.activeInHierarchy)
                return float.MaxValue;
            if (Time.time - t.LitAt < 2f && !t.Tiny)
                return -1000f;
            Vector3 lp = t.Light.transform.position;
            float range = Range(t);
            float cameraDist = Vector3.Distance(lp, eye);
            // Previously every light whose range contained the camera scored
            // exactly zero. This made near-slot ranking effectively arbitrary
            // indoors and delayed distant->near map promotions.
            float p = Mathf.Max(0f, cameraDist - range) + 0.45f * cameraDist;
            if (!GeometryUtility.TestPlanesAABB(ReassignFrustum, new Bounds(lp, Vector3.one * (range * 2f))))
                p = p * OffscreenWeight + 2f;
            return t.Tiny ? p + TinyRank : p;
        }

        // While the cache runs, no point light uses Unity's own per-frame
        // shadows: slotted lights read the cache, the rest have none (as
        // vanilla lights past their shadow distance).
        private static void EnableNativeSconceShadows(Tracked t)
        {
            if (t.Light == null) return;
            if (!t.NativeSconceShadows)
            {
                Unsuppress(t);
                t.NativeSconceShadows = true;
            }
            if (t.Lod != null) ShadowLod(t.Lod) = false;
            t.Light.shadowStrength = 1f;
            t.Light.shadowNearPlane = NearPlane(t);
            t.Light.shadows = LightShadows.Soft;
        }

        private static void DisableNativeSconceShadows(Tracked t)
        {
            if (!t.NativeSconceShadows) return;
            t.NativeSconceShadows = false;
            if (t.Light == null) return;
            if (t.Lod != null) ShadowLod(t.Lod) = t.OriginalShadowLod;
            t.Light.shadows = t.OriginalShadowLod ? LightShadows.None : t.OriginalShadows;
            t.Light.shadowStrength = t.OriginalShadowStrength;
            t.Light.shadowNearPlane = t.OriginalShadowNearPlane;
        }

        private static void Suppress(Tracked t)
        {
            if (t.Suppressed || t.Light == null)
                return;
            t.Suppressed = true;
            if (t.Lod != null) ShadowLod(t.Lod) = false;
            t.Light.shadows = LightShadows.None;
        }

        private static void Unsuppress(Tracked t)
        {
            if (!t.Suppressed || t.Light == null)
                return;
            t.Suppressed = false;
            if (t.Lod != null) ShadowLod(t.Lod) = t.OriginalShadowLod;
            t.Light.shadows = t.OriginalShadowLod ? LightShadows.None : t.OriginalShadows;
            if (t.OriginalShadowLod) t.Light.shadowStrength = 0f; // LightLod fades it back in
        }

        private void SetActive(bool active)
        {
            if (_active == active)
                return;
            _active = active;
            Lights.RemoveAll(l => l.Light == null);
            foreach (Tracked t in Lights)
            {
                if (active) Suppress(t);
                else
                {
                    DisableNativeSconceShadows(t);
                    Unsuppress(t);
                }
            }
        }

        private void Take(Tracked t, int slot)
        {
            Check("gets a " + (slot < _near ? "full-size" : "distant") + " slot: " + NameOf(t.Light) + (Time.time - t.LitAt < 2f ? " (just switched on)" : ""));
            t.Slot = slot;
            _slots[slot] = t;
            Suppress(t);
            // Fires animated by an Animator stop moving when off screen
            // (animation culling), and their shadows with them: keep them going.
            t.Animators.Clear();
            t.AnimatorModes.Clear();
            Piece piece = t.Light.GetComponentInParent<Piece>();
            Transform host = piece != null ? piece.transform : t.Light.transform.parent != null ? t.Light.transform.parent : t.Light.transform;
            foreach (Animator a in host.GetComponentsInChildren<Animator>(true))
            {
                if (a.cullingMode == AnimatorCullingMode.AlwaysAnimate)
                    continue;
                t.Animators.Add(a);
                t.AnimatorModes.Add(a.cullingMode);
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            // Following the flame: lights with no shadows in vanilla often hold
            // still; give them the fire pit's sway so their shadows move too.
            LightFlicker flicker = t.Light.GetComponent<LightFlicker>();
            bool vanillaShadows = t.OriginalShadowLod || t.OriginalShadows != LightShadows.None;
            if (flicker != null && !vanillaShadows && flicker.m_movement <= 0f && Plugin.FollowFlicker.Value)
            {
                t.OriginalMovement = flicker.m_movement;
                flicker.m_movement = FireSway;
            }
            t.DoorFaces = 0;
            t.DoorPrepared = false;
            t.Dirty = true;
            t.DirtyAt = Time.time;
            t.Ready = false;
            t.Face = -1;
        }

        private void Release(Tracked t)
        {
            if (t == null || t.Slot < 0)
                return;
            if (t.Slot < _slots.Length && _slots[t.Slot] == t)
                _slots[t.Slot] = null;
            if (t.Slot >= 0 && t.Slot < MaxSlots)
            {
                _dynActive[t.Slot] = 0;
                _dynFacesLast[t.Slot] = 0;
                _nativeMovingActive[t.Slot] = 0f;
                FreeDynSlot(t.Slot);
                _nativeFaceMasks[t.Slot] = 0;
                _nativeOwners[t.Slot] = null;
            }
            if (t.Ready) t.ReleasedFrame = Time.frameCount;
            AbortStage(t);
            t.Slot = -1;
            t.Face = -1;
            t.DoorFaces = 0;
            t.DoorPrepared = false;
            t.Ready = false;
            t.Fade = 0f;
            if (t.Light != null)
            {
                LightFlicker flicker = t.Light.GetComponent<LightFlicker>();
                if (flicker != null && t.OriginalMovement >= 0f)
                    flicker.m_movement = t.OriginalMovement;
                t.OriginalMovement = -1f;
                for (int i = 0; i < t.Animators.Count; i++)
                    if (t.Animators[i] != null) t.Animators[i].cullingMode = t.AnimatorModes[i];
                t.Animators.Clear();
                t.AnimatorModes.Clear();
                if (!_active)
                    Unsuppress(t);
            }
            if (_batchingReassign)
                return;
            if (TeleportStreaming())
            {
                // Destroying many lights while leaving a zone used to upload
                // the entire shadow-slot array once for EACH destroyed light.
                // Reassign batches the new zone once on the existing frame.
                _reassignSoon = true;
                return;
            }
            UpdateGlobals();
        }

        private void ReleaseAll()
        {
            System.Array.Clear(_nativeFaceMasks, 0, MaxSlots);
            System.Array.Clear(_nativeOwners, 0, MaxSlots);
            System.Array.Clear(_pickupInvalidFaces, 0, _pickupInvalidFaces.Length);
            System.Array.Clear(_nativeMovingActive, 0, MaxSlots);
            UploadNativeMovingActive();
            foreach (Tracked t in _slots)
                Release(t);
            System.Array.Clear(_nativeActive, 0, MaxSlots);
            Shader.SetGlobalFloatArray(NativeActiveId, _nativeActive);
            Shader.SetGlobalFloat(CountId, 0);
        }
        // Where each slotted light is this frame: the shader finds a light's
        // slot by it.
        private readonly Vector4[] _lightNow = new Vector4[MaxSlots];
        private static readonly int LightNowId = Shader.PropertyToID("_WireLightNow");

        private void UploadLightPositions()
        {
            // The previous code uploaded the entire 96-element position
            // array on EVERY main-camera frame, even when all lights were
            // static. Use exact float comparisons (not Unity's approximate
            // Vector4 != operator): tiny light/flicker movements still
            // receive the same precision and same-frame updates.
            bool changed = false;
            for (int s = 0; s < _slots.Length; s++)
            {
                Tracked t = _slots[s];
                Vector4 now = t != null && t.Ready && t.Light != null
                    ? (Vector4)t.Light.transform.position
                    : new Vector4(1e9f, 1e9f, 1e9f, 0f);
                Vector4 previous = _lightNow[s];
                if (previous.x == now.x && previous.y == now.y &&
                    previous.z == now.z && previous.w == now.w)
                    continue;
                _lightNow[s] = now;
                changed = true;
            }
            if (changed)
                Shader.SetGlobalVectorArray(LightNowId, _lightNow);
        }

        private void UpdateGlobals()
        {
            if (_batchingReassign) return;
            for (int s = 0; s < _slots.Length; s++)
            {
                Tracked t = _slots[s];
                // Not rendered yet: an unmatchable position, so the shader skips it.
                Vector3 shown = t != null && t.Staged ? t.PublishedCenter :
                    t != null ? t.Center : Vector3.zero;
                _slotData[s] = t != null && t.Ready && t.Light != null
                    ? new Vector4(shown.x, shown.y, shown.z, t.Fade)
                    : new Vector4(1e9f, 1e9f, 1e9f, 0f);
            }
            Shader.SetGlobalVectorArray(LightsId, _slotData);
            Shader.SetGlobalFloat(CountId, _slots.Length);
            Shader.SetGlobalFloat(NearCountId, _near);
            int active = 0;
            for (int sl = 0; sl < MaxSlots; sl++)
            {
                Tracked t = sl < _slots.Length ? _slots[sl] : null;
                bool ready = Plugin.NativeDepthPcf.Value && !_nativeFailed && t != null &&
                    t.Light != null && t.Ready && _nativeOwners[sl] == t &&
                    _nativeFaceMasks[sl] == 63 &&
                    (sl < _near ? _nativeNearMaps != null && _nativeNearMaps.IsCreated()
                                : _nativeFarMaps != null && _nativeFarMaps.IsCreated());
                _nativeActive[sl] = ready ? 1f : 0f;
                _nativeInvRanges[sl] = ready ? 1f / t.NativeCacheRange : 0f;
                if (ready) active++;
            }
            Shader.SetGlobalFloatArray(NativeActiveId, _nativeActive);
            Shader.SetGlobalFloatArray(NativeRangesId, _nativeInvRanges);
            Shader.SetGlobalFloatArray(NativeNearsId, _nativeNears);
            Shader.SetGlobalFloat(VanillaDepthId, Plugin.VanillaDrawing.Value ? 1f : 0f);
            if (active > 0 && !_nativeReportedReady)
            {
                Plugin.Log.LogInfo("[native-depth] ACTIVE on all eligible cached-shadow slots (" +
                    active + " currently ready, others populate via cached face renders).");
                _nativeReportedReady = true;
            }
            else if (active == 0 && _nativeReportedReady)
            {
                _nativeReportedReady = false;
                Plugin.Log.LogInfo("[native-depth] INACTIVE: all-native comparison awaiting maps or disabled.");
            }
        }
        // ---- Moving casters ----

        private static readonly List<int> DynCandidates = new List<int>();
        private static readonly int[] DynFaces = new int[MaxSlots];
        private static readonly int[] DynDraw = new int[MaxSlots];
        private static readonly int[] DynFaceHash = new int[MaxSlots * 6];
        private static readonly int[] DynFaceLp = new int[MaxSlots * 6];
        private readonly int[] _faceLp = new int[MaxSlots * 6];
        private static readonly int[] FaceHashNow = new int[6];
        private readonly int[] _faceHash = new int[MaxSlots * 6]; // per moving face, as of its last draw
        private static readonly Vector3[] DynOrigin = new Vector3[MaxSlots];

        private void LateUpdate()
        {
            // Stopwatch instrumentation is only needed when logging stats.
            // Avoid querying the high-resolution clock on every normal frame.
            if (Plugin.DebugLog.Value)
            {
                long gatherBefore = _perfGatherTicks, scanBefore = _perfScanTicks, buildBefore = _perfBuildTicks;
                _statWatch.Restart();
                LateUpdateWork();
                double cpuMs = _statWatch.Elapsed.TotalMilliseconds;
                _statCpu += cpuMs;
                double gameFrameDeltaMs = Time.unscaledDeltaTime * 1000.0; // previous Unity frame
                if (gameFrameDeltaMs > _maxFrameDeltaMs) { _maxFrameDeltaMs = gameFrameDeltaMs; _maxFrameDeltaAt = Time.frameCount; }
                if (gameFrameDeltaMs > 33.333) _framesOver33ms++;
                if (gameFrameDeltaMs > 50.0) _framesOver50ms++;
                if (Time.frameCount - _lastSpawnFrame <= 3 && gameFrameDeltaMs > _maxNearSpawnFrameDeltaMs)
                { _maxNearSpawnFrameDeltaMs = gameFrameDeltaMs; _maxNearSpawnFrameDeltaAt = Time.frameCount; }
                if (cpuMs > _maxLateCpuMs) { _maxLateCpuMs = cpuMs; _maxLateFrame = Time.frameCount; }
                double gatherMs = TicksToMs(_perfGatherTicks - gatherBefore);
                double scanMs = TicksToMs(_perfScanTicks - scanBefore);
                double buildMs = TicksToMs(_perfBuildTicks - buildBefore);
                if (gatherMs > _maxGatherMs) _maxGatherMs = gatherMs;
                if (scanMs > _maxScanMs) _maxScanMs = scanMs;
                if (buildMs > _maxBuildMs) _maxBuildMs = buildMs;
                // Correlation only: a spawn in the preceding two frames does NOT
                // establish that WLO caused the resulting frame-time spike.
                if (Time.frameCount - _lastSpawnFrame <= 2 && cpuMs > _maxSpawnAdjacentCpuMs)
                {
                    _maxSpawnAdjacentCpuMs = cpuMs;
                    _maxSpawnAdjacentFrame = Time.frameCount;
                    _maxSpawnPrefab = _lastSpawnPrefab;
                }
            }
            else
                LateUpdateWork();
            Stats();
        }

        private void LateUpdateWork()
        {
            // Turning the camera brings other lights on screen: re-rank the
            // slots now instead of at the next 1 s Reassign.
            Camera view = Utils.GetMainCamera();
            bool reassignedForTurn = false;
            if (_active && view != null && Time.time >= _nextTurnReassign && Vector3.Dot(view.transform.forward, _lastForward) < TurnDot && !Loading())
            {
                _nextTurnReassign = Time.time + 0.25f;
                Reassign();
                reassignedForTurn = true;
            }
            // Also catches the end of a teleport the frame it happens.
            bool loading = Loading();
            if (_reassignSoon)
            {
                _reassignSoon = false;
                if (!reassignedForTurn && _active && (loading || TeleportStreaming()))
                    Reassign();
            }
            if (Swings.Count > 0)
                UpdateSwings();
            UpdateStandHandoffs(); // Dropped items no longer need a static-map handoff.
            UpdateGhost();
            if (Plugin.DebugView.Value)
                PickDebugLight();
            if (!_active || !Plugin.UseShader.Value || !Plugin.CachedShadows.Value ||
                _dynMaps == null || _dynCamera == null || !Plugin.MovingCasters.Value ||
                _slots.Length == 0)
                return;
            List<Character> characters = Character.GetAllCharacters();
            Camera main = Utils.GetMainCamera();
            if (main == null)
                return;

            // A slotted light that has moved away from where its map was made
            // (a light on a ship, a carried light) re-renders.
            foreach (Tracked t in _slots)
            {
                if (t == null || t.Light == null)
                    continue;
                // A dropped item still rolling: no shadows while it moves (as
                // in vanilla), one render once it has lain still a moment.
                if (t.Body != null && !t.Body.IsSleeping())
                {
                    if (!t.Rolling)
                    {
                        t.Rolling = true;
                        MovingLight(t.Light, true, "rolling");
                    }
                    if (t.Ready || !t.Dirty)
                    {
                        AbortStage(t);
                        t.Ready = false;
                        t.Face = -1;
                        t.Dirty = true;
                        UpdateGlobals();
                        WakeRenders();
                    }
                    t.DirtyAt = Time.time + ItemSettle;
                    continue;
                }
                if (t.Rolling)
                {
                    // Settled: back to the cached map (re-rendered once now).
                    t.Rolling = false;
                    MovingLight(t.Light, false, "settled");
                }
                if (!t.Ready || t.Dirty)
                    continue;
                // The object the light belongs to moved (a ship, a carried
                // light). Animation inside it (a flame) doesn't count.
                Transform root = t.Light.transform.root;
                if ((root.position - t.RootPos).sqrMagnitude > MovedDistance * MovedDistance ||
                    Quaternion.Angle(root.rotation, t.RootRot) > 2f)
                {
                    // Equipment glow is parented to its character after Awake,
                    // so Register can't see it. Hand it back to vanilla.
                    if (t.Light.GetComponentInParent<Character>() != null)
                    {
                        Release(t);
                        Unsuppress(t);
                        ByLight.Remove(t.Light);
                        Lights.Remove(t);
                        MovingLight(t.Light, true, "carried");
                        continue;
                    }
                    t.Dirty = true;
                    t.DirtyAt = Time.time;
                    WakeRenders();
                }
            }

            if (Time.frameCount % 60 == 0)
            {
                Items.RemoveAll(it => it == null);
                CleanupDroppedItems();
            }
            Vector3 cameraPosition = main.transform.position;
            long gatherStart = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            GatherCasters(cameraPosition, characters);
            BuildMovingGroupGrid(cameraPosition);
            if (gatherStart != 0L)
            {
                _perfGatherTicks += System.Diagnostics.Stopwatch.GetTimestamp() - gatherStart;
                _perfProbeCalls += 2;
            }
            DynCandidates.Clear();
            // Count newly requested physical cubes as candidates are already
            // gathered. On streaming into a dense base, reserve capacity
            // ONCE instead of 4->8->16->32 allocations/copies this frame.
            int newNearCubes = 0, newFarCubes = 0;
            bool releasedAnyMovingMaps = false;
            GeometryUtility.CalculateFrustumPlanes(main, Frustum);
            long scanStart = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            for (int sl = 0; sl < _slots.Length; sl++)
            {
                DynFaces[sl] = 0;
                Tracked t = _slots[sl];
                if (t == null || !t.Ready || t.Light == null || !t.Light.isActiveAndEnabled)
                    continue;
                // Following the flame: drawn from where it is this frame (the
                // shader reads it from there too). Otherwise its resting spot.
                bool follow = Plugin.FollowFlicker.Value;
                Vector3 lp = follow ? t.Light.transform.position : t.Center;
                float range = Range(t);
                // Nothing this light reaches is on screen: its moving shadows
                // can wait. A changed hash redraws them once it's back in view.
                if (!GeometryUtility.TestPlanesAABB(Frustum, new Bounds(lp, Vector3.one * (range * 2f))))
                {
                    for (int f = 0; f < 6; f++) _faceHash[sl * 6 + f] = 0;
                    continue;
                }
                int faces = 0;
                // The closest caster's CAMERA distance sets update cadence,
                // regardless of where its light source is located.
                float closestCasterDistanceSq = float.PositiveInfinity;
                for (int f = 0; f < 6; f++) FaceHashNow[f] = 17;
                List<int> groupsForLight = _groupsBySlot[sl];
                if (groupsForLight == null)
                    _groupsBySlot[sl] = groupsForLight = new List<int>(16);
                else
                    groupsForLight.Clear();
                // Most lights do not overlap most moving casters. A small,
                // pre-bucketed broadphase gives only potential groups. Groups
                // remain in their original order for identical face hashes.
                // Tiny lights retain their vanilla empty moving caster set.
                List<int> possibleGroups = !t.Tiny && _groupCount > 64
                    ? MovingGroupCandidates(lp, range) : null;
                int possibleCount = t.Tiny ? 0 : possibleGroups == null
                    ? _groupCount : possibleGroups.Count;
                for (int n = 0; n < possibleCount; n++)
                {
                    int g = possibleGroups == null ? n : possibleGroups[n];
                    Group grp = Groups[g];
                    if (!Within(grp, lp, range))
                        continue;
                    groupsForLight.Add(g);
                    float cameraDistanceSq = (grp.Center - cameraPosition).sqrMagnitude;
                    if (cameraDistanceSq < closestCasterDistanceSq)
                        closestCasterDistanceSq = cameraDistanceSq;
                    int gf = FacesFor(grp.Center - lp, grp.Radius);
                    faces |= gf;
                    // Per face: you moving doesn't redraw a face that only
                    // has items lying still.
                    for (int f = 0; f < 6; f++)
                        if ((gf & (1 << f)) != 0) FaceHashNow[f] = FaceHashNow[f] * 31 + grp.Hash;
                }
                if (faces == 0)
                {
                    // An empty moving map contributes exactly 1.0 to
                    // lighting. Disable it instead of clearing and
                    // converting up to six 2048px faces on the GPU.
                    // The next nonempty use starts as a fresh cube and
                    // fully initializes faces (including empty sides).
                    if (_dynFacesLast[sl] != 0 || _dynActive[sl] > 0.5f)
                    {
                        _dynFacesLast[sl] = 0;
                        _dynActive[sl] = 0f;
                        _nativeMovingActive[sl] = 0f;
                        if (!_framePending) FreeDynSlot(sl);
                        releasedAnyMovingMaps = true;
                    }
                    _pickupInvalidFaces[sl] = 0;
                    continue;
                }
                int lpHash = follow ? Quantize(lp, 500f) : 0;
                int draw = 0;
                for (int f = 0; f < 6; f++)
                {
                    int i = sl * 6 + f;
                    int h = FaceHashNow[f];
                    DynFaceHash[i] = h;
                    DynFaceLp[i] = lpHash;
                    if ((faces & (1 << f)) == 0)
                        continue;
                    // Only the flame moved and everything in this face is
                    // still: its shadows barely shift, so every fourth frame.
                    bool still = (_dynFacesLast[sl] & (1 << f)) != 0 && h == _faceHash[i];
                    if (!still || ((_pickupInvalidFaces[sl] & (1 << f)) != 0) ||
                        (lpHash != _faceLp[i] && (Time.frameCount + i) % 4 == 0))
                        draw |= 1 << f;
                }
                // Nothing in range moved since this light's moving map was drawn: keep it.
                if (faces == _dynFacesLast[sl] && draw == 0)
                    continue;
                // Camera-relative LOD for UPDATE FREQUENCY only: near
                // moving shadows always respond this frame, while ones
                // far from view use progressively less GPU work. The map's
                // spatial resolution is never reduced for a distant light.
                int updateEvery = closestCasterDistanceSq < 64f ? 1 :
                    closestCasterDistanceSq < 256f ? 2 :
                    closestCasterDistanceSq < 576f ? 3 :
                    closestCasterDistanceSq < 1024f ? 4 : 5;
                // Stand attachment/removal must be instant even if the
                // stand is beyond the camera's normal detail distance.
                if (faces == _dynFacesLast[sl] &&
                    Time.frameCount > Mathf.Max(_standChangedFrame, _dropChangedFrame) + 1 &&
                    (Time.frameCount + sl) % updateEvery != 0)
                    continue;
                DynDraw[sl] = draw;
                DynFaces[sl] = faces;
                DynOrigin[sl] = lp;
                DynCandidates.Add(sl);
                if (_dynPhysical[sl] < 0 && faces != 0)
                {
                    if (sl < _near) newNearCubes++;
                    else newFarCubes++;
                }
            }

            if (scanStart != 0L)
            {
                _perfScanTicks += System.Diagnostics.Stopwatch.GetTimestamp() - scanStart;
                _perfProbeCalls += 2;
            }
            // No changed casters: skip building and submitting an empty
            // command buffer. Static map contents remain untouched.
            if (DynCandidates.Count == 0)
            {
                if (releasedAnyMovingMaps)
                {
                    UploadDynActive();
                    UploadNativeMovingActive();
                }
                return;
            }
            long buildStart = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            // GPU pool growth can involve gigabytes of texture copies with
            // 2048px faces and native depth enabled. Batch newly needed
            // physical cubes per tier to avoid repeated growth/copies in
            // the SAME frame after teleporting into a densely lit area.
            ReserveNewMovingCubes(newNearCubes, newFarCubes);
            // Allocate/retain a physical cube only for a light with active
            // moving casters; completed faces are never downsampled.
            for (int k = DynCandidates.Count - 1; k >= 0; k--)
            {
                int logical = DynCandidates[k];
                if (DynFaces[logical] != 0 && !EnsureDynSlot(logical))
                    DynCandidates.RemoveAt(k);
            }
            if (DynCandidates.Count == 0) return;
            // Recorded now, run when the main camera starts its frame: by then
            // the game has skinned this frame's poses, so shadows use the pose
            // you see, not last frame's.
            if (_frameCb == null)
                _frameCb = new CommandBuffer { name = "WireMovingShadows" };
            _frameCb.Clear();
            for (int k = 0; k < DynCandidates.Count; k++)
            {
                int sl = DynCandidates[k];
                Tracked t = _slots[sl];
                int faces = DynFaces[sl];
                DynTarget(sl, out RenderTexture dynMaps, out int pl);
                int draw = DynDraw[sl];
                bool nativeMoving = Plugin.NativeMovingDepthPcf.Value && EnsureNativeMovingTier(sl < _near);
                if (nativeMoving && _dynFacesLast[sl] == 0)
                    InitNativeMovingSlot(t, pl);
                for (int f = 0; f < 6; f++)
                    if ((draw & (1 << f)) != 0)
                    {
                        _faceHash[sl * 6 + f] = DynFaceHash[sl * 6 + f];
                        _faceLp[sl * 6 + f] = DynFaceLp[sl * 6 + f];
                    }
                // Faces drawn last frame but not now are cleared; when a slot
                // starts using its moving map, all its other faces are too.
                int toClear = (faces != 0 && _dynFacesLast[sl] == 0 ? 63 : _dynFacesLast[sl]) & ~faces;
                if (draw != 0 && Plugin.DirectMovingDraw.Value && DrawMovingDirect(t, pl, draw, DynOrigin[sl], sl * 6, _dynFacesLast[sl] == 0))
                {
                }
                else if (draw != 0 && !Plugin.DirectMovingDraw.Value) // both tiers have equal full-size moving faces
                {
                    _dynCamera.transform.position = DynOrigin[sl];
                    _dynCamera.nearClipPlane = NearPlane(t);
                    _dynCamera.farClipPlane = Mathf.Max(1f, Range(t));
                    Shader.SetGlobalVector(CenterId, DynOrigin[sl]);
                    if (_dynCamera.RenderToCubemap(_dynFace, draw))
                        for (int f = 0; f < 6; f++)
                            if ((draw & (1 << f)) != 0)
                                Graphics.CopyTexture(_dynFace, f, 0, dynMaps, pl * 6 + f, 0);
                }
                for (int f = 0; f < 6; f++)
                    if ((toClear & (1 << f)) != 0)
                    {
                        // RGHalf requires BOTH front and back channels to equal
                        // the empty-distance sentinel, not (60000, 0).
                        if (_combine == null)
                            _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
                        _frameCb.SetRenderTarget(new RenderTargetIdentifier(dynMaps, 0, CubemapFace.Unknown, pl * 6 + f));
                        _frameCb.DrawProcedural(Matrix4x4.identity, _combine, 2, MeshTopology.Triangles, 3);
                        _dynRect[sl * 6 + f] = default;
                    }
                if (nativeMoving && Plugin.VanillaDrawing.Value)
                {
                    for (int f = 0; f < 6; f++)
                        if ((toClear & (1 << f)) != 0 && (draw & (1 << f)) == 0)
                            ClearMovingVanilla(t, pl, f);
                }
                else if (nativeMoving)
                {
                    int dirtyFaces = draw | toClear;
                    for (int f = 0; f < 6; f++)
                        if ((dirtyFaces & (1 << f)) != 0)
                        {
                            // Cleared faces and camera-drawn faces update the
                            // entire cubemap side. Only direct draws have a
                            // provably complete changed-pixel rectangle.
                            RectInt touched = Plugin.DirectMovingDraw.Value &&
                                (draw & (1 << f)) != 0 && (toClear & (1 << f)) == 0
                                ? _nativeMovingTouched[sl * 6 + f] : default;
                            QueueNativeMovingFace(t, dynMaps, pl, f, touched);
                        }
                }
                _nativeMovingActive[sl] = nativeMoving && !_nativeMovingFailed && faces != 0 ? 1f : 0f;
                _nativeMovingInvRanges[sl] = 1f / Mathf.Max(1f, Range(t));
                _dynFacesLast[sl] = faces;
                _dynActive[sl] = faces != 0 ? 1f : 0f;
                // The cached moving map now contains the result of this pickup.
                _pickupInvalidFaces[sl] = 0;
            }
            _framePending = true;
            UploadDynActive();
            UploadNativeMovingActive();
            UploadNativeMovingRanges();
            Shader.SetGlobalFloatArray(NativeNearsId, _nativeNears);
            if (buildStart != 0L)
            {
                _perfBuildTicks += System.Diagnostics.Stopwatch.GetTimestamp() - buildStart;
                _perfProbeCalls += 2;
            }
        }

        // New shadows fade in over a second, like vanilla's LightLod does.
        private void Update()
        {
            bool changed = false;
            foreach (Tracked t in _slots)
            {
                if (t == null || !t.Ready || t.Fade >= 1f)
                    continue;
                t.Fade = Mathf.Min(1f, t.Fade + Time.deltaTime);
                changed = true;
            }
            if (changed)
                UpdateGlobals();
        }

        // Moving casters drawn straight into the map, no camera: only their
        // own renderers, so nothing else in range is culled or considered.
        private static readonly Vector3[] FaceForward =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        private static readonly Vector3[] FaceUp =
        {
            Vector3.down, Vector3.down, Vector3.forward, Vector3.back, Vector3.down, Vector3.down
        };
        // Only six cubemap orientations exist. Avoid allocating quaternions
        // and inverting TRS matrices during every moving shadow redraw.
        private static readonly Matrix4x4[] FaceViewBasis = BuildFaceViews();
        private static Matrix4x4[] BuildFaceViews()
        {
            Matrix4x4[] views = new Matrix4x4[6];
            for (int f = 0; f < 6; f++)
                views[f] = Matrix4x4.TRS(Vector3.zero,
                    Quaternion.LookRotation(FaceForward[f], FaceUp[f]),
                    new Vector3(1f, 1f, -1f)).inverse;
            return views;
        }
        private CommandBuffer _frameCb;
        private bool _framePending;
        // One-shot benchmark diagnostic. Does not change regular rendering,
        // caster discovery, cached/static shadow passes or shader sampling.
        // Only skips executing the already-recorded moving GPU buffer during
        // the named diagnostic test window. No new per-frame work.
        private bool _benchmarkSkipMovingGpu;
        internal static void BenchmarkSkipMovingGpu(bool skip)
        {
            ShadowCache c = Instance;
            if (c == null || c._benchmarkSkipMovingGpu == skip) return;
            c._benchmarkSkipMovingGpu = skip;
            if (!skip)
                c.InvalidateMovingFaces(); // refresh all stale faces on return
        }

        private void OnEnable() => Camera.onPreCull += RunFrame;
        private void OnDisable() => Camera.onPreCull -= RunFrame;

        private void RunFrame(Camera cam)
        {
            // Any camera using the deferred shader (including a modded
            // secondary camera) must never read stale cube indices. Flushing
            // before the main-camera check costs nothing when unchanged.
            UploadDynPhysical();
            if (cam != Utils.GetMainCamera())
                return;
            // Right before rendering, after anything (other mods too) has
            // moved the lights this frame.
            UploadLightPositions();
            if (!_framePending)
                return;
            _framePending = false;
            // Diagnostic only: the rest of WLO and the shader still run.
            // No shadow-map draw/copy/convert commands reach the GPU.
            if (!_benchmarkSkipMovingGpu)
            {
                long submitStart = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                Graphics.ExecuteCommandBuffer(_frameCb);
                if (submitStart != 0L)
                {
                    _perfSubmitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - submitStart;
                    _perfProbeCalls += 2;
                }
            }
        }
        private static readonly Color Empty = new Color(60000f, 0, 0, 0); // farther than any light reaches
        private Material _distMaterial;


        private static readonly Dictionary<Character, Renderer[]> CharacterRenderers = new Dictionary<Character, Renderer[]>();

        private bool DrawMovingDirect(Tracked t, int slice, int faces, Vector3 lp, int rectBase, bool firstUse)
        {
            if (_distMaterial == null)
                _distMaterial = new Material(DistanceShader) { hideFlags = HideFlags.HideAndDontSave };
            if (_combine == null)
                _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
            bool fullSize = t.Slot < _near;
            RenderTexture maps = fullSize ? _dynMaps : _farDynMaps;
            // One min-blended pass per moving cubemap face.

            LightCasters.Clear();
            LightCasterFaces.Clear();
            // Membership and distance checks were already performed during
            // hash construction above (same frame, same light and radius).
            // Reusing that list avoids _groupCount repeated tests per light,
            // including the worst dense-base / post-teleport scenes.
            List<int> groupsForLight = _groupsBySlot[t.Slot];
            for (int n = 0; n < groupsForLight.Count; n++)
            {
                Group grp = Groups[groupsForLight[n]];
                for (int k = grp.Start; k < grp.Start + grp.Count; k++)
                {
                    // Skip casters with no overlap with the faces actually
                    // requested for this light. This preserves every draw
                    // and avoids carrying irrelevant geometry into 6 passes.
                    int mask = Casters[k].Faces(lp) & faces;
                    if (mask == 0) continue;
                    LightCasters.Add(k);
                    LightCasterFaces.Add(mask);
                }
            }

            float near = NearPlane(t);
            // Cube faces are stored turned 180 degrees from a camera image
            // (RenderToCubemap does this itself). The distance shader draws
            // both sides, so the flipped winding doesn't matter.
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(Matrix4x4.Scale(new Vector3(-1f, -1f, 1f)) * Matrix4x4.Perspective(90f, 1f, near, Mathf.Max(1f, Range(t))), true);
            _frameCb.SetGlobalVector(CenterId, lp);
            Matrix4x4 cpuProj = Matrix4x4.Scale(new Vector3(-1f, -1f, 1f)) * Matrix4x4.Perspective(90f, 1f, near, Mathf.Max(1f, Range(t)));
            int size = maps.width;
            Matrix4x4 lightTranslation = Matrix4x4.Translate(-lp);
            for (int f = 0; f < 6; f++)
            {
                if ((faces & (1 << f)) == 0)
                    continue;
                Matrix4x4 view = FaceViewBasis[f] * lightTranslation;
                // Only the casters in this face, and only the part of the face
                // they cover (plus what they covered last time, to erase it).
                FaceRenderers.Clear();
                RectInt rect = default;
                Matrix4x4 vp = cpuProj * view;
                for (int ci = 0; ci < LightCasters.Count; ci++)
                {
                    if ((LightCasterFaces[ci] & (1 << f)) == 0)
                        continue;
                    int k = LightCasters[ci];
                    Caster c = Casters[k];
                    // FacesFor is an intentionally conservative sphere test.
                    // AABB projection may prove a renderer covers NO pixels
                    // on this face: skip its otherwise wasted GPU draw.
                    RectInt casterRect = ScreenRect(vp, c.B, size);
                    if (casterRect.width <= 0 || casterRect.height <= 0)
                        continue;
                    FaceRenderers.Add(k);
                    rect = Union(rect, casterRect);
                }
                RectInt last = firstUse ? new RectInt(0, 0, size, size) : _dynRect[rectBase + f];
                bool testFullFace = Plugin.DisableMovingScissor.Value;
                _dynRect[rectBase + f] = testFullFace ? new RectInt(0, 0, size, size) : rect;
                rect = testFullFace ? new RectInt(0, 0, size, size) : Union(rect, last);
                // Until a face is successfully scissored, conversion must
                // conservatively use the full face.
                _nativeMovingTouched[rectBase + f] = default;
                if (rect.width <= 0)
                    continue;
                // Unity may flip render textures vertically; covering the
                // mirrored rect too keeps it right either way.
                rect = Union(rect, new RectInt(rect.x, size - rect.yMax, rect.width, rect.height));
                _nativeMovingTouched[rectBase + f] = rect;
                var sc = new Rect(rect.x, rect.y, rect.width, rect.height);
                // Direct nearest-depth map, no RGHalf intermediate and
                // no midpoint-combine pass. Existing face caching unchanged.
                _frameCb.SetRenderTarget(new RenderTargetIdentifier(maps, 0, CubemapFace.Unknown, slice * 6 + f));
                _frameCb.EnableScissorRect(sc);
                _frameCb.DrawProcedural(Matrix4x4.identity, _combine, 2, MeshTopology.Triangles, 3);
                _frameCb.SetViewProjectionMatrices(view, proj);
                if (Plugin.VanillaDrawing.Value && DrawMovingVanilla(t, slice, f, view, near, Mathf.Max(1f, Range(t)), sc))
                {
                    _frameCb.DisableScissorRect();
                    _statFaces++;
                    _statSlotFaces[t.Slot]++;
                    _statPixels += rect.width * rect.height;
                    continue;
                }
                _faceFabric.Clear();
                foreach (int k in FaceRenderers)
                {
                    Caster c = Casters[k];
                    // Bent cloth only once the native path has proven itself (or is
                    // still calibrating); otherwise a plain moving shadow, no sway.
                    if (FabricDraws.TryGetValue(c.R, out FabricDraw fd) && FabricSway)
                    {
                        _faceFabric.Add(fd);
                        if (_calibState == 2) continue;
                    }
                    for (int sub = 0; sub < c.Subs; sub++)
                        _frameCb.DrawRenderer(c.R, _distMaterial, sub, 0);
                    _statDraws += c.Subs;
                }
                if (_faceFabric.Count > 0)
                    DrawFabricFace(maps, slice * 6 + f, f, view, proj, sc, size, near, Mathf.Max(1f, Range(t)));
                _frameCb.DisableScissorRect();
                _statFaces++;
                _statSlotFaces[t.Slot]++;
                _statPixels += rect.width * rect.height;
            }
            return true;
        }

        private static readonly List<int> FaceRenderers = new List<int>();
        // Populated while computing per-light face hashes. Drawing uses the
        // EXACT same frame's membership, eliminating a second spatial pass
        // over all moving groups for every light requiring redraw.
        // Lists grow once per slot and are reused without per-frame allocation.
        private readonly List<int>[] _groupsBySlot = new List<int>[MaxSlots];
        private static readonly List<int> LightCasters = new List<int>();
        private static readonly List<int> LightCasterFaces = new List<int>();

        // Moving casters, gathered once a frame: a group per character, loose
        // item, swinging door and the preview, with each renderer's bounds and
        // submesh count, so the lights don't each look them up again.
        private struct Caster
        {
            internal Renderer R;
            internal Bounds B;
            internal int Subs;
            internal float Radius;
            // Bounding radius is identical for all lights in this frame.
            internal int Faces(Vector3 lp) => FacesFor(B.center - lp, Radius);
        }

        private sealed class Group
        {
            internal Vector3 Center;
            internal float Radius;
            internal int Hash;
            internal bool Character;
            internal int Start, Count;
        }

        private static readonly List<Caster> Casters = new List<Caster>();
        private static readonly List<Group> Groups = new List<Group>();
        private static int _groupCount;

        // Broadphase for dense encounters. Fixed camera-relative cells ensure
        // long world travel cannot grow a persistent dictionary indefinitely.
        // All lists are reused, and only touched cells are cleared each frame.
        // Objects are bucketed by CENTER; the query widens by the largest
        // group radius, so even huge objects remain conservatively included.
        private const int MovingGridWidth = 16;
        private const float MovingGridSize = 24f;
        private const float MovingGridHalf = MovingGridWidth * MovingGridSize * 0.5f;
        private static readonly List<int>[] MovingGrid = new List<int>[MovingGridWidth * MovingGridWidth];
        private static readonly List<int> MovingGridTouched = new List<int>(64);
        private static readonly List<int> MovingGridOverflow = new List<int>(16);
        private static readonly List<int> MovingGridQuery = new List<int>(128);
        private static float MovingGridX, MovingGridZ, MovingGridMaxRadius;

        private static void BuildMovingGroupGrid(Vector3 cameraPosition)
        {
            if (_groupCount <= 64) return; // Linear scan wins for small encounters.
            foreach (int index in MovingGridTouched)
                MovingGrid[index].Clear();
            MovingGridTouched.Clear();
            MovingGridOverflow.Clear();
            MovingGridX = cameraPosition.x - MovingGridHalf;
            MovingGridZ = cameraPosition.z - MovingGridHalf;
            MovingGridMaxRadius = 0f;
            for (int g = 0; g < _groupCount; g++)
            {
                Group group = Groups[g];
                MovingGridMaxRadius = Mathf.Max(MovingGridMaxRadius, group.Radius);
                int x = Mathf.FloorToInt((group.Center.x - MovingGridX) / MovingGridSize);
                int z = Mathf.FloorToInt((group.Center.z - MovingGridZ) / MovingGridSize);
                if (x < 0 || x >= MovingGridWidth || z < 0 || z >= MovingGridWidth)
                {
                    MovingGridOverflow.Add(g);
                    continue;
                }
                int index = z * MovingGridWidth + x;
                List<int> cell = MovingGrid[index];
                if (cell == null) MovingGrid[index] = cell = new List<int>(8);
                if (cell.Count == 0) MovingGridTouched.Add(index);
                cell.Add(g);
            }
        }

        private static List<int> MovingGroupCandidates(Vector3 lightPosition, float range)
        {
            float radius = range + MovingGridMaxRadius;
            int x0 = Mathf.FloorToInt((lightPosition.x - radius - MovingGridX) / MovingGridSize);
            int x1 = Mathf.FloorToInt((lightPosition.x + radius - MovingGridX) / MovingGridSize);
            int z0 = Mathf.FloorToInt((lightPosition.z - radius - MovingGridZ) / MovingGridSize);
            int z1 = Mathf.FloorToInt((lightPosition.z + radius - MovingGridZ) / MovingGridSize);
            // When nearly every cell can contribute, avoid sorting a large
            // candidate set and just use the original sequential traversal.
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) >= MovingGridWidth * MovingGridWidth)
                return null;
            MovingGridQuery.Clear();
            x0 = Mathf.Max(0, x0); x1 = Mathf.Min(MovingGridWidth - 1, x1);
            z0 = Mathf.Max(0, z0); z1 = Mathf.Min(MovingGridWidth - 1, z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    List<int> cell = MovingGrid[z * MovingGridWidth + x];
                    if (cell != null)
                        MovingGridQuery.AddRange(cell);
                }
            MovingGridQuery.AddRange(MovingGridOverflow);
            if (MovingGridQuery.Count >= _groupCount * 3 / 4)
                return null;
            // The exact original group order is essential: face hash = hash
            // * 31 + group.Hash, so an unordered grid changes the hash.
            MovingGridQuery.Sort();
            return MovingGridQuery;
        }
        private static readonly Dictionary<Renderer, int> SubCounts = new Dictionary<Renderer, int>();

        // In reach of the light. A character only while it's well inside:
        // out near the edge the light is too dim to show its shadow.
        private static bool Within(Group g, Vector3 lp, float range)
        {
            float r = g.Character ? range * DimEdge : range + g.Radius;
            return (g.Center - lp).sqrMagnitude <= r * r;
        }

        private const float DimEdge = 0.8f;

        private void GatherCasters(Vector3 cam, List<Character> characters)
        {
            Casters.Clear();
            _groupCount = 0;
            float far = Plugin.Distance.Value + 30f;
            float far2 = far * far;
            foreach (Character c in characters)
            {
                if (c == null || (c.transform.position - cam).sqrMagnitude > far2)
                    continue;
                if (!CharacterRenderers.TryGetValue(c, out Renderer[] rs) || rs == null)
                    CharacterRenderers[c] = rs = System.Array.FindAll(c.GetComponentsInChildren<Renderer>(true), r => !SeeThrough(r));
                // Animated every frame: always redrawn (a pose check misses arms and legs).
                AddGroup(c.transform.position + Vector3.up, 1.2f, Time.frameCount, true, rs, true);
            }
            foreach (ItemDrop it in Items)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                Transform tr = it.transform;
                Vector3 pos = tr.position;
                if ((pos - cam).sqrMagnitude > far2) continue;
                DropCaster d = GetDrop(it, pos);
                if (d.Mounted) continue;
                // Sleeping bodies may also move via ZDO/mod updates. Compare
                // against the LAST observed pose (even during transitions),
                // so continuously network-moved items never falsely settle.
                bool movedPose =
                    (pos - d.CurrentPosition).sqrMagnitude > 0.0004f ||
                    Quaternion.Angle(tr.rotation, d.CurrentRotation) > 0.5f;
                bool moving = (d.Body != null && !d.Body.IsSleeping()) || movedPose;
                // Moving-map cubemap faces are persistent: a settled item stays
                // present but its unchanged hash skips GPU re-rendering.
                // On pickup, the missing group invalidates its face instantly.
                if (moving)
                {
                    d.LastMotion = Time.time;
                    d.Stable = false;
                }
                else if (!d.Stable && Time.time - d.LastMotion >= ItemSettle)
                {
                    d.Stable = true;
                    d.StaticPosition = pos;
                    d.StaticRotation = tr.rotation;
                }
                d.Handoff = false;
                d.CurrentPosition = pos;
                d.CurrentRotation = tr.rotation;
                AddGroup(pos, ItemRadius,
                    Quantize(pos, 200f) * 7 + Quantize(tr.eulerAngles, 2f),
                    false, d.Renderers, false);
            }
            if (_ghostActive)
                AddGroup(_ghostCenter, _ghostRadius, _ghostHash, false, _ghostRenderers, true); // honor source prefab's shadow exclusions
            foreach (Swing w in Swings)
                AddGroup(w.Center, w.Radius, Time.frameCount, false, w.Renderers, false); // moving every frame of the swing
            AddMachineGroups(cam, far);
            AddFabricGroups(cam);
            // A mounted item participates in the moving map ONLY during
            // the short static-cache handoff, like a closing gate.
            foreach (StandCaster s in PendingStands)
                if (s.Handoff && s.Stand != null && s.Stand.gameObject.activeInHierarchy &&
                    s.Geometry != null && s.Geometry.Length > 0 &&
                    (s.Center - cam).sqrMagnitude <
                        (far + s.Radius) * (far + s.Radius))
                    AddStandGroup(s);
        }

        private void AddStandGroup(StandCaster s)
        {
            int start = Casters.Count;
            foreach (Caster caster in s.Geometry)
                if (caster.R != null && caster.R.enabled &&
                    caster.R.gameObject.activeInHierarchy)
                    Casters.Add(caster);
            if (Casters.Count == start) return;
            if (_groupCount == Groups.Count) Groups.Add(new Group());
            Group g = Groups[_groupCount++];
            g.Center = s.Center;
            g.Radius = s.Radius;
            g.Hash = s.Version;
            g.Character = false;
            g.Start = start;
            g.Count = Casters.Count - start;
        }

        private void AddGroup(Vector3 center, float radius, int hash, bool character, Renderer[] rs, bool checkCasting)
        {
            int start = Casters.Count;
            foreach (Renderer r in rs)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || checkCasting && r.shadowCastingMode == ShadowCastingMode.Off)
                    continue;
                if (!SubCounts.TryGetValue(r, out int subs))
                {
                    if (SubCounts.Count > 8192)
                        SubCounts.Clear();
                    subs = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.sharedMesh.subMeshCount
                        : r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                    SubCounts[r] = subs;
                }
                Bounds rb = r.bounds;
                Casters.Add(new Caster { R = r, B = rb, Radius = rb.extents.magnitude, Subs = subs });
            }
            if (Casters.Count == start)
                return;
            if (_groupCount == Groups.Count)
                Groups.Add(new Group());
            Group g = Groups[_groupCount++];
            g.Center = center;
            g.Radius = radius;
            g.Hash = hash;
            g.Character = character;
            g.Start = start;
            g.Count = Casters.Count - start;
        }

        private static RectInt Union(RectInt a, RectInt b)
        {
            if (a.width <= 0) return b;
            if (b.width <= 0) return a;
            int x = Mathf.Min(a.x, b.x), y = Mathf.Min(a.y, b.y);
            return new RectInt(x, y, Mathf.Max(a.xMax, b.xMax) - x, Mathf.Max(a.yMax, b.yMax) - y);
        }

        // Pixels of a face a box covers, 2 px padded; all of it if the box
        // reaches behind the light.
        private static RectInt ScreenRect(Matrix4x4 vp, Bounds b, int size)
        {
            float x0 = 1f, y0 = 1f, x1 = -1f, y1 = -1f;
            Vector3 c = b.center, e = b.extents;
            bool anyFront = false, crossesCameraPlane = false;
            for (int i = 0; i < 8; i++)
            {
                Vector4 p = vp * new Vector4(c.x + ((i & 1) != 0 ? e.x : -e.x), c.y + ((i & 2) != 0 ? e.y : -e.y), c.z + ((i & 4) != 0 ? e.z : -e.z), 1f);
                if (p.w <= 0.01f)
                {
                    crossesCameraPlane = true;
                    continue;
                }
                anyFront = true;
                float x = p.x / p.w, y = p.y / p.w;
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
            // Perspective w is affine over the convex AABB. If ALL corners
            // are behind the camera, the whole box is behind it: zero pixels.
            // A box crossing the camera plane is still conservatively FULL.
            if (!anyFront)
                return default;
            if (crossesCameraPlane)
                return new RectInt(0, 0, size, size);
            int px0 = Mathf.Clamp(Mathf.FloorToInt((x0 * 0.5f + 0.5f) * size) - 2, 0, size);
            int py0 = Mathf.Clamp(Mathf.FloorToInt((y0 * 0.5f + 0.5f) * size) - 2, 0, size);
            int px1 = Mathf.Clamp(Mathf.CeilToInt((x1 * 0.5f + 0.5f) * size) + 2, 0, size);
            int py1 = Mathf.Clamp(Mathf.CeilToInt((y1 * 0.5f + 0.5f) * size) + 2, 0, size);
            return px1 > px0 && py1 > py0 ? new RectInt(px0, py0, px1 - px0, py1 - py0) : default;
        }

        // Reporting piggybacks on the already-existing LateUpdate -> Stats call.
        // No additional Update, coroutine, timer, polling or scene scan per frame.
        // All metadata and expensive formatting run once per 10 seconds.
        private const float ProfilePeriod = 10f;
        private int _statDraws, _statFaces, _statFrames, _statStatic;
        private readonly int[] _statSlotFaces = new int[MaxSlots];
        private long _statPixels;
        private double _statCpu;
        // Peak diagnostics reuse the existing DebugLog Stopwatch. No extra profiler,
        // Update callback, frame scan, or timer; one 10-second log summary.
        private double _maxLateCpuMs, _maxGatherMs, _maxScanMs, _maxBuildMs;
        private double _maxFrameDeltaMs, _maxNearSpawnFrameDeltaMs;
        private int _maxFrameDeltaAt, _maxNearSpawnFrameDeltaAt, _framesOver33ms, _framesOver50ms;
        private double _maxSpawnAdjacentCpuMs;
        private int _maxLateFrame, _maxSpawnAdjacentFrame, _spawnsInWindow;
        private string _lastSpawnPrefab = "(none)", _maxSpawnPrefab = "(none)";
        private int _lastSpawnFrame = -999999;
        internal static void RecordSpawn(Character character)
        {
            if (Instance == null || character == null || character is Player ||
                !Plugin.DebugLog.Value || !(character.GetBaseAI() is MonsterAI)) return;
            Instance._spawnsInWindow++;
            Instance._lastSpawnFrame = Time.frameCount;
            Instance._lastSpawnPrefab = character.gameObject.name;
            // No per-spawn log formatting: count once at existing report.
        }
        private float _statSince;
        private readonly System.Diagnostics.Stopwatch _statWatch = new System.Diagnostics.Stopwatch();

        // Stopwatch clock time is CPU wall time, not GPU execution time.
        private long _perfGatherTicks, _perfScanTicks, _perfBuildTicks;
        private long _perfStaticTicks, _perfReassignTicks, _perfSubmitTicks;
        private long _perfReassignStart, _perfEventLogTicks, _perfReportLogTicks;
        private long _perfProbeCalls;
        private double _perfClockTicksPerProbe;
        private static long _calibrationSink;

        private static readonly System.Reflection.FieldInfo MapPinsField =
            HarmonyLib.AccessTools.Field(typeof(Minimap), "m_pins");

        private static string ProfileLocation()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "player=not spawned; biome=unknown; baseMarker=unavailable";
            Vector3 pos = player.transform.position;
            string biome = WorldGenerator.instance != null
                ? WorldGenerator.instance.GetBiome(pos).ToString() : "unknown";
            string location = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "player=({0:F0},{1:F0},{2:F0}); biome={3}",
                pos.x, pos.y, pos.z, biome);
            Minimap map = Minimap.instance;
            if (map == null || MapPinsField == null) return location + "; baseMarker=map unavailable";
            try
            {
                var pins = MapPinsField.GetValue(map) as System.Collections.IEnumerable;
                if (pins == null) return location + "; baseMarker=none";
                Minimap.PinData best = null;
                int priority = 0;
                float bestDistance = float.MaxValue;
                foreach (Minimap.PinData pin in pins)
                {
                    if (pin == null) continue;
                    string name = pin.m_name;
                    // Prefer the user's actual named Base map marker. If it
                    // does not exist, report Home or the in-game Bed/respawn
                    // marker explicitly; do not invent a base location.
                    int p = !string.IsNullOrEmpty(name) &&
                        name.IndexOf("base", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 3 :
                        !string.IsNullOrEmpty(name) &&
                        name.IndexOf("home", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 2 :
                        pin.m_type == Minimap.PinType.Bed ? 1 : 0;
                    if (p == 0) continue;
                    float distance = Vector2.Distance(
                        new Vector2(pos.x, pos.z), new Vector2(pin.m_pos.x, pin.m_pos.z));
                    if (p > priority || p == priority && distance < bestDistance)
                    {
                        priority = p;
                        best = pin;
                        bestDistance = distance;
                    }
                }
                if (best == null) return location + "; baseMarker=none (no Base/Home/Bed pin)";
                return location + string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "; baseMarker={0}:{1} at ({2:F0},{3:F0}), distance={4:F0}m",
                    priority == 1 ? "Bed/spawn" : "named",
                    string.IsNullOrEmpty(best.m_name) ? "(unnamed)" : best.m_name,
                    best.m_pos.x, best.m_pos.z, bestDistance);
            }
            catch (System.Exception ex)
            {
                return location + "; baseMarker=unavailable (" + ex.GetType().Name + ")";
            }
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private void CalibrateProfilerClock()
        {
            if (_perfClockTicksPerProbe > 0) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 512; i++)
                _calibrationSink ^= System.Diagnostics.Stopwatch.GetTimestamp();
            _perfClockTicksPerProbe = System.Math.Max(1.0,
                (System.Diagnostics.Stopwatch.GetTimestamp() - start) / 512.0);
        }

        private void Stats()
        {
            if (_statSince <= 0f)
            {
                _statSince = Time.unscaledTime;
                _statFrames = 0;
                return;
            }
            _statFrames++;
            float seconds = Time.unscaledTime - _statSince;
            if (seconds < ProfilePeriod) return;
            if (Plugin.DebugLog.Value && _statFrames > 0)
            {
                long reportStart = System.Diagnostics.Stopwatch.GetTimestamp();
                CalibrateProfilerClock();
                // Time/frame uses the complete measured wall-clock interval,
                // not just the mod's CPU stopwatch. This is the game's
                // approximate average frametime at the reporting location.
                double frameMs = 1000.0 * seconds / _statFrames;
                double fps = _statFrames / (double)seconds;
                double frames = _statFrames;
                double avgTotalCpu = _statCpu / frames;
                double gather = TicksToMs(_perfGatherTicks) / frames;
                double scan = TicksToMs(_perfScanTicks) / frames;
                double build = TicksToMs(_perfBuildTicks) / frames;
                double staticCpu = TicksToMs(_perfStaticTicks) / frames;
                double reassign = TicksToMs(_perfReassignTicks) / frames;
                double submit = TicksToMs(_perfSubmitTicks) / frames;
                // The 'reassign' category may overlap with LateUpdateWork,
                // so do not claim these categories add to 100% of total work.
                double largest = System.Math.Max(System.Math.Max(gather, scan), System.Math.Max(build,
                    System.Math.Max(staticCpu, reassign)));
                string largestName = largest == gather ? "moving-caster gathering/grid" :
                    largest == scan ? "per-light/face hashing" :
                    largest == build ? "moving-map command building" :
                    largest == staticCpu ? "static cube rendering (CPU)" : "light reassign";
                double eventLogMs = TicksToMs(_perfEventLogTicks) / frames;
                double reportLogMs = TicksToMs(_perfReportLogTicks) / frames;
                _perfReportLogTicks = 0; // Cost of current report is charged to the next interval.
                double probesMs = TicksToMs((long)(_perfProbeCalls * _perfClockTicksPerProbe)) / frames;
                double loggingOverhead = eventLogMs + reportLogMs + probesMs;

                Plugin.Log.LogInfo(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO profile] last={0:F1}s frames={1} avgFrame={2:F2}ms avgFPS={3:F1} WLO_LateUpdate_CPU={4:F3}ms/frame ({5:F1}% of total frame)",
                    seconds, _statFrames, frameMs, fps, avgTotalCpu,
                    100.0 * avgTotalCpu / System.Math.Max(0.001, frameMs)));
                Plugin.Log.LogInfo("[WLO profile] location " + ProfileLocation());
                Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO spike] 10s PEAK WLO LateUpdate CPU={0:F3}ms at frame={1}; peakGather={2:F3}ms peakLightFaceScan={3:F3}ms peakMovingBuild={4:F3}ms. Individual maxima may belong to different frames; not GPU timing.",
                    _maxLateCpuMs, _maxLateFrame, _maxGatherMs, _maxScanMs, _maxBuildMs));
                Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO hitch] worstUnityFrameDelta={0:F2}ms at frame={1} framesOver33ms={2} framesOver50ms={3} maxWithin3FramesOfEnemyInit={4:F2}ms at frame={5}. Frame delta is previous Unity frame duration; not WLO CPU or GPU.",
                    _maxFrameDeltaMs, _maxFrameDeltaAt, _framesOver33ms, _framesOver50ms, _maxNearSpawnFrameDeltaMs, _maxNearSpawnFrameDeltaAt));                Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO spawn correlation] enemyInstantiations={0}; mostRecent={1} at frame={2}; maxWloCpuWithin2FramesOfSpawn={3:F3}ms at frame={4} near={5}; correlation ONLY, not proof of WLO-induced spawning lag.",
                    _spawnsInWindow, _lastSpawnPrefab, _lastSpawnFrame, _maxSpawnAdjacentCpuMs, _maxSpawnAdjacentFrame, _maxSpawnPrefab));
                Plugin.Log.LogInfo(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO profile] measured CPU ms/frame: casterGather+grid={0:F3} ({1:F1}% frame); lightFaceScan={2:F3} ({3:F1}% frame); movingBuild={4:F3} ({5:F1}% frame); staticFaces={6:F3}; lightReassign={7:F3}; GPU_commandSubmit_CPU={8:F3}; largest={9}. Sections may overlap; static/reassign also run outside LateUpdate.",
                    gather, 100*gather/System.Math.Max(frameMs,0.001), scan, 100*scan/System.Math.Max(frameMs,0.001),
                    build, 100*build/System.Math.Max(frameMs,0.001), staticCpu, reassign, submit, largestName));
                int characterGroups = 0;
                for (int g = 0; g < _groupCount; g++)
                    if (Groups[g].Character) characterGroups++;
                Plugin.Log.LogInfo(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO profile] GPU WORKLOAD PROXIES (NOT GPU TIME): movingFaces={0:F1}/frame; rendererDraws={1:F1}/frame; touchedPixels={2:F2}M/frame; staticFaces={3:F2}/frame; movingGroupsNow={4} (characters={5}, other={6}). GPU milliseconds require an external GPU profiler.",
                    (double)_statFaces / frames, (double)_statDraws / frames,
                    _statPixels / 1e6 / frames, (double)_statStatic / frames, _groupCount,
                    characterGroups, _groupCount - characterGroups));
                Plugin.Log.LogInfo(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[WLO profile] DIAGNOSTIC OVERHEAD (estimated): {0:F4}ms/frame ({1:F3}% of frame); eventLog={2:F4}ms/frame; lastReportFormatting+Logging={3:F4}ms/frame (PREVIOUS interval); timestampProbes~{4:F4}ms/frame. Current report cost appears in NEXT interval.",
                    loggingOverhead, 100*loggingOverhead/System.Math.Max(frameMs,0.001),
                    eventLogMs, reportLogMs, probesMs));
                // Only useful light-specific breakdown, bounded to 6 names.
                var sb = new System.Text.StringBuilder("[WLO profile] busiest moving-shadow lights:");
                for (int rank = 0; rank < 6; rank++)
                {
                    int chosen = -1, bestCount = 0;
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        int count = _statSlotFaces[i];
                        if (count <= bestCount || _slots[i] == null || _slots[i].Light == null)
                            continue;
                        chosen = i;
                        bestCount = count;
                    }
                    if (chosen < 0) break;
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        " {0}:{1}={2:F1} faces/frame;", chosen,
                        NameOf(_slots[chosen].Light), bestCount / frames);
                    _statSlotFaces[chosen] = -bestCount;
                }
                for (int i = 0; i < _statSlotFaces.Length; i++)
                    if (_statSlotFaces[i] < 0) _statSlotFaces[i] = -_statSlotFaces[i];
                Plugin.Log.LogInfo(sb.ToString());
                EnginePerf.Report(frameMs, avgTotalCpu);
                DeepCpuProfiler.Report(_statFrames, seconds);
                if (Plugin.SceneProbe != null && Plugin.SceneProbe.Value)
                    SceneRenderProbe.Report();
                _perfReportLogTicks += System.Diagnostics.Stopwatch.GetTimestamp() - reportStart;
            }
            System.Array.Clear(_statSlotFaces, 0, _statSlotFaces.Length);
            _statDraws = _statFaces = _statFrames = _statStatic = 0;
            _statPixels = 0;
            _statCpu = 0;
            _maxLateCpuMs = _maxGatherMs = _maxScanMs = _maxBuildMs = 0;
            _maxSpawnAdjacentCpuMs = 0;
            _maxLateFrame = _maxSpawnAdjacentFrame = 0;
            _maxFrameDeltaMs = _maxNearSpawnFrameDeltaMs = 0;
            _maxFrameDeltaAt = _maxNearSpawnFrameDeltaAt = _framesOver33ms = _framesOver50ms = 0;
            _spawnsInWindow = 0;
            _perfGatherTicks = _perfScanTicks = _perfBuildTicks = 0;
            _perfStaticTicks = _perfReassignTicks = _perfSubmitTicks = 0;
            _perfEventLogTicks = 0;
            _perfProbeCalls = 0;
            // Report cost belongs to the NEXT interval. Do not clear it.
            _statSince = Time.unscaledTime;
        }
        private static int Quantize(Vector3 v, float scale) =>
            Mathf.RoundToInt(v.x * scale) * 73856093 ^ Mathf.RoundToInt(v.y * scale) * 19349663 ^ Mathf.RoundToInt(v.z * scale) * 83492791;

        // Cube faces (+X -X +Y -Y +Z -Z) a sphere of the given radius at
        // offset d from the light can appear in (a face's sides are 45 degree
        // planes: the sphere reaches past one within radius * sqrt 2).
        private static int FacesFor(Vector3 d, float radius)
        {
            // Same six half-space tests as before, without Vector3
            // indexer/modulo operations in the light x caster hot path.
            float x = d.x, y = d.y, z = d.z;
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y), az = Mathf.Abs(z);
            float xOther = Mathf.Max(ay, az);
            float yOther = Mathf.Max(az, ax);
            float zOther = Mathf.Max(ax, ay);
            float margin = radius * 1.4143f;
            int faces = 0;
            if (x + radius > 0f && x + margin >= xOther) faces |= 1;
            if (x - radius < 0f && -x + margin >= xOther) faces |= 2;
            if (y + radius > 0f && y + margin >= yOther) faces |= 4;
            if (y - radius < 0f && -y + margin >= yOther) faces |= 8;
            if (z + radius > 0f && z + margin >= zOther) faces |= 16;
            if (z - radius < 0f && -z + margin >= zOther) faces |= 32;
            return faces;
        }

        // ---- Rendering ----

        private void WakeRenders()
        {
            if (!_rendering)
                StartCoroutine(RenderDirty());
        }

        // Waiting maps, most important first: a whole map per frame for lights
        // near the camera, a face per frame for the rest.
        private System.Collections.IEnumerator RenderDirty()
        {
            _rendering = true;
            int burst = 0;
            while (true)
            {
                Tracked job = null;
                foreach (Tracked t in _slots)
                {
                    if (t == null || t.Light == null)
                        continue;
                    if (t.Face >= 0) { job = t; break; }
                    if (((t.DoorFaces != 0 && t.Ready && !t.Dirty) ||
                         (t.Dirty && (Time.time >= t.DirtyAt || Loading()))) &&
                        (job == null || t.Priority < job.Priority))
                        job = t;
                }
                if (job == null)
                {
                    burst = 0;
                    bool waiting = false;
                    foreach (Tracked t in _slots)
                        if (t != null && t.Dirty) waiting = true;
                    if (!waiting)
                        break;
                    yield return null;
                    continue;
                }
                // Gate/shutter changes render one affected face per frame.
                // Never batch all nearby lights inside Door.SetState.
                if (job.DoorFaces != 0 && job.Ready && !job.Dirty && job.Face < 0)
                {
                    RenderDoorFace(job);
                    burst = 0;
                    yield return null;
                    continue;
                }
                // A new map close to the camera: whole this frame (nothing to
                // show until it exists). Re-renders go face by face into the
                // spare cube and swap in when done; far new maps face by face.
                Camera cam = Utils.GetMainCamera();
                if (Loading() && job.Face < 0 && !job.Ready && cam != null &&
                    Vector3.Distance(job.Light.transform.position, cam.transform.position) < Range(job) + UrgentMargin)
                {
                    RenderNow(job);
                    burst += 5;
                }
                else
                    RenderFace(job);
                // Behind a loading screen nobody sees the frame time: render
                // a lot per frame, so none pop in afterwards. Still capped, so
                // one frame never holds the whole cache's work.
                if (Loading() && ++burst < LoadingFacesPerFrame)
                    continue;
                burst = 0;
                yield return null;
            }
            _rendering = false;
        }

        // Reuse the same caster collection for all faces in this door event.
        // The normal static renderer still owns its six-face map refreshes.
        private void RenderDoorFace(Tracked t)
        {
            if (!t.DoorPrepared)
            {
                t.Center = RestingPosition(t.Light);
                t.RootPos = t.Light.transform.root.position;
                t.RootRot = t.Light.transform.root.rotation;
                FindCasters(t);
                t.DoorPrepared = true;
            }
            int face = 0;
            while ((t.DoorFaces & (1 << face)) == 0) face++;
            t.Face = face;
            DrawFace(t);
            t.Face = -1;
            t.DoorFaces &= ~(1 << face);
            if (t.DoorFaces == 0) t.DoorPrepared = false;
        }

        private void RenderNow(Tracked t)
        {
            t.Dirty = true;
            do RenderFace(t); while (t.Face >= 0 && t.Light != null);
        }

        private static float _arrivalStreamUntil = -10f;

        // Event paths only: a few seconds for destination LightLod.OnEnable
        // callbacks and object destruction to join a shared reassignment.
        // This does NOT enable loading-only expensive GPU bursts.
        private static bool TeleportStreaming()
        {
            Player player = Player.m_localPlayer;
            return (player != null && player.IsTeleporting()) ||
                   Time.time < _arrivalStreamUntil;
        }

        private static bool Loading()
        {
            Player p = Player.m_localPlayer;
            if (p == null)
                return true;
            if (p.IsTeleporting())
            {
                _teleporting = true;
                return true;
            }
            // Once Valheim reports teleportation ended, visible gameplay
            // has resumed. Stop loading-only multi-face shadow bursts here.
            // Unfinished maps use the normal frame budget and fade-in.
            if (_teleporting)
            {
                _teleporting = false;
                _spawnedAt = -10f;
                _arrivalStreamUntil = Time.time + 2.5f;
                return false;
            }
            return Time.time - _spawnedAt < 1.5f; // Initial spawn only.
        }

        private static bool _teleporting;
        // Lights loaded in while loading get slotted on the next frame, not
        // the next 1 s Reassign: by the time you see them they're done.
        private static bool _reassignSoon;

        private static float _spawnedAt = -10f;

        // Spawning (world load, respawn): pick the slots right away, so their
        // maps render behind the loading screen instead of a second later.
        internal static void Spawned()
        {
            _spawnedAt = Time.time;
            if (Instance != null)
                Instance.Reassign();
        }

        private void RenderFace(Tracked t)
        {
            if (t.Face < 0)
            {
                // A full refresh supersedes any partial door-face updates.
                t.DoorFaces = 0;
                t.DoorPrepared = false;
                t.Dirty = false;
                t.Face = 0;
                int tier = t.Slot < _near ? 0 : 1;
                t.Staged = t.Ready && _stageOwner[tier] == null;
                t.NativeStaged = false;
                if (t.Staged) _stageOwner[tier] = t;
                // LightFlicker wiggles the light; render from where its parent puts it.
                t.Center = RestingPosition(t.Light);
                t.RootPos = t.Light.transform.root.position;
                t.RootRot = t.Light.transform.root.rotation;
                FindCasters(t);
            }
            DrawFace(t);
            if (++t.Face >= 6)
                FinishMap(t);
        }

        private void DrawFace(Tracked t)
        {
            long staticStart = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            if (Plugin.VanillaDrawing.Value && DrawFaceVanilla(t))
            {
                _statStatic++;
                if (staticStart != 0L) { _perfStaticTicks += System.Diagnostics.Stopwatch.GetTimestamp() - staticStart; _perfProbeCalls += 2; }
                return;
            }
            _camera.transform.position = t.Center;
            // The near plane is cut as a sphere in the shaders (with the
            // colliders drawn inside it), so the camera starts at the light.
            _camera.nearClipPlane = 0.05f;
            Shader.SetGlobalFloat(NearCutId, NearPlane(t));
            _camera.farClipPlane = Mathf.Max(1f, Range(t));
            Shader.SetGlobalVector(CenterId, t.Center);

            foreach (Renderer r in t.Hide) if (r != null) r.forceRenderingOff = true;
            foreach (Renderer r in t.Show) if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
            bool near = t.Slot < _near;
            RenderTexture face = near ? _face : _farFace;
            // Two passes: surfaces facing the light, then facing away; the map
            // keeps the midpoint (see WireShadowCombine).
            RenderTexture back = near ? _faceBack : _farFaceBack;
            _camera.SetReplacementShader(FrontShader, "RenderType");
            DrawUntagged(t, ref _frontMat, FrontShader, true);
            _statStatic++;
            bool ok = _camera.RenderToCubemap(face, 1 << t.Face);
            _camera.SetReplacementShader(BackShader, "RenderType");
            DrawUntagged(t, ref _backMat, BackShader, false);
            ok &= _camera.RenderToCubemap(back, 1 << t.Face);
            Shader.SetGlobalFloat(NearCutId, 0f);
            _staticCb.Clear();
            foreach (Renderer r in t.Hide) if (r != null) r.forceRenderingOff = false;
            foreach (Renderer r in t.Show) if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            if (ok)
            {
                RenderTexture a = near ? _flatFront : _farFlatFront, b = near ? _flatBack : _farFlatBack;
                Graphics.CopyTexture(face, t.Face, 0, a, 0, 0);
                Graphics.CopyTexture(back, t.Face, 0, b, 0, 0);
                if (_combine == null)
                    _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
                if (_combineCb == null)
                    _combineCb = new CommandBuffer { name = "WireShadowCombine" };
                _combineCb.Clear();
                RenderTexture target = t.Staged ? (near ? _stage : _farStage) : near ? _maps : _farMaps;
                int slice = t.Staged ? t.Face : (near ? t.Slot : t.Slot - _near) * 6 + t.Face;
                _combineCb.SetRenderTarget(new RenderTargetIdentifier(target, 0, CubemapFace.Unknown, slice));
                _combineCb.SetGlobalTexture(FrontId, a);
                _combineCb.SetGlobalTexture(BackId, b);
                _combineCb.DrawProcedural(Matrix4x4.identity, _combine, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(_combineCb);
                CacheNativeFace(t, target, slice, near);
            }
            if (staticStart != 0L)
            {
                _perfStaticTicks += System.Diagnostics.Stopwatch.GetTimestamp() - staticStart;
                _perfProbeCalls += 2;
            }
        }

        private void FinishMap(Tracked t)
        {
            bool near = t.Slot < _near;
            {
                t.Face = -1;
                if (t.Staged)
                {
                    // Swap the finished map in, all six faces at once.
                    RenderTexture stage = near ? _stage : _farStage;
                    int first = (near ? t.Slot : t.Slot - _near) * 6;
                    for (int f = 0; f < 6; f++)
                        Graphics.CopyTexture(stage, f, 0, near ? _maps : _farMaps, first + f, 0);
                    // The native comparison map used by the lighting shader
                    // also needs an atomic six-face swap. It must remain live
                    // throughout the staged refresh, not drop to unshadowed.
                    RenderTexture nativeStage = near ? _nativeStageNear : _nativeStageFar;
                    RenderTexture nativeDst = near ? _nativeNearMaps : _nativeFarMaps;
                    if (t.NativeStaged && nativeStage != null && nativeStage.IsCreated() && nativeDst != null &&
                        _nativeOwners[t.Slot] == t)
                    {
                        for (int f = 0; f < 6; f++)
                            Graphics.CopyTexture(nativeStage, f, 0, nativeDst, first + f, 0);
                        _nativeFaceMasks[t.Slot] = 63;
                    }
                    _stageOwner[near ? 0 : 1] = null;
                    t.Staged = false;
                    t.NativeStaged = false;
                    Check("re-rendered in the background and swapped in: " + NameOf(t.Light));
                }
                if (!t.Ready || t.Instant)
                    t.Fade = !t.Ready && !t.Instant && !Loading() ? 0f : 1f;
                t.Instant = false;
                t.Ready = true;
                t.PublishedCenter = t.Center;
                UpdateGlobals();
            }
        }

        private static readonly List<Renderer> Renderers = new List<Renderer>();
        private static readonly Plane[] Frustum = new Plane[6];

        private static readonly HashSet<Transform> Roots = new HashSet<Transform>();
        private static readonly HashSet<Renderer> OwnHidden = new HashSet<Renderer>();
        private static readonly Collider[] Hits = new Collider[2048];

        // Same casters as Unity's own shadows: renderers set not to cast
        // shadows are left out, shadow-only ones drawn. A light that has no
        // shadows in vanilla isn't blocked by its own piece either (a
        // jack-o-turnip's shell, a sconce). Found once per re-render, through
        // colliders in the light's range.
        private static void FindCasters(Tracked t)
        {
            t.Hide.Clear();
            t.Show.Clear();
            t.Explicit.Clear();
            t.ExplicitSub.Clear();
            t.Sheets.Clear();
            t.Own.Clear();
            Roots.Clear();
            // A swinging door is drawn into the moving map instead.
            foreach (Renderer r in SwingHidden)
                if (r != null) t.Hide.Add(r);
            // Animated machine parts live in the moving maps.
            foreach (Renderer r in MachineHidden)
                if (r != null) t.Hide.Add(r);
            foreach (Renderer r in FabricHidden)
                if (r != null) t.Hide.Add(r);
            // Mounted visuals are often colliderless or on the "item"
            // layer, which is absent from our static shadow camera mask.
            // Render them directly during the static re-cache rather than
            // excluding them and sampling them every moving-shadow frame.
            foreach (Renderer r in StandVisuals)
                if (r != null) t.Hide.Add(r);
            foreach (Renderer r in DropVisuals)
                if (r != null) t.Hide.Add(r);
            // Dropped objects, including settled ones, never enter static
            // cubemaps. Their persistent moving-map faces cache them instead.
            foreach (StandCaster stand in Stands)
            {
                if (stand.Stand == null || !stand.Stand.gameObject.activeInHierarchy ||
                    stand.Geometry == null) continue;
                float reach = Range(t) + stand.Radius;
                if ((stand.Center - t.Center).sqrMagnitude > reach * reach) continue;
                foreach (Renderer r in stand.Renderers)
                    AddStaticItemRenderer(t, r);
            }
            int n = Physics.OverlapSphereNonAlloc(t.Center, Range(t), Hits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
                if (Hits[i] != null)
                    Roots.Add(Hits[i].transform.root);
            Roots.Add(t.Light.transform.root);
            // A light doesn't shade its own model (an item's glow its item or
            // the stand holding it), except fires meant to.
            Component own = OwnShades(t) ? null : t.Tiny ? t.Light.transform.root : (Component)t.Light.GetComponentInParent<Piece>();
            if (own != null)
            {
                own.GetComponentsInChildren(false, Renderers);
                foreach (Renderer r in Renderers)
                    if (!r.forceRenderingOff) t.Hide.Add(r);
            }
            Transform ownRoot = t.Light.transform.root;
            t.StructMeshes.Clear();
            t.StructMatrices.Clear();
            foreach (Transform root in Roots)
            {
                root.GetComponentsInChildren(false, Renderers);
                // Rugs and carpets lie flat on the floor: their shadow is only
                // jagged edges on the boards beneath. Characters are drawn
                // into the moving map, never baked into this one.
                bool flat = IsRug(root.name) || root.GetComponent<Character>() != null;
                // Even before GatherCasters discovers a newly dropped item,
                // its item root must never sneak into a static shadow map.
                bool looseItem = root.GetComponent<ItemDrop>() != null;
                foreach (Renderer r in Renderers)
                {
                    if (r.forceRenderingOff)
                        continue;
                    if (StandVisuals.Contains(r) || DropVisuals.Contains(r) ||
                        flat || looseItem || SeeThrough(r))
                        t.Hide.Add(r);
                    else if (r.shadowCastingMode == ShadowCastingMode.Off)
                        t.Hide.Add(r);
                    else
                    {
                        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                            t.Show.Add(r);
                        AddOwnCaster(t, r);
                        if (Sheet(r) && (StaticLayerMask & (1 << r.gameObject.layer)) != 0)
                        {
                            // Drawn from both sides instead (DrawUntagged).
                            t.Hide.Add(r);
                            t.Sheets.Add(r);
                        }
                        else
                            AddUntagged(t, r);
                    }
                }
            }
            FindProxies(t);
            // The old path hid these with forceRenderingOff at draw time; the
            // own-pass list must leave them out itself (the light's own piece etc).
            if (t.Own.Count > 0 && t.Hide.Count > 0)
            {
                OwnHidden.Clear();
                foreach (Renderer r in t.Hide) if (r != null) OwnHidden.Add(r);
                t.Own.RemoveAll(c => OwnHidden.Contains(c.R));
            }
            if (t.Explicit.Count > 0 && Plugin.DebugLog.Value)
            {
                var names = new HashSet<string>();
                foreach (Renderer r in t.Explicit) if (r != null && names.Count < 6) names.Add(r.name);
                Check(t.Explicit.Count + " untagged-shader parts drawn directly for " + NameOf(t.Light) + ": " + string.Join(", ", names));
            }
        }

        // Direct static item draw handles colliderless mounted weapons and
        // dropped items on Unity's item layer without expanding the camera
        // mask to include moving items or duplicating their casts.
        private static void AddStaticItemRenderer(Tracked t, Renderer r)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return;
            // The original moving path supported item visuals whose prefab
            // did not explicitly request Unity shadow casting. Preserve that
            // behavior when those visuals join the static cache.
            if (!OwnShades(t) && r.transform.root == t.Light.transform.root)
                return;
            r.GetSharedMaterials(Materials);
            for (int sub = 0; sub < Materials.Count; sub++)
            {
                Material m = Materials[sub];
                if (m == null || !CastsShadow(m)) continue;
                t.Explicit.Add(r);
                t.ExplicitSub.Add(sub);
            }
        }

        // The replacement camera only draws shaders tagged Opaque or
        // TransparentCutout (as Unity's shadows only draw shaders with a
        // shadow pass). Submeshes with other shaders (some modded pieces) are
        // drawn directly instead, as solid.
        private static void AddUntagged(Tracked t, Renderer r)
        {
            if ((StaticLayerMask & (1 << r.gameObject.layer)) == 0)
                return;
            r.GetSharedMaterials(Materials);
            for (int i = 0; i < Materials.Count; i++)
            {
                Material m = Materials[i];
                if (m == null || Tagged(m) || !CastsShadow(m))
                    continue;
                t.Explicit.Add(r);
                t.ExplicitSub.Add(i);
            }
        }

        private static readonly Dictionary<Material, bool> TagCache = new Dictionary<Material, bool>();

        private static bool Tagged(Material m)
        {
            if (!TagCache.TryGetValue(m, out bool tagged))
            {
                string tag = m.GetTag("RenderType", true, "");
                TagCache[m] = tagged = tag == "Opaque" || tag == "TransparentCutout";
            }
            return tagged;
        }

        internal static int StaticLayerMask;

        // Debug log: what an item on a stand is made of, to find why its
        // shadow didn't match its shape.
        internal static void DescribeStandItem(GameObject item)
        {
            if (item == null)
                return;
            var sb = new System.Text.StringBuilder("item on stand " + item.name.Replace("(Clone)", "") + ":");
            if (item.GetComponentInChildren<LODGroup>() != null) sb.Append(" [LODGroup]");
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
            {
                sb.Append(" | ").Append(r.name).Append(' ').Append(r.GetType().Name)
                  .Append(" layer ").Append(LayerMask.LayerToName(r.gameObject.layer))
                  .Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)")
                  .Append(" cast ").Append(r.shadowCastingMode)
                  .Append(" size ").Append(r.bounds.size.ToString("F2"));
                r.GetSharedMaterials(Materials);
                foreach (Material m in Materials)
                    if (m != null) sb.Append(" [").Append(m.shader.name).Append(' ').Append(m.GetTag("RenderType", true, "-")).Append(" q").Append(m.renderQueue).Append(']');
            }
            Check(sb.ToString());
        }

        // Effects and glass (a portal's swirl, particles) don't cast in
        // Unity's shadows either, even when their shader claims to be opaque.
        internal static bool SeeThrough(Renderer r)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                return true;
            // As Unity's shadows: only materials with a shadow pass cast,
            // whatever their queue (a banner, hanging herbs). Only when none
            // has one: a window's frame still casts.
            r.GetSharedMaterials(Materials);
            if (Materials.Count == 0)
                return false;
            foreach (Material m in Materials)
                if (m == null || CastsShadow(m))
                    return false;
            return true;
        }

        // A flat mesh (a banner, a hanging bundle of herbs, a painting): drawn
        // two-sided in the game, it has no back of its own, so the midpoint
        // maps would let light through it near anything behind.
        private static readonly Dictionary<Mesh, bool> SheetCache = new Dictionary<Mesh, bool>();

        private static bool Sheet(Renderer r)
        {
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>() is MeshFilter mf ? mf.sharedMesh : null;
            if (mesh == null)
                return false;
            if (!SheetCache.TryGetValue(mesh, out bool sheet))
            {
                Vector3 e = mesh.bounds.size;
                SheetCache[mesh] = sheet = Mathf.Min(e.x, Mathf.Min(e.y, e.z)) < 0.03f * Mathf.Max(e.x, Mathf.Max(e.y, e.z));
            }
            return sheet;
        }

        private static readonly Dictionary<Material, bool> CasterCache = new Dictionary<Material, bool>();

        // The shadow pass often comes from a fallback shader, which a
        // material can't be asked about: opaque or cutout ones cast, and
        // see-through ones with a cutoff (a banner, hanging herbs).
        private static bool CastsShadow(Material m)
        {
            if (!CasterCache.TryGetValue(m, out bool casts))
                CasterCache[m] = casts = m.renderQueue <= 2500 || Tagged(m) || m.HasProperty("_Cutoff");
            return casts;
        }

        private static readonly List<Material> Materials = new List<Material>();

        private static bool IsRug(string name) =>
            name.StartsWith("rug_", System.StringComparison.Ordinal) || name.IndexOf("carpet", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static readonly AccessTools.FieldRef<LightFlicker, Vector3> FlickerBase =
            AccessTools.FieldRefAccess<LightFlicker, Vector3>("m_basePosition");

        // As in vanilla: a light with Unity shadows is shaded by its own piece
        // past its near plane (a hearth's rim). One without (a sconce, a
        // jack-o-turnip, an item's glow) skips its own model.
        private static bool OwnShades(Tracked t) =>
            !t.Tiny && (t.OriginalShadowLod || t.OriginalShadows != LightShadows.None);

        // The light's own near plane, as the game's shadows use it: nothing
        // closer casts, so the stone around a sconce (0.8 m) doesn't shade
        // itself. The cached maps draw colliders there instead (Proxies).
        private static float NearPlane(Tracked t) => Mathf.Max(0.05f, t.Light.shadowNearPlane);

        // Inside the near plane, walls and floors block by their colliders:
        // plain boxes, no stones or ledges to cast shadows, and the wall a
        // sconce hangs on still stops its light reaching the room behind.
        // Not the light's own piece, one it sits inside (a hearth) or doors
        // (they swing; the moving maps draw them).
        private static readonly Collider[] ProxyHits = new Collider[64];
        private static Mesh _cube;

        private static void AddProxy(Tracked t, Collider c)
        {
            if (_cube == null)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = g.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(g);
            }
            if (c is BoxCollider box)
            {
                t.ProxyMeshes.Add(_cube);
                t.ProxyMatrices.Add(box.transform.localToWorldMatrix * Matrix4x4.TRS(box.center, Quaternion.identity, box.size));
            }
            else if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                t.ProxyMeshes.Add(mc.sharedMesh);
                t.ProxyMatrices.Add(mc.transform.localToWorldMatrix);
            }
        }

        // Walls, floors, roofs and beams cast as their colliders: plain
        // solid shapes, so a wall's stones, ledges and plank edges never shade
        // the wall itself, while the wall still blocks whatever is behind it.
        private static bool Structural(Piece p) =>
            p != null && (p.m_category == Piece.PieceCategory.BuildingWorkbench || p.m_category == Piece.PieceCategory.BuildingStonecutter)
            && p.GetComponentInChildren<Door>() == null;

        private static readonly List<Collider> PieceColliders = new List<Collider>();

        private static bool AddStructural(Tracked t, Transform root)
        {
            root.GetComponentsInChildren(false, PieceColliders);
            int before = t.StructMeshes.Count;
            foreach (Collider c in PieceColliders)
            {
                if (c.isTrigger || !c.enabled)
                    continue;
                if (c is BoxCollider box)
                {
                    t.StructMeshes.Add(null);
                    t.StructMatrices.Add(box.transform.localToWorldMatrix * Matrix4x4.TRS(box.center, Quaternion.identity, box.size));
                }
                else if (c is MeshCollider mc && mc.sharedMesh != null)
                {
                    t.StructMeshes.Add(mc.sharedMesh);
                    t.StructMatrices.Add(mc.transform.localToWorldMatrix);
                }
            }
            return t.StructMeshes.Count > before;
        }

        private static void FindProxies(Tracked t)
        {
            t.ProxyMeshes.Clear();
            t.ProxyMatrices.Clear();
            Component own = t.Tiny ? t.Light.transform.root : (Component)t.Light.GetComponentInParent<Piece>();
            float near = NearPlane(t);
            if (_cube == null)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = g.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(g);
            }
            Transform ownT = own != null ? own.transform : t.Light.transform.root;
            int n = near <= 0.06f ? 0 : Physics.OverlapSphereNonAlloc(t.Center, near, ProxyHits, StaticLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = ProxyHits[i];
                if (c == null || c.transform.IsChildOf(ownT) || c.GetComponentInParent<Door>() != null)
                    continue;
                // ClosestPoint is unsupported by Unity for non-convex MeshColliders
                // and several other collider classes. These can still provide
                // valid near-plane proxy meshes, so skip only the unsupported
                // point test rather than losing their shadows.
                bool validClosestPoint = c is BoxCollider || c is SphereCollider ||
                    c is CapsuleCollider || c is MeshCollider mc && mc.convex;
                if (validClosestPoint && (c.ClosestPoint(t.Center) - t.Center).sqrMagnitude < 1e-6f)
                    continue;
                AddProxy(t, c);
            }
            t.NearProxies = t.ProxyMeshes.Count;
            for (int i = 0; i < t.StructMeshes.Count; i++)
            {
                t.ProxyMeshes.Add(t.StructMeshes[i] != null ? t.StructMeshes[i] : _cube);
                t.ProxyMatrices.Add(t.StructMatrices[i]);
            }
            Check(t.NearProxies + " colliders inside the near plane (" + near.ToString("F2") + " m), " + t.StructMeshes.Count + " structural colliders: " + NameOf(t.Light));
        }

        private static readonly AccessTools.FieldRef<LightLod, float> BaseRange =
            AccessTools.FieldRefAccess<LightLod, float>("m_baseRange");

        private static float Range(Tracked t)
        {
            if (t == null || t.Light == null) return 0f;
            float r = t.Lod != null ? BaseRange(t.Lod) : 0f;
            return r > 0f ? r : t.Light.range;
        }

        private static Vector3 RestingPosition(Light light) =>
            light.transform.parent != null
                ? light.transform.parent.TransformPoint(BasePosition(light))
                : light.transform.position;

        private static Vector3 BasePosition(Light light)
        {
            LightFlicker flicker = light.GetComponent<LightFlicker>();
            return flicker != null ? FlickerBase(flicker) : light.transform.localPosition;
        }
    }

    // On lights without a LightLod: tells the cache when they switch on.
    internal sealed class LightWatch : MonoBehaviour
    {
        internal Light Light;

        private void OnEnable()
        {
            if (Light != null) ShadowCache.LightEnabled(Light);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerSpawned
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) ShadowCache.Spawned();
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    internal static class ItemDropAwake
    {
        private static void Postfix(ItemDrop __instance)
        {
            ShadowCache.RegisterPiece(__instance);
            ShadowCache.Items.Add(__instance);
            var lifecycle = __instance.GetComponent<DropLifecycle>();
            if (lifecycle == null)
                lifecycle = __instance.gameObject.AddComponent<DropLifecycle>();
            lifecycle.Item = __instance;
        }
    }

    // Unity fires these when a dropped item is picked up or destroyed.
    // An attached component is more reliable than relying on a game-specific
    // pickup method, including modded item removal.
    internal sealed class DropLifecycle : MonoBehaviour
    {
        internal ItemDrop Item;
        private void OnDisable()
        {
            if (!System.Object.ReferenceEquals(Item, null)) ShadowCache.DropRemoved(Item);
        }
        private void OnDestroy()
        {
            if (!System.Object.ReferenceEquals(Item, null)) ShadowCache.DropRemoved(Item);
        }
    }

    // These run on a timer and return early when nothing changed: only a
    // real change re-renders.
    [HarmonyPatch(typeof(ItemStand), "SetVisualItem")]
    internal static class ItemStandVisual
    {
        private static readonly AccessTools.FieldRef<ItemStand, int> Hash = AccessTools.FieldRefAccess<ItemStand, int>("m_visualHash");
        private static readonly AccessTools.FieldRef<ItemStand, int> Variant = AccessTools.FieldRefAccess<ItemStand, int>("m_visualVariant");
        private static readonly AccessTools.FieldRef<ItemStand, int> Orientation = AccessTools.FieldRefAccess<ItemStand, int>("m_orientation");

        private static void Prefix(ItemStand __instance, int itemHash, int variant, int orientation, out bool __state) =>
            __state = Hash(__instance) != itemHash || Variant(__instance) != variant || Orientation(__instance) != orientation;

        private static void Postfix(ItemStand __instance, bool __state)
        {
            if (!__state)
                return;
            ShadowCache.RegisterPiece(__instance); // an item with a glow
            ShadowCache.RefreshStand(__instance);
            if (Plugin.DebugLog.Value)
                ShadowCache.DescribeStandItem(VisualItem(__instance));
        }

        private static readonly AccessTools.FieldRef<ItemStand, GameObject> VisualItem = AccessTools.FieldRefAccess<ItemStand, GameObject>("m_visualItem");
        internal static GameObject CurrentItem(ItemStand stand) => VisualItem(stand);
    }

    // Vanilla turns the building preview's shadows off when it makes it.
    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    internal static class GhostShadows
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, PieceTable> BuildPieces =
            AccessTools.FieldRefAccess<Player, PieceTable>("m_buildPieces");

        private static void Postfix(Player __instance)
        {
            GameObject g = Ghost(__instance);
            if (g == null || !Plugin.GhostShadows.Value)
                return;
            // Vanilla disables every ghost renderer's shadow, so its current
            // mode cannot tell us what the real prefab would cast. Mirror
            // the original prefab instead; never resurrect rug/carpets or
            // renderers whose original casting mode was Off.
            PieceTable table = BuildPieces(__instance);
            GameObject original = table != null ? table.GetSelectedPrefab() : null;
            Renderer[] source = original != null ? original.GetComponentsInChildren<Renderer>(true) : null;
            Renderer[] preview = g.GetComponentsInChildren<Renderer>(true);
            bool match = source != null && source.Length == preview.Length;
            bool rug = g.name.StartsWith("rug_", System.StringComparison.Ordinal) ||
                g.name.IndexOf("carpet", System.StringComparison.OrdinalIgnoreCase) >= 0;
            for (int i = 0; i < preview.Length; i++)
            {
                Renderer r = preview[i];
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                r.shadowCastingMode = !rug && match && source[i].GetType() == r.GetType() &&
                    !ShadowCache.SeeThrough(source[i])
                    ? source[i].shadowCastingMode : ShadowCastingMode.Off;
            }
            foreach (Light l in g.GetComponentsInChildren<Light>(true))
                ShadowCache.MovingLight(l, true, "preview");
        }
    }

    [HarmonyPatch(typeof(Door), "SetState")]
    internal static class DoorState
    {
        private static readonly AccessTools.FieldRef<Door, Animator> AnimatorOf = AccessTools.FieldRefAccess<Door, Animator>("m_animator");

        // SetState runs on every ZDO revision; only a real open/close counts.
        private static void Prefix(Door __instance, int state, out bool __state)
        {
            Animator a = AnimatorOf(__instance);
            __state = a != null && a.GetInteger("state") != state;
        }

        // Re-render once the door has swung.
        private static void Postfix(Door __instance, bool __state)
        {
            if (__state) ShadowCache.DoorMoved(__instance);
        }
    }

    [HarmonyPatch(typeof(Demister), "Awake")]
    internal static class WispOrbAwake
    {
        private static void Postfix(Demister __instance) => ShadowCache.WispDemisterAwake(__instance);
    }

    [HarmonyPatch(typeof(LightLod), "Awake")]
    internal static class LightLodAwake
    {
        private static void Postfix(LightLod __instance) => ShadowCache.Register(__instance);
    }

    [HarmonyPatch(typeof(LightLod), "OnEnable")]
    internal static class LightLodEnable
    {
        private static void Postfix(LightLod __instance) => ShadowCache.LightEnabled(__instance.GetComponent<Light>());
    }

    [HarmonyPatch(typeof(LightLod), "OnDestroy")]
    internal static class LightLodDestroy
    {
        private static void Prefix(LightLod __instance) => ShadowCache.Unregister(__instance);
    }

    [HarmonyPatch(typeof(Piece), "Awake")]
    internal static class PieceAwake
    {
        private static void Postfix(Piece __instance) => ShadowCache.PieceAwake(__instance);
    }

    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    internal static class PieceDestroy
    {
        private static void Prefix(Piece __instance) => ShadowCache.PieceGone(__instance);
    }
}

namespace WiresLightOcclusion
{
    // Existing Character.Awake event, no per-frame scan or new polling.
    [HarmonyLib.HarmonyPatch(typeof(Character), "Awake")]
    internal static class WloSpawnDiagnostics
    {
        private static void Postfix(Character __instance) => ShadowCache.RecordSpawn(__instance);
    }
}