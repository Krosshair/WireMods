$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$src=Get-Content "$r\src\Area.cs" -Raw
$start=$src.IndexOf('        internal static void Invalidate(Transform owner)')
if($start -lt 0){throw 'Production Area.Invalidate not found'}
$bodyStart=$src.IndexOf('{',$start)
$depth=0
$end=-1
for($i=$bodyStart;$i -lt $src.Length;$i++){
 if($src[$i] -eq '{'){$depth++}
 elseif($src[$i] -eq '}'){$depth--;if($depth -eq 0){$end=$i;break}}
}
if($end -lt 0){throw 'Brace mismatch'}
$method=$src.Substring($start,$end-$start+1)
$prefix=@'
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
'@
$suffix=@'
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
'@
$dest="$r\dev\AreaInvalidationTests\Program.cs"
[System.IO.File]::WriteAllText($dest,$prefix+[Environment]::NewLine+$method+[Environment]::NewLine+$suffix,[System.Text.UTF8Encoding]::new($false))
dotnet run --project "$r\dev\AreaInvalidationTests\AreaInvalidationTests.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Area test failed'}
