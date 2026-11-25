// Wire's Shadows: every point light in one pass. Unity draws each point light on its own, a sphere
// over the screen that reads the G-buffer again and adds itself on top; where several overlap (a
// hearth, sconces and torches in one room) each pixel is lit over and over. Here each pixel reads
// the G-buffer once and adds every light that reaches its 16 x 16 tile (WireTiledLights.compute),
// each by exactly the same lighting as Unity's pass (WireShade, WireLightShared.cginc) and the
// falloff Unity uses (_WireAtten: Unity's own falloff texture, copied from its light pass).
Shader "Hidden/WireTiledLights"
{
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            Blend [_WireSrc] [_WireDst]

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ WIRE_LDR
            #pragma multi_compile _ WIRE_SMOOTH_POINT WIRE_SMOOTH_ALL
            #include "UnityCG.cginc"
            // the camera, as the mod passes it (Camera.cameraToWorldMatrix, the inverse projection,
            // raw depth to eye depth: 1 / (x d + y), its position)
            float4x4 _WireCamToWorld, _WireInvProj;
            float4 _WireZ, _WireCamPos;
            #define WIRE_EYE _WireCamPos.xyz
            #include "WireLightShared.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            StructuredBuffer<float4> _WireTiledColor;   // rgb: Unity's _LightColor, w: shadow slot (-1 none)
            StructuredBuffer<uint> _WireTileLights;
            sampler2D _WireAtten;
            float4 _WireScreen; // width, height, tiles across, light count

            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; };

            // one triangle over the whole screen; where each pixel is on screen exactly as Unity's own
            // light pass has it (vert_deferred, UnityDeferredLibrary.cginc: ComputeScreenPos, which
            // turns by _ProjectionParams.x, -1 when Unity draws flipped): 0,0 the bottom left of the view
            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.pos = float4(p * 2 - 1, 0.5, 1);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float2 ndc = uv * 2 - 1; // y up, as the camera's projection has it
                float d = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0));
                #if defined(UNITY_REVERSED_Z)
                    bool sky = d <= 0;
                #else
                    bool sky = d >= 1;
                #endif
                float3 c = 0;
                uint2 px = (uint2)min(uv * _WireScreen.xy, _WireScreen.xy - 1); // the tile in the same y-up screen as the lights' boxes
                uint tile = ((px.y / 16) * (uint)_WireScreen.z + px.x / 16) * 64;
                uint n = sky ? 0 : _WireTileLights[tile];
                if (n > 0)
                {
                    // where this pixel is: along its ray to its eye depth, by the camera's own
                    // matrices (the mod's; Unity's built-in ones aren't the camera's at this point)
                    float4 v = mul(_WireInvProj, float4(ndc, 1, 1));
                    float3 ray = v.xyz / v.w;
                    ray /= -ray.z;
                    float3 vp = ray / (_WireZ.x * d + _WireZ.y);
                    float3 wpos = mul(_WireCamToWorld, float4(vp, 1)).xyz;
                    half4 g0 = tex2D(_CameraGBufferTexture0, uv);
                    half4 g1 = tex2D(_CameraGBufferTexture1, uv);
                    float3 normal = normalize(tex2D(_CameraGBufferTexture2, uv).rgb * 2 - 1);
                    [loop]
                    for (uint k = 0; k < n; k++)
                    {
                        uint l = _WireTileLights[tile + 1 + k];
                        float4 pr = _WireTiledPos[l];
                        float3 tolight = wpos - pr.xyz;
                        float att = dot(tolight, tolight) * pr.w;
                        if (att >= 1) continue; // out of its reach
                        float atten = tex2Dlod(_WireAtten, float4(att, att, 0, 0)).r;
                        if (atten <= 0) continue;
                        float4 lc = _WireTiledColor[l];
                        c += WireShade(g0, g1, normal, wpos, -normalize(tolight), atten, lc.rgb, (int)lc.w, pr.xyz);
                    }
                }
                #ifdef WIRE_LDR
                    return exp2(-float4(c, 0)); // the light buffer's own encoding, multiplied in
                #else
                    return float4(c, 0);
                #endif
            }
            ENDCG
        }
    }
    Fallback Off
}
