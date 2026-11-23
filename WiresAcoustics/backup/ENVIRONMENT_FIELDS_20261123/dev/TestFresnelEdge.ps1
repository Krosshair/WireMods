$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom('D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\UnityEngine.CoreModule.dll')
$asm=[Reflection.Assembly]::LoadFrom('C:\Users\kajet\WireMods\WiresAcoustics\src\bin\Release\net472\WiresAcoustics.dll')
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$t=$asm.GetType('WiresAcoustics.FresnelEdge',$true)
$field=$t.GetMethod('Field',$flags)
$intensity=$t.GetMethod('Intensity',$flags)
$transfer=$t.GetMethod('Transfer',$flags)
function F([float]$v) {
  [object[]]$a=@($v,[single]0,[single]0)
  [void]$field.Invoke($null,$a)
  return @([double]$a[1],[double]$a[2])
}
function I([float]$clearance,[float]$transmission,[float]$frequency=125) {
  [object[]]$a=@($clearance,[single]2,[single]3,$frequency,$transmission)
  return [double]$intensity.Invoke($null,$a)
}
function Transfer([float]$clearance,[float]$transmission) {
  [object[]]$a=@($clearance,[single]2,[single]3,$transmission,[single]1400,[single]0,[single]0,[single]0)
  [void]$transfer.Invoke($null,$a)
  return [double[]]@($a[5],$a[6],$a[7])
}
function Check([bool]$ok,[string]$reason) {if(!$ok) { throw ('FAIL: '+$reason)}}
$at=F 0
# Independently calculated with SciPy special.fresnel, not this implementation.
$reference=@(@(0.25,0.62896738,-0.12079178),@(0.5,0.77853833,-0.21380590),@(1.0,1.10907627,-0.17081713),@(2.0,0.91583454,-0.07241886),@(3.0,1.05101689,-0.05470390),@(5.0,1.03141129,-0.03221990),@(8.0,0.98000820,-0.01979398))
foreach($referenceRow in $reference){$actual=F ([single]$referenceRow[0]); Check ([Math]::Abs($actual[0]-$referenceRow[1]) -lt .001 -and [Math]::Abs($actual[1]-$referenceRow[2]) -lt .001) ('Independent Fresnel integral check at '+$referenceRow[0])}
Check ([Math]::Abs($at[0]-.5) -lt 1e-5 -and [Math]::Abs($at[1]) -lt 1e-5) 'Fresnel 0 field'
$negative=F -3
$positive=F 3
Check ([Math]::Abs($positive[0]+$negative[0]-1) -lt 2e-5) 'Fresnel complementary real fields'
Check ([Math]::Abs($positive[1]+$negative[1]) -lt 2e-5) 'Fresnel complementary imaginary fields'
Check ([Math]::Abs((I 0 0)-.25) -lt 1e-5) 'Opaque edge must be -6.02 dB'
Check ([Math]::Abs((I 0 1)-1) -lt 1e-5) 'Transparent edge must be fully transparent'
foreach($tval in @(0.0,.1,.25,.5,.75,1.0)){
 foreach($v in @(-3.0,-1.0,-.5,-.05,0.0,.05,.5,1.0,3.0)){
  $i=I $v $tval
  Check ((-not [double]::IsNaN($i)) -and $i -ge 0 -and $i -le 1.000001) ('Intensity bounds t='+$tval+' clearance='+$v)
 }
}
foreach($v in @(-3.0,-1.0,-.2,0.0,.2,1.0,3.0)){
 Check ([Math]::Abs((I $v 1)-1) -lt 1e-6) ('Transparent wall must always yield 1 at '+$v)
}
foreach($tval in @(0.0,.1,.5,.9,1.0)){
 $nearLeft=I -.00001 $tval
 $nearRight=I .00001 $tval
 Check ([Math]::Abs($nearLeft-$nearRight) -lt .0002) ('Boundary discontinuity T='+$tval)
}
foreach($tval in @(0.0,.1,.5,.9,1.0)){
 foreach($v in @(-2.0,-.3,0.0,.3,2.0)){
  $o=Transfer $v $tval
  Check ((-not [double]::IsNaN($o[0])) -and $o[0] -ge 0 -and $o[0] -le 1 -and $o[1] -ge 200 -and $o[1] -le 22000 -and $o[2] -ge 0 -and $o[2] -le 1) ('DSP output bounds T='+$tval+' v='+$v)
 }
}
'FRESNEL COMPILED MODEL: PASS'
'At edge opaque: pressure='+((F 0)[0])+' intensity='+(I 0 0)+' gain='+((Transfer 0 0)[0])
'Transparent: '+(I 0 1)
'Far clear, opaque: '+(I 3 0)
'Far shadow, opaque: '+(I -3 0)
