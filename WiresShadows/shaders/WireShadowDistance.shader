// Moving maps (characters, swinging doors, the building preview): one pass
// records, per texel, the nearest surface facing the light (R) and the
// nearest facing away (G), with min blending and no depth test; pass 1 of
// WireShadowCombine then stores the midpoint. Shadows then start inside the
// object, so they meet it with no bias, and its lit side can't shade itself.
Shader "Hidden/WireShadowDistance"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    float3 _WireLightCenter;

    // A machine's turning parts (Machines.cs): while one is drawn into a light's moving map, any
    // of its pixels the light reaches past that light's own walls (its cached map) marks
    // _WireMachineSeen[_WireCountIndex - 1]. A machine that never marks it over a whole turn shades
    // nothing that light shows, and that light stops drawing it. 0: not counting.
    float _WireCountIndex, _WireCountSlot;
    #if SHADER_TARGET >= 45
    RWStructuredBuffer<uint> _WireMachineSeen : register(u1);
    float4 _WireShadowLights[96];
    float _WireMapTier[96];
    float _WireMapCube[96];
    float _WireNativeInvRanges[96];
    float4 _WireTierRes;
    TextureCubeArray<float> _WireTier0, _WireTier1, _WireTier2, _WireTier3;
    SamplerComparisonState sampler_linear_clamp_compare;

    float WireCountTap(int tier, float3 d, float cube, float compare)
    {
        float4 c = float4(d, cube);
        if (tier == 0) return _WireTier0.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
        if (tier == 1) return _WireTier1.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
        if (tier == 2) return _WireTier2.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
        return _WireTier3.SampleCmpLevelZero(sampler_linear_clamp_compare, c, compare);
    }

    // the same test as a receiver's (WireTierStatic): lit by this light past its walls?
    void WireCountFor(float3 world, float countIndex, float countSlot)
    {
        if (countIndex < 0.5) return;
        uint idx = (uint)countIndex - 1;
        int slot = (int)countSlot;
        int tier = (int)_WireMapTier[slot];
        if (tier < 0) { _WireMachineSeen[idx] = 1; return; }
        float3 v = world - _WireShadowLights[slot].xyz;
        float r = length(v);
        float z = r / _WireTierRes[tier];
        float compare = saturate((r - 0.01) * _WireNativeInvRanges[slot]);
        #if defined(UNITY_REVERSED_Z)
            compare = 1.0 - compare;
        #endif
        float cube = _WireMapCube[slot];
        float lit = WireCountTap(tier, v + float3(z, z, z), cube, compare) + WireCountTap(tier, v + float3(-z, -z, z), cube, compare) +
                    WireCountTap(tier, v + float3(-z, z, -z), cube, compare) + WireCountTap(tier, v + float3(z, -z, -z), cube, compare);
        if (lit > 0) _WireMachineSeen[idx] = 1;
    }
    void WireCount(float3 world) { WireCountFor(world, _WireCountIndex, _WireCountSlot); }
    #else
    void WireCountFor(float3 world, float countIndex, float countSlot) { }
    void WireCount(float3 world) { }
    #endif

    float4 WireFrontBackAt(float3 world, float3 normal, float3 center)
    {
        float3 d = world - center;
        float dist = length(d);
        bool front = dot(normal, d) < 0;
        return front ? float4(dist, 60000, 0, 0) : float4(60000, dist, 0, 0);
    }

    float4 WireFrontBack(float3 world, float3 normal)
    {
        WireCount(world);
        float3 d = world - _WireLightCenter;
        float dist = length(d);
        // Facing from the geometry, not the rasteriser: cube faces are drawn
        // with flipped projections, which flips the winding.
        bool front = dot(normal, d) < 0;
        return front ? float4(dist, 60000, 0, 0) : float4(60000, dist, 0, 0);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(v2f i) : SV_Target { return WireFrontBack(i.world, i.normal); }
            ENDCG
        }
        // Fast path for nearest-depth shadows: the minimum of front/back
        // depths equals the nearest triangle depth. Record it directly in the
        // RHalf cubemap with MIN blending. No RGHalf intermediate or full
        // screen combine pass is needed for this mode.
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vertNearest
            #pragma fragment fragNearest
            #pragma multi_compile_instancing
            struct nearestVertex { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct nearestOutput { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
            nearestOutput vertNearest(nearestVertex v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                nearestOutput o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float fragNearest(nearestOutput i) : SV_Target
            {
                return length(i.world - _WireLightCenter);
            }
            ENDCG
        }
        // Moving maps, one draw per object for every face of a light's cube it covers (instead of one
        // draw per face): a geometry shader sends each triangle to the faces in _WireFaceMask, each
        // with its own view-projection, into the cube array's slices from _WireSliceBase. Same
        // output as pass 0.
        Pass
        {
            Name "WIREGS"
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vertGS
            #pragma geometry geomGS
            #pragma fragment fragGS
            #pragma target 5.0
            #pragma require geometry
            float4x4 _WireFaceVP[6];
            float _WireFaceMask;
            float _WireSliceBase;
            struct gsIn { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2g { float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            struct g2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; uint slice : SV_RenderTargetArrayIndex; };
            v2g vertGS(gsIn v)
            {
                v2g o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            [maxvertexcount(18)]
            void geomGS(triangle v2g p[3], inout TriangleStream<g2f> stream)
            {
                uint mask = (uint)(_WireFaceMask + 0.5);
                [unroll]
                for (uint f = 0; f < 6; f++)
                {
                    if ((mask & (1u << f)) == 0) continue;
                    float4 c0 = mul(_WireFaceVP[f], float4(p[0].world, 1));
                    float4 c1 = mul(_WireFaceVP[f], float4(p[1].world, 1));
                    float4 c2 = mul(_WireFaceVP[f], float4(p[2].world, 1));
                    // all three corners past the same side of this face: nothing to draw there
                    if ((c0.x > c0.w && c1.x > c1.w && c2.x > c2.w) || (c0.x < -c0.w && c1.x < -c1.w && c2.x < -c2.w) ||
                        (c0.y > c0.w && c1.y > c1.w && c2.y > c2.w) || (c0.y < -c0.w && c1.y < -c1.w && c2.y < -c2.w))
                        continue;
                    g2f o;
                    o.slice = (uint)(_WireSliceBase + 0.5) + f;
                    o.pos = c0; o.world = p[0].world; o.normal = p[0].normal; stream.Append(o);
                    o.pos = c1; o.world = p[1].world; o.normal = p[1].normal; stream.Append(o);
                    o.pos = c2; o.world = p[2].world; o.normal = p[2].normal; stream.Append(o);
                    stream.RestartStrip();
                }
            }
            float4 fragGS(g2f i) : SV_Target { return WireFrontBack(i.world, i.normal); }
            ENDCG
        }
        // Moving shadows of every light this frame in one draw per object (ShadowCache, the batch):
        // each entry is one light's cube face (its view-projection, the light's centre, the scratch
        // slot it goes to, and the machine count for it); the geometry shader copies each triangle
        // to every entry the object has, up to 6 a draw (a small geometry shader output runs far faster).
        Pass
        {
            Name "WIREGSBATCH"
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vertB
            #pragma geometry geomB
            #pragma fragment fragB
            #pragma target 5.0
            #pragma require geometry
            struct Entry { float4x4 vp; float4 center; float4 extra; }; // center.w: slot; extra.x: count index, .y: light slot
            StructuredBuffer<Entry> _WireEntries;
            float _WireEntryStart, _WireEntryCount;
            struct bIn { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2gB { float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            struct g2fB
            {
                float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1;
                nointerpolation float3 center : TEXCOORD2; nointerpolation float2 count : TEXCOORD3;
                uint slice : SV_RenderTargetArrayIndex;
            };
            v2gB vertB(bIn v)
            {
                v2gB o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            [maxvertexcount(18)]
            void geomB(triangle v2gB p[3], inout TriangleStream<g2fB> stream)
            {
                uint start = (uint)(_WireEntryStart + 0.5), count = min((uint)(_WireEntryCount + 0.5), 6u);
                for (uint e = 0; e < count; e++)
                {
                    Entry en = _WireEntries[start + e];
                    float4 c0 = mul(en.vp, float4(p[0].world, 1));
                    float4 c1 = mul(en.vp, float4(p[1].world, 1));
                    float4 c2 = mul(en.vp, float4(p[2].world, 1));
                    if ((c0.x > c0.w && c1.x > c1.w && c2.x > c2.w) || (c0.x < -c0.w && c1.x < -c1.w && c2.x < -c2.w) ||
                        (c0.y > c0.w && c1.y > c1.w && c2.y > c2.w) || (c0.y < -c0.w && c1.y < -c1.w && c2.y < -c2.w))
                        continue;
                    g2fB o;
                    o.slice = (uint)(en.center.w + 0.5);
                    o.center = en.center.xyz; o.count = en.extra.xy;
                    o.pos = c0; o.world = p[0].world; o.normal = p[0].normal; stream.Append(o);
                    o.pos = c1; o.world = p[1].world; o.normal = p[1].normal; stream.Append(o);
                    o.pos = c2; o.world = p[2].world; o.normal = p[2].normal; stream.Append(o);
                    stream.RestartStrip();
                }
            }
            float4 fragB(g2fB i) : SV_Target
            {
                WireCountFor(i.world, i.count.x, i.count.y);
                return WireFrontBackAt(i.world, i.normal, i.center.xyz);
            }
            ENDCG
        }
        // Particles (a broken piece's dust and chips, a fire's sparks): flat sprites, cut out by
        // their texture's alpha times their own fade (vertex colour), at the material's cutoff
        // (0 for a solid one: whole). A sprite has no inside and no back, so it
        // writes its distance as both its front and its back (as the cached maps' two-sided
        // sheets): the midpoint is the sprite itself.
        Pass
        {
            Name "WIRESHEET"
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vertSheet
            #pragma fragment fragSheet
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _WireSheetCut;
            struct sheetIn { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct sheetOut { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; float alpha : TEXCOORD2; };
            sheetOut vertSheet(sheetIn v)
            {
                sheetOut o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alpha = v.color.a;
                return o;
            }
            float4 fragSheet(sheetOut i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a * i.alpha - _WireSheetCut);
                float dist = length(i.world - _WireLightCenter);
                return float4(dist, dist, 0, 0);
            }
            ENDCG
        }
        // The same, one draw for every face it covers (as WIREGS).
        Pass
        {
            Name "WIRESHEETGS"
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vertSheetGS
            #pragma geometry geomSheetGS
            #pragma fragment fragSheetGS
            #pragma target 4.0
            #pragma require geometry
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _WireSheetCut;
            float4x4 _WireFaceVP[6];
            float _WireFaceMask;
            float _WireSliceBase;
            struct sgIn { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct s2g { float3 world : TEXCOORD0; float2 uv : TEXCOORD1; float alpha : TEXCOORD2; };
            struct sg2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; float alpha : TEXCOORD2; uint slice : SV_RenderTargetArrayIndex; };
            s2g vertSheetGS(sgIn v)
            {
                s2g o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alpha = v.color.a;
                return o;
            }
            [maxvertexcount(18)]
            void geomSheetGS(triangle s2g p[3], inout TriangleStream<sg2f> stream)
            {
                uint mask = (uint)(_WireFaceMask + 0.5);
                [unroll]
                for (uint f = 0; f < 6; f++)
                {
                    if ((mask & (1u << f)) == 0) continue;
                    float4 c0 = mul(_WireFaceVP[f], float4(p[0].world, 1));
                    float4 c1 = mul(_WireFaceVP[f], float4(p[1].world, 1));
                    float4 c2 = mul(_WireFaceVP[f], float4(p[2].world, 1));
                    if ((c0.x > c0.w && c1.x > c1.w && c2.x > c2.w) || (c0.x < -c0.w && c1.x < -c1.w && c2.x < -c2.w) ||
                        (c0.y > c0.w && c1.y > c1.w && c2.y > c2.w) || (c0.y < -c0.w && c1.y < -c1.w && c2.y < -c2.w))
                        continue;
                    sg2f o;
                    o.slice = (uint)(_WireSliceBase + 0.5) + f;
                    o.pos = c0; o.world = p[0].world; o.uv = p[0].uv; o.alpha = p[0].alpha; stream.Append(o);
                    o.pos = c1; o.world = p[1].world; o.uv = p[1].uv; o.alpha = p[1].alpha; stream.Append(o);
                    o.pos = c2; o.world = p[2].world; o.uv = p[2].uv; o.alpha = p[2].alpha; stream.Append(o);
                    stream.RestartStrip();
                }
            }
            float4 fragSheetGS(sg2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a * i.alpha - _WireSheetCut);
                float dist = length(i.world - _WireLightCenter);
                return float4(dist, dist, 0, 0);
            }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
                return WireFrontBack(i.world, i.normal);
            }
            ENDCG
        }
    }
}
