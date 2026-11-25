// Wire's Shadows: the game's Custom/LitParticles (rain, snow, mist, wind dust) with every point light
// the mod knows lit in its main pass. The game's shader is a forward one: Unity draws it once, then
// once more for each point light reaching it (ForwardAdd), so heavy rain over a lit base was drawn
// again for every fire, torch and sconce around. Here there is no per-light pass at all, so Unity
// issues no extra draws: the main pass adds the lights itself (the mod's list, _WireLit*), lit at
// each particle's corners rather than per pixel.
// Everything else is the game's shader as compiled (decompiled from its bundle): the same albedo,
// normal map, fades, sky mask, lighting (_LightNormalFactor) and sun-tinted fog, number for number.
Shader "Wire/LitParticles"
{
    Properties
    {
        _Color ("Color", Vector) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Vector) = (0,0,0,1)
        _NormalTex ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal power", Range(0, 4)) = 1
        _LightNormalFactor ("Light normal factor", Range(0, 1)) = 0
        _ZFadeDistance ("Soft particle fade distance", Float) = 0.3
        _CameraFadeDistanceMin ("Camera fade distance min", Float) = 0
        _CameraFadeDistanceMax ("Camera fade distance max", Float) = 0.1
        _CameraYFadeDistance ("Camera Y fade distance", Float) = 0
        [MaterialToggle] _Billboard ("Billboard", Float) = 0
        [MaterialToggle] _SkyMask ("SkyMask", Float) = 0
        [KeywordEnum(Red,Green,Blue,Alpha)] _AlphaChannel ("Alpha Channel", Float) = 3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        #include "AutoLight.cginc"

        sampler2D _MainTex, _NormalTex, _NoiseTex, _SkyAlphaTexture;
        float4 _MainTex_ST;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _Color, _EmissionColor, _SunFogColor;
        float3 _SunDir, _SkyAlphaPosition;
        float _BumpScale, _LightNormalFactor, _ZFadeDistance, _CameraFadeDistanceMin, _CameraFadeDistanceMax,
              _CameraYFadeDistance, _Billboard, _SkyMask, _AlphaChannel;
        float unity_FogDensity, unity_FogStart, unity_FogEnd; // the game sets these itself

        // the mod's point lights: where (xyz) and 1 / range squared (w); Unity's _LightColor (rgb);
        // Unity's own falloff texture (copied from its light pass)
        StructuredBuffer<float4> _WireLitPos, _WireLitColor;
        float _WireLitCount;
        sampler2D _WireAtten;
        // Reuse the existing opaque tiled-light candidate list where it is
        // complete. The tile boxes are conservative and do NOT use opaque
        // depth, so they also cover transparent weather fragments.
        StructuredBuffer<uint> _WireTileLights;
        float _WireLitTileMode, _WireLitTilesX, _WireLitTilesY;

        struct appdata
        {
            float4 vertex : POSITION;
            float4 tangent : TANGENT;
            float3 normal : NORMAL;
            float4 texcoord : TEXCOORD0;
            fixed4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 tSpace0 : TEXCOORD1; // tangent to world rows, world position in w
            float4 tSpace1 : TEXCOORD2;
            float4 tSpace2 : TEXCOORD3;
            float fogDepth : TEXCOORD4;
            fixed4 color : COLOR;
            float4 screen : TEXCOORD5;  // for the scene depth; w: the particle's eye depth
            float3 sh : TEXCOORD6;      // ambient and Unity's vertex lights (main pass)
            float3 points : TEXCOORD7;  // the mod's point lights, lit at the particle's corners
            UNITY_VERTEX_OUTPUT_STEREO
        };

        bool WireLitKnows(float3 lightPos)
        {
            int n = (int)_WireLitCount;
            [loop]
            for (int i = 0; i < n; i++)
            {
                float3 o = _WireLitPos[i].xyz - lightPos;
                if (dot(o, o) < 1e-5) return true;
            }
            return false;
        }

        v2f vertBase(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            if (_Billboard > 0.5)
            {
                // the game's billboard: the quad turned to face the camera, at the object's scale
                float3 r = normalize(UNITY_MATRIX_V[0].xyz), u = normalize(UNITY_MATRIX_V[1].xyz), b = normalize(UNITY_MATRIX_V[2].xyz);
                float2 s = v.vertex.xy * float2(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21));
                float3 wd = s.x * r + s.y * u - v.vertex.z * b;
                v.vertex.xyz = mul((float3x3)unity_WorldToObject, wd);
                float3 wn = v.normal.x * r + v.normal.y * u - v.normal.z * b;
                v.normal = mul(wn, (float3x3)unity_ObjectToWorld);
            }
            float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
            float3 wn = UnityObjectToWorldNormal(v.normal);
            float3 wt = UnityObjectToWorldDir(v.tangent.xyz);
            float3 wb = cross(wn, wt) * (v.tangent.w * unity_WorldTransformParams.w);
            o.tSpace0 = float4(wt.x, wb.x, wn.x, wpos.x);
            o.tSpace1 = float4(wt.y, wb.y, wn.y, wpos.y);
            o.tSpace2 = float4(wt.z, wb.z, wn.z, wpos.z);
            o.fogDepth = o.pos.w;
            o.color = v.color;
            o.screen = ComputeScreenPos(o.pos);
            o.sh = 0;
            #if defined(UNITY_PASS_FORWARDBASE)
                #ifdef LIGHTPROBE_SH
                    o.sh = ShadeSH9(float4(wn, 1));
                #endif
                #ifdef VERTEXLIGHT_ON
                    // Unity's vertex lights (Shade4PointLights), less the ones the main pass lights
                    float4 lx = unity_4LightPosX0 - wpos.x, ly = unity_4LightPosY0 - wpos.y, lz = unity_4LightPosZ0 - wpos.z;
                    float4 d2 = max(lx * lx + ly * ly + lz * lz, 0.000001);
                    float4 ndl = max(0, (lx * wn.x + ly * wn.y + lz * wn.z) * rsqrt(d2));
                    float4 att = 1.0 / (1.0 + d2 * unity_4LightAtten0);
                    float4 diff = ndl * att;
                    [unroll]
                    for (int k = 0; k < 4; k++)
                        if (WireLitKnows(float3(unity_4LightPosX0[k], unity_4LightPosY0[k], unity_4LightPosZ0[k]))) diff[k] = 0;
                    o.sh += unity_LightColor[0].rgb * diff.x + unity_LightColor[1].rgb * diff.y + unity_LightColor[2].rgb * diff.z + unity_LightColor[3].rgb * diff.w;
                #endif
                // The mod's point lights, at the corners: a raindrop or flake is a few pixels across,
                // so lighting its 4 corners and blending across looks the same as lighting each pixel,
                // and the loop runs 4 times per particle instead of once per pixel of heavy overdraw.
                // The facing term uses the particle's own normal (no normal map at a corner).
                int n = (int)_WireLitCount;
                [loop]
                for (int j = 0; j < n; j++)
                {
                    float4 pr = _WireLitPos[j];
                    float3 tolight = pr.xyz - wpos;
                    float at = dot(tolight, tolight) * pr.w;
                    if (at >= 1) continue;
                    float fall = tex2Dlod(_WireAtten, float4(at, at, 0, 0)).r;
                    float facing = max(0, dot(wn, normalize(tolight)));
                    o.points += _WireLitColor[j].rgb * fall * lerp(1, facing, _LightNormalFactor);
                }
            #endif
            return o;
        }

        // The game's surface: albedo and alpha, the normal, and how far it's faded (soft particles,
        // near the camera, by height, the sky mask, below the water line).
        void Surface(v2f i, out float3 albedo, out float alpha, out float3 normal)
        {
            float4 c = tex2D(_MainTex, i.uv) * _Color;
            float a = c[(int)_AlphaChannel];
            clip(a - 0.000001);
            albedo = c.rgb * i.color.rgb;
            a *= i.color.a;

            float3 tn = float3(0, 0, 1);
            if (_BumpScale > 0)
                tn = lerp(float3(0, 0, 1), UnpackNormal(tex2D(_NormalTex, i.uv)), _BumpScale);
            normal = normalize(float3(dot(i.tSpace0.xyz, tn), dot(i.tSpace1.xyz, tn), dot(i.tSpace2.xyz, tn)));

            float3 wpos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
            float eye = i.screen.w;
            float f = saturate((eye - _CameraFadeDistanceMin) / (_CameraFadeDistanceMax - _CameraFadeDistanceMin));
            float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.screen.xy / i.screen.w));
            f = min(f, saturate((scene - eye) / _ZFadeDistance));
            f *= f;
            if (_CameraYFadeDistance > 0)
                f = min(f, saturate(abs(wpos.y - _WorldSpaceCameraPos.y) / _CameraYFadeDistance));
            if (_SkyMask > 0.5)
            {
                float2 n = tex2D(_NoiseTex, wpos.xz).xy;
                float2 uv = (n - 0.5) * 0.003 + 0.5 + (wpos.xz - _SkyAlphaPosition.xz) * 0.01;
                float sky = tex2D(_SkyAlphaTexture, uv).r;
                float edge = saturate((length(0.5 - uv) - 0.4) * 10.000001);
                float open = edge * edge * (3 - 2 * edge);
                f = min(f, length(wpos - _SkyAlphaPosition) - sky * 100 < 0.1 ? 1 : open);
            }
            f = min(f, saturate((wpos.y - 30) / _ZFadeDistance));
            alpha = a * f;
        }

        // one light, the game's way: half way to unlit by _LightNormalFactor
        float3 Light(float3 albedo, float3 normal, float3 dir, float3 color)
        {
            float ndl = max(0, dot(normal, dir));
            return albedo * color * lerp(1, ndl, _LightNormalFactor);
        }

        // the game's fog: Unity's density and linear fog, the larger, toward its colour tinted to the sun
        float FogAmount(float depth)
        {
            float e = 1 - min(exp(-depth * unity_FogDensity), 1);
            float l = 1 - saturate((unity_FogEnd - depth) / (unity_FogEnd - unity_FogStart));
            return max(e, l);
        }
        ENDCG

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vertBase
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nodirlightmap noshadow noshadowmask
            #pragma multi_compile_instancing

            float4 frag(v2f i) : SV_Target
            {
                float3 albedo, normal; float alpha;
                Surface(i, albedo, alpha, normal);
                float3 wpos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
                float atten = UnitySampleBakedOcclusion(float2(0, 0), wpos);
                float3 c = Light(albedo, normal, _WorldSpaceLightPos0.xyz, _LightColor0.rgb * atten);
                c += albedo * i.sh + _EmissionColor.rgb;
                // the mod's point lights, lit at the corners (vertBase)
                c += albedo * i.points;

                float3 dir = normalize(wpos - _WorldSpaceCameraPos);
                float3 fogColor = lerp(unity_FogColor.rgb, pow(_SunFogColor.rgb, 2.2), saturate(dot(dir, _SunDir)));
                return float4(lerp(c, fogColor, FogAmount(i.fogDepth)), alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
