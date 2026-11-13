// Cached shadow maps, pass 1: the nearest surfaces facing the light.
// Writes each object's distance from _WireLightCenter (alpha-tested ones
// cut out). Instanced pieces need the instancing variant, or every copy lands
// on one transform.
Shader "Hidden/WireShadowFront"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    float3 _WireLightCenter;
    // The light's near plane, as a sphere: nothing closer casts (the stone
    // around a sconce doesn't shade itself). Inside it, the colliders are
    // drawn instead (_WireProxy), so a wall there still blocks the light.
    float _WireNearCut;
    float _WireProxy;
    float WireDist(float3 world)
    {
        float d = distance(world, _WireLightCenter);
        // 0: models, cut at the near plane. 1: colliders inside it only.
        // 2: walls, floors and roofs as their colliders, everywhere.
        if (_WireProxy < 0.5 ? d < _WireNearCut : _WireProxy < 1.5 && d > _WireNearCut)
            discard;
        return d;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // An "Opaque" material with a cutoff (Valheim's Custom/Piece: the spice rack's herbs)
            // still has holes, as in the game's own shadows: drawn as whole cards they were blobs.
            // _WireClip: drawn directly for a material without an Opaque or
            // Cutout tag, with its texture and cutoff (the mod sets these).
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            float _WireClip;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_WireClip > 0.5 || _Cutoff > 0.001)
                    clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
                return WireDist(i.world);
            }
            ENDCG
        }
        // Pass 1, drawn directly only: two-sided sheets (a banner) count from
        // both sides. The tag keeps the shadow camera from drawing it.
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // An "Opaque" material with a cutoff (Valheim's Custom/Piece: the spice rack's herbs)
            // still has holes, as in the game's own shadows: drawn as whole cards they were blobs.
            // _WireClip: drawn directly for a material without an Opaque or
            // Cutout tag, with its texture and cutoff (the mod sets these).
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            float _WireClip;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_WireClip > 0.5 || _Cutoff > 0.001)
                    clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
                return WireDist(i.world);
            }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.01));
                return WireDist(i.world);
            }
            ENDCG
        }
    }
}
