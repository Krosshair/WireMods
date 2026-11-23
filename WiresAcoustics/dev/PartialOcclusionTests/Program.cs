using System;
using System.IO;
using WiresAcoustics;
namespace UnityEngine {
 internal static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Clamp(float a,float lo,float hi)=>Math.Clamp(a,lo,hi);
  public static float Clamp01(float a)=>Math.Clamp(a,0f,1f);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);
 }
}
namespace WiresAcoustics {
 internal static class AcousticMath {internal const float FilterStage=1.5538f;}
 internal static class Test {
  static void Verify(bool ok,string why) {if(!ok)throw new Exception("FAIL: "+why);}
  static void Mix(float[] g, float[] c, bool[] h, out float gain, out float cut,out float coverage)=>
   ThreeRayTransmission.Combine(g[0],c[0],g[1],c[1],g[2],c[2],
    h[0],h[1],h[2],out gain,out cut,out coverage);
  public static void Main(){
   int tested=0;
   foreach(var rate in new[]{44100,48000,96000,192000}) {
    SpectralPaths.SetSampleRate(rate);
    foreach(var wallGain in new[]{.02f,.1f,.3f,.6f,.9f}) {
     foreach(var wallCut in new[]{200f,800f,3000f,12000f}) {
      float previous=-1;
      foreach(int mask in new[]{7,6,4,0}) {
       float[] gains=new float[3],cuts=new float[3];
       bool[] hit=new bool[3];
       for(int i=0;i<3;i++){
        hit[i]=((mask>>i)&1)==1;
        gains[i]=hit[i]?wallGain:1f;
        cuts[i]=hit[i]?wallCut:22000f;
       }
       Mix(gains,cuts,hit,out float g,out float c,out float cover);
       double dc=.5*gains[0]*gains[0]+.25*gains[1]*gains[1]+.25*gains[2]*gains[2];
       double hi=.5*SpectralPaths.HighEnergy(gains[0],cuts[0])+
                 .25*SpectralPaths.HighEnergy(gains[1],cuts[1])+
                 .25*SpectralPaths.HighEnergy(gains[2],cuts[2]);
       Verify(Math.Abs(g*g-dc)<.00005,"DC energy mix");
       Verify(Math.Abs(SpectralPaths.HighEnergy(g,c)-hi)<.005,$"2 kHz energy mix: rate={rate} wallGain={wallGain} wallCut={wallCut} mask={mask} expected={hi} actual={SpectralPaths.HighEnergy(g,c)} representedCut={c}");
       Verify(Math.Abs(cover-(.5*(hit[0]?1:0)+.25*(hit[1]?1:0)+.25*(hit[2]?1:0)))<.00001,"coverage");
       if(previous>=0)Verify(g+1e-5>=previous,"clear path must not get quieter as rays clear");
       previous=g;
       tested+=4;
      }
      // The old median rule would snap .1 -> 1 when one of three rays clears.
      Mix(new[]{1f,wallGain,wallGain},new[]{22000f,wallCut,wallCut},
          new[]{false,true,true},out var partial,out var pc,out var coveragePartial);
      Verify(partial>wallGain && partial<1f,"partial aperture must be intermediate");
      Verify(coveragePartial>.0f && coveragePartial<1f,"partial coverage must be exposed");
      tested+=2;
      ThreeRayTransmission.Blend(wallGain,wallCut,1f,22000f,.5f,out var face,out var faceCut);
      Verify(face>wallGain && face<1f,"visible source face must not switch full open");
      Verify(SpectralPaths.HighEnergy(face,faceCut) > SpectralPaths.HighEnergy(wallGain,wallCut),"face must preserve high frequencies");
      tested+=2;
     }
    }
   }
   Console.WriteLine($"PARTIAL OCCLUSION SOURCE TEST PASS: {tested} cases at 44.1/48/96/192 kHz");
  }
 }
}
