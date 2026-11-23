$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$dll='C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll'
$asm=[Reflection.Assembly]::LoadFrom($dll)
$t=$asm.GetType('WiresAcoustics.SpectralPaths',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$rate=$t.GetMethod('SetSampleRate',$flags)
$high=$t.GetMethod('HighEnergy',$flags)
$decode=$t.GetMethod('Decode',$flags)
$combine=$t.GetMethod('Combine',$flags)
$cascade=$t.GetMethod('Cascade',$flags)
function SetRate([int]$r) { [void]$rate.Invoke($null,[object[]]@($r)) }
function HE([single]$g,[single]$c) { [double]$high.Invoke($null,[object[]]@($g,$c)) }
function Decode([single]$dc,[single]$hi) {
 [object[]]$a=@($dc,$hi,[single]0,[single]0)
 [void]$decode.Invoke($null,$a)
 return [double[]]@($a[2],$a[3])
}
function Combine([single]$g1,[single]$c1,[single]$g2,[single]$c2) {
 [object[]]$a=@($g1,$c1,$g2,$c2,[single]0,[single]0)
 [void]$combine.Invoke($null,$a)
 return [double[]]@($a[4],$a[5])
}
function Cascade([single]$g1,[single]$c1,[single]$g2,[single]$c2) {
 [object[]]$a=@($g1,$c1,$g2,$c2,[single]0,[single]0)
 [void]$cascade.Invoke($null,$a)
 return [double[]]@($a[4],$a[5])
}
function Require([bool]$yes,[string]$why) {if(!$yes) {throw "FAIL: $why"}}
# Independently modeled discrete-time cascade with the same coefficient
# equation as AcousticFilter.SmoothOcclusion; not SpectralPaths.Response.
function PhysicalPower([double]$cut,[int]$hz) {
 if ($cut -ge 20000) { return 1.0 }
 $stage=1.5538; $f=2000.0
 $a=1-[Math]::Exp(-2*[Math]::PI*$stage*$cut/$hz)
 $w=2*[Math]::PI*$f/$hz
 $single=$a*$a/($a*$a+2*(1-$a)*(1-[Math]::Cos($w)))
 return $single*$single
}
$checks=0
foreach($hz in @(44100,48000,96000,192000)){
 SetRate $hz
 foreach($cut in @(1.0,5.0,20.0,200.0,1000.0,3500.0,8000.0,16000.0,19000.0,19999.0,20000.0,22000.0)){
  $expected=PhysicalPower $cut $hz
  $actual=HE 1 $cut
  Require ([Math]::Abs($expected-$actual) -lt 1e-6) "model-vs-renderer sample=$hz cut=$cut expected=$expected actual=$actual"
  $checks++
  $recovered=Decode 1 $actual
  $represented=HE ([single]$recovered[0]) ([single]$recovered[1])
  Require ([Math]::Abs($represented-$actual) -lt .00006) "inverse mismatch sample=$hz cut=$cut expected=$actual actual=$represented"
  $checks++
 }
 foreach($one in @(@(.4,200),@(.8,4000),@(.7,15000),@(1.0,22000))){
  foreach($two in @(@(.1,100),@(.3,800),@(.4,12000),@(1.0,22000))){
   $sum=Combine $one[0] $one[1] $two[0] $two[1]
   $totalDc=$one[0]*$one[0]+$two[0]*$two[0]
   $totalHigh=(HE $one[0] $one[1])+(HE $two[0] $two[1])
   if($totalDc -le 1){
    Require ([Math]::Abs($sum[0]*$sum[0]-$totalDc) -lt .00002) "sum DC"
    Require ([Math]::Abs((HE $sum[0] $sum[1])-$totalHigh) -lt .0001) "sum spectrum"
   }
   $series=Cascade $one[0] $one[1] $two[0] $two[1]
   Require ([Math]::Abs($series[0]-$one[0]*$two[0]) -lt .0001) "cascade DC"
   $expectedHigh=(HE $one[0] $one[1])*(HE $two[0] $two[1])
   Require ([Math]::Abs((HE $series[0] $series[1])-$expectedHigh) -lt .0001) "cascade spectrum"
   $checks+=4
  }
 }
}
SetRate 48000
# If multiple strong paths exceed unity, the amplitude cap must preserve
# relative spectral shape rather than changing the tone.
$saturated=Combine 1 400 1 22000
Require ([Math]::Abs($saturated[0]-1) -lt .00001) "saturated energy must cap at unity"
$expectedRatio=((HE 1 400)+(HE 1 22000))/2.0
$actualRatio=HE $saturated[0] $saturated[1]
Require ([Math]::Abs($expectedRatio-$actualRatio) -lt .0001) "saturation changed spectral color"
$checks+=2
"INDEPENDENT DSP PARITY TEST PASS: $checks checks at 44.1, 48, 96, 192 kHz"
