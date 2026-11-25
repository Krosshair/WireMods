// Wire's Shadows: lays the contact shadows (WireContactShadows.compute) over the sun's own
// screen-space shadow mask, right after Unity makes it, so only direct sunlight is darkened, never
// the sky light or the point lights. They join the sun's shadow rather than stacking on it: each is
// only as dark as the sun's own shadows (Unity's mask goes down to 1 - the light's shadow strength,
// _LightShadowData.x), and the darker of the two is kept (BlendOp Min), so where an object already
// casts its normal shadow the contact shadow adds nothing; it only extends the shape.
Shader "Hidden/WireContactApply"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _WireSunShadowStrength; // the sun light's shadowStrength, set each frame

            fixed4 frag(v2f_img i) : SV_Target
            {
                return lerp(1, tex2D(_MainTex, i.uv).r, _WireSunShadowStrength);
            }
            ENDCG
        }
    }
    Fallback Off
}
