using System;
using System.Collections.Generic;
using System.Linq;
using WiresAcoustics;
using UnityEngine;

namespace UnityEngine {
  internal struct Vector3 {
    internal float x,y,z;
    internal Vector3(float x,float y,float z) {this.x=x;this.y=y;this.z=z;}
    public static Vector3 operator -(Vector3 a,Vector3 b)=>
       new(a.x-b.x,a.y-b.y,a.z-b.z);
    internal float sqrMagnitude=>x*x+y*y+z*z;
  }
  internal class Transform {
    internal Vector3 position;
    internal Transform root;
    private HashSet<Type> types=new();
    internal Transform(Vector3 p){position=p;root=this;}
    internal void Add<T>() where T:class=>types.Add(typeof(T));
    internal T GetComponent<T>() where T:class=>
       types.Contains(typeof(T))?(T)Activator.CreateInstance(typeof(T)):null;
  }
  internal enum QueryTriggerInteraction {Ignore}
  internal class Collider {
    internal Transform transform;
    internal ZNetView network;
    internal Collider(Transform owner) {transform=owner;network=new ZNetView{transform=owner};}
    internal T GetComponentInParent<T>() where T:class=>network as T;
  }
  internal static class Physics {
    internal static Collider[] Available=Array.Empty<Collider>();
    internal static int OverlapSphereNonAlloc(Vector3 pos,float radius,
            Collider[] buffer,int mask,QueryTriggerInteraction q) {
       Array.Clear(buffer,0,buffer.Length);
       int n=Math.Min(Available.Length,buffer.Length);
       Array.Copy(Available,buffer,n);
       return n;
    }
  }
}
namespace WiresAcoustics {
  internal class ZNetView {internal Transform transform;}
  internal class Piece {}
  internal class Container {}
  internal class Fireplace {}
  internal class Door {}
  internal class CraftingStation {}
  internal class Character {}
}
internal static class Program {
   static int checks=0;
   static void Check(bool yes,string msg) {
      if(!yes) throw new Exception("FAILED: "+msg);
      checks++;
   }
   static Transform Make(float x=0f,bool piece=true,bool character=false) {
      var t=new Transform(new Vector3(x,0,0));
      if(piece)t.Add<Piece>();
      if(character)t.Add<Character>();
      return t;
   }
   static Collider[] Colliders(Transform t,int n) =>
      Enumerable.Range(0,n).Select(_=>new Collider(t)).ToArray();
   static void Main() {
      var a=Make();
      Physics.Available=Colliders(a,1);
      Check(Spots.Choose(new Vector3(0,0,0))==a,"unique authorial pivot");
      Physics.Available=Colliders(a,7);
      Check(Spots.Choose(new Vector3(0,0,0))==a,"several colliders on SAME piece");
      var b=Make();
      Physics.Available=Colliders(a,2).Concat(Colliders(b,2)).ToArray();
      Check(Spots.Choose(new Vector3(0,0,0))==null,"ambiguous pivot refuses guessed owner");
      Physics.Available=Colliders(b,2).Concat(Colliders(a,2)).ToArray();
      Check(Spots.Choose(new Vector3(0,0,0))==null,"independent of physics buffer order");
      Physics.Available=Colliders(a,64);
      Check(Spots.Choose(new Vector3(0,0,0))==null,"saturated collider array cannot establish uniqueness");
      Physics.Available=Colliders(a,63);
      Check(Spots.Choose(new Vector3(0,0,0))==a,"non-saturated buffer may identify source");
      var unowned=Make(piece:false);
      Physics.Available=Colliders(unowned,2);
      Check(Spots.Choose(new Vector3(0,0,0))==null,"arbitrary world object rejected");
      var creature=Make(piece:false,character:true);
      Physics.Available=Colliders(creature,1);
      Check(Spots.Choose(new Vector3(0,0,0))==null,"creature cannot masquerade as building");
      var far=Make(x:2);
      Physics.Available=Colliders(far,1);
      Check(Spots.Choose(new Vector3(0,0,0))==null,"only exact pivot matching");
      Physics.Available=new Collider[]{null,new Collider(a)};
      Check(Spots.Choose(new Vector3(0,0,0))==a,"null collider tolerated");
      var container=Make(piece:false);container.Add<Container>();
      Physics.Available=Colliders(container,1);
      Check(Spots.Choose(new Vector3(0,0,0))==container,"container sound uses actual owner");
      var door=Make(piece:false);door.Add<Door>();
      Physics.Available=Colliders(door,1);
      Check(Spots.Choose(new Vector3(0,0,0))==door,"door sound uses actual owner");
      var station=Make(piece:false);station.Add<CraftingStation>();
      Physics.Available=Colliders(station,1);
      Check(Spots.Choose(new Vector3(0,0,0))==station,"station sound uses actual owner");
      Console.WriteLine($"VALHEIM SOUND-SOURCE OWNERSHIP PASS: {checks} actual-source decisions");
   }
}