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

    float4 WireFrontBack(float3 world, float3 normal)
    {
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
            #pragma target 4.0
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
