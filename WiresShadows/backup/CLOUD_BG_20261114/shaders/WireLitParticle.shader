// Dust clouds (a broken piece's, a tree's), in place of Lux Lit Particles. Lux lights a cloud by
// every light as if nothing stood in the way: in a house at sunrise a cloud glowed orange, and one
// behind a wall from a hearth still caught its light. Here a cloud's light is worked out by the mod
// (WiresShadows, ShadowCache.LightParticles): the game's ambient plus the sun and each light that
// can actually reach it, as _WireLight (linear), once when it appears. The look otherwise as Lux's
// material sets it: its colour, the texture's alpha (Lux keeps its alpha in _MainTex.a), an albedo
// map if it has one, emission if it has it, soft edges where it meets geometry (_InvFade) and its
// near and far camera fades. Flat: no normal-mapped shading. Fog as the game's (exponential, the
// main scene's mode), applied here so no fog variant can be stripped from the bundle.
Shader "Hidden/WireLitParticle"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Normal (RG) Depth (B) Alpha (A)", 2D) = "white" {}
        _EnableAlbedo ("Enable Albedo", Float) = 0
        _AlbedoMap ("Albedo", 2D) = "white" {}
        _EnableEmission ("Enable Emission", Float) = 0
        _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionScale ("Emission Scale", Float) = 1
        _InvFade ("Soft Particles Factor", Float) = 1
        _CamFadeDistance ("Near (X) Far (Y) FarRange (Z)", Vector) = (4,150,25,0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _AlbedoMap, _EmissionMap;
            float4 _MainTex_ST;
            float4 _Color, _EmissionColor, _CamFadeDistance;
            float _EnableAlbedo, _EnableEmission, _EmissionScale, _InvFade;
            float4 _WireLight; // set per cloud by the mod (linear); zero: not worked out yet
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 screen : TEXCOORD1;
                float eye : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.screen = ComputeScreenPos(o.pos);
                o.eye = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a * i.color.a * _Color.a;
                // soft where it meets what's behind it
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                a *= saturate(_InvFade * (scene - i.eye));
                // faded right at the camera and far off
                a *= saturate(i.eye / max(_CamFadeDistance.x, 0.01));
                a *= saturate((_CamFadeDistance.y - i.eye) / max(_CamFadeDistance.z, 0.01));
                float3 albedo = _Color.rgb * i.color.rgb;
                if (_EnableAlbedo > 0.5) albedo *= tex2D(_AlbedoMap, i.uv).rgb;
                // before the mod has worked it out (its first frame): the ambient alone
                float3 light = _WireLight.a > 0 ? _WireLight.rgb : unity_AmbientSky.rgb;
                float3 col = albedo * light;
                if (_EnableEmission > 0.5) col += _EmissionColor.rgb * tex2D(_EmissionMap, i.uv).rgb * _EmissionScale;
                float fog = saturate(exp2(-unity_FogParams.y * i.eye));
                col = lerp(unity_FogColor.rgb, col, fog);
                return float4(col, saturate(a));
            }
            ENDCG
        }
    }
}
