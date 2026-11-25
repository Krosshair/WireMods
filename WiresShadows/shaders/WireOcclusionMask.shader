// Wire's Shadows: level 0 of the occlusion depth pyramid (Occlusion.cs, WireOcclusion.compute).
// Pass 1: the camera's depth, as both the farthest and the nearest value.
// Pass 0: a moving thing (character, ship, cart) painted out as "nothing here": it can move away
// before the answer is used, so it never counts as hiding what's behind it.
Shader "Hidden/WireOcclusionMask"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _WireFar; // the depth value at the far end (0 with reversed depth)

            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float2 frag() : SV_Target { return float2(_WireFar, _WireFar); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float> _CameraDepthTexture;
            float4 _CameraDepthTexture_TexelSize;

            float _WireReversed;

            // A quarter of the screen's size: each texel the farthest and the nearest of the depth
            // texels round it (a 4x4 block, one more each side as the blocks don't line up exactly).
            // Read texel for texel: a blend of a near and a far depth would be neither.
            float2 frag(v2f_img i) : SV_Target
            {
                int2 size = (int2)_CameraDepthTexture_TexelSize.zw;
                int2 c = (int2)(i.uv * size);
                float far = _WireReversed > 0.5 ? 1 : 0, near = 1 - far;
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                    {
                        int2 p = clamp(c + int2(x, y), 0, size - 1);
                        float d = _CameraDepthTexture.Load(int3(p, 0));
                        far = _WireReversed > 0.5 ? min(far, d) : max(far, d);
                        near = _WireReversed > 0.5 ? max(near, d) : min(near, d);
                    }
                return float2(far, near);
            }
            ENDCG
        }
    }
}
