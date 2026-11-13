using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WiresShadows
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
            internal bool StagedNativeReady;
            internal Vector3 PublishedCenterBeforeStage;
            internal int Slot = -1;
            // During a non-destructive distant->near promotion both maps exist.
            // Only the distant source is registered in Lights/ByLight.
            internal Tracked PromotionTo;
            internal Tracked PromotionFrom;
            internal float Reach, Priority;
            internal bool Tiny;          // an item's glow: only a spare slot
            internal bool Item;          // on an item (dropped, placed or on a stand): never shaded by it
            internal ItemStand Stand;    // the stand holding it, if any
            internal float LitAt = -10f;  // when it was last switched on
            internal bool Instant;       // next render shows at full strength
            internal int ReleasedFrame = -1;
            internal Rigidbody Body;     // a loose item's physics body
            internal bool Rolling;       // using the game's own shadows while it moves
            internal bool Staged;        // re-rendering into the staging cube, swapped in when done
            internal readonly List<Renderer> Explicit = new List<Renderer>();  // untagged shaders, drawn directly
            internal readonly List<Renderer> Sheets = new List<Renderer>();     // two-sided flat ones, both passes
            internal readonly List<Mesh> ProxyMeshes = new List<Mesh>();       // colliders inside the near plane
            internal readonly List<Matrix4x4> ProxyMatrices = new List<Matrix4x4>();
            internal int NearProxies;
            internal readonly List<Mesh> StructMeshes = new List<Mesh>();      // null: a box
            internal readonly List<Matrix4x4> StructMatrices = new List<Matrix4x4>();   // the first ones are inside the near plane only
            internal readonly List<int> ExplicitSub = new List<int>();
            internal Vector3 Center;
            internal bool Dirty;         // waiting for a full re-render
            internal float DirtyAt;      // not before this time (bursts of changes coalesce)
            internal int Face = -1;      // face being rendered, -1 when idle
            internal int DoorFaces;      // affected static faces queued on a gate/shutter change
            internal bool DoorPrepared;  // casters gathered once for this queued face batch
            internal bool Ready;         // has a complete map
            internal float Fade;         // shadow strength fading in, as LightLod does (1 s)
            internal readonly List<Renderer> Hide = new List<Renderer>();
            internal readonly List<Renderer> Show = new List<Renderer>();
            internal LightShadows OriginalShadows;
            internal bool OriginalShadowLod;
            internal bool Suppressed;    // Unity's own shadows off for this light
            internal bool Sconce;        // detected once per registered light
            // Identify firelights to use the resting cached source position.
            internal bool FakeFlame;
            internal float OriginalShadowStrength;
            internal float OriginalShadowNearPlane;
            internal Vector3 RootPos;    // where the light's object stood when its map was made
            internal Quaternion RootRot;
            internal int WantTier;       // map size it needs on screen (Tiers)
            internal int RenderTier, RenderCube = -1; // where the render in progress goes (Tiers)
            internal float RenderRange;
            internal int CasterHash, PendingHash; // what cast into its map when rendered (Keep)
            internal int HashVersion = -1;        // WorldVersion when CasterHash was taken
            internal float WaitSince = -1f, WaitUntil; // waiting for its area to load in (AreaWait)
            internal Vector3 HashCenter;
            internal readonly List<Animator> Animators = new List<Animator>();
            internal readonly List<AnimatorCullingMode> AnimatorModes = new List<AnimatorCullingMode>();
        }

        internal static ShadowCache Instance;

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
        private readonly float[] _dynActive = new float[MaxSlots]; // 1 = the slot's moving map is in use
        private RenderTexture _farDynMaps;
        private static readonly int FarDynMapsId = Shader.PropertyToID("_WireFarDynMaps");

        // Static shadow slots are logical. Moving-shadow faces occupy a compact,
        // lossless physical cube pool, growing only when another light really
        // needs moving shadows. Resolution and exact shader comparisons unchanged.
        private const int InitialMovingCubes = 4;
        // The native comparison copies are made from 16-bit maps (RHalf, RGHalf): 16-bit depth
        // keeps all of it at half the memory of 24-bit (which the card stores in 4 bytes).
        private const int NativeDepthBits = 16;
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
            int limit = near ? Mathf.Min(_near, _nearMovingCap) : Mathf.Min(Mathf.Max(1, _slots.Length - _near), _farMovingCap);
            int capacity = Mathf.Min(limit, Mathf.Max(required, oldCapacity * 2));
            if (capacity <= oldCapacity) return false;
            if (_builtNativeOnly)
            {
                // no colour array to grow: the native depth array follows the capacity
                // (EnsureNativeMovingTier copies the cubes in use over)
                if (near) _dynNearCapacity = capacity; else _dynFarCapacity = capacity;
                Plugin.Note("[sparse-moving] " + (near ? "near" : "far") + " moving pool grew to " + capacity + " cubes.");
                return true;
            }
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
            Plugin.Note("[sparse-moving] " + (near ? "near" : "far") +
                " moving pool grew to " + capacity + " cubes.");
            return true;
        }
        // The pools only ever grow (a busy base, a big fight). On a teleport or
        // spawn, behind the loading screen, drop every moving map and start the
        // pools at their initial size again: whatever is in range at the
        // destination is redrawn from scratch by the normal moving pass (the
        // same path as a light's first moving shadow), growing only as needed.
        private static bool _shrinkPools;
        private void ShrinkMovingPools()
        {
            if (_dynMaps == null || _farDynMaps == null) return;
            int nearCap = InitialNear, farCap = InitialFar;
            if (_dynNearCapacity <= nearCap && _dynFarCapacity <= farCap) return;
            // native-only: the colour arrays are the tiny bound ones and stay as they are
            int nearCubes = _builtNativeOnly ? 1 : nearCap, farCubes = _builtNativeOnly ? 1 : farCap;
            var near = new RenderTexture(_dynMaps.width, _dynMaps.height, 0, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * nearCubes,
                useMipMap = false, filterMode = FilterMode.Point, name = "WireDynShadowMaps"
            };
            var far = new RenderTexture(_farDynMaps.width, _farDynMaps.height, 0, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * farCubes,
                useMipMap = false, filterMode = FilterMode.Point, name = "WireFarDynShadowMaps"
            };
            if (!near.Create() || !far.Create())
            {
                near.Release(); Destroy(near);
                far.Release(); Destroy(far);
                return; // keep the big pools rather than lose moving shadows
            }
            // A recorded moving pass would still point at the old pools.
            _framePending = false;
            if (_frameCb != null) _frameCb.Clear();
            int oldNear = _dynNearCapacity, oldFar = _dynFarCapacity;
            for (int sl = 0; sl < MaxSlots; sl++)
            {
                _dynActive[sl] = 0f;
                _dynFacesLast[sl] = 0;
                _pickupInvalidFaces[sl] = 0;
            }
            System.Array.Clear(_faceHash, 0, _faceHash.Length);
            ResetDynPool();
            ReleaseNativeMovingResources(); // reallocated at the new size on next use
            _dynMaps.Release(); Destroy(_dynMaps);
            _farDynMaps.Release(); Destroy(_farDynMaps);
            _dynMaps = near;
            _farDynMaps = far;
            _dynNearCapacity = nearCap;
            _dynFarCapacity = farCap;
            Shader.SetGlobalTexture(DynMapsId, near);
            Shader.SetGlobalTexture(FarDynMapsId, far);
            UploadDynActive();
            Plugin.Note("[sparse-moving] pools shrunk: near " + oldNear + " -> " + nearCap +
                ", far " + oldFar + " -> " + farCap + " cubes.");
        }
        private int InitialNear => Mathf.Max(1, Mathf.Min(InitialMovingCubes, Mathf.Min(_near, _nearMovingCap)));
        private int InitialFar => Mathf.Max(1, Mathf.Min(InitialMovingCubes, Mathf.Min(Mathf.Max(1, _slots.Length - _near), _farMovingCap)));

        // Out of a busy spot (a fight, a party at the base) without teleporting, the pools stay at their
        // biggest. Checked every 10 s: when no more moving maps are in use than the starting size holds,
        // three checks running (30 s), they go back to it. The few in use are redrawn from scratch the
        // next frame (as after a teleport). Times are judgement values, no source.
        private float _nextIdleCheck;
        private int _idleChecks;
        private void ShrinkWhenIdle()
        {
            if (Time.time < _nextIdleCheck) return;
            _nextIdleCheck = Time.time + 10f;
            if (_framePending || _dynNearCapacity <= InitialNear && _dynFarCapacity <= InitialFar) { _idleChecks = 0; return; }
            int usedNear = 0, usedFar = 0;
            for (int i = 0; i < _dynNearCapacity; i++) if (_dynNearOwners[i] >= 0) usedNear++;
            for (int i = 0; i < _dynFarCapacity; i++) if (_dynFarOwners[i] >= 0) usedFar++;
            if (usedNear > InitialNear || usedFar > InitialFar) { _idleChecks = 0; return; }
            if (++_idleChecks < 3) return;
            _idleChecks = 0;
            ShrinkMovingPools();
        }

        // At the cap, a light nearer the camera takes the moving map of the furthest light holding
        // one, if that one is a quarter further away (so two lights don't keep swapping). Not while a
        // recorded moving pass still points at the cubes.
        private readonly List<int> _movingDenied = new List<int>(8);
        private static Vector3 s_sortCam;
        private static Tracked[] s_sortSlots;
        private static readonly System.Comparison<int> NearerFirst = (a, b) =>
            (s_sortSlots[a].Center - s_sortCam).sqrMagnitude.CompareTo((s_sortSlots[b].Center - s_sortCam).sqrMagnitude);
        private bool EvictFurther(int sl, Vector3 cam)
        {
            if (_framePending) return false;
            Tracked want = _slots[sl];
            if (want == null) return false;
            float wantDist = (want.Center - cam).sqrMagnitude;
            bool near = sl < _near;
            int[] owners = near ? _dynNearOwners : _dynFarOwners;
            int capacity = near ? _dynNearCapacity : _dynFarCapacity;
            int victim = -1;
            float worst = wantDist * 1.5625f; // 1.25 x the distance
            for (int i = 0; i < capacity; i++)
            {
                int o = owners[i];
                if (o < 0 || o == sl) continue;
                Tracked t = _slots[o];
                float d = t != null ? (t.Center - cam).sqrMagnitude : float.MaxValue;
                if (d > worst) { worst = d; victim = o; }
            }
            if (victim < 0) return false;
            _dynFacesLast[victim] = 0;
            _dynActive[victim] = 0f;
            _nativeMovingActive[victim] = 0f;
            for (int f = 0; f < 6; f++) _faceHash[victim * 6 + f] = 0;
            FreeDynSlot(victim);
            return true;
        }
        // Squared camera distance of the furthest distant-tier light still allowed
        // moving shadows: the nearest "Distant lights with moving shadows" of them.
        private readonly float[] _farMovingDist = new float[MaxSlots];
        private float FarMovingCutoff(Vector3 cam)
        {
            int cap = _farMovingCap;
            int n = 0;
            for (int sl = _near; sl < UltraStart && sl < _slots.Length; sl++)
            {
                Tracked t = _slots[sl];
                if (t != null && t.Ready && !t.Tiny)
                    _farMovingDist[n++] = (t.Center - cam).sqrMagnitude;
            }
            if (n <= cap) return float.MaxValue;
            if (cap <= 0) return -1f;
            System.Array.Sort(_farMovingDist, 0, n);
            return _farMovingDist[cap - 1];
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
        // Re-renders of a light that already has a map go into a spare cube
        // (one per tier), a face per frame, and are copied in whole when done:
        // no hitch, and never a half-updated map.
        private Material _combine;
        private CommandBuffer _staticCb;


        private const float ClothPush = 0.15f;
        private static readonly Dictionary<Renderer, bool> ClothRenderers = new Dictionary<Renderer, bool>();
        private static readonly List<Material> ClothMats = new List<Material>();

        // Banners and hanging cloth: Custom/Vegetation bent by wind (_PushClothMode).
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

        // The pass's shader with a material's texture and cutoff, so drawn
        // directly it keeps its holes (leaves, a banner's edge).
        private static readonly Dictionary<(Material, Shader, bool), Material> ClipMaterials = new Dictionary<(Material, Shader, bool), Material>();

        private static Material ClipMaterial(Material orig, Shader shader, bool item = false)
        {
            if (!ClipMaterials.TryGetValue((orig, shader, item), out Material m) || m == null)
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
                // Items (on stands, placed food) are never cut: their textures' alpha is often
                // something else too, and the moving maps draw them whole as well.
                bool clip = tex != null && !item && orig.HasProperty("_Cutoff");
                m.SetFloat("_WireClip", clip ? 1f : 0f);
                if (!clip) m.SetFloat("_Cutoff", 0f); // the shaders clip on any cutoff now
                ClipMaterials[(orig, shader, item)] = m;
            }
            return m;
        }
        private static MaterialPropertyBlock _proxyProps, _structProps;
        private static readonly int NearCutId = Shader.PropertyToID("_WireNearCut");
        internal static Shader CombineShader;
        internal static Shader NativeCopyShader;
        // Extra depth-only cube ARRAYS: same slots/resolutions as existing WLO
        // cached static maps. Conversion runs on face refresh, not per frame.
        // Each tier owns one temporary native-depth cube, just like the
        // existing RHalf stage. Never expose a partially refreshed PCF cube.
        private CommandBuffer _nativeCopyCb;
        private readonly float[] _nativeActive = new float[MaxSlots];
        private readonly float[] _nativeInvRanges = new float[MaxSlots];
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
        // Static comparison arrays change only when a map becomes ready, a slot changes,
        // or a native resource is reset. Fade updates must not resend them.
        private readonly float[] _uploadedNativeStaticActive = new float[MaxSlots];
        private readonly float[] _uploadedNativeStaticRanges = new float[MaxSlots];

        private void UploadDynActive()
        {
            UploadArrayIfChanged(DynActiveId, _dynActive, _uploadedDynActive, ref _hasUploadedDynActive);
            // Which faces of each moving map hold anything: the shader skips its moving taps elsewhere.
            // Faces not in _dynFacesLast are cleared (or never drawn), so they read as lit.
            for (int sl = 0; sl < MaxSlots; sl++)
                _dynFaceMask[sl] = _dynActive[sl] > 0.5f ? _dynFacesLast[sl] : 0f;
            UploadArrayIfChanged(DynFaceMaskId, _dynFaceMask, _uploadedDynFaceMask, ref _hasUploadedDynFaceMask);
        }
        private static readonly int DynFaceMaskId = Shader.PropertyToID("_WireDynFaceMask");
        private readonly float[] _dynFaceMask = new float[MaxSlots];
        private readonly float[] _uploadedDynFaceMask = new float[MaxSlots];
        private bool _hasUploadedDynFaceMask;
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
            if (!Plugin.NativeMovingDepthPcf || _nativeMovingFailed || NativeCopyShader == null) return false;
            if (!SupportsShadowmap())
            {
                _nativeMovingFailed = true;
                Plugin.Log.LogWarning("[native-moving] Shadowmap unsupported; using existing moving filter.");
                return false;
            }
            RenderTexture source = near ? _dynMaps : _farDynMaps;
            if (source == null || !source.IsCreated()) return false;
            int width = _movRes; // the moving maps' size (native-only keeps only a tiny colour array)
            int slices = near ? _dynNearCapacity : _dynFarCapacity;
            RenderTexture target = near ? _nativeMovingNear : _nativeMovingFar;
            if (target != null && target.IsCreated() && target.width == width && target.volumeDepth == 6 * slices)
                return true;
            RenderTexture oldNative = target;
            int oldCubes = oldNative != null && oldNative.IsCreated() ? oldNative.volumeDepth / 6 : 0;
            // 16-bit depth: the maps it's converted from are 16-bit (RGHalf), more bits would hold nothing
            target = new RenderTexture(width, width, NativeDepthBits, RenderTextureFormat.Shadowmap)
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
            Plugin.Note("[native-moving] " + (near ? "NEAR" : "FAR") +
                " comparison depth array ready: " + slices + " slots, " + width + "px faces.");
            return true;
        }
        // src/srcSlice: where the face was drawn (its slot's cube in the colour array, or the
        // native-only scratch cube); cube/face: where it goes in the depth array.
        private bool QueueNativeMovingFace(Tracked t, RenderTexture src, int srcSlice, int cube, int face, RectInt touched)
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
                _frameCb.CopyTexture(src, srcSlice, 0,
                    touched.x, touched.y, touched.width, touched.height,
                    _nativeMovingFlat, 0, 0, touched.x, touched.y);
            else
                _frameCb.CopyTexture(src, srcSlice, 0, _nativeMovingFlat, 0, 0);
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
            float far = SystemInfo.usesReversedZBuffer ? 0f : 1f;
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

        private void ReleaseNativeResources()
        {
            System.Array.Clear(_nativeActive, 0, MaxSlots);
            Shader.SetGlobalFloatArray(NativeActiveId, _nativeActive);
            // Direct shader reset bypasses UploadArrayIfChanged: force next sync.
        }




        private static readonly int FrontId = Shader.PropertyToID("_WireFront");
        private static readonly int BackId = Shader.PropertyToID("_WireBack");

        // A staged re-render that won't finish (slot lost, item started rolling).
        private void AbortStage(Tracked t)
        {
            DropTierRender(t);
            t.StagedNativeReady = false;
            if (!t.Staged)
                return;
            t.Staged = false;
        }


        private int _farResolution;
        private static readonly int FarMapsId = Shader.PropertyToID("_WireFarMaps");
        private static readonly int NearCountId = Shader.PropertyToID("_WireNearCount");
        private Camera _camera;
        private int _resolution;
        private bool _rendering;

        internal static int SlotCount => Mathf.Clamp(Plugin.Slots, 1, NearMax);
        internal static int FarCount => Mathf.Clamp(Plugin.FarSlots, 0, FarMax);
        // Ultra-distant slots share the distant tier's maps (same resolution), after
        // its own slots; together they must fit the distant tier's maximum.
        internal static int UltraCount => Mathf.Clamp(Plugin.UltraSlots, 0, FarMax - FarCount);
        private int UltraStart => _slots.Length - _ultra;
        private int _ultra;
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
            bool tiny = LooseItemGlow(light);
            // Lights riding a cart or ship move with it: the game's own per-frame
            // shadows, like a torch in a player's hand.
            if (light.GetComponentInParent<Ship>() != null || light.GetComponentInParent<Vagon>() != null)
            {
                MovingLight(light, true, "vehicle");
                return;
            }
            Rigidbody body = light.GetComponentInParent<Rigidbody>();
            bool mounted = light.GetComponentInParent<ItemStand>() != null || light.GetComponentInParent<ArmorStand>() != null;
            if (!tiny && !mounted && body != null && !body.isKinematic)
                return;
            LightFlicker fire = light.GetComponent<LightFlicker>();
            var t = new Tracked
            {
                Light = light, Lod = lod, Tiny = tiny,
                Item = mounted || light.GetComponentInParent<ItemDrop>() != null,
                Stand = light.GetComponentInParent<ItemStand>(),
                Body = body != null && !body.isKinematic && !mounted ? body : null,
                OriginalShadows = light.shadows,
                OriginalShadowLod = lod != null && ShadowLod(lod),
                OriginalShadowStrength = light.shadowStrength,
                OriginalShadowNearPlane = light.shadowNearPlane,
                Sconce = IsSconce(light),
                FakeFlame = fire != null
            };
            Lights.Add(t);
            ByLight[light] = t;
            _reassignSoon = true;
            // No LightLod to tell us it switched on: watch it ourselves.
            if (lod == null && light.GetComponent<LightWatch>() == null)
                light.gameObject.AddComponent<LightWatch>().Light = light;
            if (Instance != null && Instance._active)
                Suppress(t);
        }

        // The glow of an item lying loose (dropped). Items mounted on an item stand or armour stand
        // stay where they are: their lights get shadows like any placed light.
        private static bool LooseItemGlow(Light light)
        {
            ItemDrop it = light.GetComponentInParent<ItemDrop>();
            return it != null && !it.IsPiece() && // placed (a mead on a table) stays put: a light of its own
                light.GetComponentInParent<ItemStand>() == null &&
                light.GetComponentInParent<ArmorStand>() == null;
        }

        internal static void Unregister(LightLod lod) => Unregister(lod != null ? lod.GetComponent<Light>() : null);

        private static void Unregister(Light light)
        {
            if (ReferenceEquals(light, null) || !ByLight.TryGetValue(light, out Tracked t))
                return;
            if (Instance != null)
                Instance.Release(t);
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
                return;
            }
            ShadowCache c = Instance;
            if (t.Slot < 0)
            {
                Player player = Player.m_localPlayer;
                if (player == null || Reach(t, player.transform.position) > Plugin.Distance)
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
            if (on && (!Plugin.MovingLightShadows || (why == "carried" && !IsTorch(light))))
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
            GameObject g = p != null && Plugin.GhostShadows ? PlacementGhost(p) : null;
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
            // Fixed MeshRenderer geometry: bounds and submeshes read once on
            // settle, not via native Renderer.bounds on every later frame.
            internal bool CanCacheGeometry;
            internal Caster[] CachedGeometry;
            internal Vector3 GeometryPosition, GeometryScale;
            internal Quaternion GeometryRotation;
            internal int GeometryHash, GeometryVisibility;
            internal bool Mounted;
            // Placed by a player (food and meads on a table: the game makes it a piece, no physics):
            // in the cached maps like a stand's item, not the moving maps.
            internal bool Placed;
            internal Vector3 PlacedCenter;
            internal float PlacedRadius;
            internal bool Handoff; // legacy flag; dropped items now live in cached moving maps
        }
        private static readonly Dictionary<ItemDrop, DropCaster> Drops = new Dictionary<ItemDrop, DropCaster>();
        // Legacy handoff list stays empty now that settled drops use moving-map caching.
        private static readonly List<DropCaster> PendingDrops = new List<DropCaster>();
        private static readonly HashSet<Renderer> DropVisuals = new HashSet<Renderer>();
        private static readonly List<DropCaster> PlacedDrops = new List<DropCaster>();

        private static void PlaceDrop(DropCaster d)
        {
            d.Placed = true;
            Bounds b = default;
            bool first = true;
            foreach (Renderer r in d.Renderers)
            {
                if (r == null) continue;
                DropVisuals.Remove(r);
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            d.PlacedCenter = first ? d.CurrentPosition : b.center;
            d.PlacedRadius = first ? ItemRadius : b.extents.magnitude + .1f;
            PlacedDrops.Add(d);
            _dropChangedFrame = Time.frameCount;
            MarkDropFaces(d.CurrentPosition);           // out of the moving maps now
            QueueStaticItemChange(d.PlacedCenter, d.PlacedRadius); // into the cached ones
        }

        private static void UnplaceDrop(DropCaster d)
        {
            if (!d.Placed) return;
            PlacedDrops.Remove(d);
            QueueStaticItemChange(d.PlacedCenter, d.PlacedRadius);
        }
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
        // Items on a stand or placed (food on a table): part of the cached maps, they never move,
        // so they leave the every-frame list. Looked at once a second (ReviewResting) in case one
        // is knocked off or moved; then it's back on the list.
        internal static readonly List<ItemDrop> Resting = new List<ItemDrop>();

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
            // Animated/skinned geometry can move without the ItemDrop transform;
            // only fixed, non-animated meshes qualify for persistent bounds.
            d.CanCacheGeometry = it.GetComponentInChildren<Animator>(true) == null &&
                it.GetComponentInChildren<Animation>(true) == null;
            foreach (Renderer renderer in d.Renderers)
                if (!(renderer is MeshRenderer)) d.CanCacheGeometry = false;
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
        // Resting items knocked off, moved or no longer placed go back on the every-frame list.
        private static void ReviewResting()
        {
            for (int i = Resting.Count - 1; i >= 0; i--)
            {
                ItemDrop it = Resting[i];
                bool back;
                if (it == null) back = false;
                else if (!Drops.TryGetValue(it, out DropCaster d)) back = true;
                else back = !(d.Mounted || d.Placed) || (it.transform.position - d.CurrentPosition).sqrMagnitude > 0.0004f;
                if (it == null || back)
                {
                    if (back) Items.Add(it);
                    Resting[i] = Resting[Resting.Count - 1];
                    Resting.RemoveAt(Resting.Count - 1);
                }
            }
        }

        internal static void DropRemoved(ItemDrop item)
        {
            WorldVersion++;
            Resting.Remove(item);
            if (System.Object.ReferenceEquals(item, null) || !Drops.TryGetValue(item, out DropCaster d))
            {
                Items.Remove(item);
                return;
            }
            _dropChangedFrame = Time.frameCount;
            MarkDropFaces(d.CurrentPosition);
            UnplaceDrop(d);
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
                    UnplaceDrop(d);
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
            WorldVersion++;
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

        // Renderer bounds, rather than only the pivot, catch long beams and
        // iron bars intersecting a light's range. Reused list; only on change.
        private static readonly List<Renderer> ChangedPieceRenderers = new List<Renderer>(16);

        internal static void PieceChanged(Vector3 position, float settle, Object who)
        {
            WorldVersion++;
            ShadowCache cache = Instance;
            if (cache == null) return;
            Bounds affected = default;
            bool hasBounds = false;
            if (who is Component piece)
            {
                ChangedPieceRenderers.Clear();
                piece.GetComponentsInChildren(true, ChangedPieceRenderers);
                bool hasAnyRenderer = false;
                foreach (Renderer renderer in ChangedPieceRenderers)
                {
                    if (renderer == null) continue;
                    hasAnyRenderer = true;
                    // Particle effects aren't a solid shadow caster; their
                    // enormous animated bounds can invalidate every light.
                    if (renderer is ParticleSystemRenderer ||
                        renderer.shadowCastingMode == ShadowCastingMode.Off)
                        continue;
                    if (!hasBounds) { affected = renderer.bounds; hasBounds = true; }
                    else affected.Encapsulate(renderer.bounds);
                }
                ChangedPieceRenderers.Clear();
                if (!hasBounds && !hasAnyRenderer) return;
            }
            bool dirtied = false;
            foreach (Tracked t in cache._slots)
            {
                if (t == null || t.Light == null) continue;
                float reach = Range(t) + 4f;
                Vector3 nearest = hasBounds ? affected.ClosestPoint(t.Center) : position;
                if ((t.Center - nearest).sqrMagnitude >= reach * reach) continue;
                // For a completed map, a local piece change only affects the
                // cube directions subtended by that piece. Reuse the already
                // supported door/shutter face queue rather than refreshing
                // all SIX faces of every nearby light. This matters for a
                // refinery surrounded by sconces/iron bars: otherwise its
                // shadows can appear or disappear seconds after the edit.
                // Never interrupt a full map already being rendered: leave
                // its next complete refresh queued instead.
                if (t.Ready && t.Face < 0 && !t.Dirty)
                {
                    Vector3 changeCenter = hasBounds ? affected.center : position;
                    float changeRadius = hasBounds ? affected.extents.magnitude + 0.05f : 1f;
                    int changedFaces = FacesFor(changeCenter - t.Center, changeRadius);
                    if (changedFaces != 0)
                    {
                        t.DoorFaces |= changedFaces;
                        t.DoorPrepared = false;
                        dirtied = true;
                    }
                }
                else
                {
                    // World streaming may generate many Piece.Awake calls.
                    // Neither abort the current six-face map nor repeatedly
                    // extend the initial map's debounce deadline.
                    if (!t.Dirty)
                    {
                        t.Dirty = true;
                        t.DirtyAt = Time.time + settle;
                    }
                    dirtied = true;
                }
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
            ReleaseTiers();
            if (_movScratch != null) _movScratch.Release();
            if (_dynMaps != null) _dynMaps.Release();

            if (_farDynMaps != null) _farDynMaps.Release();

            _frameCb?.Release();
            Stands.Clear();
            PendingStands.Clear();
            StandVisuals.Clear();
            Drops.Clear();
            PendingDrops.Clear();
            DropVisuals.Clear();
            PlacedDrops.Clear();
            LostDrops.Clear();
            if (_camera != null) Destroy(_camera.gameObject);
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

        // Video memory. All of it was sized for a big card: on 8 GB and less the maps (above 2 GB
        // with every moving map in use) push the game's own textures out to system memory, and
        // everything slows. The maps may take a fifth of the card's memory (a judgement value, no
        // source). What doesn't fit is given up in this order: moving maps (down to 6 near and 2
        // distant lights), near slots (down to 12), near resolution (1024 -> 512), near slots (down
        // to 8). Sizes as allocated below: near cube RHalf + 16-bit copy 4 B a texel, moving cube
        // RGHalf + 16-bit copy 6 B, plus the scratch faces.
        private const float VramShare = .2f;
        private int _nearMovingCap = NearMax, _farMovingCap = Plugin.FarMovingLights;
        private int _fitKey = int.MinValue, _fitRes, _fitSlots, _fitNearMoving, _fitFarMoving;

        // Bytes a texel, as allocated below. Native-only: the cached and moving maps are kept only as
        // 16-bit comparison depth (2 B); the colour versions (RHalf 2 B, RGHalf 4 B) are scratch faces
        // and one moving scratch cube (RGHalf, 24 B a texel for its 6 faces). Otherwise each map is
        // kept twice. Scratch faces: front/back cubes with 16-bit depth (4 B x 6 x 2), two flat faces
        // (2 B x 2), the native copy flat (2 B), the native stage cube (2 B x 6), the moving flat
        // (4 B), and in the old mode the RHalf stage (2 B x 6).
        private static long Need(int res, int slots, int far, int farRes, int moving, bool nativeOnly)
        {
            long r2 = (long)res * res, f2 = (long)farRes * farRes;
            if (nativeOnly)
                return slots * 6 * r2 * 2 + far * 6 * f2 * 2 + moving * 6 * r2 * 2 + r2 * (48 + 4 + 2 + 12 + 4 + 24) + f2 * (48 + 4 + 2 + 12);
            return slots * 6 * r2 * 4 + far * 6 * f2 * 4 + moving * 6 * r2 * 6 + r2 * (48 + 4 + 2 + 12 + 12 + 4) + f2 * (48 + 4 + 2 + 12 + 12);
        }

        // Every map only as native comparison depth: needs the depth conversion shaders (the
        // combine shader's depth pass, from bundle builds after 2026-11-13), the Shadowmap format,
        // and the direct moving-shadow draws. Anything failing at runtime switches back to the old
        // way at the next reassignment (EnsureResources sees the change and rebuilds).
        private bool WantNativeOnly() =>
            Plugin.NativeDepthPcf && Plugin.NativeMovingDepthPcf && Plugin.DirectMovingDraw &&
            !_nativeMovingFailed && NativeCopyShader != null &&
            CombineShader != null && CombineShader.passCount >= 4 && SupportsShadowmap();
        private bool _builtNativeOnly;
        private RenderTexture _movScratch; // native-only: moving shadows are drawn here, then converted
        private int _movRes;

        private static RenderTexture CubeArray(int res, int cubes, RenderTextureFormat format, string name)
        {
            var rt = new RenderTexture(res, res, 0, format)
            {
                dimension = TextureDimension.CubeArray, volumeDepth = 6 * Mathf.Max(1, cubes),
                useMipMap = false, filterMode = FilterMode.Point, name = name
            };
            rt.Create();
            return rt;
        }

        private void FitToMemory(ref int res, ref int slots, int far, int farRes, bool nativeOnly)
        {
            int vram = SystemInfo.graphicsMemorySize; // MB
            int key = (((vram * 31 + res) * 31 + slots) * 31 + far) * 2 + (nativeOnly ? 1 : 0);
            if (key != _fitKey)
            {
                _fitKey = key;
                int r = res, s = slots, nm = slots, fm = Mathf.Min(Plugin.FarMovingLights, Mathf.Max(1, far));
                long budget = (long)(vram * VramShare) << 20;
                if (vram > 0)
                    while (Need(r, s, far, farRes, nm + fm, nativeOnly) > budget)
                    {
                        if (nm > 6) nm = Mathf.Max(6, nm - 2);
                        else if (fm > 2) fm--;
                        else if (s > 12) { s = Mathf.Max(12, s - 4); nm = Mathf.Min(nm, s); }
                        else if (r > 512) r /= 2;
                        else if (s > 8) { s--; nm = Mathf.Min(nm, s); }
                        else break;
                    }
                _fitRes = r; _fitSlots = s; _fitNearMoving = nm; _fitFarMoving = fm;
                Plugin.Log.LogInfo("Video memory " + vram + " MB: shadow maps up to ~" + (Need(r, s, far, farRes, nm + fm, nativeOnly) >> 20) +
                    " MB (" + s + " near lights at " + r + " px, moving shadows for " + nm + " near and " + fm + " distant lights" +
                    (nativeOnly ? ", depth only" : ", depth and colour copies") + ").");
            }
            res = _fitRes;
            slots = _fitSlots;
            _nearMovingCap = _fitNearMoving;
            _farMovingCap = _fitFarMoving;
        }

        // One way to keep shadow maps: each light's map sized by the screen, kept once as 16-bit
        // depth in a pool of its size, both sides of a face drawn in one render (Tiers, OnePass).
        // The near/far arrays, their colour copies and the two-render faces that came before it are
        // gone. A GPU or bundle that can't do it gets the game's own point-light shadows instead.
        private static bool s_unsupportedLogged;
        private bool EnsureResources()
        {
            if (!WantNativeOnly() || !WantTiered(true) || !OnePass)
            {
                if (!s_unsupportedLogged)
                {
                    s_unsupportedLogged = true;
                    Plugin.Log.LogWarning("This GPU or shader bundle can't keep the shadow maps (depth maps, cube arrays, the one-render pass); point lights keep the game's own shadows.");
                }
                return false;
            }
            // Following the game: what Unity gives a point light near you at the
            // current shadow quality (its reach fills the screen, capped at
            // 1024; only Low comes out at 512).
            int res = QualitySettings.shadowResolution == ShadowResolution.Low ? 512 : 1024;
            int slots = SlotCount, far = FarCount, ultra = UltraCount;
            int farRes = res / 4;
            FitToMemory(ref res, ref slots, far + ultra, farRes, true);
            if (_tiered && _resolution == res && _near == slots && _slots.Length == slots + far + ultra && _ultra == ultra &&
                _dynMaps != null && _farDynMaps != null && _movRes == res)
                return true;
            _builtNativeOnly = true;
            _movRes = res;
            ReleaseAll();
            _resolution = res;
            _farResolution = farRes;
            _near = slots;
            _ultra = ultra;
            far += ultra;
            _slots = new Tracked[slots + far];
            // Moving-shadow pools at their full size from the start: the memory share already
            // counts them whole (FitToMemory), and growing them as lights need them meant a new
            // texture and a copy of every moving map, several times over on arriving at a base.
            _dynNearCapacity = Mathf.Max(1, Mathf.Min(slots, _nearMovingCap));
            _dynFarCapacity = Mathf.Max(1, Mathf.Min(far, _farMovingCap));
            ResetDynPool();
            Shader.SetGlobalFloat("_WireDynRes", res);

            // The cached maps' pools: what's left of the memory share after the moving maps and scratch.
            long r2 = (long)res * res;
            long other = (long)(_fitNearMoving + _fitFarMoving) * 6 * r2 * 2 + r2 * 2 * 24;
            int vram = SystemInfo.graphicsMemorySize;
            long budget = vram > 0 ? ((long)(vram * VramShare) << 20) - other : long.MaxValue / 4;
            _tiered = SetUpTiers(System.Math.Max(budget, 0L));
            if (!_tiered)
            {
                ReleaseTiers();
                _tiersFailed = true;
                Plugin.Log.LogWarning("Couldn't make the shadow map pools; point lights keep the game's own shadows.");
                return false;
            }

            // Moving shadows: drawn into one scratch cube, kept only as depth (EnsureNativeMovingTier).
            // The colour arrays the shader still declares stay tiny.
            ReleaseNativeMovingResources();
            if (_dynMaps != null) _dynMaps.Release();
            if (_farDynMaps != null) _farDynMaps.Release();
            if (_movScratch != null) { _movScratch.Release(); Destroy(_movScratch); _movScratch = null; }
            _dynMaps = CubeArray(16, 1, RenderTextureFormat.RGHalf, "WireDynShadowMaps");
            _farDynMaps = CubeArray(16, 1, RenderTextureFormat.RGHalf, "WireFarDynShadowMaps");
            _movScratch = CubeArray(res, 1, RenderTextureFormat.RGHalf, "WireMovingScratch");
            if (!_dynMaps.IsCreated() || !_farDynMaps.IsCreated() || !_movScratch.IsCreated())
            {
                Plugin.Log.LogError("Couldn't create moving-shadow maps at " + res + " px.");
                return false;
            }
            Shader.SetGlobalTexture(DynMapsId, _dynMaps);
            Shader.SetGlobalTexture(FarDynMapsId, _farDynMaps);
            System.Array.Clear(_dynActive, 0, _dynActive.Length);
            System.Array.Clear(_dynFacesLast, 0, _dynFacesLast.Length);

            if (_camera == null)
            {
                var go = new GameObject("WireShadowCamera") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _camera = go.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(60000f, 60000f, 0, 0);
                _camera.renderingPath = RenderingPath.Forward;
                _camera.allowHDR = false;
                _camera.allowMSAA = false;
                _camera.useOcclusionCulling = false;
                _camera.SetReplacementShader(FrontBackShader, "RenderType");
                _staticCb = new CommandBuffer { name = "WireUntaggedCasters" };
                _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _staticCb);
            }
            _camera.cullingMask = StaticLayerMask = StaticMask();
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

        // Reassign can evict and retake dozens of lights in one teleport.
        // Slot changes happen together: uploading all entries after EVERY
        // individual Release is quadratic in the number of changed slots.
        private bool _batchingReassign;

        private void Reassign()
        {
            // the game's point-light shadows set to Off: none from the mod either (GameLevel)
            if (!Plugin.CachedShadows || !Plugin.UseShader.Value || DistanceShader == null || GameOff || !EnsureResources())
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
            try
            {
            Vector3 at = player != null ? player.transform.position : main.transform.position;
            Vector3 eye = main.transform.position;
            _lastForward = main.transform.forward;
            _lastReassignEye = main.transform.position;
            float maxDist = Plugin.Distance;
            float ultraDist = Mathf.Max(maxDist, Plugin.UltraDistance);
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
                    Unsuppress(t);
                    ByLight.Remove(light);
                    Lights.RemoveAt(i);
                    // Same existing behavior as a carried light registered
                    // after parenting. Only real torches get native shadows.
                    MovingLight(light, true, "carried");
                    continue;
                }
                // A loose item's glow is tiny (spare slots only, no moving maps); one on a stand
                // is a light of its own, ranked like any other (a trophy's flame lights the wall).
                bool tinyNow = LooseItemGlow(light);
                if (t.Tiny != tinyNow) t.Tiny = tinyNow;
                // registered before it was put on its stand: it doesn't roll any more
                if (t.Body != null && (light.GetComponentInParent<ItemStand>() != null || light.GetComponentInParent<ArmorStand>() != null))
                {
                    t.Body = null;
                    if (t.Rolling) { t.Rolling = false; MovingLight(light, false, "settled"); }
                }
            }

            foreach (Tracked t in Lights)
            {
                if (_active && !t.Suppressed) Suppress(t);
                t.Reach = Reach(t, at);
                t.Priority = Priority(t, eye);
                t.WantTier = _tiered ? WantTier(t, main) : 0;
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
                if (_slots[sl] != null && (_slots[sl].Reach > (sl >= UltraStart ? ultraDist : maxDist) + KeepMargin ||
                                           BeyondGame(_slots[sl], at, KeepMargin)))
                    Release(_slots[sl]);
            }
            // The existing coroutine budgets new shadow-map faces; avoid
            // synchronous 6-face builds here that bypass its frame budget.
            // A near-tier upgrade renders in an extra slot while the existing
            // distant map stays visible. Up to three promotions at once (MaxPromotions)
            // to keep the normal one-face-per-frame render budget responsive.
            int promotions = 0;
            for (int s = 0; s < _near; s++)
                if (_slots[s] != null && _slots[s].PromotionFrom != null)
                    promotions++;
            int total = _slots.Length;
            int ultraStart = UltraStart;
            foreach (Tracked t in Lights)
            {
                if (t.PromotionTo != null)
                {
                    Tracked pending = t.PromotionTo;
                    if (t.Slot < _near || t.Tiny || t.Reach > maxDist)
                        Release(pending);
                    else
                    {
                        // Keep eviction priority accurate without restarting
                        // a six-face render already in progress.
                        pending.Priority = t.Priority;
                        pending.Reach = t.Reach;
                    }
                    continue;
                }
                if (BeyondGame(t, at, 0f))
                    continue; // past the distance the game's own setting gives shadows at
                if (t.Reach > maxDist)
                {
                    // Past the distant tier's reach: an ultra-distant slot, if one is free
                    // or held by something further away. Never moves anyone out of a nearer tier.
                    if (t.Slot < 0 && t.Reach <= ultraDist && _ultra > 0)
                    {
                        int us = FindSlot(ultraStart, total, t.Priority, true);
                        if (us >= 0) Take(t, us);
                    }
                    continue;
                }
                // Full-size maps go to the best-ranked lights for as many as there are full-size
                // slots (Lights is sorted, best first), the rest get low-res ones. A light used to
                // need a priority under a fixed cutoff, reached only once you were close and
                // looking at it, so its upgrade showed while free full-size slots sat empty.
                bool wantNear = !t.Tiny;
                int slot;
                if (t.Slot >= 0)
                {
                    // Moved up into view on a distant map: switch to a full-size
                    // one (one per pass; the rest keep their distant map till then).
                    if (promotions >= MaxPromotions || t.Slot < _near || !wantNear)
                        continue;
                    slot = FindSlot(0, _near, t.Priority, true);
                    if (slot < 0)
                        continue;
                    if (KeepReadyVisibleSlot(t))
                    {
                        BeginNearPromotion(t, slot);
                        promotions++;
                        continue;
                    }
                    // Offscreen/unready maps can use the existing direct move.
                    Release(t);
                    promotions++;
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
                if (slot < 0 && t.Slot < 0 && _ultra > 0)
                    slot = FindSlot(ultraStart, total, t.Priority, true); // distant tier full
                if (slot < 0)
                    continue;
                // A light promoted or switched between slot tiers previously
                // re-rendered synchronously during loading; now all such changes
                // use the same bounded background rendering queue.
                bool moving = t.ReleasedFrame == Time.frameCount;
                Take(t, slot);
                if (moving && (slot >= _near || promotions < MaxPromotions))
                {
                    if (slot < _near) promotions++;
                    // The existing renderer prioritizes this fresh map.
                    // A portal exit must not force a synchronous six-face
                    // render outside the shared loading-frame budget.
                    continue;
                }
                // Do not bypass the RenderDirty frame budget on teleport.
                // It renders urgent nearby maps first while Loading() is true
                // and fades unfinished maps in once the world becomes visible.
            }
            // Sized maps: a light that now covers more of the screen than its map is sized for
            // re-renders bigger (its old map stays on screen till the new one is done).
            if (_tiered)
            {
                // And one that covers much less re-renders smaller: when it needs two sizes less than
                // it has (one size of slack, so walking back and forth across where one size turns
                // into the next doesn't re-render it every time).
                foreach (Tracked t in _slots)
                    if (t != null && t.Ready && !t.Dirty && t.Face < 0 && t.PromotionTo == null && t.PromotionFrom == null &&
                        _mapTier[t.Slot] >= 0f &&
                        (t.WantTier < (int)_mapTier[t.Slot] || t.WantTier >= (int)_mapTier[t.Slot] + 2))
                    {
                        t.Dirty = true;
                        t.DirtyAt = Time.time;
                    }
                SizePoolsForWaiting();
            }
            }
            finally
            {
                _batchingReassign = false;
                UpdateGlobals(); // One upload for the whole reassignment.
            }
            WakeRenders();
        }

        private const float OffscreenWeight = 3f;
        private const float ScreenWeight = 4f;
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
                t.Reach > Plugin.Distance + KeepMargin)
                return false;
            float range = Range(t);
            if (range <= 0f) return false;
            return GeometryUtility.TestPlanesAABB(ReassignFrustum,
                new Bounds(t.Light.transform.position,
                    Vector3.one * (range * 2f)));
        }
        // Preserve a visible 256px shadow while preparing its 1024px successor.
        // The candidate is deliberately NOT in Lights/ByLight, and it does not
        // touch Unity shadow suppression, animators, or flame movement.
        // The existing RenderDirty coroutine renders the candidate as usual.
        private void BeginNearPromotion(Tracked original, int nearSlot)
        {
            if (original.PromotionTo != null || _slots[nearSlot] != null)
                return;
            Tracked candidate = new Tracked
            {
                Light = original.Light,
                Lod = original.Lod,
                Slot = nearSlot,
                Reach = original.Reach,
                Priority = original.Priority,
                Tiny = original.Tiny,
                Item = original.Item,
                Stand = original.Stand,
                LitAt = original.LitAt,
                Body = original.Body,
                OriginalShadows = original.OriginalShadows,
                OriginalShadowLod = original.OriginalShadowLod,
                OriginalShadowStrength = original.OriginalShadowStrength,
                OriginalShadowNearPlane = original.OriginalShadowNearPlane,
                Sconce = original.Sconce,
                FakeFlame = original.FakeFlame,
                WantTier = original.WantTier,
                PromotionFrom = original,
                Dirty = true,
                DirtyAt = Time.time
            };
            original.PromotionTo = candidate;
            _slots[nearSlot] = candidate;
            WakeRenders();
        }

        // Called only after all six near faces have been rendered and converted.
        // Reassign the slot INDEX of the original Tracked object: event hooks,
        // per-light animations, and registration all keep their original owner.
        private void CompleteNearPromotion(Tracked candidate)
        {
            Tracked original = candidate.PromotionFrom;
            int nearSlot = candidate.Slot;
            if (original == null || original.Light == null || original.PromotionTo != candidate ||
                original.Slot < _near || original.Slot >= _slots.Length ||
                nearSlot < 0 || nearSlot >= _near ||
                _slots[nearSlot] != candidate || _slots[original.Slot] != original)
            {
                Release(candidate);
                return;
            }

            int farSlot = original.Slot;
            bool refresh = original.Dirty || original.Face >= 0 || original.DoorFaces != 0 ||
                           candidate.Dirty || candidate.DoorFaces != 0;
            AbortStage(original);
            _slots[farSlot] = null;
            _dynActive[farSlot] = 0f;
            _dynFacesLast[farSlot] = 0;
            _nativeMovingActive[farSlot] = 0f;
            _pickupInvalidFaces[farSlot] = 0;
            FreeDynSlot(farSlot);
            FreeSlotMap(farSlot); // sized maps: the candidate's map is already on the near slot

            // The candidate's native comparison map contains the completed
            // high-res data. Preserve it by transferring ownership, not clearing.
            _slots[nearSlot] = original;
            original.Slot = nearSlot;
            original.Center = candidate.Center;
            original.RootPos = candidate.RootPos;
            original.RootRot = candidate.RootRot;
            original.NativeCacheRange = candidate.NativeCacheRange;
            original.Ready = true;
            original.Face = -1;
            original.Staged = false;
            original.DoorFaces = 0;
            original.DoorPrepared = false;
            original.Dirty = refresh;
            original.DirtyAt = Time.time;
            // Keep the previous fade strength; do not reveal an unshadowed frame.
            original.PromotionTo = null;
            candidate.PromotionFrom = null;
            candidate.Slot = -1;
            UploadDynActive();
            UploadNativeMovingActive();
            UpdateGlobals();
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
        // Walking up to a light also re-ranks the slots (not only turning or the 1 s pass), so a
        // light you're now close to gets its full-size map without waiting.
        private Vector3 _lastReassignEye;
        private const float MovedReassign = 2f;
        private const int MaxPromotions = 3;

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
            // Prefer a light whose illumination AND its cast shadows can
            // affect the visible room, even when the emitter itself is
            // farther from the camera. This only improves initial tier
            // ranking; completed shadows and non-destructive promotions
            // retain their existing no-pop behavior.
            float influenceGap = Mathf.Max(0f, cameraDist - range);
            float p = 0.42f * influenceGap + 0.28f * cameraDist;
            if (!GeometryUtility.TestPlanesAABB(ReassignFrustum, new Bounds(lp, Vector3.one * (range * 2f))))
                p = p * OffscreenWeight + 2f;
            else
            {
                // In view: how much of the screen its light (and so its shadows) covers.
                // Inside its reach that's all of it; further out, its reach's size against
                // the distance (about its share of the view). A big one on screen wins a
                // full-size map over a smaller one at the same distance, and a faint light
                // (low intensity) counts for less than a bright one of the same size.
                float screen = cameraDist <= range ? 1f : Mathf.Clamp01(range / cameraDist);
                float bright = Mathf.Clamp01(t.Light.intensity / 1.5f);
                p -= ScreenWeight * screen * (0.5f + 0.5f * bright);
            }
            return t.Tiny ? p + TinyRank : p;
        }

        // While the cache runs, no point light uses Unity's own per-frame
        // shadows: slotted lights read the cache, the rest have none (as
        // vanilla lights past their shadow distance).
        private static void Suppress(Tracked t)
        {
            if (t.Suppressed || t.Light == null || s_benchVanillaShadows)
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
                    Unsuppress(t);
                }
            }
        }

        private void Take(Tracked t, int slot)
        {
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
            t.DoorFaces = 0;
            t.DoorPrepared = false;
            t.Dirty = true;
            t.DirtyAt = Time.time;
            t.Ready = false;
            t.Face = -1;
        }

        private void Release(Tracked t)
        {
            if (t == null) return;
            if (t.PromotionTo != null)
            {
                Tracked pending = t.PromotionTo;
                t.PromotionTo = null;
                pending.PromotionFrom = null;
                Release(pending);
            }
            if (t.PromotionFrom != null)
            {
                if (t.PromotionFrom.PromotionTo == t)
                    t.PromotionFrom.PromotionTo = null;
                t.PromotionFrom = null;
            }
            if (t.Slot < 0) return;
            if (t.Slot < _slots.Length && _slots[t.Slot] == t)
                _slots[t.Slot] = null;
            if (t.Slot >= 0 && t.Slot < MaxSlots)
            {
                _dynActive[t.Slot] = 0;
                _dynFacesLast[t.Slot] = 0;
                _nativeMovingActive[t.Slot] = 0f;
                FreeDynSlot(t.Slot);
                KeepOrFree(t, t.Slot); // a finished map is kept for when this light comes back (Keep)
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
            System.Array.Clear(_pickupInvalidFaces, 0, _pickupInvalidFaces.Length);
            System.Array.Clear(_nativeMovingActive, 0, MaxSlots);
            UploadNativeMovingActive();
            foreach (Tracked t in _slots)
                Release(t);
            System.Array.Clear(_nativeActive, 0, MaxSlots);
            Shader.SetGlobalFloatArray(NativeActiveId, _nativeActive);
            // Direct shader reset bypasses UploadArrayIfChanged: force next sync.
            Shader.SetGlobalFloat(CountId, 0);
        }
        // Only upload actual source positions when they change.
        // Vanilla LightFlicker intensity is untouched; no fake shadow motion.
        private readonly Vector4[] _lightNow = new Vector4[MaxSlots];
        private static readonly int LightNowId = Shader.PropertyToID("_WireLightNow");
        private void UploadLightPositions()
        {
            bool changed = false;
            for (int s = 0; s < _slots.Length; s++)
            {
                Tracked t = _slots[s];
                Vector4 now = t != null && t.Ready && t.Light != null
                    ? new Vector4(t.Light.transform.position.x,
                                  t.Light.transform.position.y,
                                  t.Light.transform.position.z,
                                  t.FakeFlame ? 1f : 0f)
                    : new Vector4(1e9f, 1e9f, 1e9f, 0f);
                Vector4 prev = _lightNow[s];
                if (prev.x == now.x && prev.y == now.y &&
                    prev.z == now.z && prev.w == now.w)
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
                Vector3 published = t != null && t.Staged
                    ? t.PublishedCenterBeforeStage : t != null ? t.Center : Vector3.zero;
                _slotData[s] = t != null && t.Ready && t.Light != null
                    ? new Vector4(published.x, published.y, published.z, t.Fade)
                    : new Vector4(1e9f, 1e9f, 1e9f, 0f);
            }
            Shader.SetGlobalVectorArray(LightsId, _slotData);
            Shader.SetGlobalFloat(CountId, _slots.Length);
            Shader.SetGlobalFloat(NearCountId, _near);
            UploadTierMaps(); // where each slot's map is
        }
        // ---- Moving casters ----

        private static readonly List<int> DynCandidates = new List<int>();
        private static readonly int[] DynFaces = new int[MaxSlots];
        private static readonly int[] DynDraw = new int[MaxSlots];
        private static readonly int[] DynFaceHash = new int[MaxSlots * 6];
        private static readonly int[] DynFaceLp = new int[MaxSlots * 6];
        private readonly int[] _faceLp = new int[MaxSlots * 6];
        private static readonly int[] FaceHashNow = new int[6];
        // The same per face over only what isn't redrawn every frame: unchanged, a face only needs
        // the part where the moving things are (and were) redrawn.
        private static readonly int[] StableHashNow = new int[6];
        private static readonly int[] DynFaceStable = new int[MaxSlots * 6];
        private readonly int[] _faceStable = new int[MaxSlots * 6];
        private static readonly int[] DynPartial = new int[MaxSlots];
        private readonly RectInt[] _dynVolRect = new RectInt[MaxSlots * 6]; // where the moving things were drawn last
        private readonly int[] _faceHash = new int[MaxSlots * 6]; // per moving face, as of its last draw
        private static readonly Vector3[] DynOrigin = new Vector3[MaxSlots];

        private void LateUpdate() => LateUpdateWork();

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
            else if (_active && view != null && Time.time >= _nextTurnReassign && !Loading() &&
                     (view.transform.position - _lastReassignEye).sqrMagnitude > MovedReassign * MovedReassign)
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
            if (_shrinkPools && _active)
            {
                _shrinkPools = false;
                ShrinkMovingPools();
            }
            if (Swings.Count > 0)
                UpdateSwings();
            UpdateStandHandoffs(); // Dropped items no longer need a static-map handoff.
            UpdateGhost();
            if (!_active || !Plugin.UseShader.Value || !Plugin.CachedShadows ||
                _dynMaps == null || _movScratch == null || !Plugin.MovingCasters ||
                _slots.Length == 0)
                return;
            List<Character> characters = Character.GetAllCharacters();
            // Reuse the main camera already acquired earlier in this callback.
            Camera main = view;
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
                ReviewResting();
                CleanupDroppedItems();
            }
            Vector3 cameraPosition = main.transform.position;
            GeometryUtility.CalculateFrustumPlanes(main, Frustum);
            float farMovingCut = FarMovingCutoff(cameraPosition);
            CollectGatherLights(cameraPosition, farMovingCut);
            long benchGatherAt = _benchSampling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            // No eligible shadowed light can see moving geometry: there is
            // nothing to gather or spatially bucket. Keep normal item discovery
            // when any ItemDrop exists, since it also owns DropVisuals exclusion
            // for subsequent STATIC cubemap recaches.
            if (GatherLights.Count == 0 && Items.Count == 0)
            {
                Casters.Clear();
                _groupCount = 0;
            }
            else
            {
                GatherCasters(cameraPosition, characters);
                if (_groupCount > 64) BuildMovingGroupGrid(cameraPosition);
            }
            if (_benchSampling)
            {
                _benchGatherTicks += System.Diagnostics.Stopwatch.GetTimestamp() - benchGatherAt;
                _benchGroups += _groupCount;
            }
            long benchHashAt = _benchSampling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            DynCandidates.Clear();
            // Count newly requested physical cubes as candidates are already
            // gathered. On streaming into a dense base, reserve capacity
            // ONCE instead of 4->8->16->32 allocations/copies this frame.
            int newNearCubes = 0, newFarCubes = 0;
            bool releasedAnyMovingMaps = false;
            for (int sl = 0; sl < _slots.Length; sl++)
            {
                DynFaces[sl] = 0;
                Tracked t = _slots[sl];
                if (t == null || !t.Ready || t.Light == null || !t.Light.isActiveAndEnabled)
                    continue;
                // Following the flame: drawn from where it is this frame (the
                // shader reads it from there too). Otherwise its resting spot.
                Vector3 lp = !t.FakeFlame ? t.Light.transform.position : t.Center;
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
                float nearestMoving = float.PositiveInfinity; // the nearest thing moving every frame, as a share of the range
                for (int f = 0; f < 6; f++) { FaceHashNow[f] = 17; StableHashNow[f] = 17; }
                if (_benchSampling) System.Array.Clear(FaceKindsNow, 0, 6);
                List<int> groupsForLight = _groupsBySlot[sl];
                if (groupsForLight == null)
                    _groupsBySlot[sl] = groupsForLight = new List<int>(16);
                else
                    groupsForLight.Clear();
                // Most lights do not overlap most moving casters. A small,
                // pre-bucketed broadphase gives only potential groups. Groups
                // remain in their original order for identical face hashes.
                // Tiny lights retain their vanilla empty moving caster set.
                // Ultra-distant lights: cached shadows only, no moving maps (pools stay small).
                // Distant lights: only the nearest few get moving shadows (pool ceiling).
                bool noMoving = t.Tiny || sl >= UltraStart ||
                    sl >= _near && (t.Center - cameraPosition).sqrMagnitude > farMovingCut;
                List<int> possibleGroups = !noMoving && _groupCount > 64
                    ? MovingGroupCandidates(lp, range) : null;
                int possibleCount = noMoving ? 0 : possibleGroups == null
                    ? _groupCount : possibleGroups.Count;
                for (int n = 0; n < possibleCount; n++)
                {
                    int g = possibleGroups == null ? n : possibleGroups[n];
                    Group grp = Groups[g];
                    if (!Within(grp, lp, range) || t.Stand != null && ReferenceEquals(grp.Owner, t.Stand))
                        continue;
                    // Use each renderer's already-gathered world bounds for
                    // exact conservative face COVERAGE instead of hashing the
                    // whole group bounding sphere into unrelated cubemap faces.
                    // DrawMovingDirect uses this same Caster.Faces test. This
                    // cannot exclude a genuine caster: each AABB fits inside
                    // the sphere defined by its extents magnitude.
                    int gf = 0;
                    for (int k = grp.Start, end = grp.Start + grp.Count; k < end; k++)
                    {
                        gf |= Casters[k].Faces(lp);
                        if (gf == 63) break; // all cube faces proven covered
                    }
                    if (_benchSampling)
                    {
                        int oldMask = FacesFor(grp.Center - lp, grp.Radius);
                        int removed = oldMask & ~gf, added = gf & ~oldMask;
                        while (removed != 0) { removed &= removed - 1; _benchCoarseExtraFaceLinks++; }
                        while (added != 0) { added &= added - 1; _benchPreciseAddedFaceLinks++; }
                    }
                    if (gf == 0) continue;
                    groupsForLight.Add(g);
                    if (grp.Volatile) nearestMoving = Mathf.Min(nearestMoving, (grp.Center - lp).magnitude / Mathf.Max(.01f, range));
                    float cameraDistanceSq = (grp.Center - cameraPosition).sqrMagnitude;
                    if (cameraDistanceSq < closestCasterDistanceSq)
                        closestCasterDistanceSq = cameraDistanceSq;
                    faces |= gf;
                    // Per face: you moving doesn't redraw a face that only
                    // has items lying still.
                    for (int f = 0; f < 6; f++)
                        if ((gf & (1 << f)) != 0)
                        {
                            FaceHashNow[f] = FaceHashNow[f] * 31 + grp.Hash;
                            if (!grp.Volatile) StableHashNow[f] = StableHashNow[f] * 31 + grp.Hash;
                            if (_benchSampling) FaceKindsNow[f] |= grp.Kind;
                        }
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
                int lpHash = !t.FakeFlame ? Quantize(lp, 500f) : 0;
                int draw = 0, partial = 0;
                for (int f = 0; f < 6; f++)
                {
                    int i = sl * 6 + f;
                    int h = FaceHashNow[f];
                    DynFaceHash[i] = h;
                    DynFaceLp[i] = lpHash;
                    DynFaceStable[i] = StableHashNow[f];
                    if ((faces & (1 << f)) == 0)
                        continue;
                    // Only the flame moved and everything in this face is
                    // still: its shadows barely shift, so every fourth frame.
                    bool still = (_dynFacesLast[sl] & (1 << f)) != 0 && h == _faceHash[i];
                    if (!still || ((_pickupInvalidFaces[sl] & (1 << f)) != 0) ||
                        (lpHash != _faceLp[i] && (Time.frameCount + i) % 4 == 0))
                    {
                        draw |= 1 << f;
                        // only the moving things changed: redraw just their part of the face
                        if ((_dynFacesLast[sl] & (1 << f)) != 0 && StableHashNow[f] == _faceStable[i] &&
                            (_pickupInvalidFaces[sl] & (1 << f)) == 0 && lpHash == _faceLp[i])
                            partial |= 1 << f;
                        if (_benchSampling)
                        {
                            DynFaceKinds[i] = FaceKindsNow[f];
                            DynFaceReasons[i] =
                                (((_dynFacesLast[sl] & (1 << f)) == 0) ? 1 : 0) |
                                (h != _faceHash[i] ? 2 : 0) |
                                (lpHash != _faceLp[i] ? 4 : 0) |
                                (((_pickupInvalidFaces[sl] & (1 << f)) != 0) ? 8 : 0);
                        }
                    }
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
                if (Plugin.FaintCut && nearestMoving < float.PositiveInfinity)
                    updateEvery = Mathf.Max(updateEvery, FaintEvery(nearestMoving));
                // Stand attachment/removal must be instant even if the
                // stand is beyond the camera's normal detail distance.
                if (faces == _dynFacesLast[sl] &&
                    Time.frameCount > Mathf.Max(_standChangedFrame, _dropChangedFrame) + 1 &&
                    (Time.frameCount + sl) % updateEvery != 0)
                    continue;
                DynDraw[sl] = draw;
                DynPartial[sl] = partial;
                DynFaces[sl] = faces;
                DynOrigin[sl] = lp;
                DynCandidates.Add(sl);
                if (_dynPhysical[sl] < 0 && faces != 0)
                {
                    if (sl < _near) newNearCubes++;
                    else newFarCubes++;
                }
            }

            if (_benchSampling)
            {
                _benchHashTicks += System.Diagnostics.Stopwatch.GetTimestamp() - benchHashAt;
                _benchSamples++;
                _benchCandidates += DynCandidates.Count;
                foreach (int candidate in DynCandidates)
                {
                    int facesDrawn = DynDraw[candidate];
                    while (facesDrawn != 0)
                    {
                        int bit = facesDrawn & -facesDrawn;
                        int f = 0;
                        while ((1 << f) != bit) f++;
                        int index = candidate * 6 + f;
                        int kind = DynFaceKinds[index], reason = DynFaceReasons[index];
                        if ((kind & KindCharacter) != 0) _benchCharacterFaces++;
                        if ((kind & KindSwing) != 0) _benchSwingFaces++;
                        if ((kind & KindMachine) != 0) _benchMachineFaces++;
                        if ((kind & KindDrop) != 0) _benchDropFaces++;
                        if ((kind & KindGhost) != 0) _benchGhostFaces++;
                        if ((kind & KindStand) != 0) _benchStandFaces++;
                        if ((reason & 1) != 0) _benchNewFaces++;
                        if ((reason & 2) != 0) _benchChangedHashFaces++;
                        if ((reason & 4) != 0) _benchMovingLightFaces++;
                        if ((reason & 8) != 0) _benchPickupFaces++;
                        facesDrawn &= facesDrawn - 1;
                        _benchFaces++;
                    }
                }
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
            // The game's point-light shadow setting below its highest: vanilla's per-frame work only.
            if (GameLimited)
            {
                HoldToGameBudget(DynCandidates, DynDraw, cameraPosition);
                if (DynCandidates.Count == 0)
                {
                    if (releasedAnyMovingMaps) { UploadDynActive(); UploadNativeMovingActive(); }
                    return;
                }
                newNearCubes = newFarCubes = 0; // only the ones drawn this frame need a cube
                foreach (int sl in DynCandidates)
                    if (_dynPhysical[sl] < 0 && DynFaces[sl] != 0) { if (sl < _near) newNearCubes++; else newFarCubes++; }
            }
            // GPU pool growth can involve gigabytes of texture copies with
            // 2048px faces and native depth enabled. Batch newly needed
            // physical cubes per tier to avoid repeated growth/copies in
            // the SAME frame after teleporting into a densely lit area.
            ReserveNewMovingCubes(newNearCubes, newFarCubes);
            // Allocate/retain a physical cube only for a light with active
            // moving casters; completed faces are never downsampled.
            _movingDenied.Clear();
            for (int k = DynCandidates.Count - 1; k >= 0; k--)
            {
                int logical = DynCandidates[k];
                if (DynFaces[logical] != 0 && !EnsureDynSlot(logical))
                    _movingDenied.Add(logical);
            }
            // The pool is at its cap: nearer lights first, each may take a further light's map.
            bool evicted = false;
            if (_movingDenied.Count > 0)
            {
                Vector3 cam = cameraPosition;
                s_sortCam = cam;
                s_sortSlots = _slots;
                _movingDenied.Sort(NearerFirst);
                foreach (int sl in _movingDenied)
                    if (EvictFurther(sl, cam)) { evicted = true; EnsureDynSlot(sl); }
                // whoever ends up without a cube (denied, or evicted this frame) isn't drawn
                for (int k = DynCandidates.Count - 1; k >= 0; k--)
                    if (DynFaces[DynCandidates[k]] != 0 && _dynPhysical[DynCandidates[k]] < 0)
                        DynCandidates.RemoveAt(k);
            }
            if (DynCandidates.Count == 0)
            {
                if (evicted) { UploadDynActive(); UploadNativeMovingActive(); }
                return;
            }
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
                // Native-only: drawn into the scratch cube and converted into the depth array right
                // after, in the same command buffer, before the next light draws there.
                RenderTexture drawMaps = _builtNativeOnly ? _movScratch : dynMaps;
                int drawBase = _builtNativeOnly ? 0 : pl * 6;
                int draw = DynDraw[sl];
                bool nativeMoving = Plugin.NativeMovingDepthPcf && EnsureNativeMovingTier(sl < _near);
                if (nativeMoving && _dynFacesLast[sl] == 0)
                    InitNativeMovingSlot(t, pl);
                for (int f = 0; f < 6; f++)
                    if ((draw & (1 << f)) != 0)
                    {
                        _faceHash[sl * 6 + f] = DynFaceHash[sl * 6 + f];
                        _faceLp[sl * 6 + f] = DynFaceLp[sl * 6 + f];
                        _faceStable[sl * 6 + f] = DynFaceStable[sl * 6 + f];
                    }
                // Faces drawn last frame but not now are cleared; when a slot
                // starts using its moving map, all its other faces are too.
                int toClear = (faces != 0 && _dynFacesLast[sl] == 0 ? 63 : _dynFacesLast[sl]) & ~faces;
                if (draw != 0)
                    DrawMovingDirect(t, pl, draw, DynOrigin[sl], sl * 6, _dynFacesLast[sl] == 0, _dynFacesLast[sl] == 0 ? 0 : DynPartial[sl]);
                for (int f = 0; f < 6; f++)
                    if ((toClear & (1 << f)) != 0)
                    {
                        // RGHalf requires BOTH front and back channels to equal
                        // the empty-distance sentinel, not (60000, 0).
                        if (_combine == null)
                            _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
                        _frameCb.SetRenderTarget(new RenderTargetIdentifier(drawMaps, 0, CubemapFace.Unknown, drawBase + f));
                        _frameCb.DrawProcedural(Matrix4x4.identity, _combine, 2, MeshTopology.Triangles, 3);
                        _dynRect[sl * 6 + f] = default;
                    }
                if (nativeMoving)
                {
                    int dirtyFaces = draw | toClear;
                    for (int f = 0; f < 6; f++)
                        if ((dirtyFaces & (1 << f)) != 0)
                        {
                            // Cleared faces and camera-drawn faces update the
                            // entire cubemap side. Only direct draws have a
                            // provably complete changed-pixel rectangle.
                            bool drawn = (draw & (1 << f)) != 0 && (toClear & (1 << f)) == 0;
                            RectInt touched = Plugin.DirectMovingDraw && drawn
                                ? _nativeMovingTouched[sl * 6 + f] : default;
                            // Native-only: a drawn face with nothing drawn on it this time has no
                            // new pixels in the scratch cube (what's there is another light's).
                            if (_builtNativeOnly && drawn && (touched.width <= 0 || touched.height <= 0))
                                continue;
                            QueueNativeMovingFace(t, drawMaps, drawBase + f, pl, f, touched);
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
        // Benchmark: the game's own shadows back on for every tracked light (as vanilla draws them),
        // next to the cached maps, to compare what vanilla's way of moving shadows costs.
        private static bool s_benchVanillaShadows;
        internal static void BenchmarkVanillaShadows(bool on)
        {
            if (s_benchVanillaShadows == on) return;
            s_benchVanillaShadows = on;
            foreach (Tracked t in Lights)
                if (t != null && t.Light != null)
                {
                    if (on) Unsuppress(t);
                    else if (Instance != null && Instance._active) Suppress(t);
                }
        }

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
                Graphics.ExecuteCommandBuffer(_frameCb);
            }
        }
        private static readonly Color Empty = new Color(60000f, 0, 0, 0); // farther than any light reaches
        private Material _distMaterial;


        private static readonly Dictionary<Character, Renderer[]> CharacterRenderers = new Dictionary<Character, Renderer[]>();

        // Native VisEquipment.UpdateLodgroup runs ONLY when an equipped item
        // actually changes (VisEquipment.UpdateEquipmentVisuals' changed flag).
        // Previously this renderer array stayed stale for the character's
        // entire lifetime after its first sword/armor/hair attachment.
        internal static void EquipmentVisualChanged(VisEquipment visual)
        {
            if (visual == null) return;
            Character c = visual.GetComponentInParent<Character>();
            if (c != null)
            {
                CharacterRenderers.Remove(c);
                return; // characters belong to the moving map, never static
            }
            // Armor stands and other non-character equipment displays are
            // static pieces. A changed visual invalidates nearby static faces
            // once, at the native equipment-change event, not every frame.
            Piece p = visual.GetComponentInParent<Piece>();
            if (p != null) PieceChanged(p.transform.position, 0f, p);
        }

        // Partial faces: only what moves changed. The part of the face where the moving things are now
        // and were last frame is cleared, and everything that overlaps it is drawn again (still things
        // included, so nothing is cut). The rest of the face keeps what it had. A whole face otherwise.
        private bool DrawMovingDirect(Tracked t, int slice, int faces, Vector3 lp, int rectBase, bool firstUse, int partialFaces)
        {
            if (_distMaterial == null)
                _distMaterial = new Material(DistanceShader) { hideFlags = HideFlags.HideAndDontSave };
            if (_combine == null)
                _combine = new Material(CombineShader) { hideFlags = HideFlags.HideAndDontSave };
            bool fullSize = t.Slot < _near;
            RenderTexture maps = _builtNativeOnly ? _movScratch : fullSize ? _dynMaps : _farDynMaps;
            if (_builtNativeOnly) slice = 0; // the scratch cube: converted into this light's cube after
            if (s_gsPass == -2)
            {
                s_gsPass = SystemInfo.supportsGeometryShaders ? _distMaterial.FindPass("WIREGS") : -1;
                Plugin.Note("[moving] " + (s_gsPass >= 0 ? "one draw per object for all cube faces (geometry shader)" : "one draw per object per cube face"));
            }
            bool gs = s_gsPass >= 0;
            GsMask.Clear();
            GsOrder.Clear();
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
                    LightCasterFaces.Add(grp.Volatile ? mask | VolatileBit : mask);
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
                FaceRects.Clear();
                RectInt rect = default, vol = default;
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
                    FaceRects.Add(casterRect);
                    rect = Union(rect, casterRect);
                    if ((LightCasterFaces[ci] & VolatileBit) != 0) vol = Union(vol, casterRect);
                }
                bool partial = !firstUse && (partialFaces & (1 << f)) != 0;
                RectInt lastVol = _dynVolRect[rectBase + f];
                _dynVolRect[rectBase + f] = vol;
                if (partial)
                {
                    // the moving things' part, now and last frame; the face's whole extent grows by it
                    RectInt dirty = Union(vol, lastVol);
                    _dynRect[rectBase + f] = Union(_dynRect[rectBase + f], vol);
                    if (dirty.width <= 0)
                    {
                        _nativeMovingTouched[rectBase + f] = default;
                        continue;
                    }
                    dirty = Union(dirty, new RectInt(dirty.x, size - dirty.yMax, dirty.width, dirty.height));
                    int keep = 0;
                    for (int n = 0; n < FaceRenderers.Count; n++)
                        if (FaceRects[n].Overlaps(dirty)) // dirty already holds its mirror
                            FaceRenderers[keep++] = FaceRenderers[n];
                    FaceRenderers.RemoveRange(keep, FaceRenderers.Count - keep);
                    rect = dirty;
                }
                else
                {
                    RectInt last = firstUse ? new RectInt(0, 0, size, size) : _dynRect[rectBase + f];
                    _dynRect[rectBase + f] = rect;
                    rect = Union(rect, last);
                }
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
                if (_benchSampling) { if (partial) _benchPartialFaces++; else _benchFullFaces++; }
                if (gs)
                {
                    // drawn below, once per object for all its faces
                    _frameCb.DisableScissorRect();
                    // as the per-face path's matrices end up on the GPU (SetViewProjectionMatrices
                    // converts its projection once more)
                    GsVP[f] = GL.GetGPUProjectionMatrix(proj, true) * view;
                    foreach (int k in FaceRenderers)
                    {
                        if (!GsMask.TryGetValue(k, out int m)) GsOrder.Add(k);
                        GsMask[k] = m | (1 << f);
                    }
                    continue;
                }
                _frameCb.SetViewProjectionMatrices(view, proj);
                if (_benchSampling) _benchDraws += FaceRenderers.Count;
                foreach (int k in FaceRenderers)
                {
                    Caster c = Casters[k];
                    for (int sub = 0; sub < c.Subs; sub++)
                        _frameCb.DrawRenderer(c.R, _distMaterial, sub, 0);
                }
                _frameCb.DisableScissorRect();
            }
            if (gs && GsOrder.Count > 0)
            {
                // Each face's part was cleared above. Without a scissor an object also redraws its
                // unchanged pixels outside it, which min blending leaves as they were.
                _frameCb.SetRenderTarget(new RenderTargetIdentifier(maps, 0, CubemapFace.Unknown, -1));
                _frameCb.SetGlobalMatrixArray(FaceVPId, GsVP);
                _frameCb.SetGlobalFloat(SliceBaseId, slice * 6);
                foreach (int k in GsOrder)
                {
                    Caster c = Casters[k];
                    _frameCb.SetGlobalFloat(FaceMaskId, GsMask[k]);
                    for (int sub = 0; sub < c.Subs; sub++)
                        _frameCb.DrawRenderer(c.R, _distMaterial, sub, s_gsPass);
                }
                if (_benchSampling) _benchDraws += GsOrder.Count;
            }
            return true;
        }

        // The one-draw-for-all-faces pass (WireShadowDistance "WIREGS"), if the bundle has it and
        // the GPU runs geometry shaders; -1 otherwise (one draw per face, as before).
        private static int s_gsPass = -2;
        private static readonly Matrix4x4[] GsVP = new Matrix4x4[6];
        private static readonly Dictionary<int, int> GsMask = new Dictionary<int, int>();
        private static readonly List<int> GsOrder = new List<int>();
        private static readonly int FaceVPId = Shader.PropertyToID("_WireFaceVP");
        private static readonly int FaceMaskId = Shader.PropertyToID("_WireFaceMask");
        private static readonly int SliceBaseId = Shader.PropertyToID("_WireSliceBase");

        private static readonly List<int> FaceRenderers = new List<int>();
        private static readonly List<RectInt> FaceRects = new List<RectInt>();
        private const int VolatileBit = 1 << 6; // in LightCasterFaces: the caster's group is redrawn every frame
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
            // Redrawn every frame (a character, a swinging door, a turning machine: hash = frame).
            // Only these change a moving face from one frame to the next without it being redrawn
            // whole (Partial).
            internal bool Volatile;
            // Diagnostic classification only. Does not participate in hashes,
            // face culling, drawing, or cache invalidation.
            internal int Kind;
            internal Object Owner; // the stand of a mounted item's group: its own light skips it
            internal int Start, Count;
        }

        // Only used for benchmark attribution. Overlap is not proof that a
        // particular caster CAUSED the invalidation; counters label which
        // types are present in each dirty cubemap face.
        internal const int KindCharacter = 1, KindSwing = 2, KindMachine = 4,
            KindDrop = 8, KindGhost = 16, KindStand = 32;
        private static readonly int[] FaceKindsNow = new int[6];
        private static readonly int[] DynFaceKinds = new int[MaxSlots * 6];
        private static readonly int[] DynFaceReasons = new int[MaxSlots * 6];
        private static readonly List<Caster> Casters = new List<Caster>();
        private static readonly List<Group> Groups = new List<Group>();
        private static int _groupCount;

        // Opt-in scope timing inside the EXISTING four-mode benchmark only.
        // It observes the hot paths without adding Update, timers, allocations
        // or profiling overhead to normal play. GPU cost comes separately
        // from the benchmark's full-vs-skip-moving submission comparison.
        private static bool _benchSampling;
        private static long _benchGatherTicks, _benchHashTicks;
        private static int _benchSamples, _benchGroups, _benchCandidates, _benchFaces;
        private static int _benchCharacterFaces, _benchSwingFaces, _benchMachineFaces,
            _benchDropFaces, _benchGhostFaces, _benchStandFaces;
        private static int _benchChangedHashFaces, _benchNewFaces, _benchMovingLightFaces,
            _benchPickupFaces;
        private static int _benchCoarseExtraFaceLinks, _benchPreciseAddedFaceLinks, _benchPartialFaces, _benchFullFaces, _benchDraws;
        internal static void BeginBenchmarkSampling()
        {
            _benchSampling = true;
            _benchGatherTicks = _benchHashTicks = 0;
            _benchSamples = _benchGroups = _benchCandidates = _benchFaces = 0;
            _benchCharacterFaces = _benchSwingFaces = _benchMachineFaces =
                _benchDropFaces = _benchGhostFaces = _benchStandFaces = 0;
            _benchChangedHashFaces = _benchNewFaces = _benchMovingLightFaces =
                _benchPickupFaces = 0;
            _benchCoarseExtraFaceLinks = _benchPreciseAddedFaceLinks = 0;
            _benchPartialFaces = _benchFullFaces = _benchDraws = 0;
        }
        internal static string EndBenchmarkSampling()
        {
            _benchSampling = false;
            double scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int n = System.Math.Max(1, _benchSamples);
            ShadowCache c = Instance;
            int near = c != null ? c._dynNearCapacity : 0;
            int far = c != null ? c._dynFarCapacity : 0;
            return "CPU gather " + (_benchGatherTicks * scale / n).ToString("F3") +
                "ms/frame, face grouping " + (_benchHashTicks * scale / n).ToString("F3") +
                "ms/frame, groups " + ((double)_benchGroups / n).ToString("F1") +
                ", updated cubes " + ((double)_benchCandidates / n).ToString("F2") +
                ", dirty faces planned " + ((double)_benchFaces / n).ToString("F2") +
                "/frame; dirty face overlaps character/swing/machine/drop/ghost/stand " +
                _benchCharacterFaces + "/" + _benchSwingFaces + "/" +
                _benchMachineFaces + "/" + _benchDropFaces + "/" +
                _benchGhostFaces + "/" + _benchStandFaces +
                "; hash/new/light/pickup face triggers " +
                _benchChangedHashFaces + "/" + _benchNewFaces + "/" +
                _benchMovingLightFaces + "/" + _benchPickupFaces +
                "; group-face links avoided/added vs coarse " +
                _benchCoarseExtraFaceLinks + "/" + _benchPreciseAddedFaceLinks +
                "; sparse capacities near/far " + near + "/" + far +
                "; faces redrawn in part/whole " + _benchPartialFaces + "/" + _benchFullFaces +
                ", caster draws " + ((double)_benchDraws / n).ToString("F1") + "/frame";
        }

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

        // Build once when a rigid, unanimated dropped item settles. The
        // renderer still gets rechecked for activation on every group insert.
        // Animated, skinned or moving items use the original live bounds path.
        private static int DropGeometryVisibility(Renderer[] renderers)
        {
            int hash = 17;
            foreach (Renderer renderer in renderers)
                hash = unchecked(hash * 31 +
                    (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy ? 1 : 0));
            return hash;
        }

        private static void CacheDropGeometry(DropCaster d, Transform tr, Vector3 pos)
        {
            var geometry = new Caster[d.Renderers.Length];
            for (int i = 0; i < geometry.Length; i++)
            {
                Renderer renderer = d.Renderers[i];
                if (renderer == null) { d.CachedGeometry = null; return; }
                if (!SubCounts.TryGetValue(renderer, out int subs))
                {
                    if (SubCounts.Count > 8192) SubCounts.Clear();
                    MeshFilter mf = renderer.GetComponent<MeshFilter>();
                    subs = mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                    SubCounts[renderer] = subs;
                }
                Bounds b = renderer.bounds;
                geometry[i] = new Caster { R = renderer, B = b,
                    Radius = b.extents.magnitude, Subs = subs };
            }
            d.CachedGeometry = geometry;
            d.GeometryPosition = pos;
            d.GeometryRotation = tr.rotation;
            d.GeometryScale = tr.lossyScale;
            d.GeometryVisibility = DropGeometryVisibility(d.Renderers);
            // An enabling/disabling subrenderer is a genuine geometry change.
            // Fold it into the face hash so the cached shadow redraws.
            d.GeometryHash = (Quantize(pos, 200f) * 7 + Quantize(tr.eulerAngles, 2f)) ^
                d.GeometryVisibility;
        }

        private static void AddCachedCasterGroup(Vector3 center, float radius, int hash, Caster[] geometry, int kind = 0)
        {
            if (!LitByAny(center, radius)) return;
            int start = Casters.Count;
            foreach (Caster caster in geometry)
                if (caster.R != null && caster.R.enabled && caster.R.gameObject.activeInHierarchy)
                    Casters.Add(caster);
            if (Casters.Count == start) return;
            if (_groupCount == Groups.Count) Groups.Add(new Group());
            Group g = Groups[_groupCount++];
            g.Center = center;
            g.Radius = radius;
            g.Hash = hash;
            g.Character = false;
            g.Volatile = hash == Time.frameCount;
            g.Kind = kind;
            g.Owner = null;
            g.Start = start;
            g.Count = Casters.Count - start;
        }

        // In reach of the light. A character only while it's well inside:
        // out near the edge the light is too dim to show its shadow.
        private static bool Within(Group g, Vector3 lp, float range)
        {
            float r = g.Character ? range * DimEdge : range + g.Radius;
            return (g.Center - lp).sqrMagnitude <= r * r;
        }

        // Characters cast moving shadows out to the light's whole range.
        private const float DimEdge = 1f;
        // The fainter the light where the moving things are, the less often their shadows are
        // redrawn: every frame within half its range, then every 2nd, 3rd and 4th frame out to its
        // edge (by the nearest moving thing). No source: judgement values.
        private static int FaintEvery(float share) => share < .5f ? 1 : share < .7f ? 2 : share < .85f ? 3 : 4;

        private void GatherCasters(Vector3 cam, List<Character> characters)
        {
            Casters.Clear();
            _groupCount = 0;
            float far = Plugin.Distance + 30f;
            float far2 = far * far;
            foreach (Character c in characters)
            {
                if (c == null || (c.transform.position - cam).sqrMagnitude > far2)
                    continue;
                if (!CharacterRenderers.TryGetValue(c, out Renderer[] rs) || rs == null)
                    CharacterRenderers[c] = rs = System.Array.FindAll(c.GetComponentsInChildren<Renderer>(true), r => !SeeThrough(r));
                // Animated every frame: always redrawn (a pose check misses arms and legs).
                AddGroup(c.transform.position + Vector3.up, 1.2f, Time.frameCount, true, rs, true, KindCharacter);
            }
            for (int i = 0; i < Items.Count; i++)
            {
                ItemDrop it = Items[i];
                if (it == null || !it.isActiveAndEnabled) continue;
                Transform tr = it.transform;
                Vector3 pos = tr.position;
                if ((pos - cam).sqrMagnitude > far2) continue;
                DropCaster d = GetDrop(it, pos);
                if (!d.Mounted && !d.Placed && it.IsPiece()) PlaceDrop(d);
                if (d.Mounted || d.Placed)
                {
                    // resting for good: off the every-frame list
                    d.CurrentPosition = pos;
                    Resting.Add(it);
                    Items[i] = Items[Items.Count - 1];
                    Items.RemoveAt(Items.Count - 1);
                    i--;
                    continue;
                }
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
                    d.CachedGeometry = null;
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
                // Fixed, settled dropped meshes keep exactly the same world
                // bounds and submesh counts. Cache them once, not every frame.
                // Exact pose/scale comparisons bypass caching for any change.
                if (d.Stable && d.CanCacheGeometry &&
                    (d.CachedGeometry == null || d.GeometryPosition != pos ||
                     d.GeometryRotation != tr.rotation || d.GeometryScale != tr.lossyScale ||
                     d.GeometryVisibility != DropGeometryVisibility(d.Renderers)))
                    CacheDropGeometry(d, tr, pos);
                if (d.Stable && d.CachedGeometry != null)
                    AddCachedCasterGroup(pos, ItemRadius, d.GeometryHash, d.CachedGeometry, KindDrop);
                else
                    AddGroup(pos, ItemRadius,
                        Quantize(pos, 200f) * 7 + Quantize(tr.eulerAngles, 2f),
                        false, d.Renderers, false, KindDrop);
            }
            if (_ghostActive)
                AddGroup(_ghostCenter, _ghostRadius, _ghostHash, false, _ghostRenderers, true, KindGhost); // honor source prefab's shadow exclusions
            foreach (Swing w in Swings)
                AddGroup(w.Center, w.Radius, Time.frameCount, false, w.Renderers, false, KindSwing); // moving every frame of the swing
            AddMachineGroups(cam, far);
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
            g.Volatile = false;
            g.Kind = KindStand;
            g.Owner = s.Stand;
            g.Start = start;
            g.Count = Casters.Count - start;
        }

        // Lights the scan below will give moving shadows this frame (same skips: not ready, tiny,
        // ultra tier, far over the cap, off screen). A group none of them reaches would be dropped
        // by Within anyway, so its renderers aren't read at all.
        private static readonly List<Vector4> GatherLights = new List<Vector4>(64);
        private void CollectGatherLights(Vector3 cam, float farMovingCut)
        {
            GatherLights.Clear();
            for (int sl = 0; sl < _slots.Length; sl++)
            {
                Tracked t = _slots[sl];
                if (t == null || !t.Ready || t.Light == null || !t.Light.isActiveAndEnabled)
                    continue;
                if (t.Tiny || sl >= UltraStart || sl >= _near && (t.Center - cam).sqrMagnitude > farMovingCut)
                    continue;
                Vector3 lp = !t.FakeFlame ? t.Light.transform.position : t.Center;
                float range = Range(t);
                if (!GeometryUtility.TestPlanesAABB(Frustum, new Bounds(lp, Vector3.one * (range * 2f))))
                    continue;
                GatherLights.Add(new Vector4(lp.x, lp.y, lp.z, range));
            }
        }

        private static bool LitByAny(Vector3 c, float radius)
        {
            for (int i = 0; i < GatherLights.Count; i++)
            {
                Vector4 l = GatherLights[i];
                float dx = c.x - l.x, dy = c.y - l.y, dz = c.z - l.z, r = l.w + radius;
                if (dx * dx + dy * dy + dz * dz <= r * r) return true;
            }
            return false;
        }

        private void AddGroup(Vector3 center, float radius, int hash, bool character, Renderer[] rs, bool checkCasting, int kind = 0)
        {
            if (!LitByAny(center, radius))
                return;
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
            g.Volatile = hash == Time.frameCount;
            g.Kind = kind;
            g.Owner = null;
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
            // Four native moving PCF comparisons sample +/- 1/128 in
            // cubemap direction, then hardware bilinear taps adjacent texels.
            // The old 2-pixel scissor margin could leave stale depth at a
            // moving silhouette fringe. Pad the dirty region to include the
            // filter footprint without rendering the complete face.
            int pad = Mathf.Max(4, size / 128 + 2);
            int px0 = Mathf.Clamp(Mathf.FloorToInt((x0 * 0.5f + 0.5f) * size) - pad, 0, size);
            int py0 = Mathf.Clamp(Mathf.FloorToInt((y0 * 0.5f + 0.5f) * size) - pad, 0, size);
            int px1 = Mathf.Clamp(Mathf.CeilToInt((x1 * 0.5f + 0.5f) * size) + pad, 0, size);
            int py1 = Mathf.Clamp(Mathf.CeilToInt((y1 * 0.5f + 0.5f) * size) + pad, 0, size);
            return px1 > px0 && py1 > py0 ? new RectInt(px0, py0, px1 - px0, py1 - py0) : default;
        }

        // Reporting piggybacks on the already-existing LateUpdate -> Stats call.
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
                // Right after a world load or teleport: lights without a map yet that
                // are in view go first (checked live, the 1 s priorities may be stale),
                // then the rest. Re-renders of lights that already have a map wait.
                bool filling = Time.time < _fillUntil;
                Camera fillCam = filling ? Utils.GetMainCamera() : null;
                if (fillCam != null)
                    GeometryUtility.CalculateFrustumPlanes(fillCam, FillFrustum);
                bool jobInView = false;
                float jobRank = float.MaxValue;
                foreach (Tracked t in _slots)
                {
                    if (t == null || t.Light == null)
                        continue;
                    if (t.Face >= 0) { job = t; jobInView = fillCam != null && !t.Ready && InView(t); break; }
                    if (!((t.DoorFaces != 0 && t.Ready && !t.Dirty) ||
                          (t.Dirty && (Time.time >= t.DirtyAt || Loading()) && Time.time >= t.WaitUntil)))
                        continue;
                    if (fillCam == null)
                    {
                        if (job == null || t.Priority < job.Priority) job = t;
                        continue;
                    }
                    bool inView = !t.Ready && InView(t);
                    float rank = (t.Ready ? 2e6f : inView ? 0f : 1e6f) +
                        Vector3.Distance(t.Light.transform.position, fillCam.transform.position);
                    if (rank < jobRank) { job = t; jobRank = rank; jobInView = inView; }
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
                // While filling in after a load/teleport, missing maps in view get a few
                // faces per frame (bounded, so no single long frame).
                if (filling && jobInView && ++burst < FillFacesPerFrame)
                    continue;
                // An upgrade to a full-size map (the low-res one stays on screen meanwhile): two
                // faces a frame, so it's there in three frames instead of six.
                if (job.PromotionFrom != null && ++burst < 2)
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
        // Fill-in window after world load / teleport: missing maps in view first.
        private static float _fillUntil = -10f;
        private const float FillSeconds = 8f;
        // After arriving: maps that have to be built go at the usual one face a frame, spread out
        // (kept maps of an unchanged base come back without any, Keep). It was three a frame,
        // a couple of seconds of lag on getting home.
        private const int FillFacesPerFrame = 1;
        private static readonly Plane[] FillFrustum = new Plane[6];

        private static bool InView(Tracked t) =>
            GeometryUtility.TestPlanesAABB(FillFrustum, new Bounds(t.Light.transform.position, Vector3.one * (Range(t) * 2f)));

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
                // (moving pools used to be shrunk here and grown back on arrival: they stay full size now)
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
                _fillUntil = Time.time + FillSeconds;
                _reassignSoon = true;
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
            _fillUntil = Time.time + FillSeconds;
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
                // Its surroundings still loading in (after a portal): not yet (AreaWait).
                if (!ReadyToRender(t))
                    return;
                // Every render goes into a new cube of its own (Tiers): the old map stays on screen
                // until the new one is done, so none waits for another.
                if (_tiered)
                {
                    // Its map from before it unloaded, if nothing in its reach changed (Keep).
                    Vector3 c0 = RestingPosition(t.Light);
                    float r0 = Mathf.Max(1f, Range(t));
                    // Nothing built, broken, dropped or opened anywhere since its last fingerprint
                    // and it hasn't moved (a size change): the same one, no search.
                    int hash = t.Ready && t.CasterHash != 0 && t.HashVersion == WorldVersion && t.HashCenter == c0
                        ? t.CasterHash : CasterHash(c0, r0);
                    t.HashVersion = WorldVersion;
                    t.HashCenter = c0;
                    if (!t.Ready && TryReuseKept(t, c0, r0, hash))
                    {
                        t.Dirty = false;
                        t.Face = -1;
                        t.Staged = false;
                        t.Ready = true;
                        t.Fade = 1f;
                        t.RootPos = t.Light.transform.root.position;
                        t.RootRot = t.Light.transform.root.rotation;
                        if (t.PromotionFrom != null) CompleteNearPromotion(t);
                        else UpdateGlobals();
                        return;
                    }
                    t.PendingHash = hash;
                }
                if (!StartTierRender(t))
                {
                    t.DirtyAt = Time.time + 0.25f; // no room yet: tried again (oversized maps are shrinking)
                    return;
                }
                t.Dirty = false;
                t.Face = 0;
                t.Staged = t.Ready;
                t.StagedNativeReady = false;
                if (t.Staged) t.PublishedCenterBeforeStage = t.Center;
                // Still sample the original/resting source. Flame wobble is
                // permanently disabled, and vanilla brightness still flickers.
                t.Center = RestingPosition(t.Light);
                t.RootPos = t.Light.transform.root.position;
                t.RootRot = t.Light.transform.root.rotation;
                FindCasters(t);
            }
            DrawFace(t);
            if (++t.Face >= 6)
                FinishMap(t);
        }

        // Reused per static face capture. A renderer may already be hidden by
        // vanilla or another mod; never make it visible accidentally afterward.
        private static readonly List<Renderer> ChangedForceOff = new List<Renderer>();
        private static readonly List<Renderer> ChangedShadowMode = new List<Renderer>();
        private static readonly List<ShadowCastingMode> PriorShadowModes = new List<ShadowCastingMode>();

        private void DrawFace(Tracked t)
        {
            // sized maps: nothing to draw into (no render cube and none on screen)
            if (_tiered && t.RenderCube < 0 && (t.Slot < 0 || _mapTier[t.Slot] < 0f)) return;
            _camera.transform.position = t.Center;
            // The near plane is cut as a sphere in the shaders (with the
            // colliders drawn inside it), so the camera starts at the light.
            _camera.nearClipPlane = 0.05f;
            Shader.SetGlobalFloat(NearCutId, NearPlane(t));
            _camera.farClipPlane = Mathf.Max(1f, Range(t));
            Shader.SetGlobalVector(CenterId, t.Center);

            ChangedForceOff.Clear();
            ChangedShadowMode.Clear();
            PriorShadowModes.Clear();
            foreach (Renderer r in t.Hide)
            {
                if (r == null || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                ChangedForceOff.Add(r);
            }
            foreach (Renderer r in t.Show)
            {
                if (r == null || r.shadowCastingMode == ShadowCastingMode.On) continue;
                ChangedShadowMode.Add(r);
                PriorShadowModes.Add(r.shadowCastingMode);
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            // The scratch face of this map's size (the render's, or the one on screen for a door's
            // single face): the front (R) and back (G) in one camera render (OnePass).
            bool rendering = t.RenderCube >= 0;
            int tierNow = rendering ? t.RenderTier : (int)_mapTier[t.Slot];
            // Always the full-detail models: the cube camera picks LODs by its own view (wide,
            // small target), which swapped detailed pieces (the spice rack's herbs and frame)
            // for their simplified LOD in the cached shadow.
            float lodBias = QualitySettings.lodBias;
            QualitySettings.lodBias = 10000f;
            DrawUntaggedOnePass(t);
            bool ok = _camera.RenderToCubemap(_tierRG[tierNow], 1 << t.Face);
            QualitySettings.lodBias = lodBias;
            Shader.SetGlobalFloat(NearCutId, 0f);
            _staticCb.Clear();
            foreach (Renderer r in ChangedForceOff)
                if (r != null) r.forceRenderingOff = false;
            for (int i = 0; i < ChangedShadowMode.Count; i++)
                if (ChangedShadowMode[i] != null)
                    ChangedShadowMode[i].shadowCastingMode = PriorShadowModes[i];
            if (!ok) return;
            Graphics.CopyTexture(_tierRG[tierNow], t.Face, 0, _tierFlatRG[tierNow], 0, 0);
            TierFaceOnePass(t, t.Face, rendering);
        }

        private void FinishMap(Tracked t)
        {
            {
                t.Face = -1;
                // the new cube on screen, the old one freed
                PublishTierMap(t);
                t.Staged = false;
                if (!t.Ready || t.Instant)
                    t.Fade = !t.Ready && !t.Instant && !Loading() ? 0f : 1f;
                t.Instant = false;
                t.Ready = true;
                if (t.PromotionFrom != null)
                    CompleteNearPromotion(t);
                else
                    UpdateGlobals();
            }
        }

        private static readonly List<Renderer> Renderers = new List<Renderer>();
        private static readonly Plane[] Frustum = new Plane[6];

        private static readonly HashSet<Transform> Roots = new HashSet<Transform>();
        // Saturation previously silently omitted nearby shadow casters in
        // dense builds. Grow only on a static recache that actually overflows.
        private static Collider[] Hits = new Collider[2048];
        private static int OverflowReports;
        private static int CollectStaticHits(Vector3 center, float range)
        {
            int n;
            while (true)
            {
                n = Physics.OverlapSphereNonAlloc(center, range, Hits, ~0, QueryTriggerInteraction.Collide);
                if (n < Hits.Length) return n;
                if (Hits.Length >= 65536)
                {
                    if (OverflowReports++ < 4)
                        Plugin.Log.LogWarning("[static-casters] collider limit 65536 reached; some geometry may be omitted.");
                    return n;
                }
                System.Array.Resize(ref Hits, Hits.Length * 2);
                if (OverflowReports++ < 8)
                    Plugin.Note("[static-casters] expanded collider buffer to " + Hits.Length);
            }
        }

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
            Roots.Clear();
            // A swinging door is drawn into the moving map instead.
            foreach (Renderer r in SwingHidden)
                if (r != null) t.Hide.Add(r);
            // Animated machine parts live in the moving maps.
            foreach (Renderer r in MachineHidden)
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
                // an item never shades its own light (a trophy's flame, a weapon's glow)
                if (t.Light.GetComponentInParent<ItemStand>() == stand.Stand) continue;
                foreach (Renderer r in stand.Renderers)
                    AddStaticItemRenderer(t, r);
            }
            // placed food and meads, the same way
            ItemDrop ownItem = t.Light.GetComponentInParent<ItemDrop>();
            foreach (DropCaster d in PlacedDrops)
            {
                if (d.Item == null || !d.Item.isActiveAndEnabled || d.Item == ownItem) continue;
                float reach = Range(t) + d.PlacedRadius;
                if ((d.PlacedCenter - t.Center).sqrMagnitude > reach * reach) continue;
                foreach (Renderer r in d.Renderers)
                    if (r != null) AddStaticItemRenderer(t, r);
            }
            int n = CollectStaticHits(t.Center, Range(t));
            for (int i = 0; i < n; i++)
                if (Hits[i] != null)
                    Roots.Add(Hits[i].transform.root);
            Roots.Add(t.Light.transform.root);
            // A light doesn't shade its own model (an item's glow its item or
            // the stand holding it), except fires meant to.
            Component own = OwnShades(t) ? null : OwnPart(t);
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
                    else if (r.shadowCastingMode == ShadowCastingMode.Off || FarLod(r))
                        t.Hide.Add(r);
                    else
                    {
                        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                            t.Show.Add(r);
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
            // The replacement camera hides own-piece renderers via t.Hide,
            // but its independent untagged-material and two-sided-sheet
            // command-buffer passes bypass that visibility flag. Exclude only
            // the owning piece in those paths too, and ONLY for lights that
            // did not cast shadows in vanilla. All other geometry—including
            // nearby walls, door frames, and beams—continues to occlude.
            if (own != null)
            {
                Transform ownTransform = own.transform;
                t.Sheets.RemoveAll(r => r != null && r.transform.IsChildOf(ownTransform));
                for (int i = t.Explicit.Count - 1; i >= 0; --i)
                {
                    Renderer r = t.Explicit[i];
                    if (r == null || !r.transform.IsChildOf(ownTransform)) continue;
                    t.Explicit.RemoveAt(i);
                    t.ExplicitSub.RemoveAt(i);
                }
                t.Show.RemoveAll(r => r != null && r.transform.IsChildOf(ownTransform));
            }
            FindProxies(t);
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
        // An item's light never: some (Brenna's trophy) reuse the carried torch's flame, whose
        // 1.47 m near plane was made to clear the hand holding it and left a 2 m light nothing to shade.
        // A renderer only in a far LOD level of its LOD group (not in LOD 0). The camera
        // pass gets LOD 0 by the forced bias, but the explicit and two-sided passes draw
        // every renderer they're given, so these are left out there: the spice rack's
        // simplified model (flat cards) was being drawn over its detailed one.
        private static readonly List<LODGroup> s_lodGroups = new List<LODGroup>();
        private static bool FarLod(Renderer r)
        {
            r.GetComponentsInParent(true, s_lodGroups);
            if (s_lodGroups.Count == 0) return false;
            LOD[] lods = s_lodGroups[0].GetLODs();
            if (lods.Length < 2) return false;
            foreach (Renderer l in lods[0].renderers)
                if (l == r) return false;
            for (int i = 1; i < lods.Length; i++)
                foreach (Renderer l in lods[i].renderers)
                    if (l == r) return true;
            return false;
        }

        private static bool OwnShades(Tracked t) =>
            !t.Item && (t.OriginalShadowLod || t.OriginalShadows != LightShadows.None);

        // What a light doesn't shade itself with: its item (and the stand holding it), its piece,
        // or a loose item's whole object.
        private static Component OwnPart(Tracked t)
        {
            if (t.Tiny) return t.Light.transform.root;
            Piece p = t.Light.GetComponentInParent<Piece>();
            if (p != null) return p;
            if (t.Item) return t.Light.transform.root;
            // The same torches placed by the world, not built (the crypt's green ground torches,
            // CastleKit_*): no Piece, but their own saved object. Not a whole dungeon or location
            // the light happens to sit in.
            ZNetView nv = t.Light.GetComponentInParent<ZNetView>();
            if (nv != null && nv.GetComponent<DungeonGenerator>() == null && nv.GetComponent<Location>() == null)
                return nv;
            return null;
        }

        // Preserve vanilla's authored self-shadowing and near plane for any
        // light that already casts shadows in the unmodified game.
        // For lights WLO uniquely gives shadows to, exclude the light's own
        // piece by identity (FindCasters), NOT by removing all objects inside
        // an arbitrary near-plane radius. A tiny positive camera clip is
        // still required for depth projection; it is not a self-shadow margin.
        private static float NearPlane(Tracked t) => OwnShades(t)
            ? Mathf.Max(0.05f, t.OriginalShadowNearPlane) : 0.05f;

        // Inside the near plane, walls and floors block by their colliders:
        // plain boxes, no stones or ledges to cast shadows, and the wall a
        // sconce hangs on still stops its light reaching the room behind.
        // Not the light's own piece, one it sits inside (a hearth) or doors
        // (they swing; the moving maps draw them).
        private static Collider[] ProxyHits = new Collider[64];
        private static int CollectProxyHits(Vector3 center, float near)
        {
            int n;
            while (true)
            {
                n = Physics.OverlapSphereNonAlloc(center, near, ProxyHits,
                    StaticLayerMask, QueryTriggerInteraction.Ignore);
                if (n < ProxyHits.Length) return n;
                if (ProxyHits.Length >= 4096)
                {
                    if (OverflowReports++ < 8)
                        Plugin.Log.LogWarning("[near-proxies] collider limit 4096 reached.");
                    return n;
                }
                System.Array.Resize(ref ProxyHits, ProxyHits.Length * 2);
                if (OverflowReports++ < 8)
                    Plugin.Note("[near-proxies] expanded collider buffer to " + ProxyHits.Length);
            }
        }
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
            Component own = OwnPart(t);
            float near = NearPlane(t);
            if (_cube == null)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = g.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(g);
            }
            Transform ownT = own != null ? own.transform : t.Light.transform.root;
            int n = near <= 0.06f ? 0 : CollectProxyHits(t.Center, near);
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
            ShadowCache.WorldVersion++;
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
            if (g == null || !Plugin.GhostShadows)
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
            if (__state) { ShadowCache.WorldVersion++; ShadowCache.DoorMoved(__instance); }
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

