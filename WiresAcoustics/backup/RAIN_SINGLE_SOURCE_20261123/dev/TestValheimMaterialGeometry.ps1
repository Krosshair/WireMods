$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$sources='C:\Users\kajet\WireMods\ValheimSOURCE'
$occ=[IO.File]::ReadAllText("$r\src\Occlusion.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
function Extract([string]$source,[string]$signature) {
 $start=$source.IndexOf($signature,[StringComparison]::Ordinal)
 if($start -lt 0){throw "Missing production method $signature"}
 $begin=$source.IndexOf('{',$start)
 $depth=0
 for($i=$begin;$i -lt $source.Length;$i++) {
  if($source[$i] -eq '{'){$depth++}
  elseif($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($start,$i-$start+1)}}
 }
 throw "Production method brace count failed: $signature"
}
$open=Extract $room 'internal static bool IsCoarseOpenPanel(Collider c, Piece p)'
$material=Extract $occ 'internal static void TransmissionEnergy(WearNTear.MaterialType material,'
$loss=Extract $occ 'private static void Loss(Solids.Info s, WearNTear part)'
$generated='using UnityEngine; namespace WiresAcoustics { internal static class Solids { internal sealed class Info {internal bool Structural,AcousticProxy,Wall;internal float OpenFraction,First,Cut;}'+$open+'} internal static class Occlusion {'+$material+$loss+'internal static void TestLoss(Solids.Info s, WearNTear part) {Loss(s,part);} } }'
[IO.File]::WriteAllText("$r\dev\ValheimMaterialTests\ProductionMethods.cs",$generated,[Text.UTF8Encoding]::new($false))
$asset=Join-Path $sources 'ValheimProject\ExportedProject\Assets\Editor\GameElements\Pieces'
$checks=@(
 @('iron_grate',5,'m_Size: {x: 1.8834926, y: 2.780014, z: 0.2}'),
 @('ashwood_decowall_divider',1,'m_Size: {x: 1, y: 2, z: 0.14663896}'),
 @('ashwood_decowall_2x2',1,'m_Size: {x: 2, y: 2, z: 0.2}'),
 @('darkwood_decowall',1,'m_Size: {x: 1, y: 1, z: 1}'),
 @('piece_dvergr_metal_wall_2x2',2,'m_Size: {x: 2, y: 2, z: 0.1}'),
 @('wood_fence_gate',4,'m_Size: {x: 1.7462871, y: 1.3688736, z: 0.2}'),
 @('wood_fence',26,'m_Size: {x: 0.238672, y: 0.224546, z: 2.1047049}')
)
foreach($row in $checks){
 $path=Join-Path $asset ($row[0]+'.prefab')
 if(!(Test-Path $path)){throw "Missing actual ValheimSOURCE prefab $path"}
 $raw=[IO.File]::ReadAllText($path)
 $count=[regex]::Matches($raw,'(?m)^--- !u!65 ').Count
 if($count -ne [int]$row[1]){throw "Changed prefab collider inventory: $($row[0]) $count != $($row[1])"}
 if(!$raw.Contains([string]$row[2])){throw "Prefab collision dimension mismatch $($row[0])"}
}
foreach($required in @('s.AcousticProxy = IsCoarseOpenPanel(c, p);','s.OpenFraction = OpenPanelArea.FromPrefab','if (s.AcousticProxy)','b.Through *= s.First;')){
 if(!(($room+$occ).Contains($required))){throw "Runtime integration missing $required"}
}
if(!$occ.Contains('low = phi + (1f - phi) * low;') -or !$occ.Contains('mid = phi + (1f - phi) * mid;')){throw 'Area-weighted energy model missing'}
if(!$room.Contains('float solidFraction = hit && surface != null')){throw 'Room enclosure not accounting for open area'}
if($occ.Contains('private const float NextWall')){throw 'Arbitrary extra-wall attenuation has returned'}
if($occ.Contains('b.Walls = 1;'+[Environment]::NewLine+'                b.Through *= Mathf.Pow(10f, -15f / 20f)')){throw 'Artificial saturated-ray wall has returned'}
dotnet run --project "$r\dev\ValheimMaterialTests\ValheimMaterialTests.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Compiled material and prefab tests failed'}
Write-Output "VALHEIM SOURCE PREFAB INVENTORY PASS: $($checks.Count) exact assets verified"
