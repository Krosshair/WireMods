$ErrorActionPreference='Stop'
$src=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\WiresAcoustics\src\Starts.cs')
function ExtractMethod([string]$signature){
 $start=$src.IndexOf($signature)
 if($start -lt 0){throw "Missing production method $signature"}
 $open=$src.IndexOf('{',$start);$depth=0;$end=-1
 for($i=$open;$i -lt $src.Length;$i++){
  if($src[$i] -eq '{'){$depth++}
  if($src[$i] -eq '}'){$depth--;if($depth -eq 0){$end=$i;break}}
 }
 if($end -lt 0){throw "Unbalanced method $signature"}
 return $src.Substring($start,$end-$start+1)
}
$helper=ExtractMethod 'private static bool IndependentWorldLoop(ZSFX z)'
$enable=ExtractMethod 'private static void AllowWorldLoop(ZSFX z)'
$patch=ExtractMethod 'private static void Postfix(List<ZSFX> ___m_loopingSfx)'
$helper=$helper.Replace('private static','public static')
$enable=$enable.Replace('private static','public static')
$patch=$patch.Replace('private static','public static')
$stub=@'
using System;
using System.Collections.Generic;
public sealed class AudioSource {public float spatialBlend=1f;}
public sealed class BoolSetting {public bool Value=true;}
public static class Plugin {public static BoolSetting Enabled=new BoolSetting();}
public static class Time {public static int frameCount=16;}
public sealed class AcousticFilter {
 public enum Kinds {Sfx,Voice,Ocean,Other,Menu,Ambient,Outside}
 public bool WorldRandomAmbient,Cue,Voice;
 public Kinds Kind=Kinds.Sfx;
}
public sealed class ZSFX {
 public bool loop=true,Disabled=true;
 public int EnableCalls;
 public AudioSource Source=new AudioSource();
 public AcousticFilter Filter=new AcousticFilter();
 public bool IsLooping(){return loop;}
 public T GetComponent<T>() where T:class {
  if(typeof(T)==typeof(AudioSource)) return Source as T;
  if(typeof(T)==typeof(AcousticFilter)) return Filter as T;
  return null;
 }
 public void ConcurrencyEnable(){Disabled=false;EnableCalls++;}
}
'@
Add-Type -TypeDefinition ($stub+[Environment]::NewLine+'public static class ActualLoopPatch {'+[Environment]::NewLine+$helper+[Environment]::NewLine+$enable+[Environment]::NewLine+$patch+[Environment]::NewLine+'}') -ErrorAction Stop
function Require([bool]$ok,[string]$why){if(!$ok){throw "WORLD LOOP TEST FAILED: $why"}}
$a=[ZSFX]::new();$b=[ZSFX]::new();$a.Disabled=$false;$b.Disabled=$true
$group=[System.Collections.Generic.List[ZSFX]]::new();$group.Add($a);$group.Add($b)
[ActualLoopPatch]::Postfix($group)
Require (-not $a.Disabled -and -not $b.Disabled) 'both copies must be audible after vanilla disables the second'
Require ($a.EnableCalls -eq 1 -and $b.EnableCalls -eq 1) 'each positional instance must be enabled'
$second=[ZSFX]::new();$second.Disabled=$true
$group.Clear();$group.Add($second)
[Time]::frameCount=17
[ActualLoopPatch]::Postfix($group)
Require ($second.Disabled) 'existing every-16th-frame cadence must be preserved'
[Time]::frameCount=32
[ActualLoopPatch]::Postfix($group)
Require (-not $second.Disabled) 'suppressed instance must recover on next vanilla concurrency cycle'
foreach($kind in @('Ambient','Outside','Menu')){
 $z=[ZSFX]::new();$z.Filter.Kind=[AcousticFilter+Kinds]::$kind
 $group.Clear();$group.Add($z);[ActualLoopPatch]::Postfix($group)
 Require ($z.Disabled -and $z.EnableCalls -eq 0) "$kind must retain vanilla concurrency"
}
foreach($property in @('WorldRandomAmbient','Cue','Voice')){
 $z=[ZSFX]::new();$z.Filter.$property=$true
 $group.Clear();$group.Add($z);[ActualLoopPatch]::Postfix($group)
 Require ($z.Disabled -and $z.EnableCalls -eq 0) "$property must retain vanilla concurrency"
}
$z=[ZSFX]::new();$z.Source.spatialBlend=0.0
$group.Clear();$group.Add($z);[ActualLoopPatch]::Postfix($group)
Require ($z.Disabled) '2D loop must retain vanilla concurrency'
$z=[ZSFX]::new();$z.loop=$false
$group.Clear();$group.Add($z);[ActualLoopPatch]::Postfix($group)
Require ($z.Disabled) 'one-shot must retain vanilla concurrency'
[Plugin]::Enabled.Value=$false
$z=[ZSFX]::new();$group.Clear();$group.Add($z);[ActualLoopPatch]::Postfix($group)
Require ($z.Disabled) 'WA disabled must retain vanilla concurrency'
[Plugin]::Enabled.Value=$true
Require ($src.Contains('[HarmonyPatch(typeof(AudioMan), "UpdateLoopingConcurrency")]')) 'actual caller hook is missing'
Require ($src.Contains('AllowWorldLoop(__instance);')) 'initial ZSFX.Play still allows first-sound suppression'
Require ($src.Contains('RandomizeLoop(z, src);')) 'individual loop phase randomization is missing'
Require ($src.Contains('src.timeSamples = UnityEngine.Random.Range(0, src.clip.samples);')) 'loop phase not independently randomized'
Require (-not $src.Contains('[HarmonyPatch(typeof(ZSFX), nameof(ZSFX.ConcurrencyDisable))]')) 'unreliable inlined callee patch remains'
Write-Output 'WORLD LOOP CALLSITE TEST PASS: two same-clip world sources, periodic recovery, vanilla ambient/2D/one-shot/mod-off and independent phase'
