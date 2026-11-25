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
                // Identical midpoint/near-cap rule to WLO's moving shader.
                float distanceMeters = fb.y < fb.x ? fb.y : min(0.5 * (fb.x + fb.y), fb.x + 0.35);
                float normalized = saturate(distanceMeters * _WireNativeInvRange);
                #if defined(UNITY_REVERSED_Z)
                    return 1.0 - normalized;
                #else
                    return normalized;
                #endif
            }
            ENDCG
        }
        // The same conversion, reading the drawn face straight from its cube array: no copy of it
        // to a flat texture first. Each pixel reads its own texel (point sampling at the texel's
        // centre, D3D's cube face layout), so the result is the same as pass 1's.
        Pass
        {
            Cull Off ZTest Always ZWrite On ColorMask 0
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            TextureCubeArray<float2> _WireNativeMovingCube;
            SamplerState sampler_point_clamp;
            float _WireNativeInvRange;
            float _WireMovingFace, _WireMovingCubeIndex, _WireMovingRes;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
                float2 st = pos.xy / _WireMovingRes * 2 - 1; // texel centre, -1..1, row 0 at the top
                float sc = st.x, tc = st.y;
                int f = (int)_WireMovingFace;
                float3 d = f == 0 ? float3( 1, -tc, -sc)
                         : f == 1 ? float3(-1, -tc,  sc)
                         : f == 2 ? float3(sc,  1,  tc)
                         : f == 3 ? float3(sc, -1, -tc)
                         : f == 4 ? float3(sc, -tc,  1)
                         :          float3(-sc, -tc, -1);
                float2 fb = _WireNativeMovingCube.SampleLevel(sampler_point_clamp, float4(d, _WireMovingCubeIndex), 0).rg;
                // Identical midpoint/near-cap rule to pass 1.
                float distanceMeters = fb.y < fb.x ? fb.y : min(0.5 * (fb.x + fb.y), fb.x + 0.35);
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

