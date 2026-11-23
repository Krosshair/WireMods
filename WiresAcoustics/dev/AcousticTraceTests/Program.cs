using System;
using System.Collections.Generic;
using WiresAcoustics;

namespace UnityEngine
{
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float magnitude => (float)Math.Sqrt(x*x+y*y+z*z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public static class Mathf {
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Abs(float f)=>Math.Abs(f);
        public static float Log(float f)=>(float)Math.Log(f);
        public static float Log10(float f)=>(float)Math.Log10(f);
    }
    public static class Time {public static float time = 10f;}
    public class GameObject {public string name = "source";}
    public class Transform {public Vector3 position;}
    public class AudioClip {public string name = "testclip"; public int samples=48000;}
    public class AudioSource {
        private static int nextId;
        private readonly int id=++nextId;
        public int GetInstanceID()=>id;
        public bool enabled=true,loop,isPlaying=true,isVirtual,mute;
        public float volume=1f,spatialBlend=1f,pitch=1f,reverbZoneMix,maxDistance=10f;
        public int priority=128,timeSamples=5;
        public AudioClip clip=new();
    }
}
namespace WiresAcoustics {
    internal sealed class LogCapture { internal readonly List<string> Lines = new(); public void LogInfo(string s)=>Lines.Add(s); }
    internal sealed class BoolSetting {internal bool Value = true;}
    internal static class Plugin {
        internal static readonly BoolSetting DebugLog = new();
        internal static readonly BoolSetting Enabled = new();
        internal static readonly LogCapture Log = new();
    }
    internal static class Hearing { internal static UnityEngine.Vector3 At = new(0,0,0); }
    internal static class Occlusion { internal static int WorldGen=2; }
    internal static class Budget {internal static float Open(AcousticFilter f) => f.DistanceGain; }
    internal static class Corners {
        internal sealed class Record {internal bool Valid;internal int Pending;}
    }
    internal class AcousticFilter {
        internal enum Kinds : byte { Sfx, Voice, Ocean, Other, Menu, Ambient, Outside }
        internal static readonly List<AcousticFilter> Live = new();
        internal UnityEngine.GameObject gameObject = new();
        internal UnityEngine.Transform transform = new();
        internal UnityEngine.AudioSource Source = new();
        internal bool Active=true,Routed,Hrtf,Placed=true;
        internal Kinds Kind=Kinds.Sfx;
        internal float AcousticResolvedAt=9f,RouteSpread,LastDist=4f,LastOcc=.4f,LastCoverage=.5f;
        internal float TargetGain=.5f,TargetCutoff=1200f,AirCutoff=18000f;
        internal float DistanceGain=.8f,SourceLevel=1f,DspGain=.35f,DspCutoff=1100f;
        internal float AuthoredMaxDistance=>Source.maxDistance;
        internal float EstimatedCompositeGain=>TargetGain*DistanceGain*SourceLevel;
        internal float EstimatedCompositeCutoff=>Math.Min(TargetCutoff,AirCutoff);
        internal float Doppler=1f,UnderAmount,LastInputRms=.2f,LastOutputRms=.1f,LastProcessingDb=-6f;
        internal long LastMeterSamples=1024,LastDroppedMeterPackets;
        internal int AcousticRouteCount=1,AcousticGeneration=2,LastVolumeSamples=8;
        internal string AcousticPath="portal";
        internal UnityEngine.Vector3 RoutePoint = new(1,1,1);
        internal Corners.Record[] Records;
    }
    internal static class Program {
        private static void Check(bool good,string message){if(!good)throw new Exception(message);}
        public static void Main() {
            var f=new AcousticFilter();f.gameObject.name="chest";f.Source.clip.name="open";AcousticFilter.Live.Add(f);
            AcousticTrace.Record(f,1f,22000f,.5f,1200f);
            AcousticTrace.RecordGeometry(new UnityEngine.Vector3(1,2,3),false,true,3);
            AcousticTrace.Emit();
            string report=string.Join("\n",Plugin.Log.Lines);
            Check(report.Contains("[acoustics.trace.transition]"),"missing change history");
            Check(report.Contains("source=chest"),"missing source");
            Check(report.Contains("kind=door"),"missing door geometry event");
            Check(report.Contains("actualDSPcut=1100"),"missing measured DSP values");
            Console.WriteLine(report);
            Check(report.Contains("outputRMS=0.10000"),"missing output meter");
            Check(report.Contains("path=portal"),"missing path classification");
            Check(report.Contains("sourcePos="),"missing coordinates");
            Check(report.Contains("selected=24"),"missing selection count");
            Check(report.Contains("pathAtUpdate=portal"),"missing transition path");
            int count=Plugin.Log.Lines.Count;
            AcousticTrace.Emit();
            Check(Plugin.Log.Lines.Count==count+2,"history should be cleared between reports");
            f.Source.loop=true;
            var second=new AcousticFilter();second.gameObject.name="second_fire";
            second.Source.clip.name="open"; second.Source.loop=true;
            second.Source.isVirtual=true; second.Source.volume=.2f;
            AcousticFilter.Live.Add(second);
            var third=new AcousticFilter();third.gameObject.name="third_fire";
            third.Source.clip.name="open";third.Source.loop=true;
            third.Source.isPlaying=false;
            AcousticFilter.Live.Add(third);
            AcousticTrace.Emit();
            string loops=string.Join("\n",Plugin.Log.Lines);
            Check(loops.Contains("[acoustics.polyphony] clip=open instances=3 playing=2 virtual=1 disabled=0"),
                  "concurrent-loop source census incomplete");
            Check(loops.Contains("authoredRange=10.00"),"original game range must appear in diagnostics");
            Check(loops.Contains("name=second_fire") && loops.Contains("name=third_fire"),
                  "quieter/virtual and nonplaying instances must be represented");
            Console.WriteLine("ACOUSTIC TRACE PASS: source snapshot, geometry, transitions, and per-instance loop polyphony");
        }
    }
}
