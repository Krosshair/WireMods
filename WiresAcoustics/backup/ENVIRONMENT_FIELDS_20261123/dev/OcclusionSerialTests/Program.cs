using System;
using System.Diagnostics;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {}
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Clamp(float a,float lo,float hi)=>Math.Clamp(a,lo,hi);
  public static float Clamp01(float a)=>Math.Clamp(a,0f,1f);
  public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);
  public static float Exp(float a)=>(float)Math.Exp(a);
  public static float Log(float a)=>(float)Math.Log(a);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
  public static float LerpUnclamped(float a,float b,float t)=>a+(b-a)*t;
 }
}
namespace WiresAcoustics {
 internal static class AcousticMath {
  internal const float FilterStage=1.5538f;
  internal static float OcclusionStrength=1f;
  internal static float LogLerp(float from,float to,float t)=>
   (float)Math.Exp(Math.Log(from)+(Math.Log(to)-Math.Log(from))*t);
 }
 internal static class Solids {
  internal sealed class Info {internal bool Wall,Ground=false;internal float First,Cut;}
 }
 internal static class Occlusion {
  private static readonly float[] Gain={1f,.6f,.4f,.28f};
  private static readonly float[] Cutoff={22000f,1500f,800f,500f};
  internal const float SameWall=.30f;
  private const float NextWall=.5f;
  internal struct Block {
   internal float Soft,Through,ThroughHigh,ThroughCut,BendGain,BendHigh,BendCut,LastWall;
   internal int Walls;
   internal bool Terrain;
   internal static Block Clear => new Block {
    Through=1f,ThroughHigh=1f,ThroughCut=22000f,BendGain=1f,BendHigh=1f,BendCut=22000f,LastWall=-10f
   };
  }
internal static bool Layer(Solids.Info s, float w, Vector3 from, Vector3 to, float at, ref Block b)
        {
            if (s.Wall)
            {
                if (at - b.LastWall < SameWall) return false;
                b.LastWall = at;
                b.Walls++;
                b.Through *= s.First;
                // Serial wall transfers multiply at 2 kHz. Keep the
                // high-band energy, NOT an approximate cutoff per layer.
                float layerCut = s.Cut;
                b.ThroughHigh *= SpectralPaths.Response(layerCut);
                return true;
            }
            if (s.Ground && !b.Terrain) { b.Terrain = true; b.Soft += w; } // undone by Ridge
            return true;
        }
internal static void Muffle(float occ, out float gain, out float cutoff)
        {
            float s = AcousticMath.OcclusionStrength;
            float x = Mathf.Clamp(occ, 0f, Gain.Length - 1);
            int i = Mathf.Min((int)x, Gain.Length - 2);
            float t = x - i;
            float g = Mathf.Lerp(Gain[i], Gain[i + 1], t);
            float lc = Mathf.Lerp(Mathf.Log(Cutoff[i]), Mathf.Log(Cutoff[i + 1]), t);
            gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, g, s), .05f, 1f);
            cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), lc, s)), 200f, 22000f);
        }
internal static void Wall(in Block b, out float gain, out float cutoff)
        {
            Muffle(b.Soft, out gain, out cutoff);
            float s = AcousticMath.OcclusionStrength;
            if (s <= 0f || (b.Through >= 1f && b.BendGain >= 1f)) return;
            // Geometry sampling accumulates the low-frequency amplitude
            // and 2-kHz energy independently. Applying strength as a
            // logarithmic attenuation exponent scales BOTH bands
            // consistently. Fit to the actual DSP ONCE per sound route.
            float dc = gain * gain;
            float high = SpectralPaths.HighEnergy(gain, cutoff);
            if (b.Through < 1f)
            {
                float g = Mathf.Pow(b.Through, s);
                float energy = g * g;
                dc *= energy;
                high *= energy * Mathf.Pow(b.ThroughHigh, s);
            }
            if (b.BendGain < 1f)
            {
                float g = Mathf.Pow(b.BendGain, s);
                float energy = g * g;
                dc *= energy;
                high *= energy * Mathf.Pow(b.BendHigh, s);
            }
            SpectralPaths.Decode(dc, high, out gain, out cutoff);
        }
