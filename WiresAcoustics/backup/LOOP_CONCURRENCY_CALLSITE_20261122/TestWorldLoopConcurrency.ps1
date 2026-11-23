$ErrorActionPreference='Stop'
$src=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\WiresAcoustics\src\Starts.cs')
$start=$src.IndexOf('private static bool Prefix(ZSFX __instance)', $src.IndexOf('private static class IndependentWorldLoops'))
if($start -lt 0){throw 'Missing production concurrency interceptor'}
$open=$src.IndexOf('{',$start)
$depth=0;$finish=-1
for($i=$open;$i -lt $src.Length;$i++){
 if($src[$i] -eq '{'){$depth++}
 if($src[$i] -eq '}'){$depth--;if($depth -eq 0){$finish=$i;break}}
}
if($finish -lt 0){throw 'Unbalanced concurrency method'}
$method=$src.Substring($start,$finish-$start+1).Replace('private static bool Prefix','public static bool Prefix')
$stub=@'
public class AudioSource { public float spatialBlend=1f; }
public sealed class BoolSetting {public bool Value=true;}
public static class Plugin {public static BoolSetting Enabled=new BoolSetting();}
public class AcousticFilter {
 public enum Kinds {Sfx, Voice, Ocean, Other, Menu, Ambient, Outside}
 public bool WorldRandomAmbient,Cue,Voice;
 public Kinds Kind=Kinds.Sfx;
}
public class ZSFX {
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
Add-Type -TypeDefinition ($stub+[Environment]::NewLine+'public static class ActualWorldLoopPatch {'+[Environment]::NewLine+$method+[Environment]::NewLine+'}') -ErrorAction Stop
function Require([bool]$ok,[string]$why){if(!$ok){throw "World loop concurrency failed: $why"}}
$a=[ZSFX]::new();$b=[ZSFX]::new()
Require (-not [ActualWorldLoopPatch]::Prefix($a)) 'first positional source not protected'
Require (-not [ActualWorldLoopPatch]::Prefix($b)) 'second positional source not protected'
Require (!$a.Disabled -and !$b.Disabled -and $a.EnableCalls -eq 1 -and $b.EnableCalls -eq 1) 'both sources must be audible together'
foreach($kind in @('Ambient','Outside','Menu')){
 $z=[ZSFX]::new(); $z.Filter.Kind=[AcousticFilter+Kinds]::$kind
 Require ([ActualWorldLoopPatch]::Prefix($z) -and $z.EnableCalls -eq 0) "$kind should preserve vanilla"
}
foreach($prop in @('WorldRandomAmbient','Cue','Voice')){
 $z=[ZSFX]::new();$z.Filter.$prop=$true
 Require ([ActualWorldLoopPatch]::Prefix($z) -and $z.EnableCalls -eq 0) "$prop should preserve vanilla"
}
$z=[ZSFX]::new();$z.Source.spatialBlend=0.0
Require ([ActualWorldLoopPatch]::Prefix($z) -and $z.EnableCalls -eq 0) '2D source should preserve vanilla'
$z=[ZSFX]::new();$z.loop=$false
Require ([ActualWorldLoopPatch]::Prefix($z)) 'one-shot should preserve vanilla'
$z=[ZSFX]::new();$z.Filter=$null
Require (-not [ActualWorldLoopPatch]::Prefix($z)) 'positional source without filter should still be protected'
[Plugin]::Enabled.Value=$false
$z=[ZSFX]::new()
Require ([ActualWorldLoopPatch]::Prefix($z)) 'mod off must restore vanilla concurrent suppression'
[Plugin]::Enabled.Value=$true
Write-Output 'WORLD LOOP CONCURRENCY SOURCE-LINKED TEST PASS: both matching world sources enabled, all excluded categories and mod-off preserved'
