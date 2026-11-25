// Wire's Shadows: what every point light's lighting shares, for Unity's own per-light pass
// (WireToonDeferredShading) and the tiled one that lights many at once (WireTiledLights): the
// G-buffer, the cached and moving shadow maps, and Valheim's toon ramp.
#ifndef WIRE_LIGHT_SHARED
#define WIRE_LIGHT_SHARED

// Where the camera is, for the specular: Unity's own, unless the includer has its own (the tiled
// pass runs outside Unity's light pass and passes the camera itself).
#ifndef WIRE_EYE
#define WIRE_EYE _WorldSpaceCameraPos
#endif

sampler2D _CameraGBufferTexture0;
sampler2D _CameraGBufferTexture1;
sampler2D _CameraGBufferTexture2;

// Each slotted light: where its map was rendered from and how strong (w), where it is now.
float4 _WireShadowLights[96]; // xyz: where the map was rendered from, w: shadow strength
float4 _WireLightNow[96];     // where each slotted light is this frame
float _WireShadowCount;
float _WireNearCount;
float _WireDynRes;
// One comparison sampler for every depth map here (Unity's inline sampler: bilinear,
// clamped, depth compare): a pixel shader has 16 sampler slots and one per map went past them.
SamplerComparisonState sampler_linear_clamp_compare;
float _WireNativeInvRanges[96];
// Sized maps (the mod's Tiers): each light's cached map sits in one of four pools, by
// the size it needs on screen (_WireTierRes: full, half, quarter, eighth); slot ->
// pool and cube. Four comparison taps, one map texel apart.
float _WireMapTier[96];
float _WireMapCube[96];
float4 _WireTierRes;
TextureCubeArray<float> _WireTier0;
TextureCubeArray<float> _WireTier1;
TextureCubeArray<float> _WireTier2;
TextureCubeArray<float> _WireTier3;

float WireTierTap(int tier, float3 d, float cube, float compare)
{
    float4 c = float4(d, cube);
    if (tier == 0) return _WireTier0.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
    if (tier == 1) return _WireTier1.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
    if (tier == 2) return _WireTier2.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
    return _WireTier3.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
}

float WireTierStatic(float3 direction, float receiverDepthMeters, int slot)
{
    int tier = (int)_WireMapTier[slot];
    float cube = _WireMapCube[slot];
    float z = receiverDepthMeters / _WireTierRes[tier];
    float compare = saturate((receiverDepthMeters - 0.01) * _WireNativeInvRanges[slot]);
    #if defined(UNITY_REVERSED_Z)
        compare = 1.0 - compare;
    #endif
    return 0.25 * (
        WireTierTap(tier, direction + float3( z,  z,  z), cube, compare) +
        WireTierTap(tier, direction + float3(-z, -z,  z), cube, compare) +
        WireTierTap(tier, direction + float3(-z,  z, -z), cube, compare) +
        WireTierTap(tier, direction + float3( z, -z, -z), cube, compare));
}

// Moving casters: a depth map per slot (one of a pool, _WireDynPhysicalSlot), near or
// distant pool like the slot; _WireDynActive is 1 while it's in use.
float _WireDynActive[96];
// Which faces of each moving map hold anything (bits +X -X +Y -Y +Z -Z); the rest are empty.
float _WireDynFaceMask[96];

// The faces any shadow tap round direction v can land on: every tap is within e of v on
// each axis, and a point is on an axis's face only if that axis is the largest. So an axis
// counts if it can reach the largest within e, on the signs it can have within e.
uint WireFacesNear(float3 v, float e)
{
    float3 a = abs(v);
    float m = max(max(a.x, a.y), a.z) - e;
    uint bits = 0;
    if (a.x + e >= m) bits |= (v.x + e > 0 ? 1u : 0u) | (v.x - e < 0 ? 2u : 0u);
    if (a.y + e >= m) bits |= (v.y + e > 0 ? 4u : 0u) | (v.y - e < 0 ? 8u : 0u);
    if (a.z + e >= m) bits |= (v.z + e > 0 ? 16u : 0u) | (v.z - e < 0 ? 32u : 0u);
    return bits;
}
// Logical shadow-light slot -> physical moving cube index.
float _WireDynPhysicalSlot[96];
// Comparison-depth projection for only refreshed moving-caster
// faces. The cubemap's untouched faces remain cached indefinitely.
TextureCubeArray<float> _WireNativeMovingNearMaps;
TextureCubeArray<float> _WireNativeMovingFarMaps;
float _WireNativeMovingActive[96];
float _WireNativeMovingInvRanges[96];

float WireMovingNativeDepth(float3 direction, float dist, int slot, bool isNear, float bias)
{
    float compare = saturate((dist - bias) * _WireNativeMovingInvRanges[slot]);
    #if defined(UNITY_REVERSED_Z)
        compare = 1.0 - compare;
    #endif
    int cube = (int)_WireDynPhysicalSlot[slot];
    float z = dist / _WireDynRes; // one map pixel, as for the cached maps
    float3 d0 = direction + float3( z,  z,  z);
    float3 d1 = direction + float3(-z, -z,  z);
    float3 d2 = direction + float3(-z,  z, -z);
    float3 d3 = direction + float3( z, -z, -z);
    if (isNear)
        return 0.25 * (
            _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d0, cube), compare) +
            _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d1, cube), compare) +
            _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d2, cube), compare) +
            _WireNativeMovingNearMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d3, cube), compare));
    return 0.25 * (
        _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d0, cube), compare) +
        _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d1, cube), compare) +
        _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d2, cube), compare) +
        _WireNativeMovingFarMaps.SampleCmpLevelZero(sampler_linear_clamp_compare, float4(d3, cube), compare));
}


