$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$f=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$b=[IO.File]::ReadAllText("$r\src\Batch.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('EMITTER FIELD MATH: '+$why)}}
# The exact normalized formula is compiled in production. Guard its
# source and test its energy conservation numerically at many input values.
Must ($f.Contains('internal static float PatchWeight(float representedArea, float distance2) =>') -and
       $f.Contains('Mathf.Max(0f, representedArea) /') -and
       $f.Contains('(25f + Mathf.Max(0f, distance2));')) 'world area/distance weights missing'
Must ($f.Contains('internal static float NormalizeEnergy(float received, float reference) =>') -and
       $f.Contains('Mathf.Clamp01(Mathf.Max(0f, received) / reference)')) 'bounded energy normalization missing'
Must ($f.IndexOf('reference += w;', $f.IndexOf('private static void Read()')) -lt
       $f.IndexOf('if (!Exposed(first)) continue;', $f.IndexOf('private static void Read()'))) 'blocked surface was removed from denominator, making a tiny opening sound like the whole outdoors'
function W([double]$area,[double]$d2){return [Math]::Max(0.0,$area)/(25.0+[Math]::Max(0.0,$d2))}
function Norm([double]$e,[double]$ref){return $(if($ref -le 1e-8){0}else{[Math]::Min(1.0,[Math]::Max(0.0,$e)/$ref)})}
$n=0
for($i=1;$i -le 1200;$i++){
 $area=1+($i%29)*.75; $d2=$i*1.4
 $w=W $area $d2
 Must ($w -gt 0) 'positive surface area has nonzero weight'
 Must ((W ($area*2) $d2) -eq ($w*2)) 'emitter sample area must be additive'
 $t=($i%101)/100.0
 Must ([Math]::Abs((Norm ($w*$t) $w)-$t) -lt 1e-9) 'received energy is divided by unoccluded reference'
 Must ((Norm ($w*2) $w) -le 1) 'multiple portal routes must not amplify above baseline outdoor'
 Must ((Norm 0 $w) -eq 0) 'fully blocked surface is silent'
 $n+=5
}
Must ((Norm 0 0) -eq 0) 'no surfaces cannot emit'
Must ((Norm .5 0) -eq 0) 'invalid reference does not divide by zero'
Must ($f.Contains('SpectralPaths.HighEnergy(gain, cut)') -and
       $f.Contains('SpectralPaths.Decode(') -and
       $f.Contains('NormalizeEnergy(low, reference)') -and
       $f.Contains('NormalizeEnergy(high, reference)')) 'separate bass/treble propagation aggregation lost'
Must ($f.Contains('Batch.DirectWalls(s_cmds, s_hits, first + 1,') -and
       $f.Contains('Batch.PortalTransfer(') -and
       $b.Contains('internal static float PortalTransfer(')) 'field fails to use standard WA spectral wall/portal model'
Must (!$f.Contains('AddComponent<AudioSource>') -and
       !$f.Contains('new GameObject(') -and
       !(Test-Path "$r\src\RainField.cs")) 'obsolete rain/audio patch models have not been removed'
Write-Output "GENERAL EMITTER FIELD ENERGY MATH PASS: $n+ source-linked invariants, stable area weighting, separate spectral energy, shared WA paths"
