using System;
using WiresAcoustics;
namespace UnityEngine {
public struct Vector3 {public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} public float sqrMagnitude=>x*x+y*y+z*z; public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z); public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;}
}
namespace WiresAcoustics {
internal static class Corners {internal const int Dirs=4;internal sealed class Record {internal bool Valid;internal UnityEngine.Vector3 At,N;internal float Span;internal int Gen,Pending,Version;internal readonly UnityEngine.Vector3[] Edges=new UnityEngine.Vector3[Dirs];internal readonly bool[] Has=new bool[Dirs];}}
internal static class Occlusion {internal const float SameWall=.4f;internal static int WorldGen;private static readonly System.Collections.Generic.List<UnityEngine.Vector3> Changes=new();internal static bool ChangedNear(int since,UnityEngine.Vector3 at,UnityEngine.Vector3 unused,float radius){for(int i=since;i<Changes.Count;i++) if((Changes[i]-at).sqrMagnitude<=radius*radius)return true;return false;}internal static void Change(UnityEngine.Vector3 at){Changes.Add(at);WorldGen++;}internal static void Reset(){Changes.Clear();WorldGen=0;}}
internal static class Program {private static void Check(bool good,string label){if(!good)throw new Exception(label);}public static void Main(){
var at=new UnityEngine.Vector3(10,0,0);var n=new UnityEngine.Vector3(1,0,0);
SharedEdges.Clear();Occlusion.Reset();
var rec=new Corners.Record{Valid=true,Pending=1,At=at,N=n,Span=5f,Gen=0};
rec.Has[0]=true;rec.Edges[0]=new UnityEngine.Vector3(10,0,3);
SharedEdges.Publish(rec);Check(!SharedEdges.TryCopy(new Corners.Record(),at,n,4f),"incomplete probe published");
rec.Pending=0;SharedEdges.Publish(rec);
var first=new Corners.Record();Check(SharedEdges.TryCopy(first,at,n,4f),"cache miss for exact geometry");
Check(first.Has[0]&&first.Edges[0].z==3&&first.Pending==0,"contents not copied");
first.Has[0]=false;var second=new Corners.Record();
Check(SharedEdges.TryCopy(second,at,n,4f)&&second.Has[0],"shared mutable source");
Check(!SharedEdges.TryCopy(new Corners.Record(),new(11,0,0),n,4f),"incorrect spatial match");
Check(!SharedEdges.TryCopy(new Corners.Record(),at,new(-1,0,0),4f),"incorrect normal match");
Check(!SharedEdges.TryCopy(new Corners.Record(),at,n,6f),"insufficient probe coverage");
Occlusion.Change(new(1000,0,0));Check(SharedEdges.TryCopy(new Corners.Record(),at,n,4f),"unrelated change invalidated");
Occlusion.Change(new(10,0,1));Check(!SharedEdges.TryCopy(new Corners.Record(),at,n,4f),"nearby change not invalidated");
SharedEdges.Clear();Check(!SharedEdges.TryCopy(new Corners.Record(),at,n,4f),"clear didn't invalidate");
Console.WriteLine("SHARED EDGES: PASS (production SharedEdges.cs compiled with deterministic Unity/geometry stubs)");
}}
}
