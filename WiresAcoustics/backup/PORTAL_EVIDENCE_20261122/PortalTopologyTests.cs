using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using WiresAcoustics;

namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;
  public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>new(0,0,0);
  public float sqrMagnitude=>x*x+y*y+z*z;
  public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>1e-8f?this/magnitude:zero;
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
  public static Vector3 operator /(Vector3 a,float f)=>new(a.x/f,a.y/f,a.z/f);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 }
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static int Max(int a,int b)=>Math.Max(a,b);
  public static float Clamp(float a,float lo,float hi)=>Math.Clamp(a,lo,hi);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);
 }
}
namespace WiresAcoustics {
 internal static class Room {
  internal static readonly bool[] Hit=new bool[64];
  internal static readonly float[] Dist=new float[64];
  internal static readonly bool[] Escaped=new bool[64];
  internal static bool Out(int i)=>Escaped[i];
 }
 internal static class Test {
  static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
  static UnityEngine.Vector3[] Dirs(){
   var dirs=new UnityEngine.Vector3[64];
   double angle=Math.PI*(3-Math.Sqrt(5));
   for(int i=0;i<64;i++){
    double y=1-(i+.5)/64.0*2;
    double r=Math.Sqrt(1-y*y);
    dirs[i]=new((float)(r*Math.Cos(angle*i)),(float)y,(float)(r*Math.Sin(angle*i)));
   }
   return dirs;
  }
  static int[][] Graph(UnityEngine.Vector3[] dirs){
   var flags=BindingFlags.Static|BindingFlags.NonPublic;
   return (int[][])typeof(Portals).GetMethod("BuildNeighbours",flags)!.Invoke(null,new object[]{dirs})!;
  }
  static void CheckGraph(UnityEngine.Vector3[] dirs){
   var graph=Graph(dirs);
   Assert(graph.Length==dirs.Length,"wrong graph size");
   for(int i=0;i<dirs.Length;i++){
    Assert(graph[i].Distinct().Count()==graph[i].Length,"duplicate neighbor");
    foreach(int j in graph[i]){
     Assert(i!=j && j>=0 && j<dirs.Length,"invalid neighbor");
     Assert(graph[j].Contains(i),$"asymmetric {i}->{j}");
    }
   }
  }
  static string Signature(UnityEngine.Vector3[] dirs,bool[] escaped){
   var graph=Graph(dirs);
   var field=typeof(Portals).GetField("s_nb",BindingFlags.Static|BindingFlags.NonPublic)!;
   field.SetValue(null,graph);
   for(int i=0;i<64;i++){
    Room.Escaped[i]=escaped[i];
    Room.Hit[i]=!escaped[i];
    Room.Dist[i]=4;
   }
   Portals.Find(UnityEngine.Vector3.zero,dirs,.8f,5);
   return string.Join("|",Portals.List.Select(p=>p.Share.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)).OrderBy(x=>x));
  }
  static void Main(){
   var dirs=Dirs();
   CheckGraph(dirs);
   int patterns=0;
   var rng=new Random(1077);
   for(int variant=0;variant<40;variant++){
    var escaped=new bool[64];
    for(int i=0;i<64;i++){
     escaped[i]=dirs[i].y>-.05f && (variant%3==0?dirs[i].x>.3f:(variant%3==1?dirs[i].z>-.25f:rng.NextDouble()<.3));
    }
    var expected=Signature(dirs,escaped);
    for(int trial=0;trial<15;trial++){
     var order=Enumerable.Range(0,64).OrderBy(i=>rng.Next()).ToArray();
     var perm=order.Select(i=>dirs[i]).ToArray();
     var mask=order.Select(i=>escaped[i]).ToArray();
     CheckGraph(perm);
     var actual=Signature(perm,mask);
     Assert(expected==actual,$"portal groups change under ray permutation (pattern {variant}, trial {trial}): expected {expected}; got {actual}");
     patterns++;
    }
   }
   Console.WriteLine($"PORTAL TOPOLOGY PASS: 40 opening masks, {patterns} ray permutations, undirected graph invariants");
  }
 }
}
