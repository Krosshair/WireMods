// Cached shadow maps in one render: WireShadowFront and WireShadowBack together. Pass 0 draws the
// surfaces facing the light into red, pass 1 the ones facing away into green, each keeping the
// nearest (min blending, no depth buffer: a distance is its own depth). The camera draws both
// passes of an object in its one render, so the scene is culled once. Same near-plane cut,
// collider proxies and cut-out holes as the two shaders.
Shader "Hidden/WireShadowFrontBack"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    float3 _WireLightCenter;
    float _WireNearCut;
    float _WireProxy;
    sampler2D _MainTex;
    float4 _MainTex_ST;
    float _Cutoff;
    float _WireClip;
    float WireDist(float3 world)
    {
        float d = distance(world, _WireLightCenter);
        // 0: models, cut at the near plane. 1: colliders inside it only.
        // 2: walls, floors and roofs as their colliders, everywhere.
        if (_WireProxy < 0.5 ? d < _WireNearCut : _WireProxy < 1.5 && d > _WireNearCut)
            discard;
        return d;
    }
    float _WireMapRes; // the map's face size (ShadowCache, per render)
    struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };
    v2f vert(appdata v)
    {
        UNITY_SETUP_INSTANCE_ID(v);
        v2f o;
        // Each caster pulled in along its normals by one map texel (Unity's own shadow normal bias):
        // an edge sticking past a piece it overlaps (snapped beams crossing) shrinks away instead of
        // shading it. Whole pieces still cast.
        float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
        // never less than the 1 cm the lighting allows (as its receiver offset, WireCachedShadow)
        float texel = max(2.0 * distance(world, _WireLightCenter) / max(_WireMapRes, 1.0), 0.01);
        float3 n = UnityObjectToWorldNormal(v.normal);
        world -= n * (texel * rsqrt(max(dot(n, n), 1e-8))); // a mesh without normals stays as it is
        o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
        o.world = world;
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        return o;
    }
    // Opaque: holes only where a cutoff (or _WireClip, drawn directly) says so.
    float4 fragOpaque(v2f i) : SV_Target
    {
        if (_WireClip > 0.5 || _Cutoff > 0.001)
            clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
        float d = WireDist(i.world);
        return float4(d, d, 0, 0);
    }
    float4 fragCutout(v2f i) : SV_Target
    {
        clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
        float d = WireDist(i.world);
        return float4(d, d, 0, 0);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        // 0: facing the light -> red
        Pass
        {
            Cull Back ZTest Always ZWrite Off ColorMask R
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOpaque
            #pragma multi_compile_instancing
            ENDCG
        }
        // 1: facing away -> green
        Pass
        {
            Cull Front ZTest Always ZWrite Off ColorMask G
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOpaque
            #pragma multi_compile_instancing
            ENDCG
        }
        // 2, drawn directly only: two-sided sheets (a banner) count from both sides, both
        // channels. The tag keeps the camera from drawing it.
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off ZTest Always ZWrite Off ColorMask RG
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOpaque
            #pragma multi_compile_instancing
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Back ZTest Always ZWrite Off ColorMask R
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragCutout
            #pragma multi_compile_instancing
            ENDCG
        }
        Pass
        {
            Cull Front ZTest Always ZWrite Off ColorMask G
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragCutout
            #pragma multi_compile_instancing
            ENDCG
        }
    }
}
