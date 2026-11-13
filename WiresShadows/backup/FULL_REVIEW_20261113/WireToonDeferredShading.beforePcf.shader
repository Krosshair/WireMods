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
            // Native hardware-comparison cube ARRAYS: every WLO cached light,
            // near and distant. No single-slot restriction.
            TextureCubeArray<float> _WireNativeNearMaps;
            TextureCubeArray<float> _WireNativeFarMaps;
            SamplerComparisonState sampler_WireNativeNearMaps;
            SamplerComparisonState sampler_WireNativeFarMaps;
            float _WireNativeActive[96];
            float _WireNativeInvRanges[96];
            float WireNativeStatic(float3 direction, float receiverDepthMeters, int slot, bool isNear)
            {
                float compare = saturate((receiverDepthMeters - 0.01) * _WireNativeInvRanges[slot]);
                #if defined(UNITY_REVERSED_Z)
                    compare = 1.0 - compare;
                #endif
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
                float compare = saturate((dist - bias) * _WireNativeMovingInvRanges[slot]);
                #if defined(UNITY_REVERSED_Z)
                    compare = 1.0 - compare;
                #endif
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

            float WireMoving(float3 v, int cube, bool isNear)
            {
                // The single moving-caster pass writes front and back depth
                // in R/G. Match the STATIC map's midpoint rule when reading
                // them, avoiding a second render/combination pass.
                float2 fb = isNear
                    ? UNITY_SAMPLE_TEXCUBEARRAY_LOD(_WireDynMaps, float4(v, cube), 0).rg
                    : UNITY_SAMPLE_TEXCUBEARRAY_LOD(_WireFarDynMaps, float4(v, cube), 0).rg;
                return fb.y < fb.x ? fb.y : min(0.5 * (fb.x + fb.y), fb.x + 0.35);
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

            // Static and moving depth use the exact SAME origin as the last
            // completed real-geometry shadow capture. No UV rotation,
            // virtual lookup offset, or per-receiver displacement estimates.
            // Static shadow geometry is captured from each firelight's
            // resting origin. Flame intensity still flickers in vanilla, but
            // flame position and shadows do not wobble. Dynamic casters
            // retain their own physically meaningful animation.
            float WireCachedShadow(int slot, float3 wpos, float3 normal)
            {
                float3 center = _WireShadowLights[slot].xyz;
                bool isNear = slot < (int)_WireNearCount;
                float3 v = wpos - center;
                float r = length(v);
                int dyn = _WireDynActive[slot] > 0.5
                    ? (int)_WireDynPhysicalSlot[slot] : -1;
                float staticLit = _WireNativeActive[slot] > 0.5
                    ? WireNativeStatic(v, r, slot, isNear)
                    : WireStaticBilinear(v, slot, isNear, r);

                // Fire maps always use their stable center. Actual moving
                // NON-fire sources (where supported) continue to use
                // their live position for dynamically rendered casters.
                float useLiveMovingOrigin = 1.0 - _WireLightNow[slot].w;
                float3 vd = wpos - lerp(center, _LightPos.xyz, useLiveMovingOrigin);
                float rd = length(vd);
                const float compareBias = 0.01;
                float dynamicLit = 1.0;
                if (dyn >= 0)
                    dynamicLit = _WireNativeMovingActive[slot] > 0.5
                        ? WireMovingNativeDepth(vd, rd, slot, isNear, compareBias)
                        : WireMovingPCF(vd, dyn, isNear, rd, compareBias);
                return lerp(1, staticLit * dynamicLit, _WireShadowLights[slot].w);
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


                float3 eyeVec = normalize(wpos - _WorldSpaceCameraPos);
                float3 h = -eyeVec + lightDir;
                h = h * rsqrt(max(0.001, dot(h, h)));

                float lh = saturate(dot(lightDir, h));
                float nh = saturate(dot(normal, h));
                float nl = saturate(dot(normal, lightDir));

                // Shadow only where the light actually reaches: in range, facing it.
                if (slot >= 0 && atten > 0.0001 && nl > 0)
                {
                    atten *= WireCachedShadow(slot, wpos, normal);
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
