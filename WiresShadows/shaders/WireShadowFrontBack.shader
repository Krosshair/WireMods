// Cached shadow maps, drawn straight from each light's own casters (ShadowCache.DrawDirect and
// DrawUntaggedOnePass, no camera): the surfaces facing the light into red, the ones facing away into
// green, each keeping the nearest (min blending, no depth buffer: a distance is its own depth).
// Combine pass 4 takes the midpoint from there. Same near-plane cut, collider proxies and cut-out
// holes as before.
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
    struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; float3 normal : TEXCOORD2; };
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
        o.normal = n;
        return o;
    }
    // Whether this surface faces the light. Told by the triangle itself (turned outward by the
    // mesh's normals), not by which way it's wound on screen: a face is drawn without a camera, and
    // a winding flipped by the render target would swap fronts and backs.
    bool FacesLight(v2f i)
    {
        float3 t = cross(ddx(i.world), ddy(i.world));
        if (dot(t, i.normal) < 0) t = -t;
        return dot(t, i.world - _WireLightCenter) < 0;
    }
    void Holes(v2f i)
    {
        // holes only where a cutoff (_WireClip) says so
        if (_WireClip > 0.5 || _Cutoff > 0.001)
            clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
    }
    float4 fragSheet(v2f i) : SV_Target
    {
        Holes(i);
        float d = WireDist(i.world);
        return float4(d, d, 0, 0);
    }
    float4 fragBoth(v2f i) : SV_Target
    {
        Holes(i);
        float d = WireDist(i.world);
        return FacesLight(i) ? float4(d, 60000, 0, 0) : float4(60000, d, 0, 0);
    }
    float4 fragBack(v2f i) : SV_Target
    {
        float d = WireDist(i.world);
        if (FacesLight(i)) discard;
        return float4(60000, d, 0, 0);
    }
    ENDCG

    SubShader
    {
        // 0: two-sided sheets (a banner) count from both sides, both channels.
        Pass
        {
            Cull Off ZTest Always ZWrite Off ColorMask RG
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragSheet
            #pragma multi_compile_instancing
            ENDCG
        }
        // 1: models: facing the light -> red, facing away -> green
        Pass
        {
            Cull Off ZTest Always ZWrite Off ColorMask RG
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBoth
            #pragma multi_compile_instancing
            ENDCG
        }
        // 2: colliders: only as backs -> green
        Pass
        {
            Cull Off ZTest Always ZWrite Off ColorMask G
            BlendOp Min Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBack
            #pragma multi_compile_instancing
            ENDCG
        }
    }
}
