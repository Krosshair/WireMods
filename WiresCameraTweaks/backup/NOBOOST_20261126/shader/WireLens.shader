// Wire's Camera Tweaks lens: depth of field.
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
    float _WireBokeh;     // highlight lift, 0 = none
    float _WireHdrHold;   // per-frame keep factor for the HDR flag
    sampler2D _WireSource;
    float _WireRadStep;   // gather spacing, grows with max radius to keep the sample count down

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
        // HDR seen: any of a 6x6 grid above 1. Held for a while so a dark scene
        // under an HDR tonemapper doesn't switch the lift back on.
        float peak = 0;
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                float3 c = tex2Dlod(_WireSource, float4((x + 0.5) / 6, (y + 0.5) / 6, 0, 0)).rgb;
                peak = max(peak, max(c.r, max(c.g, c.b)));
            }
        float hdr = max(tex2D(_MainTex, float2(0.5, 0.5)).b * _WireHdrHold, peak > 1.05 ? 1 : 0);
        return float4(lerp(old, target, k), hdr, 1);
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
        // Bokeh: the picture is already tonemapped, so bright spots are capped
        // near white. Lift them where they'll be blurred so they spread into
        // bright discs instead of a grey smear.
        float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
        float highlight = saturate((lum - 0.7) / 0.3);
        // With an HDR tonemapper (RenoDX) highlights already go above 1 and
        // make their own bokeh, so the lift is only for a clamped picture.
        float hdr = tex2D(_WireFocus, float2(0.5, 0.5)).b;
        col *= 1 + _WireBokeh * 4 * highlight * highlight * saturate((abs(coc) - 1) / 3) * (1 - hdr);
        return float4(col, coc);
    }

    float4 FragBlur(v2f_img i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float centerSize = abs(c.a);
        float3 col = c.rgb;
        float tot = 1;
        float fg = 0;
        float radius = _WireRadStep;
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
            radius += _WireRadStep / radius;
        }
        return float4(col / tot, max(saturate(centerSize - 0.5), fg));
    }

    float4 FragFinal(v2f_img i) : SV_Target
    {
        float4 col = tex2D(_MainTex, i.uv);
        float4 b = tex2D(_WireBlur, i.uv);
        col.rgb = lerp(col.rgb, b.rgb, b.a);
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
