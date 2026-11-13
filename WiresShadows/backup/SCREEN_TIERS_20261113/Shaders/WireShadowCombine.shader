// Cached shadow maps, pass 3: stores the midpoint between the first surface
// facing the light and the back of that object. A piece's own relief (stones,
// plank edges) sits in front of its midpoint, so it doesn't shade itself; what
// is behind the piece is beyond it, so it still blocks. At most 35 cm behind
// the front (thick walls, one-sided surfaces); a back seen first (a plane from
// behind, or the light inside something) is used as it is.
Shader "Hidden/WireShadowCombine"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            Texture2D<float> _WireFront;
            Texture2D<float> _WireBack;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int3 px = int3(pos.xy, 0);
                float f = _WireFront.Load(px);
                float b = _WireBack.Load(px);
                // 35 cm: covers a wall piece's own relief (stones, plank edges)
                // when its back is further than that.
                float cap = 0.35;
                // A back seen first just past the near plane is an object
                // whose front the near plane cut off (a sconce's 0.8 m through
                // the stones beside it), not the light inside something: it
                // shaded the wall in shards. Walls there block via the colliders.
                // Hybrid: use the nearest actual surface rather than moving
                // the blocker into the mesh. Back-only near-plane wall proxies
                // and thin sheets must still block, even if a later front
                // surface is visible along the same ray. Don't apply the old
                // near-cut heuristic to legitimate back-facing surfaces.
                // Removed: the "back seen first just past the near cut" rule. _WireCombineCut
                // was never set (always 0), so it dropped every back face within 0.5 m of a
                // light, including real blockers whose front is inside the near plane.
                return b < f ? b : min(0.5 * (f + b), f + cap);
            }
            ENDCG
        }
        // Pass 1, moving maps: front (R) and back (G) from one min-blended
        // pass. Same 35 cm cap as the cached maps, so nothing shifts when a door hands over.
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            Texture2D<float2> _WireFrontBack;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                float2 fb = _WireFrontBack.Load(int3(pos.xy, 0));
                return fb.y < fb.x ? fb.y : min(0.5 * (fb.x + fb.y), fb.x + 0.35);
            }
            ENDCG
        }
        // Pass 2: empties the moving temp inside the scissor rect (a clear
        // would wipe the whole texture).
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float4 frag() : SV_Target { return float4(60000, 60000, 0, 0); }
            ENDCG
        }
        // Pass 3: pass 0's midpoint written straight into the native comparison depth (the same
        // conversion as WireShadowNativeDepthCopy pass 0), so no 16-bit colour copy is kept.
        Pass
        {
            ZTest Always ZWrite On Cull Off ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            Texture2D<float> _WireFront;
            Texture2D<float> _WireBack;
            float _WireNativeInvRange;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
                int3 px = int3(pos.xy, 0);
                float f = _WireFront.Load(px);
                float b = _WireBack.Load(px);
                float d = b < f ? b : min(0.5 * (f + b), f + 0.35);
                float depth01 = saturate(d * _WireNativeInvRange);
                #if defined(UNITY_REVERSED_Z)
                    return 1.0 - depth01;
                #else
                    return depth01;
                #endif
            }
            ENDCG
        }
    }
}
