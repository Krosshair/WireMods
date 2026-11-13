// Only for active animated fabric inside WLO's EXISTING moving cubemap.
// Valheim's original vegetation ShadowCaster renders into a reused depth RT;
// this pass converts those depth pixels to WLO's light-centred radial distance.
// Both channels get the SAME front distance because fabric is a thin sheet.
// It never modifies the light's main shader, the static map, or any other face.
Shader "Hidden/WireFabricDepthMerge"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float> _WireFabricDepth;
            float _WireFabricSize;
            float _WireFabricNear;
            float _WireFabricFar;
            struct Screen { float4 position : SV_POSITION; };
            Screen vert(uint id : SV_VertexID)
            {
                Screen o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.position = float4(p * 2 - 1, 0, 1);
                return o;
            }
            float4 frag(Screen i) : SV_Target
            {
                int2 pixel = int2(i.position.xy);
                float raw = _WireFabricDepth.Load(int3(pixel, 0));
                // Empty depth samples must not shade anything. Discard them
                // rather than sending a sentinel through the radial conversion.
                #if defined(UNITY_REVERSED_Z)
                    clip(raw - 0.00001);
                    float z = _WireFabricNear * _WireFabricFar /
                        max(0.00001, _WireFabricNear + raw * (_WireFabricFar - _WireFabricNear));
                #else
                    clip(0.99999 - raw);
                    float z = _WireFabricNear * _WireFabricFar /
                        max(0.00001, _WireFabricFar - raw * (_WireFabricFar - _WireFabricNear));
                #endif
                float2 xy = ((float2(pixel) + 0.5) / _WireFabricSize) * 2 - 1;
                float radius = z * sqrt(1 + dot(xy, xy));
                clip(_WireFabricFar * 1.733 - radius);
                // Identical values: for cloth, no 35-cm midpoint bias.
                return float4(radius, radius, 0, 0);
            }
            ENDHLSL
        }
        // Clear just the affected scissor rectangle of the reused depth
        // surface. Full 2048x2048 clears per banner/light face are wasted.
        Pass
        {
            Name "ClearFabricDepth"
            ColorMask 0
            Cull Off ZTest Always ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex clearVert
            #pragma fragment clearFrag
            float4 clearVert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float clearFrag() : SV_Depth
            {
                #if defined(UNITY_REVERSED_Z)
                    return 0;
                #else
                    return 1;
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
