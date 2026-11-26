// Wire's Camera Tweaks lens: depth of field and barrel distortion.
// Blur is measured in diopters (1/metres) like a real lens, so looking far
// blurs nothing and only things very close to the camera get near blur.
// The gather blur is Dennis Gustafsson's single-pass bokeh.
Shader "Hidden/WireLens"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    #define GOLDEN_ANGLE 2.39996323

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    sampler2D _WireFocus;
    sampler2D _WireBlur;

    float _WireExclude;   // metres: depth closer than this is the player, not a focus target
    float _WireKIn;       // blend toward a closer focus this frame
    float _WireKOut;      // blend toward a further focus this frame
    float _WireNearLimit; // diopters: near blur only in front of the player
    float _WireNearOn;
    float _WireFarCap;    // diopters: far focus plane never closer than this
    float _WireCocScale;  // half-res pixels of blur per diopter
    float _WireMaxRadius; // half-res pixels
    float _WireDofOn;
    float _WireBarrel;

    float EyeDepth(float2 uv)
    {
        return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0)));
    }

    // Average diopters in a golden-angle disc around the screen centre.
    float AverageDiopter(float size)
    {
        float sum = 0;
        float count = 0;
        float ang = 0;
        [loop] for (float radius = 0; radius < size; radius += 0.005)
        {
            float2 tc = float2(0.5, 0.5) + float2(cos(ang), sin(ang)) * radius;
            float d = EyeDepth(tc);
            if (d > _WireExclude)
            {
                sum += 1.0 / d;
                count += 1;
            }
            ang += GOLDEN_ANGLE;
        }
        return count > 0 ? sum / count : 0;
    }

    float4 FragFocus(v2f_img i) : SV_Target
    {
        float a0 = AverageDiopter(0.05);
        float a1 = AverageDiopter(0.15);
        float2 target = float2(max(a0, a1), min(a0, a1)); // near plane, far plane
        float2 old = tex2D(_MainTex, float2(0.5, 0.5)).rg;
        // More diopters means focus comes closer and blur grows: that side is slower.
        float2 k = float2(target.x > old.x ? _WireKIn : _WireKOut,
                          target.y > old.y ? _WireKIn : _WireKOut);
        return float4(lerp(old, target, k), 0, 1);
    }

    float4 FragPrepare(v2f_img i) : SV_Target
    {
        float3 col = tex2D(_MainTex, i.uv).rgb;
        float inv = 1.0 / EyeDepth(i.uv);
        float2 f = tex2D(_WireFocus, float2(0.5, 0.5)).rg;
        float nearPlane = max(f.x, _WireNearLimit);
        float farPlane = min(f.y, _WireFarCap);
        float cocNear = min(0, nearPlane - inv) * _WireNearOn;
        float cocFar = max(0, farPlane - inv);
        float coc = clamp((cocNear + cocFar) * _WireCocScale, -_WireMaxRadius, _WireMaxRadius);
        return float4(col, coc);
    }

    float4 FragBlur(v2f_img i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float centerSize = abs(c.a);
        float3 col = c.rgb;
        float tot = 1;
        float fg = 0;
        float radius = 0.5;
        float ang = 0;
        [loop] for (; radius < _WireMaxRadius; ang += GOLDEN_ANGLE)
        {
            float2 tc = i.uv + float2(cos(ang), sin(ang)) * _MainTex_TexelSize.xy * radius;
            float4 s = tex2Dlod(_MainTex, float4(tc, 0, 0));
            float size = abs(s.a);
            // Signed blur grows with distance, so a larger value is further away.
            // Something behind the centre can't spill over it much.
            if (s.a > c.a) size = min(size, centerSize * 2);
            float m = smoothstep(radius - 0.5, radius + 0.5, size);
            col += lerp(col / tot, s.rgb, m);
            tot += 1;
            if (s.a < c.a) fg = max(fg, m);
            radius += 0.5 / radius;
        }
        return float4(col / tot, max(saturate(centerSize - 0.5), fg));
    }

    float4 FragFinal(v2f_img i) : SV_Target
    {
        // Barrel distortion (Teardown's curve), measured in half-widths.
        float aspect = _MainTex_TexelSize.x / _MainTex_TexelSize.y;
        float2 p = (2.0 * i.uv - 1.0) * float2(1.0, aspect);
        float r = length(p);
        float r2 = r * (0.94 + 0.04 * r * r);
        float2 barrel = r > 0 ? normalize(p) * r2 / float2(1.0, aspect) : 0;
        float2 tc = lerp(i.uv, (barrel + 1.0) * 0.5, _WireBarrel);

        float4 col = tex2D(_MainTex, tc);
        if (_WireDofOn > 0.5)
        {
            float4 b = tex2D(_WireBlur, tc);
            col.rgb = lerp(col.rgb, b.rgb, b.a);
        }
        return col;
    }
    ENDCG

    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragFocus
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragPrepare
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBlur
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragFinal
            #pragma target 3.0
            ENDCG }
    }
}
