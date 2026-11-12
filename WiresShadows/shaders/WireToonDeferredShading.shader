// Valheim's deferred lighting shader (ToonDeferredShading2017), rebuilt from
// its compiled code: Unity's standard deferred light pass with a minimal
// GGX-style specular and a stepped toon ramp on N.L. Point lights
// whose shadows this mod handles read a cached shadow map instead of Unity's
// per-frame one (see WireShadowDistance and the mod's ShadowCache).
Shader "Hidden/WireToonDeferredShading"
{
    Properties
    {
        _LightTexture0 ("", any) = "" {}
        _LightTextureB0 ("", 2D) = "" {}
        _ShadowMapTexture ("", any) = "" {}
        _SrcBlend ("", Float) = 1
        _DstBlend ("", Float) = 1
    }
    SubShader
    {
        // Pass 1: lighting pass
        Pass
        {
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_lightpass
            #pragma multi_compile ___ UNITY_HDR_ON
            #pragma multi_compile _ WIRE_SMOOTH_POINT WIRE_SMOOTH_ALL
            #pragma exclude_renderers nomrt

            #include "UnityCG.cginc"
            #include "UnityDeferredLibrary.cginc"

            sampler2D _CameraGBufferTexture0;
            sampler2D _CameraGBufferTexture1;
            sampler2D _CameraGBufferTexture2;

            // Cached shadow maps (one cube per slot) for point lights whose
            // Unity shadows this mod has taken over: distance from the light
            // to the nearest caster, per direction.
            // Slots below _WireNearCount use the full-size maps, the rest the
            // low-res maps for distant lights.
            UNITY_DECLARE_TEXCUBEARRAY(_WireShadowMaps);
            UNITY_DECLARE_TEXCUBEARRAY(_WireFarMaps);
            float4 _WireShadowLights[96]; // xyz: where the map was rendered from, w: shadow strength
            float4 _WireLightNow[96];     // where each slotted light is this frame
            float _WireShadowCount;
            float _WireNearCount;
            float _WireNearRes, _WireFarRes, _WireDynRes;
            float _WireFollowFlicker;
            float _WireDiagMode; // zero in normal operation; runtime shadow audit
            float _WireNativeMovingPCF; // native-style four hardware-PCF footprints
            float _WireStaticBilinearPCF; // proper cubemap-face texel grid A/B
            // Native hardware-comparison cube ARRAYS: every WLO cached light,
            // near and distant. No single-slot restriction.
            TextureCubeArray<float> _WireNativeNearMaps;
            TextureCubeArray<float> _WireNativeFarMaps;
            SamplerComparisonState sampler_WireNativeNearMaps;
            SamplerComparisonState sampler_WireNativeFarMaps;
            float _WireNativeActive[96];
            float _WireNativeInvRanges[96];
            // Pointlight Rework: the comparison cubes hold Unity's own point-shadow
            // depth (each face's 90 degree projection, the light's near plane), so a
            // receiver is compared the way Unity does: by its distance along the
            // face's main axis, turned into that projection's depth.
            float _WireNativeNears[96];
            float _WireVanillaDepth;
            float WireVanillaCompare(float3 direction, float radial, float n, float invFar)
            {
                float f = 1.0 / max(invFar, 1e-6);
                float3 a = abs(direction);
                float axis = max(a.x, max(a.y, a.z));
                float za = clamp(axis * radial / max(length(direction), 1e-6), n, f);
                #if defined(UNITY_REVERSED_Z)
                    return n * (f - za) / (za * (f - n));
                #else
                    return f * (za - n) / (za * (f - n));
                #endif
            }

            // Stored depth back to distance along this direction (flame-follow search, debug).
            float WireVanillaDistance(float raw, float3 direction, float n, float invFar)
            {
                float f = 1.0 / max(invFar, 1e-6);
                #if defined(UNITY_REVERSED_Z)
                    float za = n * f / max(n + raw * (f - n), 1e-6);
                #else
                    float za = n * f / max(f - raw * (f - n), 1e-6);
                #endif
                float3 a = abs(direction);
                return za * length(direction) / max(max(a.x, max(a.y, a.z)), 1e-6);
            }

            float WireNativeStatic(float3 direction, float receiverDepthMeters, int slot, bool isNear)
            {
                float compare;
                if (_WireVanillaDepth > 0.5)
                    compare = WireVanillaCompare(direction, receiverDepthMeters - 0.01, _WireNativeNears[slot], _WireNativeInvRanges[slot]);
                else
                {
                    compare = saturate((receiverDepthMeters - 0.01) * _WireNativeInvRanges[slot]);
                    #if defined(UNITY_REVERSED_Z)
                        compare = 1.0 - compare;
                    #endif
                }
                int cube = isNear ? slot : slot - (int)_WireNearCount;
                const float z = 1.0 / 128.0;
                float3 d0 = direction + float3( z,  z,  z);
                float3 d1 = direction + float3(-z, -z,  z);
                float3 d2 = direction + float3(-z,  z, -z);
                float3 d3 = direction + float3( z, -z, -z);
                if (isNear)
                    return 0.25 * (
                        _WireNativeNearMaps.SampleCmpLevelZero(sampler_WireNativeNearMaps, float4(d0, cube), compare) +
                        _WireNativeNearMaps.SampleCmpLevelZero(sampler_WireNativeNearMaps, float4(d1, cube), compare) +
                        _WireNativeNearMaps.SampleCmpLevelZero(sampler_WireNativeNearMaps, float4(d2, cube), compare) +
                        _WireNativeNearMaps.SampleCmpLevelZero(sampler_WireNativeNearMaps, float4(d3, cube), compare));
                return 0.25 * (
                    _WireNativeFarMaps.SampleCmpLevelZero(sampler_WireNativeFarMaps, float4(d0, cube), compare) +
                    _WireNativeFarMaps.SampleCmpLevelZero(sampler_WireNativeFarMaps, float4(d1, cube), compare) +
                    _WireNativeFarMaps.SampleCmpLevelZero(sampler_WireNativeFarMaps, float4(d2, cube), compare) +
                    _WireNativeFarMaps.SampleCmpLevelZero(sampler_WireNativeFarMaps, float4(d3, cube), compare));
            }
            float _WireDebug;
            float _WireDebugSlot;
            static float3 _wireDebugColor;
            // Moving casters: a map per slot, full-size or distant like the
            // cached one; _WireDynActive is 1 while it's in use.
            UNITY_DECLARE_TEXCUBEARRAY(_WireDynMaps);
            UNITY_DECLARE_TEXCUBEARRAY(_WireFarDynMaps);
            float _WireDynActive[96];
            // Logical shadow-light slot -> physical moving cube index.
            float _WireDynPhysicalSlot[96];
            // Comparison-depth projection for only refreshed moving-caster
            // faces. The cubemap's untouched faces remain cached indefinitely.
            TextureCubeArray<float> _WireNativeMovingNearMaps;
            TextureCubeArray<float> _WireNativeMovingFarMaps;
            SamplerComparisonState sampler_WireNativeMovingNearMaps;
            SamplerComparisonState sampler_WireNativeMovingFarMaps;
            float _WireNativeMovingActive[96];
            float _WireNativeMovingInvRanges[96];

            float WireMovingNativeDepth(float3 direction, float dist, int slot, bool isNear, float bias)
            {
                float compare;
                if (_WireVanillaDepth > 0.5)
                    compare = WireVanillaCompare(direction, dist - bias, _WireNativeNears[slot], _WireNativeMovingInvRanges[slot]);
                else
                {
                    compare = saturate((dist - bias) * _WireNativeMovingInvRanges[slot]);
                    #if defined(UNITY_REVERSED_Z)
                        compare = 1.0 - compare;
                    #endif
                }
                int cube = (int)_WireDynPhysicalSlot[slot];
                const float z = 1.0 / 128.0;
                float3 d0 = direction + float3( z,  z,  z);
                float3 d1 = direction + float3(-z, -z,  z);
                float3 d2 = direction + float3(-z,  z, -z);
                float3 d3 = direction + float3( z, -z, -z);
                if (isNear)
                    return 0.25 * (
                        _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_WireNativeMovingNearMaps, float4(d0, cube), compare) +
                        _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_WireNativeMovingNearMaps, float4(d1, cube), compare) +
                        _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_WireNativeMovingNearMaps, float4(d2, cube), compare) +
                        _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_WireNativeMovingNearMaps, float4(d3, cube), compare));
                return 0.25 * (
                    _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_WireNativeMovingFarMaps, float4(d0, cube), compare) +
                    _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_WireNativeMovingFarMaps, float4(d1, cube), compare) +
                    _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_WireNativeMovingFarMaps, float4(d2, cube), compare) +
                    _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_WireNativeMovingFarMaps, float4(d3, cube), compare));
            }

            // One tap: lit unless the cached map or (when active) the moving
            // map has something closer. Each has its own bias: the moving map
            // only holds characters, never the floor they stand on, so it
            // needs no slope bias (that would lift their shadows off them).
            float WireStatic(float3 v, int slot, bool isNear)
            {
                return isNear ? UNITY_SAMPLE_TEXCUBEARRAY_LOD(_WireShadowMaps, float4(v, slot), 0).r
                              : UNITY_SAMPLE_TEXCUBEARRAY_LOD(_WireFarMaps, float4(v, slot - (int)_WireNearCount), 0).r;
            }

            SamplerState wire_point_clamp_sampler;
            float WireStaticDist(float3 v, int slot, bool isNear)
            {
                if (_WireVanillaDepth < 0.5)
                    return WireStatic(v, slot, isNear);
                if (_WireNativeActive[slot] < 0.5)
                    return 60000.0;
                int cube = isNear ? slot : slot - (int)_WireNearCount;
                float raw = isNear
                    ? _WireNativeNearMaps.SampleLevel(wire_point_clamp_sampler, float4(v, cube), 0)
                    : _WireNativeFarMaps.SampleLevel(wire_point_clamp_sampler, float4(v, cube), 0);
                #if defined(UNITY_REVERSED_Z)
                    if (raw <= 0.000001) return 60000.0;
                #else
                    if (raw >= 0.999999) return 60000.0;
                #endif
                return WireVanillaDistance(raw, v, _WireNativeNears[slot], _WireNativeInvRanges[slot]);
            }

            float WireMoving(float3 v, int cube, bool isNear)
            {
                // The single moving-caster pass writes front and back depth
                // in R/G. Match the STATIC map's midpoint rule when reading
                // them, avoiding a second render/combination pass.
                float2 fb = isNear
                    ? UNITY_SAMPLE_TEXCUBEARRAY_LOD(_WireDynMaps, float4(v, cube), 0).rg
                    : UNITY_SAMPLE_TEXCUBEARRAY_LOD(_WireFarDynMaps, float4(v, cube), 0).rg;
                // Diagnostic: show isolated front/back depth without changing
                // rasterization, shadow resolution, or caster collection.
                if (_WireDiagMode > 3.5 && _WireDiagMode < 4.5) return fb.x;
                if (_WireDiagMode > 4.5 && _WireDiagMode < 5.5) return fb.y;
                return min(fb.x, fb.y);
            }

            // Receiver-side bias, like Unity's normal + depth bias: look the
            // shadow up from a point pushed off the surface along its geometric
            // normal, by about a map texel at that distance (more at grazing
            // angles), then allow half a texel of depth. Replaces the old
            // midpoint/35 cm cap, which let light through just behind blockers.
            float3 WireBiasPos(float3 wpos, float3 n, float3 lightPos, float res, out float depthBias)
            {
                float3 toLight = lightPos - wpos;
                float dist = length(toLight);
                float3 l = toLight / max(dist, 1e-4);
                if (dot(n, l) < 0) n = -n;
                float texelSize = dist * 2.0 / max(res, 1.0);
                float nl = saturate(dot(n, l));
                // Slope term: the 4 comparison taps sit ~1.35 cm to the side
                // (1/128 per axis); on a surface lit at a shallow angle that
                // sideways spread is a depth difference of spread * tan(angle).
                float tanAngle = sqrt(1.0 - nl * nl) / max(nl, 0.05);
                depthBias = min(0.005 + 0.5 * texelSize + 0.0135 * tanAngle, 0.3);
                return wpos + n * texelSize * (0.6 + 1.4 * (1.0 - nl));
            }

            // Use the actual four neighbouring CUBEMAP texels for the moving
            // shadow comparison. Previous world-space diagonal offsets skipped
            // texels and sometimes hit the same texel several times, producing
            // coarse 0/25/50/75/100% stair-stepped silhouettes. This is a
            // 2x2 hardware-style comparison filter, not a wider blur kernel:
            // four map samples in total, the same as the previous moving path.
            float WireMovingPCF(float3 dir, int cube, bool isNear, float distD, float compareBias)
            {
                float3 a = abs(dir);
                float3 face, uAxis, vAxis;
                if (a.x >= a.y && a.x >= a.z)
                {
                    face = float3(dir.x >= 0 ? 1 : -1, 0, 0);
                    uAxis = float3(0, 0, dir.x >= 0 ? -1 : 1);
                    vAxis = float3(0, -1, 0);
                }
                else if (a.y >= a.z)
                {
                    face = float3(0, dir.y >= 0 ? 1 : -1, 0);
                    uAxis = float3(1, 0, 0);
                    vAxis = float3(0, 0, dir.y >= 0 ? 1 : -1);
                }
                else
                {
                    face = float3(0, 0, dir.z >= 0 ? 1 : -1);
                    uAxis = float3(dir.z >= 0 ? 1 : -1, 0, 0);
                    vAxis = float3(0, -1, 0);
                }

                float dominant = max(max(a.x, a.y), a.z);
                float2 uv = float2(dot(dir, uAxis), dot(dir, vAxis)) / max(0.0001, dominant);
                // Convert [-1,1] cube-face coordinates into texel-centre
                // coordinates; determine neighbours and continuous weights.
                float res = _WireDynRes; // actual moving-tier resolution, both tiers equal
                float2 pixel = (uv * 0.5 + 0.5) * res - 0.5;
                float2 cell = floor(pixel);
                float2 w = saturate(pixel - cell);
                float2 uv00 = (cell + 0.5) * (2.0 / res) - 1.0;
                float2 uv11 = uv00 + (2.0 / res);

                float d00 = WireMoving(face + uAxis * uv00.x + vAxis * uv00.y, cube, isNear);
                float d10 = WireMoving(face + uAxis * uv11.x + vAxis * uv00.y, cube, isNear);
                float d01 = WireMoving(face + uAxis * uv00.x + vAxis * uv11.y, cube, isNear);
                float d11 = WireMoving(face + uAxis * uv11.x + vAxis * uv11.y, cube, isNear);
                float4 lit = step(distD - compareBias, float4(d00, d10, d01, d11));
                return lerp(lerp(lit.x, lit.y, w.x),
                            lerp(lit.z, lit.w, w.x), w.y);
            }

            // Unity's native SOFT POINT shadow path (UnityShadowLibrary.cginc)
            // samples four cube directions offset by +-1/128 in world-space
            // light vectors. On D3D11 each SampleCmpLevelZero performs
            // bilinear hardware PCF. Our RGHalf front/back representation
            // cannot use hardware comparison: emulate its four bilinear
            // footprints explicitly. 4 x (2 x 2) = 16 comparisons.
            // This is the native 4-footprint pattern, not a made-up blur.
            float WireMovingNativePCF(float3 dir, int cube, bool isNear, float distD, float compareBias)
            {
                const float z = 1.0 / 128.0;
                float4 a = float4(
                    WireMovingPCF(dir + float3( z,  z,  z), cube, isNear, distD, compareBias),
                    WireMovingPCF(dir + float3(-z, -z,  z), cube, isNear, distD, compareBias),
                    WireMovingPCF(dir + float3(-z,  z, -z), cube, isNear, distD, compareBias),
                    WireMovingPCF(dir + float3( z, -z, -z), cube, isNear, distD, compareBias));
                return dot(a, float4(0.25, 0.25, 0.25, 0.25));
            }

            // Static maps used four displaced point samples separated by
            // several texels. That makes their edges jump in 25% brightness
            // steps even where the cubemap contains finer information.
            // Sample the EXACT four neighbouring cube texels instead, and
            // interpolate the binary comparisons (bilinear PCF).
            // Four shadow fetches, same as the previous static filter.
            float WireStaticBilinear(float3 dir, int slot, bool isNear, float distS)
            {
                float3 a = abs(dir);
                float3 face, uAxis, vAxis;
                if (a.x >= a.y && a.x >= a.z)
                {
                    face = float3(dir.x >= 0 ? 1 : -1, 0, 0);
                    uAxis = float3(0, 0, dir.x >= 0 ? -1 : 1);
                    vAxis = float3(0, -1, 0);
                }
                else if (a.y >= a.z)
                {
                    face = float3(0, dir.y >= 0 ? 1 : -1, 0);
                    uAxis = float3(1, 0, 0);
                    vAxis = float3(0, 0, dir.y >= 0 ? 1 : -1);
                }
                else
                {
                    face = float3(0, 0, dir.z >= 0 ? 1 : -1);
                    uAxis = float3(dir.z >= 0 ? 1 : -1, 0, 0);
                    vAxis = float3(0, -1, 0);
                }
                float dominant = max(max(a.x, a.y), a.z);
                float2 uv = float2(dot(dir, uAxis), dot(dir, vAxis)) / max(dominant, 0.0001);
                float res = isNear ? _WireNearRes : _WireFarRes;
                float2 pixel = (uv * 0.5 + 0.5) * res - 0.5;
                float2 cell = floor(pixel);
                float2 w = saturate(pixel - cell);
                float2 uv00 = (cell + 0.5) * (2.0 / res) - 1.0;
                float2 uv11 = uv00 + 2.0 / res;

                float d00 = WireStatic(face + uAxis * uv00.x + vAxis * uv00.y, slot, isNear);
                float d10 = WireStatic(face + uAxis * uv11.x + vAxis * uv00.y, slot, isNear);
                float d01 = WireStatic(face + uAxis * uv00.x + vAxis * uv11.y, slot, isNear);
                float d11 = WireStatic(face + uAxis * uv11.x + vAxis * uv11.y, slot, isNear);
                float4 lit = step(distS - 0.01, float4(d00, d10, d01, d11));
                return lerp(lerp(lit.x, lit.y, w.x),
                            lerp(lit.z, lit.w, w.x), w.y);
            }

            float WireStaticTap(float3 vs, int slot, bool isNear, float distS)
            {
                if (WireStatic(vs, slot, isNear) + 0.01 < distS)
                {
                    _wireDebugColor.r += 0.25;
                    return 0;
                }
                return 1;
            }

            // Which slot this light has: found once per vertex of the light's
            // volume (the same for all of them), not once per pixel. By where
            // each slotted light is this frame, exactly: a light without a
            // slot next to one with (a hearth's small light) mustn't borrow
            // its map.
            float WireFindSlot()
            {
                int count = (int)_WireShadowCount;
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float3 offset = _LightPos.xyz - _WireLightNow[i].xyz;
                    if (dot(offset, offset) < 1e-5)
                        return i;
                }
                return -1;
            }

            struct wire_v2f
            {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 ray : TEXCOORD1;
                nointerpolation float slot : TEXCOORD2;
            };

            wire_v2f vert(float4 vertex : POSITION, float3 normal : NORMAL)
            {
                unity_v2f_deferred d = vert_deferred(vertex, normal);
                wire_v2f o;
                o.pos = d.pos;
                o.uv = d.uv;
                o.ray = d.ray;
                #if (defined(POINT) || defined(POINT_COOKIE)) && !defined(SHADOWS_CUBE)
                o.slot = WireFindSlot();
                #else
                o.slot = -1;
                #endif
                return o;
            }

            float WireCachedShadow(int slot, float3 wpos, float3 normal)
            {
                float3 center = _WireShadowLights[slot].xyz;
                bool isNear = slot < (int)_WireNearCount;
                float texel = 2.0 / (isNear ? _WireNearRes : _WireFarRes); // one texel, in radians
                // The cached map was drawn from the light's resting spot.
                float staticDepthBias;
                float3 wposS = WireBiasPos(wpos, normal, center, isNear ? _WireNearRes : _WireFarRes, staticDepthBias);
                float3 v = wposS - center;
                float r = length(v);
                float3 vs = v;
                // Following the flame: when the light moves by delta, a shadow
                // edge on a surface r away, cast by a blocker s away, moves by
                // delta * (r - s) / s. Find the blocker (here, or where the
                // light's move would bring one in), then look up where the
                // edge has moved to. At contact r = s: nothing moves, no gaps.
                float3 delta = (_LightPos.xyz - center) * _WireFollowFlicker;
                // Soft lookup spread: about 1.5 texels of the map at this distance.
                float o = r * texel * 0.87;
                // Moving maps get a separate face-aligned 2x2 texel lookup
                // below. The static four-tap sampling is unchanged.
                if (dot(delta, delta) > 1e-8)
                {
                    // Search the footprint the active shadow comparison uses:
                    // native PCF samples the four fixed +/-1/128 directions,
                    // whereas the nonnative cached filter spreads by a
                    // resolution-scaled texel size. The old search could
                    // miss an edge visible to the final soft comparison,
                    // leaving a stationary one-pixel fringe on flame corners.
                    float filterOffset = _WireNativeActive[slot] > 0.5
                        ? (1.0 / 128.0) : o;
                    float sbCentre = 60000.0;
                    float sbEdge = 60000.0;
                    [unroll]
                    for (int q = 0; q < 5; q++)
                    {
                        float3 off = q == 0 ? float3(0, 0, 0) :
                                     q == 1 ? float3( filterOffset,  filterOffset,  filterOffset) :
                                     q == 2 ? float3(-filterOffset, -filterOffset,  filterOffset) :
                                     q == 3 ? float3(-filterOffset,  filterOffset, -filterOffset) :
                                              float3( filterOffset, -filterOffset, -filterOffset);
                        float a0 = WireStaticDist(v + off, slot, isNear);
                        float a1 = WireStaticDist(v + off + delta, slot, isNear);
                        float a2 = WireStaticDist(v + off + delta * 3, slot, isNear);
                        // Prefer the actual receiver ray. At a geometry
                        // corner, an adjacent filter tap can see a different
                        // (closer) blocker: using that minimum everywhere
                        // stretches the reprojection and sharpens edges at
                        // large flame offsets. Neighbour taps are fallback
                        // only for the soft-PCF silhouette fringe.
                        float candidate = 60000.0;
                        if (a0 + 0.01 < r) candidate = min(candidate, a0);
                        if (a1 + 0.01 < r) candidate = min(candidate, a1);
                        if (a2 + 0.01 < r) candidate = min(candidate, a2);
                        if (q == 0) sbCentre = candidate;
                        else sbEdge = min(sbEdge, candidate);
                    }
                    float sb = sbCentre < 60000.0 ? sbCentre : sbEdge;
                    if (sb < 60000.0)
                        vs = v + delta * (r / max(sb, 0.05) - 1);
                }
                // The moving map is drawn from where the flame is this frame.
                float movingDepthBias;
                float3 dynCenter = lerp(center, _LightPos.xyz, _WireFollowFlicker);
                float3 vd = WireBiasPos(wpos, normal, dynCenter, _WireDynRes, movingDepthBias) - dynCenter;
                float rd = length(vd);
                // The light's moving map, if in use: cube index in its tier.
                int dyn = _WireDynActive[slot] > 0.5 ? (int)_WireDynPhysicalSlot[slot] : -1;
                _wireDebugColor = 0;
                // Do not compute the legacy cached filter when native static
                // comparison is active: it was overwritten by WireNativeStatic
                // later in this shader. The result is pixel-for-pixel identical
                // with fewer texture fetches for every WLO point light.
                float staticLit = 1.0;
                if (_WireNativeActive[slot] <= 0.5)
                {
                    if (_WireStaticBilinearPCF > 0.5)
                        staticLit = WireStaticBilinear(vs, slot, isNear, r);
                    else
                    {
                        staticLit = 0;
                        staticLit += WireStaticTap(vs + float3( o,  o,  o), slot, isNear, r);
                        staticLit += WireStaticTap(vs + float3(-o, -o,  o), slot, isNear, r);
                        staticLit += WireStaticTap(vs + float3(-o,  o, -o), slot, isNear, r);
                        staticLit += WireStaticTap(vs + float3( o, -o, -o), slot, isNear, r);
                        staticLit *= 0.25;
                    }
                }
                // An independent exact two-by-two comparison footprint on
                // the moving shadow cube. Only one map pixel in each axis
                // contributes, so distant silhouettes do not jump in 4x
                // artificially sparse, view-independent world diagonal steps.
                int diag = (int)(_WireDiagMode + 0.5);
                float compareBias = diag == 6 ? 0.0 : diag == 7 ? 0.05 : movingDepthBias;
                float dynamicLit = 1.0;
                if (dyn >= 0 && diag != 1)
                {
                    if (diag == 3)
                    {
                        // The raw point sample reveals whether blockiness
                        // already exists BEFORE the four-sample comparison.
                        dynamicLit = WireMoving(vd, dyn, isNear) + 0.01 >= rd ? 1.0 : 0.0;
                    }
                    else if (diag == 8)
                    {
                        // The pre-fix diagonal offsets, an independent control.
                        float od = rd * (2.0 / _WireDynRes) * 0.87;
                        float4 raw = float4(
                            WireMoving(vd + float3( od,  od,  od), dyn, isNear),
                            WireMoving(vd + float3(-od, -od,  od), dyn, isNear),
                            WireMoving(vd + float3(-od,  od, -od), dyn, isNear),
                            WireMoving(vd + float3( od, -od, -od), dyn, isNear));
                        dynamicLit = dot(step(rd - 0.01, raw), float4(0.25, 0.25, 0.25, 0.25));
                    }
                    else if (_WireNativeMovingActive[slot] > 0.5)
                        dynamicLit = WireMovingNativeDepth(vd, rd, slot, isNear, compareBias);
                    else if (_WireNativeMovingPCF > 0.5)
                        dynamicLit = WireMovingNativePCF(vd, dyn, isNear, rd, compareBias);
                    else
                        dynamicLit = WireMovingPCF(vd, dyn, isNear, rd, compareBias);
                }
                if (_WireNativeActive[slot] > 0.5)
                    staticLit = WireNativeStatic(vs, r - staticDepthBias + 0.01, slot, isNear);
                else if (_WireVanillaDepth > 0.5)
                    staticLit = 1.0; // the old distance maps aren't drawn in this mode
                if (diag == 2) staticLit = 1.0; // isolate moving shadows
                float lit = staticLit * dynamicLit;
                if (_WireDebug > 0.5)
                {
                    // Measured, centre tap only: red = how far in front of this
                    // surface the cached map has something (full red at 10 cm
                    // or more), blue = the moving map does, green = lit.
                    float s = WireStaticDist(vs, slot, isNear);
                    float sd = dyn >= 0 ? WireMoving(vd, dyn, isNear) : 60000;
                    _wireDebugColor = float3(saturate((r - s) * 10), lit * 0.6, saturate((rd - sd) * 10));
                }
                return lerp(1, lit, _WireShadowLights[slot].w);
            }

            #if defined(WIRE_SMOOTH_ALL) || (defined(WIRE_SMOOTH_POINT) && !defined(DIRECTIONAL) && !defined(DIRECTIONAL_COOKIE))
                #define WIRE_SMOOTH 1
            #endif

            // The stepped ramp: bands at N.L = 0, .2, .4, .6, .8, .9.
            // Smooth: a straight line through each band's middle, so the
            // average brightness stays the same.
            float ToonRamp(float nl)
            {
            #ifdef WIRE_SMOOTH
                return 1.1 * (saturate(nl) + max(0, nl - 0.8));
            #else
                float v = lerp(0.1 * smoothstep(0, 1, min(1, 20 * nl)), 0.3, smoothstep(0, 1, saturate(20 * (nl - 0.2))));
                v = lerp(v, 0.5, smoothstep(0, 1, saturate(20 * (nl - 0.4))));
                v = lerp(v, 0.7, smoothstep(0, 1, saturate(20 * (nl - 0.6))));
                v = lerp(v, 0.9, smoothstep(0, 1, saturate(20 * (nl - 0.8))));
                v = lerp(v, 1.1, smoothstep(0, 1, saturate(20 * (nl - 0.9))));
                return 1.1 * v;
            #endif
            }

            half4 CalculateLight(unity_v2f_deferred i, int slot)
            {
                float3 wpos;
                float2 uv;
                float atten, fadeDist;
                half3 lightDir;
                UnityDeferredCalculateLightParams(i, wpos, uv, lightDir, atten, fadeDist);

                half4 gbuffer0 = tex2D(_CameraGBufferTexture0, uv);
                half4 gbuffer1 = tex2D(_CameraGBufferTexture1, uv);
                half4 gbuffer2 = tex2D(_CameraGBufferTexture2, uv);
                float3 normal = normalize(gbuffer2.rgb * 2 - 1);
                // Geometric normal from the reconstructed position (no normal-map
                // detail, so stone relief textures don't jitter the bias). Falls
                // back to the G-buffer normal across depth edges.
                float3 geo = cross(ddy(wpos), ddx(wpos));
                float geoLen = length(geo);
                geo = geoLen > 1e-8 ? geo / geoLen : normal;
                if (dot(geo, normal) < 0) geo = -geo;
                float3 biasNormal = dot(geo, normal) > 0.3 ? geo : normal;


                float3 eyeVec = normalize(wpos - _WorldSpaceCameraPos);
                float3 h = -eyeVec + lightDir;
                h = h * rsqrt(max(0.001, dot(h, h)));

                float lh = saturate(dot(lightDir, h));
                float nh = saturate(dot(normal, h));
                float nl = saturate(dot(normal, lightDir));

                // Shadow only where the light actually reaches: in range, facing it.
                // Debug view: only the chosen light, everything else black.
                if (_WireDebug > 0.5 && slot != (int)_WireDebugSlot)
                    return 0;
                if (slot >= 0 && atten > 0.0001 && nl > 0)
                {
                    float reach = atten;
                    atten *= WireCachedShadow(slot, wpos, biasNormal);
                    // Debug view: red = shaded by the cached map, blue = by the
                    // moving map, faint tint = which cube side.
                    if (_WireDebug > 0.5)
                        return half4(_wireDebugColor * saturate(reach * 4 + 0.2), 1);
                }

                float perceptualRoughness = 1 - gbuffer1.a;
                float roughness = perceptualRoughness * perceptualRoughness;
                float a2 = roughness * roughness;
                float d = nh * nh * (a2 - 1) + 1.00001;
                float spec = a2 / (4 * d * d * max(0.1, lh * lh) * (roughness + 0.5));

                float3 color = (spec * gbuffer1.rgb + gbuffer0.rgb) * (_LightColor.rgb * atten);
                return half4(color * ToonRamp(nl), 1);
            }

            #ifdef UNITY_HDR_ON
            half4
            #else
            fixed4
            #endif
            frag(wire_v2f w) : SV_Target
            {
                unity_v2f_deferred i;
                i.pos = w.pos;
                i.uv = w.uv;
                i.ray = w.ray;
                half4 c = CalculateLight(i, (int)w.slot);
                #ifdef UNITY_HDR_ON
                return c;
                #else
                return exp2(-c);
                #endif
            }
            ENDCG
        }

        // Pass 2: final decode pass (LDR), as in Unity's deferred shading
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            Stencil
            {
                ref [_StencilNonBackground]
                readmask [_StencilNonBackground]
                compback equal
                compfront equal
            }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers nomrt

            #include "UnityCG.cginc"

            sampler2D _LightBuffer;
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION, float2 texcoord : TEXCOORD0)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(vertex);
                o.texcoord = texcoord.xy;
                #ifdef UNITY_SINGLE_PASS_STEREO
                o.texcoord = TransformStereoScreenSpaceTex(o.texcoord, 1.0f);
                #endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return -log2(tex2D(_LightBuffer, i.texcoord));
            }
            ENDCG
        }
    }
    Fallback Off
}
