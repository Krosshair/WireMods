$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$area=[IO.File]::ReadAllText("$r\src\Area.cs")
$volume=[IO.File]::ReadAllText("$r\src\SourceVolume.cs")
$occlusion=[IO.File]::ReadAllText("$r\src\Occlusion.cs")
$match=[regex]::Matches($area,'(?s)internal static bool SurfaceDomain\s*\([^)]*\)\s*=>[^;]+;')
if($match.Count -ne 1){throw 'Expected exactly one production surface domain predicate'}
if(!$area.Contains('Transform owner = f.ShapeRoot != null ? f.ShapeRoot : NearestOwner(sound);')){throw 'New source ownership not used'}
if(!$area.Contains('f.ShapeCenter = Vector3.zero;') -or !$area.Contains('f.ShapeHalf = Vector3.zero;') -or !$area.Contains('f.AreaRadius = 0f;')){throw 'Stale source bounds not reset on measurement'}
if(!$occlusion.Contains('if (starting && f != null)') -or !$occlusion.Contains('f.AreaKnown = false;')){throw 'Playback source geometry not refreshed'}
if(!$volume.Contains('float dist = (p.Center - listener).magnitude;')){throw 'Volume sampling still budgets from spawn pivot'}
if(!$occlusion.Contains('"; emission " + domain + ", owner " + owner +')){throw 'All-source diagnostics missing ownership'}
foreach($name in @('Area.cs','Occlusion.cs','SourceVolume.cs')){
 $s=[IO.File]::ReadAllText("$r\src\$name")
 if($s.Contains('StartCoroutine(') -or $s.Contains('void LateUpdate(')){throw ('New frame or coroutine update in '+$name)}
}
$cs=@'
using System;
namespace WiresAcoustics {
 internal static class Area {
@@METHOD@@
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
'@
$cs=$cs.Replace('@@METHOD@@',$match[0].Value)
[IO.File]::WriteAllText("$r\dev\GeneralSourceGeometryTests\Program.cs",$cs,[Text.UTF8Encoding]::new($false))
dotnet run --project "$r\dev\GeneralSourceGeometryTests\GeneralSourceGeometryTests.csproj" -c Release --no-restore
if($LASTEXITCODE -ne 0){throw 'General source domain compiled tests failed'}
Write-Output 'GENERAL SOURCE OWNERSHIP + SHAPE CONTRACT PASS'