// Static and moving depth use the exact SAME origin as the last
// completed real-geometry shadow capture. No UV rotation,
// virtual lookup offset, or per-receiver displacement estimates.
// Static shadow geometry is captured from each firelight's
// resting origin. Flame intensity still flickers in vanilla, but
// flame position and shadows do not wobble. Dynamic casters
// retain their own physically meaningful animation.
float WireCachedShadow(int slot, float3 wpos, float3 normal, float3 lightPos)
{
    float3 center = _WireShadowLights[slot].xyz;
    bool isNear = slot < (int)_WireNearCount;
    float3 v = wpos - center;
    float r = length(v);
    int dyn = _WireDynActive[slot] > 0.5
        ? (int)_WireDynPhysicalSlot[slot] : -1;
    // Looked up one map texel out along the surface's normal: where one beam lies flush over
    // another, the other's surface sits a hair behind the first one's edge and got a sliver of its
    // shadow along it. A real blocker over the surface still covers the point moved out. Never less
    // than the 1 cm the depth test allows (WireTierStatic): right by a light a texel is a couple of
    // millimetres, less than snapped pieces stick out past each other.
    float staticLit = 1.0;
    if (_WireMapTier[slot] >= 0)
    {
        float3 vo = v + normal * max(2.0 * r / _WireTierRes[(int)_WireMapTier[slot]], 0.01);
        staticLit = WireTierStatic(vo, length(vo), slot);
    }

    // Fire maps always use their stable center. Actual moving
    // NON-fire sources (where supported) continue to use
    // their live position for dynamically rendered casters.
    float useLiveMovingOrigin = 1.0 - _WireLightNow[slot].w;
    float3 vd = wpos - lerp(center, lightPos, useLiveMovingOrigin);
    float rd = length(vd);
    const float compareBias = 0.01;
    float dynamicLit = 1.0;
    // Only where something moving is in the map: an empty face reads as lit anyway.
    // The taps reach one map texel each way (2 rd / res covers both filters).
    if (dyn >= 0 && _WireNativeMovingActive[slot] > 0.5 && (WireFacesNear(vd, 2.0 * rd / _WireDynRes) & (uint)_WireDynFaceMask[slot]) != 0)
        dynamicLit = WireMovingNativeDepth(vd, rd, slot, isNear, compareBias);
    return lerp(1, staticLit * dynamicLit, _WireShadowLights[slot].w);
}

#if defined(WIRE_SMOOTH_ALL) || (defined(WIRE_SMOOTH_POINT) && !defined(DIRECTIONAL) && !defined(DIRECTIONAL_COOKIE))
    #define WIRE_SMOOTH 1
#endif

// 1: Burley's diffuse instead of Lambert (the mod's "Diffuse lighting" setting, set once when it changes).
float _WireBurley;

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

// One point light on one pixel: Valheim's specular and toon ramp, with the cached shadow where the
// light faces the surface. g0, g1 and the normal from the G-buffer (read once by the caller); atten
// is the light's falloff here, lightColor what Unity calls _LightColor.
float3 WireShade(half4 g0, half4 g1, float3 normal, float3 wpos, float3 lightDir, float atten, float3 lightColor, int slot, float3 lightPos)
{
    float3 eyeVec = normalize(wpos - WIRE_EYE);
    float3 h = -eyeVec + lightDir;
    h = h * rsqrt(max(0.001, dot(h, h)));

    float lh = saturate(dot(lightDir, h));
    float nh = saturate(dot(normal, h));
    float nl = saturate(dot(normal, lightDir));

    // Shadow only where the light actually reaches: in range, facing it.
    if (slot >= 0 && atten > 0.0001 && nl > 0)
        atten *= WireCachedShadow(slot, wpos, normal, lightPos);

    float perceptualRoughness = 1 - g1.a;
    float roughness = perceptualRoughness * perceptualRoughness;
    float a2 = roughness * roughness;
    float d = nh * nh * (a2 - 1) + 1.00001;
    float spec = a2 / (4 * d * d * max(0.1, lh * lh) * (roughness + 0.5));

    // Burley's diffuse (Disney 2012, as Unity's own Standard shader has it: DisneyDiffuse in
    // UnityStandardBRDF.cginc). Rough surfaces light up a little when lit from behind you
    // (retro-reflection), smooth ones darken a little at the edges (diffuse Fresnel). Lit straight
    // on it's 1, the same as Lambert. Off (_WireBurley 0): the game's Lambert.
    float diffuse = 1;
    if (_WireBurley > 0.5)
    {
        float nv = saturate(dot(normal, -eyeVec));
        float fd90 = 0.5 + 2 * lh * lh * perceptualRoughness;
        float l5 = 1 - nl, v5 = 1 - nv;
        l5 = l5 * l5 * l5 * l5 * l5;
        v5 = v5 * v5 * v5 * v5 * v5;
        diffuse = (1 + (fd90 - 1) * l5) * (1 + (fd90 - 1) * v5);
    }

    float3 color = (spec * g1.rgb + g0.rgb * diffuse) * (lightColor * atten);
    return color * ToonRamp(nl);
}

// The lights the tiled pass draws (WireTiledLights): where each one is (xyz) and 1 / range² (w).
// Unity still draws its own pass for each of them, collapsed to nothing in the vertex shader.
StructuredBuffer<float4> _WireTiledPos;
float _WireTiledCount;

bool WireIsTiled(float3 lightPos)
{
    int n = (int)_WireTiledCount;
    [loop]
    for (int i = 0; i < n; i++)
    {
        float3 o = _WireTiledPos[i].xyz - lightPos;
        if (dot(o, o) < 1e-5) return true;
    }
    return false;
}

#endif
