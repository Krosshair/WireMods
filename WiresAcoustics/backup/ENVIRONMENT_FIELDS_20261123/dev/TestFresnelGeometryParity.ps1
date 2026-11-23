$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$asm=[Reflection.Assembly]::LoadFrom("$root\src\bin\Release\net472\WiresAcoustics.dll")
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$f=$asm.GetType('WiresAcoustics.FresnelEdge',$true)
$s=$asm.GetType('WiresAcoustics.SpectralPaths',$true)
$dist=$f.GetMethod('PathDistances',$flags)
$param=$f.GetMethod('Parameter',$flags)
$intensity=$f.GetMethod('Intensity',$flags)
$transfer=$f.GetMethod('Transfer',$flags)
$response=$s.GetMethod('Response',$flags)
$high=$s.GetMethod('HighEnergy',$flags)
function Require([bool]$ok,[string]$msg){if(!$ok){throw "FAIL $msg"}}
function Paths([single]$len,[single]$fraction) {
 [object[]]$p=@($len,$fraction,[single]0,[single]0)
 [void]$dist.Invoke($null,$p)
 return [double[]]@($p[2],$p[3])
}
function Par([single]$h,[single]$d1,[single]$d2) {
 return [double]$param.Invoke($null,[object[]]@($h,$d1,$d2,[single]2000))
}
function Intensity([single]$clear,[single]$d1,[single]$d2,[single]$hz,[single]$t) {
 return [double]$intensity.Invoke($null,[object[]]@($clear,$d1,$d2,$hz,$t))
}
function Transfer([single]$clear,[single]$d1,[single]$d2,[single]$wall,[single]$cut) {
 [object[]]$a=@($clear,$d1,$d2,$wall,$cut,[single]0,[single]0,[single]0)
 [void]$transfer.Invoke($null,$a)
 return [double[]]@($a[5],$a[6],$a[7])
}
function High([single]$g,[single]$c){return [double]$high.Invoke($null,[object[]]@($g,$c))}
function Resp([single]$cut){return [double]$response.Invoke($null,[object[]]@($cut))}
$checked=0
foreach($offset in @(0.0,1.0,4.0,12.0)){
 foreach($near in @(1.0,2.0,5.0)){
  foreach($far in @(1.0,3.0,8.0)){
   $length=[Math]::Sqrt(($near+$far)*($near+$far)+$offset*$offset)
   $fraction=$near/($near+$far)
   $pair=Paths $length $fraction
   Require ([Math]::Abs($pair[0]-$length*$fraction) -lt 1e-5) "listener distance oblique=$offset"
   Require ([Math]::Abs($pair[1]-$length*(1-$fraction)) -lt 1e-5) "source distance oblique=$offset"
   Require ([Math]::Abs($pair[0]+$pair[1]-$length) -lt 1e-5) "path length conservation"
   $v=Par .35 $pair[0] $pair[1]
   $expected=.35*[Math]::Sqrt(2*(1/$pair[0]+1/$pair[1])*2000/343)
   Require ([Math]::Abs($v-$expected) -lt 1e-5) "ITU diffraction parameter"
   $checked+=4
  }
 }
}
foreach($cut in @(200.0,500.0,1000.0,4000.0,12000.0,22000.0)){
 foreach($wall in @(0.0,.1,.5,.9,1.0)){
  foreach($clear in @(-.1,0,.1)){
   $trans=Transfer $clear 2 3 $wall $cut
   $th=$wall*[Math]::Sqrt((Resp $cut))
   $expectLow=Intensity $clear 2 3 125 $wall
   $expectHigh=Intensity $clear 2 3 2000 $th
   $expectedHigh=[Math]::Min($expectLow,$expectHigh)
   Require ([Math]::Abs($trans[0]*$trans[0]-$expectLow) -lt .00003) "wall/DC transfer"
   Require ([Math]::Abs((High $trans[0] $trans[1])-$expectedHigh) -lt .0006) "wall/HF transfer cut=$cut wall=$wall clear=$clear measured=$(High $trans[0] $trans[1]) expected=$expectedHigh outCut=$($trans[1])"
   $checked+=2
  }
 }
}
$source=Get-Content "$root\src\ContinuousEdges.cs" -Raw
Require ($source.Contains('FresnelEdge.PathDistances(direct.magnitude, t,')) 'the geometry helper is not used by production ContinuousEdges'
$wallSource=Get-Content "$root\src\FresnelEdge.cs" -Raw
Require ($wallSource.Contains('SpectralPaths.Response(wallCutoff)')) 'real digital DSP response not used in wall transmission'
"FRESNEL GEOMETRY AND TRANSMISSION PARITY PASSED: $checked cases, 4 angles and 6 cutoff settings"
