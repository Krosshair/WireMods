using System;
using System.Collections.Generic;
using WiresAcoustics;
using UnityEngine;

namespace UnityEngine
{
    internal struct Vector3 { internal float y; internal Vector3(float y_) { y=y_; } }
    internal struct RaycastHit { internal int colliderInstanceID; }
    internal static class Mathf
    {
        internal static float Max(float a,float b) => Math.Max(a,b);
        internal static float Clamp01(float a) => Math.Clamp(a,0f,1f);
    }
}
namespace WiresAcoustics
{
    internal static class Solids
    {
        internal class Info {
            internal bool Ground,Wall;
            internal float First=1f,Cut=22000f,Weight;
        }
        internal static readonly Dictionary<int,Info> Map=new();
        internal static Info Of(RaycastHit h) =>
            Map.TryGetValue(h.colliderInstanceID,out Info s)?s:null;
    }
    internal static class Room
    {
        internal static readonly bool[] Opens=new bool[64];
        internal static readonly int[] Boundary=new int[64];
        internal static bool Out(int i)=>Opens[i];
        internal static int AcousticBoundary(int i)=>Boundary[i];
    }
    internal static class SpectralPaths
    {
        internal static float Response(float cutoff) => Math.Clamp(cutoff/22000f,0f,1f);
        internal static float HighEnergy(float gain,float cutoff) =>
            gain*gain*Response(cutoff);
        internal static void Decode(float low,float high,out float gain,out float cutoff)
        {
            gain=(float)Math.Sqrt(Math.Max(0f,low));
            cutoff=low>1e-8f?Math.Clamp(high/low,0f,1f)*22000f:500f;
        }
    }
    internal static class Occlusion
    {
        internal struct Block
        {
            internal int Walls;
            internal float Through,ThroughHigh,Soft;
            internal static Block Clear=>new Block {Through=1,ThroughHigh=1};
        }
        internal static void Wall(in Block b,out float gain,out float cutoff)
        {
            // Only the material DSP is stubbed. The ENTIRE field measurement
            // is compiled directly from WA's production EnvironmentalField.cs.
            gain=b.Walls>0?b.Through:Math.Clamp(1f-.5f*b.Soft,0f,1f);
            cutoff=b.Walls>0?b.ThroughHigh*22000f:22000f;
        }
    }
}
internal static class Program
{
    private static int checks;
    private static readonly Vector3[] dirs=new Vector3[64];
    private static readonly RaycastHit[] hits=new RaycastHit[64];
    private static readonly bool[] didHit=new bool[64];
    static void Check(bool x,string why) { if(!x) throw new Exception(why); checks++; }
    static void Set(int open,float through=.12247f,float cut=700f,bool seam=false,bool ground=false)
    {
        Solids.Map.Clear();
        Solids.Map[1]=new Solids.Info {Wall=!ground,Ground=ground,First=through,Cut=cut,Weight=1};
        for(int i=0;i<64;i++)
        {
            Room.Opens[i]=i<open;
            Room.Boundary[i]=i;
            hits[i]=new RaycastHit{colliderInstanceID=1};
            didHit[i]=i>=open;
        }
        if(seam){Room.Opens[0]=false;Room.Boundary[0]=1;hits[0]=new RaycastHit{colliderInstanceID=0};}
        EnvironmentalField.Reset();
        EnvironmentalField.Measure(dirs,hits,didHit);
    }
    static void Main()
    {
        for(int i=0;i<64;i++)dirs[i]=new Vector3(1-(i+.5f)*2f/64f);
        Set(64);
        Check(Math.Abs(EnvironmentalField.Gain-1)<1e-5,"outdoors must remain vanilla volume");
        Check(EnvironmentalField.Cutoff>=21999,"outdoors must remain bright");
        Set(0);
        float stone=EnvironmentalField.Gain;
        float dark=EnvironmentalField.Cutoff;
        Check(stone>0&&stone<.25,"sealed stone should be quiet but not binary mute");
        Check(dark<1000,"sealed stone should lose highs");
        Set(0,.264575f,2600f);
        Check(EnvironmentalField.Gain>stone,"wood should transmit more than stone");
        Set(8);
        float arch=EnvironmentalField.Gain;
        Check(arch>.48f&&arch>stone*2,"open arches must sound much clearer than sealed room");
        Check(EnvironmentalField.Cutoff>dark*3,"clear paths must carry high frequencies");
        Check(EnvironmentalField.Summary().Contains("EXTRA RAYS 0"),"no extra rays allowed");
        Set(16);
        Check(EnvironmentalField.Gain>arch,"more opening area must be louder");
        float last=0;
        for(int n=0;n<=31;n++)
        {
            Set(n);
            Check(EnvironmentalField.Gain>=last-1e-6f,"openness monotonic");
            Check(EnvironmentalField.Gain<=1.000001f,"cannot exceed vanilla");
            last=EnvironmentalField.Gain;
        }
        Set(1,seam:true);
        Check(EnvironmentalField.Gain<.25,"false one-ray seam cannot create full opening");
        Set(0,ground:true);
        Check(EnvironmentalField.Gain<.001,"indoor ground isn't emitting outdoor rain");
        Set(12);
        Check(EnvironmentalField.Gain>.55,"covered arch area not erased by roof");
        for(int i=0;i<1000;i++)
        {
            float e=i/1000f;
            float n=EnvironmentalField.Normalize(e,.4f);
            Check(n>=0&&n<=1,"bounded energy");
            Check(i==0||n>=EnvironmentalField.Normalize((i-1)/1000f,.4f),"normalized monotonic");
        }
        Console.WriteLine("ENVIRONMENT FIELD SOURCE-LINKED PASS: "+checks+" checks; arch gain="+arch.ToString("F3")+", stone gain="+stone.ToString("F3"));
    }
}
