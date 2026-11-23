using System;
namespace WiresAcoustics {
 internal static class Area {
internal static bool SurfaceDomain(bool owner, bool localized,
                                           bool fire, bool weapon) =>
            owner && !localized && !fire && !weapon;
 }
}
internal static class Test {
 static void Main() {
  int checks=0;
  foreach(bool owner in new[]{false,true})
   foreach(bool localized in new[]{false,true})
    foreach(bool fire in new[]{false,true})
     foreach(bool weapon in new[]{false,true}){
      bool expected=owner&&!localized&&!fire&&!weapon;
      bool actual=WiresAcoustics.Area.SurfaceDomain(owner,localized,fire,weapon);
      if(actual!=expected)throw new Exception($"Bad domain for {owner}/{localized}/{fire}/{weapon}");
      checks++;
     }
  Console.WriteLine($"GENERAL SOURCE DOMAIN PASS: {checks} real production classifications; point, fire, weapon and unowned preserved");
 }
}