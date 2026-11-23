using System;
using WiresAcoustics;

namespace UnityEngine
{
    internal static class Mathf
    {
        internal static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        internal static float Max(float a, float b) => Math.Max(a, b);
        internal static float Sqrt(float x) => (float)Math.Sqrt(x);
        internal static float Lerp(float a,float b,float t) =>
            a + (b - a) * Clamp01(t);
    }
}

namespace WiresAcoustics
{
    internal static class AcousticMath
    {
        internal static float LogLerp(float a, float b, float t) =>
            (float)Math.Exp(Math.Log(Math.Max(80f,a)) +
                (Math.Log(Math.Max(80f,b)) - Math.Log(Math.Max(80f,a))) *
                UnityEngine.Mathf.Clamp01(t));
    }
}

internal static class Program
{
    static int n;
    static void Check(bool ok, string why)
    {
        if (!ok) throw new Exception("FAILED: " + why);
        n++;
    }
    static void Near(float got, float expected, float tolerance, string why) =>
        Check(Math.Abs(got - expected) <= tolerance,
            why + ": " + got + " rather than " + expected);
    static void Main()
    {
        // A single escaped secondary ray must not turn a one-ray seam
        // into a full-strength opening; three nearby rays must agree.
        Check(!WeatherAcoustics.VerifiedNarrowAperture(
            float.PositiveInfinity, 1.04f, 1f), "one clear offset is a seam");
        Check(!WeatherAcoustics.VerifiedNarrowAperture(
            2f, 1f, 1f), "blocked orthogonal offset is a seam");
        Check(!WeatherAcoustics.VerifiedNarrowAperture(
            1.07f, 1.08f, 1f), "slanted solid within 10cm tolerance");
        Check(WeatherAcoustics.VerifiedNarrowAperture(
            float.PositiveInfinity, float.PositiveInfinity, 1f),
            "genuine window through both offsets");
        Check(WeatherAcoustics.VerifiedNarrowAperture(
            1.12f, 1.13f, 1f), "consistent narrow opening behind wall plane");
        // The same non-rain opening geometry stays intact.
        Near(WeatherAcoustics.OpeningGain(0,0,1),0,.00001f,"no opening");
        Near(WeatherAcoustics.OpeningGain(.25f,0,1),.5f,.00001f,"diffuse aperture");
        Near(WeatherAcoustics.OpeningGain(.25f,2,1),1,.00001f,"near wide door");
        Near(WeatherAcoustics.OpeningGain(.25f,2,8),.5f,.00001f,"distant door");
        float prev = 0f;
        for(int cm=2000;cm>=0;cm--)
        {
            float d = cm * .01f;
            float x = WeatherAcoustics.OpeningGain(.25f,2f,d);
            Check(x >= prev - .00001f, "approach must not reduce sound");
            Check(x >= 0f && x <= 1f, "bounded aperture");
            prev = x;
        }
        foreach(string roof in new[]{
            "wood_roof(Clone)", "darkwood_roof_45",
            "piece_grausten_roof_45", "piece_ashwood_roof_26"})
            Check(WeatherAcoustics.IsRoofPieceName(roof),
                "authored roof recognized: "+roof);
        foreach(string notRoof in new[]{
            "wood_floor","stone_floor_2x2",
            "wood_wall_roof_45","ashwood_wall_roof_26","stone_wall"})
            Check(!WeatherAcoustics.IsRoofPieceName(notRoof),
                "floor or gable wall isn't exposed roof: "+notRoof);
        Check(!WeatherAcoustics.IsRoofPieceName(null),
            "null collider name is not an exposed roof");
        Near(WeatherAcoustics.RoofImpactWeight(0),1,.00001f,"near roof");
        Near(WeatherAcoustics.RoofImpactWeight(3),20.25f/29.25f,
            .00001f,"3m roof height");
        Near(WeatherAcoustics.RoofImpactWeight(6),.36f,.00001f,"distant roof");
        float last = 1f;
        for (int cm=1; cm<=1500; cm++)
        {
            float next=WeatherAcoustics.RoofImpactWeight(cm*.01f);
            Check(next<=last+.00001f, "roof weighting decreases with height");
            Check(last-next<.003f, "roof geometry has no abrupt transitions");
            last=next;
        }
        // Restoration of the old roofing sound *treatment* uses original
        // Valheim's existing recording; no new clip or emitter voices.
        Near(WeatherAcoustics.RoofRainPresence(0f, 1f),
            0f, .00001f, "no real roof never sounds like rain on a roof");
        Near(WeatherAcoustics.RoofRainPresence(1f, .8f),
            .8f, .00001f, "covered attic retains familiar roof presence");
        Near(WeatherAcoustics.RoofRainPresence(.25f, 0f),
            .1125f, .00001f, "partial open canopy should not make full roof sound");
        Near(WeatherAcoustics.RoofRainPresence(1f, 0f),
            .45f, .00001f, "outdoors below overhang still hears nearby roofing");
        float lastPresence=0f;
        for (int i=0;i<=1000;i++)
        {
            float coverage=i/1000f;
            float current=WeatherAcoustics.RoofRainPresence(coverage,.65f);
            Check(current >= lastPresence-.00001f,
                "greater roof coverage cannot reduce roof effect");
            Check(current>=0f && current<=1f, "roof presence is normalized");
            lastPresence=current;
        }
        float[] bases = {.05f,.2f,.55f,.75f,.95f,1f};
        foreach(float basis in bases)
        for(int i=0;i<=100;i++)
        {
            float mix=i/100f;
            WeatherAcoustics.BlendRoof(basis,800f,mix,
                out float mixedGain,out float mixedCut);
            Check(mixedGain + .00001f >= basis,
                "roof layer must never reduce audible outside rain");
            Check(mixedGain<=1.00001f,
                "roof layer may not exceed vanilla outdoor volume");
            Check(mixedCut>=799.9f && mixedCut<=22000f,
                "roof never darkens the previous environmental field");
            if(mix==0)
            {
                Near(mixedGain,basis,.00001f,"zero mix keeps original sound");
                Near(mixedCut,800f,.001f,"zero mix keeps original cutoff");
            }
            if(basis >= WeatherAcoustics.RainRoofGain)
                Near(mixedGain,basis,.00001f,
                    "already audible open courtyard must stay unchanged");
        }
        WeatherAcoustics.BlendRoof(.13f, 400f, 1f,
            out float roofGain, out float roofCut);
        Near(roofGain,.72f,.00001f,"legacy roof gain recovered");
        Near(roofCut,6500f,.01f,"legacy roof brightness recovered");

        // Numeric envelope: one bad 200ms survey should not abruptly
        // erase audible roof character. New roofs fade in faster than
        // departing roofs fade out. No hard on/off transitions.
        float attackTau=.42f, releaseTau=1.25f;
        float mixture=0f;
        for(int frame=0;frame<120;frame++)
            mixture += (1f-mixture) *
                (1f-(float)Math.Exp(-.016f/attackTau));
        Check(mixture>.98f,"roof fades into normal sound over a few seconds");
        float held=mixture;
        for(int frame=0;frame<13;frame++)
            mixture *= (float)Math.Exp(-.016f/releaseTau);
        Check(mixture>held*.8f,
            "one missed 200ms roof survey cannot abruptly mute impact texture");
        float previous=mixture;
        for(int frame=0;frame<300;frame++)
        {
            mixture *= (float)Math.Exp(-.016f/releaseTau);
            Check(mixture<=previous+.00001f && mixture>=0f,
                "roof release must be monotonic without level jumps");
            previous=mixture;
        }
        Check(mixture<.03f,"roof must fade away after leaving the building");

        Console.WriteLine("ENVIRONMENT ROOF GLIDE + EXISTING MATERIAL CLASSIFICATION PASS: "+n+
            " checks (one Valheim rain recording; no synthesized sounds)");
    }
}
