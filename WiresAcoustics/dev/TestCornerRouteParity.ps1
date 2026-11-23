$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$code=Get-Content "$root\src\Batch.cs" -Raw
if($code -notmatch 'SpectralPaths\.Cascade\(cg, cc, exitGain, exitCut,'){throw 'Corner serial legs are not combined spectrally'}
if($code -notmatch 'SpectralPaths\.Cascade\(cg, cc, bg, bc, out cg, out cc\)'){throw 'Corner diffraction must cascade after both measured legs'}
if($code -match 'cut = Mathf.Min\(cut, bendCut\)' -or $code -match 'cc = Mathf.Min\(cc, bc\)'){throw 'Old corner spectral shortcut remains'}
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$a=[Reflection.Assembly]::LoadFrom("$root\src\bin\Release\net472\WiresAcoustics.dll")
$t=$a.GetType('WiresAcoustics.SpectralPaths')
$f=[Reflection.BindingFlags]'NonPublic,Static'
$cas=$t.GetMethod('Cascade',$f)
$he=$t.GetMethod('HighEnergy',$f)
function HighEnergyTest([single]$g,[single]$c){[double]$he.Invoke($null,[object[]]@($g,$c))}
function C([single]$g,[single]$c,[single]$b,[single]$d){[object[]]$args=@($g,$c,$b,$d,[single]0,[single]0);[void]$cas.Invoke($null,$args);return [double[]]@($args[4],$args[5])}
$count=0
foreach($g in @(.05,.25,.5,1.0)){
 foreach($bg in @(.05,.25,.5,1.0)){
  foreach($cut in @(100,400,2000,9000,22000)){
   foreach($bendCut in @(100,400,2000,9000,22000)){
    $out=C $g $cut $bg $bendCut
    $expected=(HighEnergyTest $g $cut)*(HighEnergyTest $bg $bendCut)
    $got=HighEnergyTest ([single]$out[0]) ([single]$out[1])
    if([Math]::Abs($expected-$got) -gt .0001){throw "Spectral mismatch: $g $cut $bg $bendCut"}
    if([Math]::Abs($out[0]-$g*$bg) -gt .0001){throw 'Incorrect DC gain'}
    $count++
   }
  }
 }
}
'CORNER ROUTE CHECK PASS: '+$count+' path combinations; source call sites checked'
