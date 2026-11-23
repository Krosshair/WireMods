using System;
using WiresAcoustics;
namespace UnityEngine {
    internal class AudioClip {internal string name;internal AudioClip(string name){this.name=name;}}
    internal static class Mathf {
        internal static float Clamp01(float v)=>Math.Clamp(v,0f,1f);
        internal static float Max(float a,float b)=>Math.Max(a,b);
        internal static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    }
}
namespace WiresAcoustics {
    internal static class AcousticMath {
        internal static float LogLerp(float a,float b,float t)=>
            (float)Math.Exp(Math.Log(a)+(Math.Log(b)-Math.Log(a))*t);
    }
}
internal static class Program {
    static int n;
    static void Check(bool ok,string why) {
        if(!ok)throw new Exception("FAILED "+why);
        n++;
    }
    static void Near(float actual,float expected,float epsilon,string what) =>
       Check(Math.Abs(actual-expected)<=epsilon,$"{what}: actual {actual} expected {expected}");
    static void Main() {
        var rain=new UnityEngine.AudioClip("Rain");
        var copy=new UnityEngine.AudioClip("Rain");
        var birds=new UnityEngine.AudioClip("Birds");
        Check(WeatherAcoustics.IsActiveRainLoop(true,rain,rain),"exact active weather clip");
        Check(!WeatherAcoustics.IsActiveRainLoop(false,rain,rain),"dry weather");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,rain,birds),"mismatched weather clip");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,rain,copy),"same name is not clip identity");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,null,rain),"no weather clip");
        Check(!WeatherAcoustics.IsActiveRainLoop(true,rain,null),"no playing clip");
        WeatherAcoustics.Blend(.08f,500f,1f,1f,1f,out float gain,out float cut);
        Near(gain,.72f,.00001f,"roof rain remains clearly audible");
        Near(cut,6500f,.01f,"roof rain retains its spectral texture");
        WeatherAcoustics.Blend(.08f,500f,1f,0f,1f,out gain,out cut);
        Near(gain,.08f,.00001f,"natural cave retains genuine shielding");
        Near(cut,500f,.001f,"no synthetic cave roof");
        WeatherAcoustics.Blend(.08f,500f,0f,1f,1f,out gain,out cut);
        Near(gain,.08f,.00001f,"dry weather unchanged");
        Near(cut,500f,.001f,"dry lowpass unchanged");
        WeatherAcoustics.Blend(.08f,500f,1f,.15f,.35f,out gain,out cut);
        Check(gain>.08f && gain<.72f,"partial shelter perceptually intermediate");
        Check(cut>500f && cut<6500f,"partial shelter spectrally intermediate");
        float previous=.08f;
        for(int i=0;i<=1000;i++){
            float roof=i/1000f;
            WeatherAcoustics.Blend(.08f,500f,1f,roof,1f,out gain,out cut);
            Check(gain>=previous-.00001f,"no decreasing rain presence across increasing roof");
            Check(Math.Abs(gain-previous)<.002f,"rain roof interpolation has no sudden jumps");
            previous=gain;
        }
        Console.WriteLine($"WEATHER SOURCE + ROOF ACOUSTICS PASS: {n} source-linked checks");
    }
}