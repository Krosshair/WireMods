$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$s=[IO.File]::ReadAllText("$r\src\RainField.cs")
$begin=$s.IndexOf('        internal static int Sector(Vector3 d)')
$end=$s.IndexOf('        // The only new physics work:', $begin)
if($begin -lt 0 -or $end -lt 0){throw 'Cannot extract current production window exposure methods'}
$windowStart=$s.IndexOf('        internal static float WindowAccess(float radius', $end)
$windowEnd=$s.IndexOf('        internal static void Measure(Vector3 from', $windowStart)
if($windowStart -lt 0 -or $windowEnd -lt 0){throw 'Cannot extract production window distance math'}
$code=($s.Substring($begin,$end-$begin) +
       $s.Substring($windowStart,$windowEnd-$windowStart)).Replace(
       'internal static ','public static ')
$code=$code.Replace('public static bool ImpactRay(Vector3 d) =>','public static bool ImpactRay(Vector3 d) { return')
$code=$code.Replace('d.y >= -.24f && d.y <= .65f;','d.y >= -.24f && d.y <= .65f; }')
$code=$code.Replace('public static float Exposure(float open, float total) =>','public static float Exposure(float open, float total) { return')
$code=$code.Replace('total > .001f ? Mathf.Sqrt(Mathf.Clamp01(open / total)) : 0f;','total > .001f ? Mathf.Sqrt(Mathf.Clamp01(open / total)) : 0f; }')
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
Add-Type -TypeDefinition ($stub+[Environment]::NewLine+$code+[Environment]::NewLine+'} }') -ErrorAction Stop
$t=[WiresAcoustics.RainField]
function Must([bool]$yes,[string]$why){if(!$yes){throw ('RAIN WINDOW MATH: '+$why)}}
function V([double]$x,[double]$y,[double]$z){return [UnityEngine.Vector3]::new($x,$y,$z)}
Must ($t::Sector((V 1 0 0)) -eq 0) 'east bearing'
Must ($t::Sector((V 0 0 1)) -eq 2) 'north bearing'
Must ($t::Sector((V -1 0 0)) -eq 4) 'west bearing'
Must ($t::Sector((V 0 0 -1)) -eq 6) 'south bearing'
Must ($t::ImpactRay((V 1 0 0))) 'level ray open to rain'
Must (!$t::ImpactRay((V 0 .7 1))) 'roof ray wrongly counted as ground'
Must ([Math]::Abs($t::Exposure(.25,1)-.5) -lt .00001) 'exposed angular share'
Must ([Math]::Abs($t::WindowAccess(1,0)-1) -lt .00001) 'standing at opening'
Must ($t::WindowAccess(0,0) -eq 0) 'zero-size opening'
Must ($t::WindowAccess(1,14) -eq 0) 'very distant opening'
# Perceptual level comes from the nearest actually VISIBLE ground patch,
# not view direction or an assumption that every room ray sees empty sky.
Must ([Math]::Abs($t::GroundAccess(2)-1) -lt .000001) 'ground immediately outside is fully audible'
Must ($t::GroundAccess(4.5) -gt .90) 'nearby ground through a large open arch is prominent'
Must ($t::GroundAccess(10) -gt .65) 'ground outside a big room remains clearly audible'
Must ($t::GroundAccess(20) -lt .50) 'distant ground is quieter'
$previous=1.01
for($i=0;$i -le 400;$i++) {
 $distance=$i*.05
 $amount=$t::GroundAccess([float]$distance)
 Must ($amount -ge 0 -and $amount -le 1) 'ground access must remain normalized'
 Must ($amount -le $previous+.000001) 'ground access cannot grow when the listener moves farther away'
 if($i -gt 0){Must ([Math]::Abs($amount-$previous) -lt .02) 'ground loudness must vary smoothly with each 5cm step'}
 $previous=$amount
}
# Regression for the screenshot: a wide, visibly rainy arch must remain
# acoustically open even when Portals has already cached a smaller window.
Must ([Math]::Abs($t::CombineAccess(.45,.02)-.9) -lt .00001) 'large open arch must win over cached narrow window'
Must ([Math]::Abs($t::CombineAccess(.02,.45)-.9) -lt .00001) 'nearby large window must win over sparse current rays'
Must ([Math]::Abs($t::CombineAccess(.45,.45)-.9) -lt .00001) 'independent opening measurements must not double rain level'
Must ($t::CombineAccess(0,0) -eq 0) 'fully enclosed room stays indirect and filtered'
Must ($t::CombineAccess(1,0) -eq 1) 'fully exposed horizon must sound outdoors'
Must ($t::CombineAccess(0,1) -eq 1) 'full size nearby window must sound outdoors'
for($i=0;$i -le 100;$i++){
 $h=$i*.01
 $mixed=$t::CombineAccess([float]$h,.08)
 Must ($mixed -ge .16-.00001 -and $mixed -le 1+.00001) 'horizon evidence must not reduce known opening contribution'
 if($i -gt 0) {
  Must ($mixed + .00001 -ge $t::CombineAccess([float](($i-1)*.01),.08)) 'larger arch must not sound quieter'
 }
}
$last=2.0
$checks=10
for($i=0;$i -le 280;$i++) {
 $d=$i*.05
 $a=$t::WindowAccess(1,[float]$d)
 Must ($a -ge 0 -and $a -le 1) 'out-of-range gain'
 Must ($a -le $last+.00001) 'walking away increases rain'
 if($i -gt 0){Must ([Math]::Abs($last-$a) -lt .06) 'hard audible edge between 5cm steps'}
 $checks+=3
 $last=$a
}
Must ($s.Contains('internal const int GroundBearings = 24;') -and
      $s.Contains('internal const int GroundRings = 2;') -and
      $s.Contains('internal const int GroundCommands = GroundSamples * 2;') -and
      $s.Contains('Heightmap.GetHeight(point, out float ground)') -and
      $s.Contains('new RaycastCommand(') -and
      $s.Contains('internal static void ReadGroundSight(') -and
      $s.Contains('if (direct.colliderInstanceID != 0)') -and
      $s.Contains('if (s == null || (!s.Tree && (s.Built || s.Structural)))') -and
      $s.Contains('GroundAccess((GroundPoints[i] - listener).magnitude)')) '360-degree direct ground sight or structural overhead rejection not implemented'
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
Must ($room.Contains('Rays * 3 + RainField.GroundCommands') -and
      $room.Contains('RainField.ScheduleGroundSight(from, s_cmds,') -and
      $room.Contains('s_cmds.GetSubArray(0, count)') -and
      $room.Contains('RainField.ReadGroundSight(s_from, s_hits, Rays * 3);')) 'rain terrain visibility must join existing async room batch'
