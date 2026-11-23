using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
 public class Transform {
  private readonly int id;
  public Transform parent;
  public Transform(int number, Transform parent=null){id=number;this.parent=parent;}
  public int GetInstanceID()=>id;
  public bool IsChildOf(Transform owner){
   for(Transform a=parent;a!=null;a=a.parent)if(ReferenceEquals(a,owner))return true;
   return false;
  }
 }
}
namespace WiresAcoustics {
 internal class AcousticFilter {
  internal static readonly List<AcousticFilter> Live=new();
  internal Transform ShapeRoot;internal bool AreaKnown=true;
 }
 internal static class Area {
  private static readonly Dictionary<int,int> Shapes=new();
  private static readonly Dictionary<int,int> WeaponShapes=new();
  internal static void Set(Transform t){Shapes[t.GetInstanceID()]=1;WeaponShapes[t.GetInstanceID()]=1;}
  internal static bool Has(Transform t)=>Shapes.ContainsKey(t.GetInstanceID());
  internal static bool HasWeapon(Transform t)=>WeaponShapes.ContainsKey(t.GetInstanceID());
        internal static void Invalidate(Transform owner)
        {
            if (owner == null) return;
            // Collider geometry can belong to a nested child while the
            // sound caches the shape of its parent or root. Invalidate all
            // cached ancestor boxes, not just this one Transform's ID.
            for (Transform t = owner; t != null; t = t.parent)
            {
                Shapes.Remove(t.GetInstanceID());
                WeaponShapes.Remove(t.GetInstanceID());
            }
            foreach (AcousticFilter f in AcousticFilter.Live)
                if (f != null && f.ShapeRoot != null &&
                    (f.ShapeRoot == owner || f.ShapeRoot.IsChildOf(owner) ||
                     owner.IsChildOf(f.ShapeRoot)))
                {
                    f.AreaKnown = false;
                    Shapes.Remove(f.ShapeRoot.GetInstanceID());
                    WeaponShapes.Remove(f.ShapeRoot.GetInstanceID());
                }
        }
 }
 public static class Program {
  static void Assert(bool yes,string why){if(!yes)throw new Exception(why);}
  public static void Main(){
   var root = new Transform(10);var child = new Transform(11,root);
   var sibling = new Transform(12,root);var unrelated=new Transform(13);
   var nested = new Transform(14,child);
   Area.Set(root);Area.Set(child);Area.Set(sibling);Area.Set(unrelated);Area.Set(nested);
   var rootSound = new AcousticFilter{ShapeRoot=root,AreaKnown=true};
   var nestedSound = new AcousticFilter{ShapeRoot=nested,AreaKnown=true};
   var siblingSound = new AcousticFilter{ShapeRoot=sibling,AreaKnown=true};
   var otherSound = new AcousticFilter{ShapeRoot=unrelated,AreaKnown=true};
   AcousticFilter.Live.AddRange(new[]{rootSound,nestedSound,siblingSound,otherSound});
   Area.Invalidate(child);
   Assert(!Area.Has(root),"changed child must invalidate cached parent");
   Assert(!Area.Has(child),"changed child must invalidate itself");
   Assert(!Area.Has(nested),"child-root sound cache invalidated when parent changes");
   Assert(!Area.HasWeapon(root) && !Area.HasWeapon(child) && !Area.HasWeapon(nested),
          "equipped weapon visual cache must invalidate with owner geometry");
   Assert(Area.Has(sibling),"sibling geometry must stay cached");
   Assert(Area.Has(unrelated),"unrelated geometry must remain cached");
   Assert(Area.HasWeapon(sibling) && Area.HasWeapon(unrelated),
          "unrelated weapon visual caches must stay stable");
   Assert(!rootSound.AreaKnown && !nestedSound.AreaKnown,"dependent sound shapes invalidated");
   Assert(siblingSound.AreaKnown && otherSound.AreaKnown,"unrelated sounds unaffected");
   Console.WriteLine("AREA SOURCE EXTRACTION TEST PASS: actual Area.Invalidate from production; ancestors, nested sources, siblings, unrelated caches");
  }
 }
}