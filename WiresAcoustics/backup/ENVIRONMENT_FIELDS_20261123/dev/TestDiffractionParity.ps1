$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$asm=[Reflection.Assembly]::LoadFrom('C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll')
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$d=$asm.GetType('WiresAcoustics.Diffraction',$true)
$paths=$asm.GetType('WiresAcoustics.SpectralPaths',$true)
$loss=$d.GetMethod('Loss',$flags)
$round=$d.GetMethod('RoundDetour',$flags)
$lit=$d.GetMethod('LitEdge',$flags)
$high=$paths.GetMethod('HighEnergy',$flags)
function Require($ok,$message){if(!$ok){throw "FAIL: $message"}}
function Loss([single]$delta,[single]$freq){return [double]$loss.Invoke($null,[object[]]@($delta,$freq))}
function High([single]$g,[single]$c){return [double]$high.Invoke($null,[object[]]@($g,$c))}
function Round([single]$delta){[object[]]$a=@($delta,[single]0,[single]0);[void]$round.Invoke($null,$a);return [double[]]@($a[1],$a[2])}
function Lit([single]$delta){[object[]]$a=@($delta,[single]0,[single]0);[void]$lit.Invoke($null,$a);return [double[]]@($a[1],$a[2])}
$tested=0
foreach($delta in @(0,.05,.1,.25,.5,1,2,5,10,20,35,50)){
 $rr=Round $delta
 $expectedLow=[Math]::Pow(10,-(Loss $delta 125)/10)
 $expectedHigh=[Math]::Pow(10,-(Loss $delta 2000)/10)
 Require ([Math]::Abs($rr[0]*$rr[0]-$expectedLow) -lt .00002) "round low delta=$delta"
 Require ([Math]::Abs((High $rr[0] $rr[1])-$expectedHigh) -lt .0002) "round high delta=$delta"
 $tested+=2
}
foreach($delta in @(0,.001,.005,.02,.1,.3,.5)){
 $lr=Lit $delta
 Require ($lr[0] -ge 0 -and $lr[0] -le 1) "lit gain bounds $delta"
 Require ($lr[1] -ge 1 -and $lr[1] -le 22000) "lit cutoff bounds $delta"
 $tested+=2
}
"DIFFRACTION->DIGITAL-DSP SPECTRAL PARITY PASSED: $tested checks"
