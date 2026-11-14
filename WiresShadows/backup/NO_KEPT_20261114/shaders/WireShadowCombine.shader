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
        // Pass 4: as pass 3, front and back from the one-render face (WireShadowFrontBack:
        // red front, green back).
        Pass
        {
            ZTest Always ZWrite On Cull Off ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            Texture2D<float2> _WireFrontBack;
            float _WireNativeInvRange;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
                float2 fb = _WireFrontBack.Load(int3(pos.xy, 0));
                float d = fb.y < fb.x ? fb.y : min(0.5 * (fb.x + fb.y), fb.x + 0.35);
                float depth01 = saturate(d * _WireNativeInvRange);
                #if defined(UNITY_REVERSED_Z)
                    return 1.0 - depth01;
                #else
                    return depth01;
                #endif
            }
            ENDCG
        }
        // Pass 5: a kept map made small (WiresShadows, Keep). One face of a smaller pool's cube from
        // the same face of a bigger pool's cube: each texel the NEAREST depth of the block of texels
        // it covers (_WireKeepStep across: 8, 4 or 2), so nothing that cast a shadow is lost, only
        // its outline gets coarser. Raw pool depth in and out (distance over range, reversed where
        // the platform reverses depth), read by direction as the lighting reads the cube.
        Pass
        {
            ZTest Always ZWrite On Cull Off ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            TextureCubeArray<float> _WireKeepSrc;
            SamplerState sampler_point_clamp;
            float _WireKeepSlice, _WireKeepFace, _WireKeepStep, _WireKeepSize;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            // The direction of a point on a cube face (s across, t down, both -1..1), as the GPU
            // lays a cube's faces out (+X, -X, +Y, -Y, +Z, -Z).
            float3 FaceDir(int f, float2 st)
            {
                float s = st.x, t = st.y;
                if (f == 0) return float3(1, -t, -s);
                if (f == 1) return float3(-1, -t, s);
                if (f == 2) return float3(s, 1, t);
                if (f == 3) return float3(s, -1, -t);
                if (f == 4) return float3(s, -t, 1);
                return float3(-s, -t, -1);
            }
            float frag(float4 pos : SV_POSITION) : SV_Depth
            {
                int step = (int)(_WireKeepStep + 0.5);
                int face = (int)(_WireKeepFace + 0.5);
                float big = _WireKeepSize * step;
                float nearest = 1;
                for (int y = 0; y < 8; y++)
                {
                    if (y >= step) break;
                    for (int x = 0; x < 8; x++)
                    {
                        if (x >= step) break;
                        float2 texel = floor(pos.xy) * step + float2(x, y) + 0.5;
                        float raw = _WireKeepSrc.SampleLevel(sampler_point_clamp, float4(FaceDir(face, texel / big * 2 - 1), _WireKeepSlice), 0);
                        #if defined(UNITY_REVERSED_Z)
                            raw = 1.0 - raw;
                        #endif
                        nearest = min(nearest, raw);
                    }
                }
                #if defined(UNITY_REVERSED_Z)
                    return 1.0 - nearest;
                #else
                    return nearest;
                #endif
            }
            ENDCG
        }
    }
}
