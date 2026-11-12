// Diagnostic native hardware-comparison shadow cache: converts the ALREADY
// combined WLO radial-distance face into a GPU comparison-depth cubemap face.
// Static caster collection, midpoint/near-plane treatment, and cache updates
// remain exactly as they were. Only a single chosen light gets this texture.
Shader "Hidden/WireShadowNativeDepthCopy"
{
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite On ColorMask 0
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float> _WireNativeDepthSource;
            float _WireNativeInvRange;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
                float distanceMeters = _WireNativeDepthSource.Load(int3(pos.xy, 0));
                // Empty map = far-depth = lit. No midpoint or bias alterations.
                float depth01 = saturate(distanceMeters * _WireNativeInvRange);
                #if defined(UNITY_REVERSED_Z)
                    return 1.0 - depth01;
                #else
                    return depth01;
                #endif
            }
            ENDCG
        }
            // Moving-map conversion. One fullscreen depth write per CHANGED
        // cubemap face, not once per frame or for unchanged shadow faces.
        Pass
        {
            Cull Off ZTest Always ZWrite On ColorMask 0
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float2> _WireNativeMovingSource;
            float _WireNativeInvRange;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
                float2 fb = _WireNativeMovingSource.Load(int3(pos.xy, 0)).rg;
                // Nearest surface, same rule as the cached maps.
                float distanceMeters = min(fb.x, fb.y);
                float normalized = saturate(distanceMeters * _WireNativeInvRange);
                #if defined(UNITY_REVERSED_Z)
                    return 1.0 - normalized;
                #else
                    return normalized;
                #endif
            }
            ENDCG
        }
    }
    Fallback Off
}

