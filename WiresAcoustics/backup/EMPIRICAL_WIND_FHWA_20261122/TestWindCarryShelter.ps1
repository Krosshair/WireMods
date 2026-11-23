$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$directions=[IO.File]::ReadAllText((Join-Path $r 'src\Directions.cs'))
$ambient=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$start=$directions.IndexOf('internal static float WindCarryDb(')
if($start -lt 0){throw 'Missing production wind function'}
$open=$directions.IndexOf('{',$start)
$d=0;$end=-1
for($i=$open;$i -lt $directions.Length;$i++){
 if($directions[$i] -eq '{'){$d++}
 if($directions[$i] -eq '}'){$d--;if($d -eq 0){$end=$i;break}}
}
if($end -lt 0){throw 'Unclosed WindCarryDb'}
$method=$directions.Substring($start,$end-$start+1).Replace('internal static float','public static float')
$stub=@'
using System;
public static class Mathf {
 public static float Clamp01(float x) {return Math.Max(0f,Math.Min(1f,x));}
}
public static class Model {
'@+[Environment]::NewLine+$method+[Environment]::NewLine+'}'
Add-Type -TypeDefinition $stub -ErrorAction Stop
function Near([double]$got,[double]$want,[double]$epsilon,[string]$name){
 if([Math]::Abs($got-$want) -gt $epsilon){throw ("WIND CARRY: {0}, got {1}, want {2}" -f $name,$got,$want)}
}
Near ([Model]::WindCarryDb(7,112,1,$true)) 3 0.0001 'full wind exposed downwind'
Near ([Model]::WindCarryDb(-4,112,1,$true)) -10 0.0001 'full wind exposed upwind'
Near ([Model]::WindCarryDb(7,112,0,$true)) 0 0.0001 'fully sheltered downwind'
Near ([Model]::WindCarryDb(-4,112,0,$true)) 0 0.0001 'fully sheltered upwind'
Near ([Model]::WindCarryDb(7,112,.5,$true)) 1.5 0.0001 'half sheltered downwind'
Near ([Model]::WindCarryDb(-4,112,.5,$true)) -5 0.0001 'half sheltered upwind'
Near ([Model]::WindCarryDb(7,112,.5,$false)) 0 0.0001 'Wind toggle off'
Near ([Model]::WindCarryDb(-4,14,1,$true)) 0 0.0001 'close sounds wind-neutral'
Near ([Model]::WindCarryDb(7,63.5,1,$true)) 1.5 0.0001 'distance ramp'
Near ([Model]::WindCarryDb(-4,63.5,1,$true)) -5 0.0001 'distance ramp upwind'
Near ([Model]::WindCarryDb(7,112,2,$true)) 3 0.0001 'exposure clamped high'
Near ([Model]::WindCarryDb(-4,112,-2,$true)) 0 0.0001 'exposure clamped low'
Near ([Model]::WindCarryDb(7,5000,1,$true)) 3 0.0001 'long distance clamped'
Near ([Model]::WindCarryDb(0,112,1,$true)) 0 0.0001 'still air'
function Require([bool]$ok,[string]$reason){if(!$ok){throw 'WIND CARRY INTEGRATION: '+$reason}}
Require ($directions.Contains('float exposure = Plugin.Occlusion.Value ? Ambience.WindExposure : 1f;')) 'listener shelter not linked'
Require ($directions.Contains('f.DistanceGain = f.PastRange(er.w) * Mathf.Pow(10f, carryDb / 20f);')) 'distance rolloff lost'
Require ($directions.Contains('if (slow && f.Placed)')) 'wind not limited to positional sources or throttled'
Require ($ambient.Contains('internal static float WindExposure => Room.WindSurveyReady ?')) 'room wind exposure not exposed'
Require ($ambient.Contains('s_exposureTarget = Room.Exposure(windFrom, out Vector3 way);')) 'wind heading changes while idle not measured'
Require ($ambient.Contains('Vector3.Dot(windFrom, s_exposureWindFrom) < .992f')) 'wind direction refresh re-run unconditionally'
Require ($room.Contains('internal static bool WindSurveyReady => s_haveCurrent && s_haveCast;')) 'uninitialized room guard missing'
Require ($ambient.Contains('s_exposureWindFrom = Vector3.zero; s_windWay = Vector3.zero;')) 'wind reset missing'
Write-Output 'WIND CARRY SHELTER TEST PASS: source-linked dB curve, 0-100% room exposure, wind settings, heading changes, resets and falloff'
