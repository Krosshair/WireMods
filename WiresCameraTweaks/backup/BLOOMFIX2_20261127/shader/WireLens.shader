// Wire's Camera Tweaks lens: depth of field.
// Focus and blur size work like a real lens (diopters, after Teardown):
// looking far blurs nothing, only things very close to the camera get near blur.
// The blur follows Jimenez's Call of Duty: Advanced Warfare talk the way
// Unity's Post Processing v2 does it: half-res prefilter, a ring kernel that
// keeps foreground and background apart, a tent postfilter, full-res combine.
Shader "Hidden/WireLens"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    #define GOLDEN_ANGLE 2.39996323
    #define RINGS 6
    #define RING_STEP 7

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
    sampler2D _WireExposure;
    float4 _WireBloomCurve; // x threshold - knee, y knee * 2, z 0.25 / knee, w threshold

    float EyeDepth(float2 uv)
    {
        return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0)));
    }

    // Signed blur radius in half-res pixels: negative in front of focus, positive behind.
    float Coc(float2 uv, float2 focus)
    {
        float inv = 1.0 / EyeDepth(uv);
        float nearPlane = max(focus.x, _WireNearLimit);
        float farPlane = min(focus.y, _WireFarCap);
        float cocNear = min(0, nearPlane - inv) * _WireNearOn;
        float cocFar = max(0, farPlane - inv);
        return clamp((cocNear + cocFar) * _WireCocScale, -_WireMaxRadius, _WireMaxRadius);
    }

    float2 Focus()
    {
        return tex2Dlod(_WireFocus, float4(0.5, 0.5, 0, 0)).rg;
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
        // More diopters means focus comes closer and blur grows.
        float2 k = float2(target.x > old.x ? _WireKIn : _WireKOut,
                          target.y > old.y ? _WireKIn : _WireKOut);
        return float4(lerp(old, target, k), 0, 1);
    }

    // Full-res source down to half res. Keeps the strongest blur of the four,
    // and fades out focused pixels so they don't bleed into the blur.
    float4 FragPrefilter(v2f_img i) : SV_Target
    {
        float2 focus = Focus();
        float3 d = _MainTex_TexelSize.xyx * float3(0.5, 0.5, -0.5);
        float2 uv0 = i.uv - d.xy;
        float2 uv1 = i.uv - d.zy;
        float2 uv2 = i.uv + d.zy;
        float2 uv3 = i.uv + d.xy;
        float3 col = (tex2D(_MainTex, uv0).rgb + tex2D(_MainTex, uv1).rgb +
                      tex2D(_MainTex, uv2).rgb + tex2D(_MainTex, uv3).rgb) * 0.25;
        float c0 = Coc(uv0, focus);
        float c1 = Coc(uv1, focus);
        float c2 = Coc(uv2, focus);
        float c3 = Coc(uv3, focus);
        float cocMin = min(min(c0, c1), min(c2, c3));
        float cocMax = max(max(c0, c1), max(c2, c3));
        float coc = -cocMin > cocMax ? cocMin : cocMax;
        col *= smoothstep(0, 2, abs(coc));
        return float4(col, coc);
    }

    // Ring kernel over the whole max radius. Background only takes samples
    // whose blur reaches this pixel and isn't larger than the centre's, so it
    // never spills over a sharper thing; foreground spreads by its own size.
    float4 FragBlur(v2f_img i) : SV_Target
    {
        float4 center = tex2D(_MainTex, i.uv);
        float4 bg = 0;
        float4 fg = 0;
        const float margin = 2;
        float count = 0;
        [unroll] for (int ring = 0; ring <= RINGS; ring++)
        {
            int n = ring == 0 ? 1 : ring * RING_STEP;
            float r = (float)ring / RINGS * _WireMaxRadius;
            [loop] for (int j = 0; j < n; j++)
            {
                float ang = 6.28318530 * (j + 0.5 * ring) / n;
                float2 disp = float2(cos(ang), sin(ang)) * r;
                float4 s = tex2Dlod(_MainTex, float4(i.uv + disp * _MainTex_TexelSize.xy, 0, 0));
                float bgCoc = max(min(center.a, s.a), 0);
                float bw = saturate((bgCoc - r + margin) / margin);
                float fw = saturate((-s.a - r + margin) / margin) * step(1, -s.a);
                bg += float4(s.rgb, 1) * bw;
                fg += float4(s.rgb, 1) * fw;
                count += 1;
            }
        }
        bg.rgb /= bg.a + (bg.a == 0);
        fg.rgb /= fg.a + (fg.a == 0);
        float alpha = saturate(fg.a * UNITY_PI / count);
        return float4(lerp(bg.rgb, fg.rgb, alpha), alpha);
    }

    // Small tent filter to close the gaps between kernel samples.
    float4 FragPostfilter(v2f_img i) : SV_Target
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(0.5, 0.5, -0.5, 0);
        return (tex2D(_MainTex, i.uv - d.xy) + tex2D(_MainTex, i.uv - d.zy) +
                tex2D(_MainTex, i.uv + d.zy) + tex2D(_MainTex, i.uv + d.xy)) * 0.25;
    }

    float4 FragCombine(v2f_img i) : SV_Target
    {
        float4 col = tex2D(_MainTex, i.uv);
        float4 dof = tex2D(_WireBlur, i.uv);
        float coc = Coc(i.uv, Focus()) * 2; // full-res pixels
        float far = smoothstep(2, 4, coc);
        col.rgb = lerp(col.rgb, dof.rgb, far + dof.a - far * dof.a);
        return col;
    }
    // Bloom: Valheim's bloom prefilter (auto exposure, threshold with soft knee),
    // from full res to half. Thresholding before any further downsampling keeps
    // small flames from being averaged away.
    float4 FragBloomPrefilter(v2f_img i) : SV_Target
    {
        float3 d = _MainTex_TexelSize.xyx * float3(0.5, 0.5, -0.5);
        float3 c = (tex2D(_MainTex, i.uv - d.xy).rgb + tex2D(_MainTex, i.uv - d.zy).rgb +
                    tex2D(_MainTex, i.uv + d.zy).rgb + tex2D(_MainTex, i.uv + d.xy).rgb) * 0.25;
        c *= tex2D(_WireExposure, float2(0.5, 0.5)).r;
        float br = max(c.r, max(c.g, c.b));
        float rq = clamp(br - _WireBloomCurve.x, 0, _WireBloomCurve.y);
        rq = _WireBloomCurve.z * rq * rq;
        c *= max(rq, br - _WireBloomCurve.w) / max(br, 1e-5);
        return float4(c, 1);
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
            #pragma fragment FragPrefilter
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBlur
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragPostfilter
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragCombine
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBloomPrefilter
            #pragma target 3.0
            ENDCG }
    }
}
