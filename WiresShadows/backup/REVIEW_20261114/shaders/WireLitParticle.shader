// Dust clouds (a broken piece's, a tree's), in place of Lux Lit Particles, which lit them by every
// light as if nothing stood in the way (orange in a house at sunrise). A cloud here takes its
// colour and brightness from what was behind it when it appeared: the lit scene, captured small
// once for all the clouds appearing then, averaged round each cloud's spot and given to it as
// _WireBehind (WiresShadows: one capture, read back once, nothing after). Until that arrives (a few
// frames) the cloud isn't drawn. Tinted by the cloud's own dust colour (its hue, not its
// brightness), shaded a little by the texture's own normals so it keeps its shape. The rest as Lux's
// material sets it: the texture's alpha (Lux keeps it in _MainTex.a), soft edges where it meets
// geometry (_InvFade), the near and far camera fades. No fog of its own: the capture has the
// game's fog in it already.
Shader "Hidden/WireLitParticle"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Normal (RG) Depth (B) Alpha (A)", 2D) = "white" {}
        _EnableAlbedo ("Enable Albedo", Float) = 0
        _AlbedoMap ("Albedo", 2D) = "white" {}
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

            sampler2D _MainTex, _AlbedoMap;
            float4 _MainTex_ST;
            float4 _Color, _CamFadeDistance;
            float _EnableAlbedo, _InvFade;
            float4 _WireBehind; // per cloud (linear); alpha 0 until it's been worked out
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
                float4 tex = tex2D(_MainTex, i.uv);
                float a = tex.a * i.color.a * _Color.a * _WireBehind.a;
                // soft where it meets what's behind it
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                a *= saturate(_InvFade * (scene - i.eye));
                // faded right at the camera and far off
                a *= saturate(i.eye / max(_CamFadeDistance.x, 0.01));
                a *= saturate((_CamFadeDistance.y - i.eye) / max(_CamFadeDistance.z, 0.01));
                // the dust's own hue, not its brightness (Rec. 709 luminance)
                float3 dust = _Color.rgb * i.color.rgb;
                if (_EnableAlbedo > 0.5) dust *= tex2D(_AlbedoMap, i.uv).rgb;
                dust /= max(dot(dust, float3(0.2126, 0.7152, 0.0722)), 1e-3);
                // its shape: the texture's normals, upward-facing parts a little lighter
                float up = tex.g * 2 - 1;
                float3 col = _WireBehind.rgb * dust * (1 + 0.25 * up);
                return float4(col, saturate(a));
            }
            ENDCG
        }
    }
}