Must ($room.Contains('(rainSelected ? RainField.GroundCommands : 0)') -and
      $room.Contains('if (rainSelected != s_groundRainSelected)')) 'dry weather must avoid extra ray commands, rain onset must invalidate cached room survey'
Must ($s.Contains('internal static void Measure(Vector3 from, Vector3[] dirs)') -and
      $s.Contains('if (Room.Out(i)) outside += w;') -and
      $s.Contains('Portals.List.Count')) 'window exposure no longer uses cached survey'
Must ($s.Contains('s_worldGen != world || s_doorGen != door')) 'world changes leave cached windows stale'
Must ($s.Contains('CombineAccess(s_openFraction, nearestOpening),') -and
      $s.Contains('float nearestOpening = 0f;')) 'one cached window still hides other broad horizon openings'
$a=[IO.File]::ReadAllText("$r\src\Ambience.cs")
Must ($a.Contains('RainField.Access(Hearing.At) : 0f;') -and
      !$a.Contains('RainField.Access(Hearing.At) * Mathf.Clamp01(')) 'room enclosure still incorrectly reduces direct outdoor rain at an open arch'
Must (!$s.Contains('AddComponent<AudioSource>') -and !$s.Contains('s_bridge') -and !$s.Contains('src.Play(')) 'rain field must be geometry only'
Write-Output "SINGLE LOOP WINDOW MATH PASS: $checks geometry/continuous movement checks, no rain audio voices"
