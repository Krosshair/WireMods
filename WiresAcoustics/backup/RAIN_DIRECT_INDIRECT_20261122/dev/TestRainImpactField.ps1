$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$field=[IO.File]::ReadAllText((Join-Path $r 'src\RainField.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$amb=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$weather=[IO.File]::ReadAllText((Join-Path $r 'src\WeatherAcoustics.cs'))
function Must([bool]$ok,[string]$message){if(!$ok){throw ('RAIN IMPACT FIELD: '+$message)}}
$start=$field.IndexOf('        internal static int Sector(Vector3 d)')
$end=$field.IndexOf('        internal static void Measure(Vector3 from', $start)
Must ($start -ge 0 -and $end -gt $start) 'cannot extract actual mathematical production methods'
$actual=$field.Substring($start,$end-$start).Replace('internal static ','public static ')
# Windows PowerShell's Add-Type uses the legacy C# compiler. Convert only
# two expression-bodied pure methods into equivalent C# 5 function bodies.
$actual=$actual.Replace('public static bool ImpactRay(Vector3 d) =>',
    'public static bool ImpactRay(Vector3 d) { return').Replace(
    'd.y >= -.24f && d.y <= .65f;',
    'd.y >= -.24f && d.y <= .65f; }')
$actual=$actual.Replace('public static float Exposure(float open, float total) =>',
    'public static float Exposure(float open, float total) { return').Replace(
    'total > .001f ? Mathf.Sqrt(Mathf.Clamp01(open / total)) : 0f;',
    'total > .001f ? Mathf.Sqrt(Mathf.Clamp01(open / total)) : 0f; }')
$stub=@'
using System;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;
  public Vector3(float X,float Y,float Z){x=X;y=Y;z=Z;}
 }
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Atan2(float a,float b) {return (float)Math.Atan2(a,b);}
  public static int Min(int a,int b) {return Math.Min(a,b);}
  public static float Sqrt(float a) {return (float)Math.Sqrt(a);}
  public static float Clamp01(float a) {return Math.Max(0f,Math.Min(1f,a));}
 }
}
namespace WiresAcoustics { public static class RainField {
 public const int Sectors = 8;
'@
$foot='} }'
Add-Type -TypeDefinition ($stub+[Environment]::NewLine+$actual+[Environment]::NewLine+$foot) -ErrorAction Stop
$type=[WiresAcoustics.RainField]
function V([double]$x,[double]$y,[double]$z){return [UnityEngine.Vector3]::new($x,$y,$z)}
Must ($type::Sector((V 1 0 0)) -eq 0) 'positive X sector'
Must ($type::Sector((V 0 0 1)) -eq 2) 'positive Z sector'
Must ($type::Sector((V -1 0 0)) -eq 4) 'negative X sector'
Must ($type::Sector((V 0 0 -1)) -eq 6) 'negative Z sector'
Must ($type::Sector((V 1 0 -.01)) -eq 7) 'angle wrapping across 0/360'
Must ($type::ImpactRay((V 1 0 0))) 'horizontal world exposure ray'
Must (!$type::ImpactRay((V 0 .7 1))) 'skywards overhead rays excluded (not ground region)'
Must (!$type::ImpactRay((V 0 -.3 1))) 'downward interior ground rays excluded'
Must ([Math]::Abs($type::Exposure(.25,1)-.5) -lt .00001) 'continuous solid angle openness'
Must ($type::Exposure(0,0) -eq 0 -and $type::Exposure(1,1) -eq 1) 'extreme coverage'
Must ($type::Exposure(3,1) -eq 1) 'clamp physical maximum'
$counts=New-Object int[] 8
$gold=[Math]::PI*(3-[Math]::Sqrt(5))
$open=New-Object int[] 8
for($i=0;$i -lt 64;$i++){
 $y=1-($i+.5)/64*2
 $rad=[Math]::Sqrt(1-$y*$y)
 $az=$gold*$i
 $v=V ($rad*[Math]::Cos($az)) $y ($rad*[Math]::Sin($az))
 if(!$type::ImpactRay($v)){continue}
 $sector=$type::Sector($v)
 $counts[$sector]++
 # Simple synthetic wide doorway toward +X, 80 degree arc.
 $ang=[Math]::Atan2($v.z,$v.x)
 if([Math]::Abs($ang) -lt .70){$open[$sector]++}
}
Must (($counts|Measure-Object -Minimum).Minimum -ge 2) '64 original Fibonacci rays do not cover all eight impact regions'
Must (($open|Measure-Object -Sum).Sum -gt 1) 'synthetic open door must expose multiple conceptual rain sectors'
Must (($open|Measure-Object -Sum).Sum -lt ($counts|Measure-Object -Sum).Sum) 'indoor wall sectors must stay dark'
Must ($field.Contains('internal const int Sectors = 8;') -and
      $field.Contains('internal const int MaxAudible = 4;')) 'bounded impact sectors/playing copies'
Must ($field.Contains('if (!Room.Out(i)) continue;') -and
      $room.Contains('RainField.Measure(s_from, Dirs);')) 'impact regions not driven by actual existing survey openness'
Must ($field.Contains('src.clip = original.clip;') -and
      $field.Contains('src.timeSamples = (int)(((long)baseSample + offset) % samples);')) 'ambient recording source or decorrelated sector playback missing'
Must ($field.Contains('src.volume = original.volume * current;')) 'game mixer rain volume not authoritative'
Must ($amb.Contains('float impactMix = RainField.Glide(outdoor, rain,') -and
      $amb.Contains('Play(slot, 0, amb, rainNow ? 0f :') -and
      $amb.Contains('float directionalMix = rain ? impactMix : s_openingMix;')) 'legacy portal/rain mixing still in use'
Must ($amb.Contains('RainField.Reset();') -and
      $field.Contains('internal static void Reset()')) 'persistent rain AudioSources leak across worlds'
Must ($weather.Contains('WeatherAcoustics')) 'rain roof bed no longer available'
Must (!$field.Contains('Physics.Raycast') -and
      !$field.Contains('new RaycastCommand(') -and
      !$field.Contains('StartCoroutine(') -and
      !$field.Contains('void Update(') -and
      !$field.Contains('void LateUpdate(')) 'new raycasts or per-frame components introduced'
Write-Output 'RAIN IMPACT FIELD SOURCE+MATH PASS: production 8-sector 64-ray ground field, visibility under doors, 4 selected recordings, actual Valheim rain, 0 new physics'
