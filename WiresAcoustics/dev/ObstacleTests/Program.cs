using System;
using WiresAcoustics;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 right=>new(1,0,0);public static Vector3 up=>new(0,1,0);
  public float sqrMagnitude=>x*x+y*y+z*z;
  public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>1e-15f?this/magnitude:new(0,0,0);
  public void Normalize(){this=normalized;}
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float x)=>new(a.x*x,a.y*x,a.z*x);
  public static Vector3 operator /(Vector3 a,float x)=>new(a.x/x,a.y/x,a.z/x);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
  public static Vector3 ProjectOnPlane(Vector3 a,Vector3 n)=>a-n*(Dot(a,n)/Dot(n,n));
 }
 public struct Bounds {
  public Vector3 center,extents;
  public Bounds(Vector3 center,Vector3 size){this.center=center;extents=size*.5f;}
  public Vector3 max=>center+extents;
  public bool Contains(Vector3 p)=>Math.Abs(p.x-center.x)<=extents.x&&Math.Abs(p.y-center.y)<=extents.y&&Math.Abs(p.z-center.z)<=extents.z;
 }
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);
  public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
  public static float Log10(float a)=>(float)Math.Log10(a);
  public static float Tan(float a)=>(float)Math.Tan(a);
  public static float Abs(float a)=>Math.Abs(a);
  public static float Clamp(float a,float lo,float hi)=>Math.Clamp(a,lo,hi);
  public static float Clamp01(float a)=>Math.Clamp(a,0,1);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
 }
}
namespace WiresAcoustics {
 internal static class AcousticMath { internal const float FilterStage=1.5538f; }
 internal static class Test {
  static void Check(bool yes,string label){if(!yes)throw new Exception(label);}
  static float Loss(double delta,double hz) {
   double n=2*delta*hz/343.0;
   if(n<=1e-6)return 5;
   double x=Math.Sqrt(2*Math.PI*n);
   return (float)Math.Min(20,5+20*Math.Log10(x/Math.Tanh(x)));
  }
  static void Main() {
   int checks=0;int saturated=0;
   SpectralPaths.SetSampleRate(48000);
   var l=new UnityEngine.Vector3(-3,0,0);var s=new UnityEngine.Vector3(3,0,0);
   foreach(float w in new float[]{.1f,.4f,1f,3f,5f})
   foreach(float h in new float[]{.1f,.5f,1.5f,4f,8f})
   foreach(float depth in new float[]{.1f,1f,3f}) {
    var b=new UnityEngine.Bounds(new(0,0,0),new(w,h,depth));
    Check(Diffraction.RoundObstacle(b,l,s,out float gain,out float cutoff),"unexpected obstacle false");
    Check(float.IsFinite(gain)&&float.IsFinite(cutoff)&&gain>=0&&gain<=1&&cutoff>=1&&cutoff<=22000,"output bounds");
    var d=s-l;float len2=d.sqrMagnitude;
    var p=l+d*UnityEngine.Mathf.Clamp01(UnityEngine.Vector3.Dot(b.center-l,d)/len2);
    var perp=UnityEngine.Vector3.Cross(UnityEngine.Vector3.up,d).normalized;
    float side=UnityEngine.Mathf.Abs(b.extents.x*perp.x)+UnityEngine.Mathf.Abs(b.extents.z*perp.z);
    float off=UnityEngine.Vector3.Dot(b.center-p,perp);
    double low=0,high=0;
    for(int k=0;k<3;k++) {
     var e=k==0?new UnityEngine.Vector3(p.x,UnityEngine.Mathf.Max(p.y,b.max.y),p.z):p+perp*(k==1?off+side:off-side);
     double delta=Math.Max(0,(e-l).magnitude+(s-e).magnitude-d.magnitude);
     low+=Math.Pow(10,-Loss(delta,125)/10);
     high+=Math.Pow(10,-Loss(delta,2000)/10);
    }
    double scale=low>1?1/low:1;
    if(low>1)saturated++;
    double expectedGain=Math.Sqrt(low*scale);
    double expectedHigh=high*scale;
    double actualHigh=SpectralPaths.HighEnergy(gain,cutoff);
    Check(Math.Abs(gain-expectedGain)<.0001,"DC mismatch");
    Check(Math.Abs(actualHigh-expectedHigh)<.0002,"spectral shape mismatch after common energy cap");
    checks++;
   }
   // Three minimum-5 dB edge routes are bounded below unity in this
   // particular barrier model. The shared decoder's saturation behavior
   // is tested separately by TestRendererParity.ps1.
   Check(saturated==0,"unexpected total over-unity energy in three-edge model");
   Console.WriteLine($"OBSTACLE VERIFIED: {checks} geometries, {saturated} spectral-saturation cases; actual production Diffraction/SpectralPaths files linked");
  }
 }
}
