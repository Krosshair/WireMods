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
        // Present: this shader reads the sized map pools (the mod checks for it).
        _WireTieredShader ("", Float) = 1
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
            // Only while the tiled lights copy Unity's falloff (ShadowCache.Tiled). A pixel shader
            // that can write a buffer loses early depth/stencil rejection on D3D11, so normal play
            // uses the variant without it.
            #pragma multi_compile _ WIRE_CAPTURE
            #pragma exclude_renderers nomrt

            #include "UnityCG.cginc"
            #include "UnityDeferredLibrary.cginc"

            #include "WireLightShared.cginc"

            // the falloff copy (frag), while the mod is taking it
            #ifdef WIRE_CAPTURE
            RWStructuredBuffer<float> _WireAttenCapture : register(u1);
            float _WireCapturing;
            #endif

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
                #if defined(POINT) && !defined(SHADOWS_CUBE)
                // lit by the tiled pass with every other light at once: this one draws nothing
                if (WireIsTiled(_LightPos.xyz)) o.pos = float4(0, 0, -2, 1);
                #endif
                return o;
            }

            half4 CalculateLight(unity_v2f_deferred i, int slot)
            {
                float3 wpos;
                float2 uv;
                float atten, fadeDist;
                half3 lightDir;
                UnityDeferredCalculateLightParams(i, wpos, uv, lightDir, atten, fadeDist);
                // Out of the light's reach (the sphere covers pixels far behind or in front of it):
                // everything below is multiplied by atten, so the answer is 0 without reading anything.
                // Not for the sun: its atten is the shadow mask, and waiting on that read before the
                // G-buffer ones made its full-screen pass slower than reading all of them at once.
                #if !defined(DIRECTIONAL) && !defined(DIRECTIONAL_COOKIE)
                if (atten <= 0)
                    return half4(0, 0, 0, 1);
                #endif

                half4 gbuffer0 = tex2D(_CameraGBufferTexture0, uv);
                half4 gbuffer1 = tex2D(_CameraGBufferTexture1, uv);
                half4 gbuffer2 = tex2D(_CameraGBufferTexture2, uv);
                float3 normal = normalize(gbuffer2.rgb * 2 - 1);
                return half4(WireShade(gbuffer0, gbuffer1, normal, wpos, lightDir, atten, _LightColor.rgb, slot, _LightPos.xyz), 1);
            }

            #ifdef UNITY_HDR_ON
            half4
            #else
            fixed4
            #endif
            frag(wire_v2f w) : SV_Target
            {
                #if defined(POINT) && !defined(SHADOWS_CUBE) && defined(WIRE_CAPTURE)
                // Copying Unity's point light falloff for the tiled pass (WireTiledLights): its
                // texture is only bound here, in Unity's own light pass. Each pixel writes the one
                // sample its column stands for (256 of them); identical values, so no order matters.
                if (_WireCapturing > 0.5)
                {
                    uint k = (uint)w.pos.x & 255;
                    float u = k / 255.0;
                    _WireAttenCapture[k] = tex2Dlod(_LightTextureB0, float4(u, u, 0, 0)).r;
                }
                #endif
                unity_v2f_deferred i;
                i.pos = w.pos;
                i.uv = w.uv;
                i.ray = w.ray;
                // Only point lights have a map. Everywhere else the slot is a fixed -1, so the
                // compiler leaves the whole cached-shadow code (every map, the 96-entry tables) out
                // of this variant: the sun's full-screen pass was carrying all of it, unused.
                #if (defined(POINT) || defined(POINT_COOKIE)) && !defined(SHADOWS_CUBE)
                int slot = (int)w.slot;
                #else
                const int slot = -1;
                #endif
                half4 c = CalculateLight(i, slot);
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
