$ErrorActionPreference='Stop'
$unity='D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll'
[void][Reflection.Assembly]::LoadFrom($unity)
$asm=[Reflection.Assembly]::LoadFrom('C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll')
$f=[Reflection.BindingFlags]'NonPublic,Static'
$paths=$asm.GetType('WiresAcoustics.SpectralPaths',$true)
$high=$paths.GetMethod('HighEnergy',$f)
$combine=$paths.GetMethod('Combine',$f)
function High([float]$gain,[float]$cut) { return [double]$high.Invoke($null,[object[]]@($gain,$cut)) }
function Combine([float]$g1,[float]$c1,[float]$g2,[float]$c2) {
  [object[]]$args=@($g1,$c1,$g2,$c2,[single]0,[single]0)
  [void]$combine.Invoke($null,$args)
  return [double[]]@($args[4],$args[5])
}
function Assert([bool]$ok,[string]$text) {if(!$ok){throw 'FAIL '+$text}}
foreach($g in @(0,.02,.1,.25,.5,1.0)){
  foreach($cut in @(200,400,1600,8000,22000)){
    $result=Combine $g $cut 0 22000
    Assert ([Math]::Abs($result[0]-$g) -lt .0001) "identity gain $g cutoff $cut"
    if($g -gt .001 -and $cut -lt 22000) {
      Assert ([Math]::Abs($result[1]-$cut) -lt .2) "identity cutoff $g $cut got $($result[1])"
    }
  }
}
$combined=Combine .5 400 .5 22000
$targetHigh=(High .5 400)+(High .5 22000)
$actualHigh=High ([float]$combined[0]) ([float]$combined[1])
Assert ([Math]::Abs($actualHigh-$targetHigh) -lt .00001) 'spectral sum high frequency'
Assert ([Math]::Abs($combined[0]-[Math]::Sqrt(.5)) -lt .0001) 'spectral sum low frequency'
foreach($g in @(0,.1,.5,1)){
 foreach($cut in @(200,1000,10000,22000)){
   $r=Combine $g $cut $g $cut
   Assert ((-not [double]::IsNaN($r[0])) -and $r[0] -ge 0 -and $r[0] -le 1 -and $r[1] -ge 200 -and $r[1] -le 22000) 'bounded mix'
 }
}
$cascade=$paths.GetMethod('Cascade',$f)
function Cascade([float]$g1,[float]$c1,[float]$g2,[float]$c2) {
  [object[]]$args=@($g1,$c1,$g2,$c2,[single]0,[single]0)
  [void]$cascade.Invoke($null,$args)
  return [double[]]@($args[4],$args[5])
}
foreach($g1 in @(.05,.25,.5,1.0)){
 foreach($g2 in @(.05,.25,.5,1.0)){
  foreach($c1 in @(200,1500,8000,22000)){
   foreach($c2 in @(200,1500,8000,22000)){
    $result=Cascade $g1 $c1 $g2 $c2
    $expectedLow=$g1*$g2
    $expectedHigh=(High $g1 $c1)*(High $g2 $c2)
    $actualHigh=High ([single]$result[0]) ([single]$result[1])
    Assert ([Math]::Abs($result[0]-$expectedLow) -lt .0001) 'cascade low band identity'
    Assert ([Math]::Abs($actualHigh-$expectedHigh) -lt .0001) "cascade high band transmission c1=$c1 c2=$c2 g1=$g1 g2=$g2 actual=$actualHigh expected=$expectedHigh"
    $swapped=Cascade $g2 $c2 $g1 $c1
    Assert ([Math]::Abs($swapped[0]-$result[0]) -lt .0001 -and [Math]::Abs($swapped[1]-$result[1]) -lt .1) 'cascade commutative'
   }
  }
 }
}
'SPECTRAL SUM + CASCADE: PASS'
