using System;
using WiresAcoustics;

namespace UnityEngine
{
    internal static class Mathf
    {
        internal static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        internal static float Max(float a, float b) => Math.Max(a, b);
        internal static float Sqrt(float x) => (float)Math.Sqrt(x);
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
        Console.WriteLine("ENVIRONMENT APERTURE + SURFACE CLASSIFICATION PASS: "+n+
            " tests (removed obsolete rain-only mixing equations)");
    }
}