internal static void WallLegacy(in Block b, out float gain, out float cutoff)
        {
            Muffle(b.Soft, out gain, out cutoff);
            float s = AcousticMath.OcclusionStrength;
            if (s <= 0f) return;
            float k = Mathf.Min(1f, s);
            if (b.Through < 1f)
            {
                gain *= Mathf.Pow(b.Through, s);
                cutoff = Mathf.Min(cutoff, AcousticMath.LogLerp(22000f, b.ThroughCut, k));
            }
            if (b.BendGain < 1f)
            {
                gain *= Mathf.Pow(b.BendGain, s);
                cutoff = Mathf.Min(cutoff, AcousticMath.LogLerp(22000f, b.BendCut, k));
            }
        }
 }
 internal static class Test {
  static void Must(bool v,string why){if(!v)throw new Exception("FAIL: "+why);}
  static (float gain,float cut) Evaluate(Occlusion.Block b) {
   Occlusion.Wall(b,out float gain,out float cut);
   return(gain,cut);
  }
  static void Main(){
   int cases=0;
   foreach(int sampleRate in new[]{44100,48000,96000,192000}) {
   SpectralPaths.SetSampleRate(sampleRate);
   foreach(float strength in new[]{.5f,1f,1.5f}) {
   AcousticMath.OcclusionStrength=strength;
   var empty=Occlusion.Block.Clear;
   var transparent=Evaluate(empty);
   Must(Math.Abs(transparent.gain-1)<.0001&&transparent.cut>=20000,"clear space should be unfiltered");
   foreach(float soft in new[]{0f,.5f,1f,2f,3f})
   foreach(float g in new[]{.02f,.1f,.4f,.8f,1f})
   foreach(float cut in new[]{200f,550f,1500f,8000f,22000f})
   foreach(float bend in new[]{.04f,.2f,.6f,1f}) {
    var b=Occlusion.Block.Clear;
    b.Soft=soft; b.Through=g;b.ThroughCut=cut;b.ThroughHigh=SpectralPaths.Response(cut);b.BendGain=bend;b.BendCut=900f;b.BendHigh=SpectralPaths.Response(900f);
    var actual=Evaluate(b);
    Occlusion.Muffle(soft,out float ag,out float ac);
    double dc=ag*ag,hi=SpectralPaths.HighEnergy(ag,ac);
    if(g<1) {
     double amp=Math.Pow(g,strength);
     dc*=amp*amp;hi*=amp*amp*Math.Pow(SpectralPaths.Response(cut),strength);
    }
    if(bend<1) {
     double amp=Math.Pow(bend,strength);
     dc*=amp*amp;hi*=amp*amp*Math.Pow(SpectralPaths.Response(900),strength);
    }
    Must(Math.Abs(actual.gain*actual.gain-dc)<.0001,"direct wall DC");
    Must(Math.Abs(SpectralPaths.HighEnergy(actual.gain,actual.cut)-hi)<.00015,"direct wall high band");
    cases++;
   }
   foreach(int layers in new[]{1,2,3,4,6}){
    var b=Occlusion.Block.Clear;
    for(int i=0;i<layers;i++) {
     var s=new Solids.Info{Wall=true,First=.15f,Cut=551.7f};
     Must(Occlusion.Layer(s,1f,new(),new(),(i+1)*.7f,ref b),"wall was not counted");
    }
    Must(b.Walls==layers,"wrong wall number");
    var result=Evaluate(b);
    double dc=Math.Pow(.15f,2*strength*layers);
    double hi=dc*Math.Pow(SpectralPaths.Response(551.7f),strength*layers);
    Must(Math.Abs(result.gain*result.gain-dc)<.0001,"serial walls DC count "+layers);
    Must(Math.Abs(SpectralPaths.HighEnergy(result.gain,result.cut)-hi)<.0001,"serial walls HF count "+layers);
    cases++;
   }
   }
   }
   AcousticMath.OcclusionStrength=0f;
   var off=Occlusion.Block.Clear;off.Soft=2f;off.Through=.1f;off.ThroughCut=300;off.ThroughHigh=SpectralPaths.Response(300);off.BendGain=.2f;off.BendCut=600;off.BendHigh=SpectralPaths.Response(600);
   var original=Evaluate(off);
   Must(original.gain==1f && original.cut>=20000,"disabled occlusion must be identity");
   Console.WriteLine($"OCCLUSION PRODUCTION SOURCE PASS: {cases} spectral wall/obstacle cases plus disabled/transparent");
   AcousticMath.OcclusionStrength=1f;
   SpectralPaths.SetSampleRate(48000);
   var perf=Occlusion.Block.Clear;
   perf.Soft=1f;perf.Through=.16f;perf.ThroughCut=500;perf.ThroughHigh=SpectralPaths.Response(500);perf.BendGain=.7f;perf.BendCut=1200f;perf.BendHigh=SpectralPaths.Response(1200f);
   const int iterations=100000;
   // Cold JIT/loop warm-up, then same workload with old/new implementations.
   for(int i=0;i<10000;i++) {Occlusion.Wall(perf,out _,out _);Occlusion.WallLegacy(perf,out _,out _);}
   double oldMs=0,newMs=0;
   for(int repeat=0;repeat<3;repeat++) {
    var sw=Stopwatch.StartNew();
    for(int i=0;i<iterations;i++)Occlusion.WallLegacy(perf,out _,out _);
    sw.Stop();oldMs+=sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    for(int i=0;i<iterations;i++)Occlusion.Wall(perf,out _,out _);
    sw.Stop();newMs+=sw.Elapsed.TotalMilliseconds;
   }
   Console.WriteLine($"SYNTHETIC WALL MATH COST: old={oldMs/3:F3} ms per 100k, new={newMs/3:F3} ms per 100k, ratio={newMs/oldMs:F2}x (NOT a Unity profiler result)");
  }
 }
}