using System;
namespace UnityEngine {
 internal class GameObject {internal string name="";}
 internal struct Vector3 {internal float x,y,z;internal Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 internal class Collider {}
 internal static class Mathf {internal static float Clamp01(float f)=>Math.Clamp(f,0f,1f);}
 internal class BoxCollider:Collider {internal Vector3 size;internal BoxCollider(float x,float y,float z){size=new(x,y,z);}}
}
internal sealed class Piece {internal UnityEngine.GameObject gameObject=new();internal Piece(string n){gameObject.name=n;}}
internal sealed class WearNTear {internal enum MaterialType {Wood,Stone,Iron,HardWood,Marble,Ashstone,Ancient,Ice,Timberwood} internal MaterialType m_materialType;}
namespace WiresAcoustics {
 internal static class SpectralPaths {internal static void Decode(float low,float mid,out float gain,out float cutoff){gain=(float)Math.Sqrt(low);cutoff=mid/Math.Max(low,1e-10f);}}
}
internal static class Tests {
 static int checks;
 static void Check(bool yes,string why){if(!yes)throw new Exception("FAILED "+why);checks++;}
 static bool Proxy(string name,float x,float y,float z)=>WiresAcoustics.Solids.IsCoarseOpenPanel(new UnityEngine.BoxCollider(x,y,z),new Piece(name));
 static void Material(WearNTear.MaterialType kind,float low,float mid){
  WiresAcoustics.Occlusion.TransmissionEnergy(kind,out float actualLow,out float actualMid);
  Check(Math.Abs(actualLow-low)<1e-6f&&Math.Abs(actualMid-mid)<1e-6f,"Valve material "+kind);
 }
 static void Main(){
  Material(WearNTear.MaterialType.Wood,.070f,.014f);
  Material(WearNTear.MaterialType.HardWood,.070f,.014f);
  Material(WearNTear.MaterialType.Timberwood,.070f,.014f);
  Material(WearNTear.MaterialType.Ancient,.070f,.014f);
  Material(WearNTear.MaterialType.Iron,.200f,.025f);
  Material(WearNTear.MaterialType.Stone,.015f,.002f);
  Material(WearNTear.MaterialType.Marble,.015f,.002f);
  Material(WearNTear.MaterialType.Ashstone,.015f,.002f);
  Material(WearNTear.MaterialType.Ice,.060f,.044f);
  Material((WearNTear.MaterialType)999,.100f,.050f);
  Check(Proxy("ashwood_decowall_divider",1,2,.14663896f),"ashwood divider coarse collision");
  Check(Proxy("ashwood_decowall_2x2",2,2,.2f),"ashwood window");
  Check(Proxy("darkwood_decowall",1,1,1),"darkwood decorative divider");
  Check(Proxy("piece_dvergr_metal_wall_2x2",2,2,.1f),"dvergr metal screen");
  Check(Proxy("iron_grate",1.8834926f,2.780014f,.2f),"iron gate broad collision");
  Check(!Proxy("iron_grate",.07499999f,2.8400002f,.075f),"iron narrow frame retained");
  Check(Proxy("wood_fence_gate",1.7462871f,1.3688736f,.2f),"fence door broad collision");
  Check(!Proxy("wood_fence_gate",.124428f,2.326968f,.124428f),"fence post retained");
  Check(Proxy("wood_window",.9915862f,1,.15f),"wood window aperture");
  Check(Proxy("ashwood_decowall_divider(Clone)",1,2,.14663896f),"clone instance");
  Check(!Proxy("wood_fence",.238672f,.224546f,2.1047049f),"wood fence actual bars retained");
  Check(!Proxy("woodwall",2,2,.1f),"wood wall retained");
  Check(!Proxy("stone_fence",2,2,.5f),"stone wall retained");
  Check(!Proxy("random_modded_wall",2,2,.1f),"unknown walls retained");
  Check(!WiresAcoustics.Solids.IsCoarseOpenPanel(new UnityEngine.BoxCollider(2,2,.1f),null!),"null piece");
  Check(!WiresAcoustics.Solids.IsCoarseOpenPanel(new UnityEngine.Collider(),new Piece("iron_grate")),"mesh colliders retained");
  foreach(float phi in new[]{0f,.2f,.5f,.8f,1f}) {
    foreach(var material in new[]{WearNTear.MaterialType.Iron,WearNTear.MaterialType.Wood,WearNTear.MaterialType.Stone}) {
      WiresAcoustics.Occlusion.TransmissionEnergy(material,out float low,out float mid);
      var info=new WiresAcoustics.Solids.Info{Structural=true,AcousticProxy=true,OpenFraction=phi};
      WiresAcoustics.Occlusion.TestLoss(info,new WearNTear{m_materialType=material});
      float expectedLow=phi+(1f-phi)*low;
      float expectedMid=phi+(1f-phi)*mid;
      Check(info.Wall,"composite is still an acoustic surface");
      Check(Math.Abs(info.First*info.First-expectedLow)<.00001f,"composite low energy");
      Check(Math.Abs(info.Cut-expectedMid/expectedLow)<.00001f,"composite high/low ratio");
    }
  }
  var solid=new WiresAcoustics.Solids.Info{Structural=true};
  WiresAcoustics.Occlusion.TestLoss(solid,new WearNTear{m_materialType=WearNTear.MaterialType.Wood});
  Check(Math.Abs(solid.First*solid.First-.07f)<.00001f,"solid wooden wall unchanged");
  Console.WriteLine($"VALHEIM PREFAB + STEAM AUDIO MATERIAL SOURCE TEST PASS: {checks} compiled-production cases");
 }
}
