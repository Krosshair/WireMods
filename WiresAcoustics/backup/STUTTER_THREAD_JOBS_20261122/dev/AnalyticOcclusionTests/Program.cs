using System;
using UnityEngine;
using WiresAcoustics;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public float this[int i] {get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}}
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 right=>new(1,0,0);
        public static Vector3 up=>new(0,1,0);
        public static Vector3 forward=>new(0,0,1);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float t)=>new(a.x*t,a.y*t,a.z*t);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public static Vector3 operator /(Vector3 a,float t)=>new(a.x/t,a.y/t,a.z/t);
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a*(1-t)+b*t;
        public Vector3 normalized { get {float l=(float)Math.Sqrt(sqrMagnitude);return l>1e-8f?this*(1f/l):new(0,0,0);} }
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public class Transform
    {
        public Vector3 lossyScale=new(1,1,1);
        public Vector3 position=new(0,0,0);
        public bool rotateY90;
        public Vector3 TransformDirection(Vector3 v)=>rotateY90?new(v.z,v.y,-v.x):v;
        public Vector3 TransformPoint(Vector3 v) {
            var s=new Vector3(v.x*lossyScale.x,v.y*lossyScale.y,v.z*lossyScale.z);
            return TransformDirection(s)+position;
        }
        public Vector3 InverseTransformPoint(Vector3 world) {
            var d=world-position;
            if (rotateY90) d=new(-d.z,d.y,d.x);
            return new(d.x/lossyScale.x,d.y/lossyScale.y,d.z/lossyScale.z);
        }
    }
    public class GameObject { public bool activeInHierarchy=true; }
    public class BoxCollider
    {
        public Vector3 size=new(2,2,.2f),center=new(0,0,0);
        public bool enabled=true;
        public Transform transform=new();
        public GameObject gameObject=new();
    }
    public static class Mathf
    {
        public const float PI=(float)Math.PI;
        public static float Sqrt(float v)=>(float)Math.Sqrt(v);
        public static float Asin(float v)=>(float)Math.Asin(v);
        public static float Abs(float v)=>Math.Abs(v);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp01(float v)=>Math.Clamp(v,0f,1f);
        public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
        public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
        public static float Sign(float v)=>v<0f?-1f:1f;
    }
}
namespace WiresAcoustics
{
    internal class Sound {internal bool isPlaying=true;}
    internal class AcousticFilter
    {
        internal BoxCollider AnalyticWall;
        internal Vector3 AnalyticSource;
        internal int AnalyticWorldGen;
        internal float AnalyticLow, AnalyticMid;
        internal float AnalyticIndirectGain, AnalyticIndirectCutoff = 22000f;
        internal bool AnalyticActive,AnalyticHasPortal,Active=true,Placed=true,AreaKnown=false;
        internal bool AnalyticPoseValid;
        internal Vector3 AnalyticLastEar,AnalyticLastSource;
        internal float AnalyticLastStrength,AnalyticLastWater,AnalyticLastIndirect;
        internal float AnalyticBendShare;
        internal Vector3 AnalyticEdgePoint;
        internal bool Routed;
        internal Vector3 RoutePoint;
        internal Sound Source=new();
        internal float TargetGain=1f,TargetCutoff=22000f,LastCoverage,LastOcc,UnderAmount=0f;
        internal bool FastGeometryResponse;
        internal void Set(float gain,float cutoff,bool snap){TargetGain=gain;TargetCutoff=cutoff;}
    }
    internal static class Solids {
      internal class Info {internal float First=.264575f,Cut=1000f;}
    }
    internal static class Occlusion {
        internal static int WorldGen=1;
        internal static bool Change;
        internal static bool ChangedNear(int gen,Vector3 at,Vector3 source,float radius)
            => Change;
    }
    internal static class Corners {internal const float Reach=5f;}
    internal static class SourceVolume {
        internal struct Projection {internal float Radius;internal Vector3 Center;}
        internal static Projection Project(AcousticFilter f,Vector3 listener,Vector3 source)
          =>new() {Radius=1f,Center=source};
    }
    internal static class SpectralPaths {
        internal static float HighEnergy(float gain,float cut)=>gain*gain*.2f;
        internal static float Response(float cutoff)=>.2f;
        internal static void Decode(float low,float mid,out float gain,out float cutoff){
            gain=Mathf.Sqrt(Mathf.Clamp01(low));
            cutoff=Mathf.Clamp01(mid/Mathf.Max(.000001f,low))*22000f;
        }
        internal static void Combine(float a,float ca,float b,float cb,
                                     out float gain,out float cutoff)
        {
            Decode(a*a+b*b,HighEnergy(a,ca)+HighEnergy(b,cb),
                   out gain,out cutoff);
        }
    }
    internal static class AcousticMath {internal static float OcclusionStrength=1f;}
    internal static class Underwater {internal static void Apply(ref float g,ref float cut,float amount){}}
    internal static class Plugin {
        internal sealed class Flag {internal bool Value=true;}
        internal static Flag Enabled=new(),Occlusion=new();
    }
}
internal static class Tests {
    static int checks;
    static void Check(bool condition,string msg){
        if(!condition) throw new Exception("FAIL "+msg);
        checks++;
    }
    static void Near(float actual,float expected,float eps,string msg){
        Check(Math.Abs(actual-expected)<=eps,$"{msg}: {actual} expected {expected}");
    }
    static void Main() {
        // Analytically exact disk/rectangle intersections.
        Near(AnalyticOcclusion.DiskRectangle(-2,-2,2,2),1f,1e-5f,"full disk");
        Near(AnalyticOcclusion.DiskRectangle(-2,0,2,2),.5f,1e-5f,"half disk");
        Near(AnalyticOcclusion.DiskRectangle(0,0,2,2),.25f,1e-5f,"quadrant");
        Near(AnalyticOcclusion.DiskRectangle(1,-2,2,2),0f,1e-5f,"tangent");
        Near(AnalyticOcclusion.DiskRectangle(2,-2,3,2),0f,1e-5f,"outside");
        Near(AnalyticOcclusion.DiskRectangle(-.1f,-2,.1f,2),.12711f,.0001f,"thin stripe");
        Near(AnalyticOcclusion.DiskRectangle(-2,-2,0,2),.5f,1e-5f,"reverse-half disk");
        Near(AnalyticOcclusion.DiskRectangle(-2,-2,2,0),.5f,1e-5f,"other half");

        var box=new BoxCollider();
        bool ok=AnalyticOcclusion.BoxCoverage(box,new(0,0,-5),new(0,0,5),1f,out float covered);
        Check(ok,"center case supported");
        Near(covered,1f,1e-5f,"centered box covers all source");
        ok=AnalyticOcclusion.BoxCoverage(box,new(0,0,-5),new(2,0,5),1f,out covered);
        Check(ok,"half case supported");
        Near(covered,.5f,1e-5f,"half-occluded projected source");
        ok=AnalyticOcclusion.BoxCoverage(box,new(0,0,-5),new(3,0,5),1f,out covered);
        Check(ok,"tangent supported");
        Near(covered,0f,1e-5f,"tangent no obstruction");
        float previous=1f;
        // Continuous monotonic coverage with a moving listener/source.
        for(int i=0;i<=1000;i++) {
            float sx=1.5f+i*.002f;
            Check(AnalyticOcclusion.BoxCoverage(box,new(0,0,-5),new(sx,0,5),1f,
                 out covered),"trajectory candidate");
            Check(covered<=previous+.00001f,"no upward jumps approaching edge");
            if(i>0) Check(Math.Abs(covered-previous)<.005f,
                 "1000-step geometric walk never discrete-jumps");
            previous=covered;
        }
        box.transform.rotateY90=true;
        ok=AnalyticOcclusion.BoxCoverage(box,new(-5,0,0),new(5,0,0),1f,out covered);
        Check(ok,"rotated wall supported");
        Near(covered,1f,1e-5f,"rotated collider remains opaque");
        box.transform.rotateY90=false;
        box.transform.lossyScale=new(2,1,1);
        ok=AnalyticOcclusion.BoxCoverage(box,new(0,0,-5),new(4,0,5),1f,out covered);
        Check(ok,"scaled wall supported");
        Near(covered,.5f,1e-5f,"scaled wall width changes area");
        box.transform.lossyScale=new(1,1,1);
        box.size=new(.075f,2.84f,.075f);
        Check(AnalyticOcclusion.BoxCoverage(box,new(0,0,-5),new(0,0,5),1f,
             out covered),"iron grate bar oriented toward listener");
        Check(covered>.07f && covered<.12f,
              "narrow metal post gives a small continuous shadow");
        box.size=new(2,2,.2f);

        var f=new AcousticFilter();
        var wall=new Solids.Info();
        f.AnalyticActive=true;
        f.AnalyticIndirectGain=.3f;
        f.AnalyticIndirectCutoff=22000f;
        AnalyticOcclusion.Bind(f,box,wall,new(2,0,5));
        Check(AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(2,0,5),
             out float gain,out _,out covered),"model works");
        Near(covered,.5f,1e-5f,"model covered fraction");
        Check(gain < 1f && gain > .70f,
            "half-shadow has substantial direct and diffracted sound, not wall-only suppression");
        AnalyticOcclusion.Refresh(f,new(0,0,-5),new(2,0,5));
        Near(f.LastCoverage,.5f,1e-5f,"frame refresh continuous");
        Check(f.TargetGain > gain,
             "separate indirect path adds real spectral energy to direct diffraction");
        // A physically real grazing edge must keep most of the sound,
        // rather than switching immediately to a fully muffled wall.
        f.AnalyticIndirectGain=0f;
        Check(AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(0,0,5),
              out float deepGain,out _,out _),"center behind wall computed");
        Check(AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(2,0,5),
              out float grazingGain,out _,out _),"grazing wall edge computed");
        Check(AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(4,0,5),
              out float exposedGain,out _,out _),"fully exposed computed");
        Check(deepGain < grazingGain,
              "diffraction around wall edge is more audible than deep shadow");
        Check(grazingGain > .7f,
              "only slightly occluded source must not receive full wall muffling");
        Near(exposedGain,1f,.0001f,
             "sound returns to original level outside shadow");
        float lastGain=0f;
        for(int i=0;i<=1200;i++){
            float sx=1.0f+i*.002f;
            Check(AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(sx,0,5),
                  out float movingGain,out float movingCut,out float movingCoverage),
                  "continuous propagation trajectory supported");
            Check(movingGain>=0f && movingGain<=1f,
                  "physical spectral energy stays bounded");
            Check(movingCut>=0f && movingCut<=22000f,
                  "filtered bandwidth stays bounded");
            if(i>0) Check(Math.Abs(movingGain-lastGain)<.03f,
               "no stepped A/B muffle as source moves past screen");
            lastGain=movingGain;
        }
        AnalyticOcclusion.Refresh(f,new(0,0,-5),new(2,0,5));
        Check(f.AnalyticBendShare>0f && f.AnalyticBendShare<1f,
              "partially obstructed sound has physically routed energy");
        Check(f.Routed,
              "direction can arise at a measured wall edge");
        Check(f.RoutePoint.x>f.AnalyticEdgePoint.x &&
              f.RoutePoint.x<2f,
              "source direction blends toward measured edge, not arbitrary midpoint");
        AcousticMath.OcclusionStrength=0f;
        Check(AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(2,0,5),
              out float disabledGain,out _,out _),"disabled strength supported");
        Near(disabledGain,1f,.00001f,"disabled muffling exactly transparent");
        Near(f.AnalyticBendShare,0f,.00001f,"disabled muffling no ghost edge direction");
        AcousticMath.OcclusionStrength=1f;
        Occlusion.Change=true;
        Check(!AnalyticOcclusion.Evaluate(f,new(0,0,-5),new(2,0,5),
              out _,out _,out _),"world edit invalidates wall");
        Check(f.AnalyticWall==null,"stale wall removed");
        Console.WriteLine($"SINGLE BOX SHADOW (synthetic scene, not Valheim audio): deep={deepGain:F3}, grazing={grazingGain:F3}, clear={exposedGain:F3}");
        Console.WriteLine($"ANALYTIC COVERAGE PASS: {checks} source-linked geometric, trajectory, material-energy, invalidation assertions");
    }
}
