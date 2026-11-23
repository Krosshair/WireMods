using System;
using WiresAcoustics;
namespace UnityEngine {
 internal struct Vector3 {
  public float x,y,z;
  public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>new(0,0,0);
  public static Vector3 up=>new(0,1,0);
  public static Vector3 right=>new(1,0,0);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public float sqrMagnitude=>x*x+y*y+z*z;
  public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>1e-7f?this*(1f/magnitude):new(0,0,0);
  public void Normalize(){this=normalized;}
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 }
 internal static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Abs(float x)=>Math.Abs(x);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Sqrt(float x)=>(float)Math.Sqrt(x);
  public static float Cos(float x)=>(float)Math.Cos(x);
  public static float Sin(float x)=>(float)Math.Sin(x);
  public static float Pow(float x,float y)=>(float)Math.Pow(x,y);
  public static float Clamp(float x,float lo,float hi)=>Math.Clamp(x,lo,hi);
  public static float Clamp01(float x)=>Math.Clamp(x,0f,1f);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0f,1f);
 }
 internal sealed class AudioSource {public bool loop;}
 internal sealed class Transform {
  public Vector3 Scale=new(1,1,1), Position;public float Yaw;
  public Transform parent;
  public Vector3 position=>Position;
  public Vector3 TransformVector(Vector3 p) {
   double t=Yaw*Math.PI/180,c=Math.Cos(t),s=Math.Sin(t);
   float x=p.x*Scale.x,z=p.z*Scale.z;
   return new((float)(x*c+z*s),p.y*Scale.y,(float)(-x*s+z*c));
  }
  public Vector3 TransformPoint(Vector3 p)=>Position+TransformVector(p);
  public Vector3 InverseTransformPoint(Vector3 point) {
   Vector3 v=point-Position;
   double t=Yaw*Math.PI/180,c=Math.Cos(t),s=Math.Sin(t);
   float x=(float)(v.x*c-v.z*s),z=(float)(v.x*s+v.z*c);
   return new(x/Scale.x,v.y/Scale.y,z/Scale.z);
  }
 }
}
namespace WiresAcoustics {
 internal static class AcousticMath {internal const float FilterStage=1.5538f;}
 internal static class Plugin {
  internal static RadiusEntry SourceSphereRadius;
  internal sealed class RadiusEntry{internal float Value;}
 }
 internal sealed class AcousticFilter {
  internal bool AreaKnown,LocalizedEvent,SurfaceEmitter; internal UnityEngine.Transform ShapeRoot;
  internal UnityEngine.Vector3 ShapeHalf,ShapeCenter;
  internal UnityEngine.AudioSource Source;
 }
 internal static class Test {
  static void Must(bool ok,string why){if(!ok)throw new Exception(why);}
  static bool Near(float a,float b,float epsilon=.0001f)=>Math.Abs(a-b)<epsilon;
  static void Main() {
   int checks=0;
   var camera=new UnityEngine.Transform{Position=new UnityEngine.Vector3(0,3,-4)};
   var ear=new UnityEngine.Transform{Position=new UnityEngine.Vector3(0,1.6f,0),parent=camera};
   Hearing.Update(ear);
   Must((Hearing.At-ear.Position).sqrMagnitude<1e-8f,"physical listener position");
   Must(object.ReferenceEquals(Hearing.Camera,camera),"camera orientation preserved independently");
   checks+=3;
   var eye=new UnityEngine.Vector3(0,1.6f,0);
   var source=new UnityEngine.Vector3(0,1.6f,3);
   var plain=SourceVolume.Project(null!,eye,source);
   Must(!plain.Box && Near(plain.Radius,.01f),"point-source numerical radius");checks++;
   Must(SourceVolume.Count(null!,eye,source,32)==0,"unknown sources must not fabricate volume rays");checks++;
   Plugin.SourceSphereRadius=new Plugin.RadiusEntry{Value=.55f};
   Must(Near(SourceVolume.Project(null!,eye,source).Radius,.55f),"explicit optional radius");checks++;
   Must(SourceVolume.Count(null!,eye,source,32)>0,"explicit finite volume should sample");checks++;
   Plugin.SourceSphereRadius=null;
   foreach(var dist in new[]{.25f,1f,3f,8f,25f}) {
    source=new(0,1.6f,dist);
    var sphere=SourceVolume.Project(null!,eye,source);
    foreach(var quality in new[]{0,4,8,16,32}) {
     int n=SourceVolume.Count(null!,eye,source,quality);
     Must(n<=quality&&n<=32&&(n==0||n==4||n==8||n==16||n==32),"quality bounds");
     Must(n==0,"default unshaped point should have no volume ray budget");
     checks++;
    }
   }
   foreach(var yaw in new[]{0f,45f,90f,180f}) {
    foreach(var scale in new[]{.5f,1f,2f}) {
     var f=new AcousticFilter {
      AreaKnown=true,ShapeRoot=new UnityEngine.Transform{Yaw=yaw,
       Scale=new(scale,1,scale),Position=new(2,1.6f,2)},
      ShapeHalf=new(1.8f,.5f,.2f),ShapeCenter=new(.2f,.1f,0),
      Source=new UnityEngine.AudioSource{loop=true}
     };
     var p=SourceVolume.Project(f,eye,f.ShapeRoot.Position);
     var root=f.ShapeRoot;
     var h=f.ShapeHalf;
     double expected=Math.Sqrt(root.TransformVector(new(h.x,0,0)).sqrMagnitude+
                               root.TransformVector(new(0,h.y,0)).sqrMagnitude+
                               root.TransformVector(new(0,0,h.z)).sqrMagnitude);
     Must(p.Box,"known owner must sample its actual rotated box");
     Must(Near(p.Radius,(float)expected,.0002f),"incorrect bounding box angular extent");
     Must((p.Center-root.TransformPoint(f.ShapeCenter)).sqrMagnitude<1e-8f,"wrong world center");
     int n=SourceVolume.Count(f,eye,f.ShapeRoot.Position,32);
     for(int i=0;i<n;i++) {
      var point=SourceVolume.Point(in p,f.ShapeRoot.Position,i,n);
      Must((point-p.Center).magnitude<=p.Radius+.0001f,"sample beyond conservative radius");
      var local=root.InverseTransformPoint(point)-f.ShapeCenter;
      Must(Math.Abs(local.x)<=f.ShapeHalf.x+.0001f &&
           Math.Abs(local.y)<=f.ShapeHalf.y+.0001f &&
           Math.Abs(local.z)<=f.ShapeHalf.z+.0001f,"sample outside actual oriented owner bounds");
      checks+=2;
     }
     f.Source.loop=false;
     var oneShot=SourceVolume.Project(f,eye,f.ShapeRoot.Position);
     Must(oneShot.Box,"non-loop known area must retain its collider geometry");
     Must((oneShot.Center-p.Center).sqrMagnitude<1e-9f,"one-shot area center changed");
     Must(Near(oneShot.Radius,p.Radius),"one-shot emitter size changed with loop status");
     Must(SourceVolume.Count(f,eye,f.ShapeRoot.Position,32)==n,
          "one-shot source ray count incorrectly depends on loop");
     for(int i=0;i<n;i++) {
      var point=SourceVolume.Point(in oneShot,f.ShapeRoot.Position,i,n);
      var local=root.InverseTransformPoint(point)-f.ShapeCenter;
      Must(Math.Abs(local.x)<=f.ShapeHalf.x+.0001f &&
           Math.Abs(local.y)<=f.ShapeHalf.y+.0001f &&
           Math.Abs(local.z)<=f.ShapeHalf.z+.0001f,
           "nonloop area sample outside owner collider");
      checks++;
     }
     f.LocalizedEvent=true;
     Plugin.SourceSphereRadius=new Plugin.RadiusEntry{Value=.55f};
     var localized=SourceVolume.Project(f,eye,f.ShapeRoot.Position);
     Must(!localized.Box,"hinge/lid/hand event incorrectly uses whole owner bounds");
     Must(Near(localized.Radius,.01f),"point event inherits arbitrary fallback sphere");
     Must(SourceVolume.Count(f,eye,f.ShapeRoot.Position,32)==0,
          "point event schedules unnecessary volume rays");
     Must((localized.Center-f.ShapeRoot.Position).sqrMagnitude<1e-9f,
          "point event must keep its authored world position");
     Plugin.SourceSphereRadius=null;
     checks+=8;
    }
   }
   {
    // A compact chest with another chest above its lid has an audible
    // listener-facing panel. Sample the shell, never its buried centre.
    var chest=new AcousticFilter {
     AreaKnown=true,SurfaceEmitter=true,
     ShapeRoot=new UnityEngine.Transform{Position=new(0,0,2)},
     ShapeCenter=new(0,.35f,0),ShapeHalf=new(.5f,.35f,.35f),
     Source=new UnityEngine.AudioSource{loop=false}
    };
    var chestEar=new UnityEngine.Vector3(0,1.65f,0);
    var shell=SourceVolume.Project(chest,chestEar,new(0,.7f,2));
    Must(shell.Box&&shell.Surface,"chest opening must retain physical shell");
    int front=0,top=0;
    for(int i=0;i<32;i++){
     var point=SourceVolume.Point(in shell,new(0,.7f,2),i,32);
     var local=chest.ShapeRoot.InverseTransformPoint(point)-chest.ShapeCenter;
     bool visibleFront=Near(local.z,-.33f,.002f);
     bool visibleTop=Near(local.y,.33f,.002f);
     Must(visibleFront||visibleTop,"shell sample must be on listener-facing surface");
     Must(Math.Abs(local.x)<=.5001f&&Math.Abs(local.y)<=.3501f&&
          Math.Abs(local.z)<=.3501f,"shell sample outside real chest");
     if(visibleFront)front++;if(visibleTop)top++;
     checks+=2;
    }
    Must(front>=6&&top>=4,"front side must remain audible when stacked lid is blocked");
    checks+=2;
    chest.LocalizedEvent=true;
    Must(!SourceVolume.Project(chest,chestEar,new(0,.7f,2)).Box,
         "actual point-localized events must not become chest surface sounds");
    Must(SourceVolume.Count(chest,chestEar,new(0,.7f,2),32)==0,
         "point events still have no extra source rays");
    checks+=2;
   }
   int fresnel=0;
   var wall=new UnityEngine.Vector3(0,0,0);
   var normal=new UnityEngine.Vector3(1,0,0);
   var open=new UnityEngine.Vector3(0,1,0);
   var listener=new UnityEngine.Vector3(-3,0,0);
   foreach(float transmission in new[]{0f,.05f,.5f,1f}) {
    float last=-1;
    for(int i=-200;i<=200;i++){
     var point=new UnityEngine.Vector3(3,i*.01f,0);
     Must(VolumeFresnel.Transfer(listener,point,wall,normal,wall,open,
       10,transmission,550,out float gain,out float cutoff,out _),"wave transfer rejected");
     Must(float.IsFinite(gain)&&float.IsFinite(cutoff)&&gain>=0&&gain<=1.00001f,"wave bounds");
     if(last>=0)Must(Math.Abs(gain-last)<.08f,"wave edge discontinuity");
     if(transmission==1f)Must(Near(gain,1f,.0005f),"transparent screen");
     last=gain;fresnel++;
    }
   }
   Must(VolumeFresnel.Transfer(listener,new(-1,0,0),wall,normal,
      wall,open,10,.1f,500f,out float clear,out _,out _)
      &&Near(clear,1f),"listener-side emitter must be unobstructed");
   checks++;
   Console.WriteLine($"SOURCE EVENT + SHAPE PASS: {checks} point fallback, owned one-shot/loop box parity, localized hinge/hand override, rotated/scaled samples and quality tests");
   Console.WriteLine($"SOURCE VOLUME FRESNEL PASS: {fresnel} analytic screen cases + front-side sound");
  }
 }
}
