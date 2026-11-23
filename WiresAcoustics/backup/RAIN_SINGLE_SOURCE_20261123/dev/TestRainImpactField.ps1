$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$field=[IO.File]::ReadAllText((Join-Path $r 'src\RainField.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$amb=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$weather=[IO.File]::ReadAllText((Join-Path $r 'src\WeatherAcoustics.cs'))
function Must([bool]$ok,[string]$message){if(!$ok){throw ('RAIN IMPACT FIELD: '+$message)}}
$start=$field.IndexOf('        internal static int Sector(Vector3 d)')
$end=$field.IndexOf('        private static void RefreshTargets(Vector3 listener)', $start)
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
  public static float Max(float a,float b) {return Math.Max(a,b);}
  public static float Clamp(float a,float lo,float hi) {return Math.Max(lo,Math.Min(hi,a));}
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
# Continuous physical access through the SAME cached opening, regardless of
# whether a new room-ray scan happens to hit its aperture.
Must ($type::WindowAccess(0.0,0.0) -eq 0.0) 'nonexistent opening contributes no rain'
Must ([Math]::Abs($type::WindowAccess(1.0,0.0)-1.0) -lt .00001) 'standing at window is directly exposed'
$last=2.0
for($i=0;$i -le 280;$i++) {
 $distance=$i*.05
 $access=$type::WindowAccess(1.0,[float]$distance)
 Must ($access -le $last+.000001) "moving away from opening must never increase direct rain: $distance"
 Must ($access -ge 0.0 -and $access -le 1.0) "window access must stay normalized: $distance"
 if($i -gt 0) { Must ([Math]::Abs($last-$access) -lt .06) 'window audible margin must not jump across 5cm' }
 $last=$access
}
Must ($type::WindowAccess(1.0,14.0) -eq 0.0) 'direct path fades entirely past a distant window'
Must ($type::WindowAccess(1.0,6.0) -gt 0.1) 'window audible at mid-range without discrete ray threshold'
Must ($type::WindowAccess(1.0,10.0) -gt 0.0) 'farther window has a smooth quiet tail'
Must ($type::WindowAccess(1.0,3.0) -gt $type::WindowAccess(1.0,8.0)) 'approach enhances source continuously'
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
Must ($field.Contains('private static void CaptureApertures()') -and
      $field.Contains('int worldGen = Occlusion.WorldGen;') -and
      $field.Contains('s_apertureCount = 0; // door/build change invalidates old openings') -and
      $field.Contains('(listener - s_lastTargetPosition).sqrMagnitude < .0025f') -and
      $field.Contains('s_targetsDirty = true;') -and
      $field.Contains('RefreshTargets(Hearing.At);') -and
      $field.Contains('DirectTarget[i] * active')) 'cached openings not continuously evaluated as player moves, or world updates do not clear stale openings'
Must ($field.Contains('if (incoming + 4f >= farthest) continue;') -and
      $field.Contains('replacement = farthestIndex;') -and
      $field.Contains('if (replacement < 0)')) 'newly approached windows cannot replace stale distant ones'
Must ($field.Contains('Mathf.Clamp01(Room.BuiltRoof * 2f)')) 'under-roof rain improperly tied to changing global room enclosure'
Must ($field.Contains('if (rain && s_apertureCount > 0)') -and
      $field.Contains('(Apertures[a].Point - Hearing.At).sqrMagnitude < 100f') -and
      $field.Contains('if (current <= .0001f && !warm)')) 'sources must be ready as user approaches known openings'
Must ($field.Contains('current = Mathf.MoveTowards(current, desired, 12f * Mathf.Max(0f, dt));')) 'response fade too slow despite continuous window distance'
Must ($field.Contains('internal const int Sectors = 8;') -and
      $field.Contains('internal const int MaxAudible = 4;')) 'bounded impact sectors/playing copies'
Must ($field.Contains('if (!Room.Out(i)) continue;') -and
      $room.Contains('RainField.Measure(s_from, Dirs);')) 'impact regions not driven by actual existing survey openness'
Must ($field.Contains('src.clip = chosenRain;') -and
      $field.Contains('src.timeSamples = (int)(((long)baseSample + offset) % samples);')) 'selected weather recording or decorrelated sector playback missing'
Must ($field.Contains('src.volume = s_lastVolume * current;')) 'selected weather rain volume not authoritative during the game audio delay'
Must ($amb.Contains('float impactMix = RainField.Glide(outdoor,') -and
      $amb.Contains('rainSelected ? env.m_ambientLoop : null,') -and
      $amb.Contains('Play(slot, 0, amb, rainNow ? 0f :') -and
      $amb.Contains('float diffuseBed = rainPlaying')) 'selected rain is incorrectly gated by active weather clip or generic portal routing'
Must ($amb.Contains('RainField.Reset();') -and
      $field.Contains('internal static void Reset()')) 'persistent rain AudioSources leak across worlds'
Must ($weather.Contains('WeatherAcoustics')) 'rain roof bed no longer available'
Must (!$field.Contains('Physics.Raycast') -and
      !$field.Contains('new RaycastCommand(') -and
      !$field.Contains('StartCoroutine(') -and
      !$field.Contains('void Update(') -and
      !$field.Contains('void LateUpdate(')) 'new raycasts or per-frame components introduced'
Write-Output 'RAIN IMPACT FIELD SOURCE+MATH PASS: production 8-sector 64-ray ground field, visibility under doors, 4 selected recordings, actual Valheim rain, 0 new physics'
