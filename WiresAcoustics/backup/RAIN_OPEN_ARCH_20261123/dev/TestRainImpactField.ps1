$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$s=[IO.File]::ReadAllText("$r\src\RainField.cs")
$begin=$s.IndexOf('        internal static int Sector(Vector3 d)')
$end=$s.IndexOf('        internal static void Measure(Vector3 from', $begin)
if($begin -lt 0 -or $end -lt 0){throw 'Cannot extract current production window exposure methods'}
$code=$s.Substring($begin,$end-$begin).Replace('internal static ','public static ')
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
Must ($s.Contains('internal static void Measure(Vector3 from, Vector3[] dirs)') -and
      $s.Contains('if (Room.Out(i)) outside += w;') -and
      $s.Contains('Portals.List.Count')) 'window exposure no longer uses cached survey'
Must ($s.Contains('s_worldGen != world || s_doorGen != door')) 'world changes leave cached windows stale'
Must (!$s.Contains('AddComponent<AudioSource>') -and !$s.Contains('s_bridge') -and !$s.Contains('src.Play(')) 'rain field must be geometry only'
Write-Output "SINGLE LOOP WINDOW MATH PASS: $checks geometry/continuous movement checks, no rain audio voices"
