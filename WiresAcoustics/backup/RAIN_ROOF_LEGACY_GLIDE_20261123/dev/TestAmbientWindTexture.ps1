$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$code=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$signature='internal static bool IsWindTexture(AudioClip clip)'
$start=$code.IndexOf($signature)
if($start -lt 0){throw 'Missing wind-texture source classifier'}
$open=$code.IndexOf('{',$start);$depth=0;$end=-1
for($i=$open;$i -lt $code.Length;$i++){
 if($code[$i] -eq '{'){$depth++}
 if($code[$i] -eq '}'){$depth--;if($depth -eq 0){$end=$i;break}}
}
if($end -lt 0){throw 'Unclosed wind-texture classifier'}
$method=$code.Substring($start,$end-$start+1).Replace('internal static bool IsWindTexture','public static bool FromProduction')
$stub=@'
using System;
public class AudioClip {
 private readonly string m_name;
 public int Lookups=0;
 public string name { get {Lookups++;return m_name;} }
 public AudioClip(string value){m_name=value;}
}
public static class WindTextureClassify {
 private static AudioClip s_identifiedWindClip;
 private static bool s_identifiedIsWind;
'@+[Environment]::NewLine+$method+[Environment]::NewLine+'}'
Add-Type -TypeDefinition $stub -ErrorAction Stop
function Check([bool]$truth,[string]$label){if(!$truth){throw 'WIND TEXTURE REGRESSION FAILED: '+$label}}
foreach($clip in @(
 'SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only',
 'Amb_MainWind_S_Loop','Wind_BlowingLoop3','Wind_RushingStormLoop')){
 Check ([WindTextureClassify]::FromProduction([AudioClip]::new($clip))) "Wind clip missed: $clip"
}
foreach($clip in @('Amb_MainRain_S_Loop','Amb_LightRain_S_Loop',
                  'Amb_Birds_ForestPark','Amb_WindingTunnel_Main','')){
 Check (![WindTextureClassify]::FromProduction([AudioClip]::new($clip))) "Nonwind misidentified: $clip"
}
Check (![WindTextureClassify]::FromProduction($null)) 'Null clip wrongly classified'
$windClip=[AudioClip]::new('SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only')
Check ([WindTextureClassify]::FromProduction($windClip)) 'Fresh wind clip incorrectly classified'
$lookups=$windClip.Lookups
for($i=0;$i -lt 200;$i++){[void][WindTextureClassify]::FromProduction($windClip)}
Check ($windClip.Lookups -eq $lookups) 'AudioClip.name still read every render frame'
[void][WindTextureClassify]::FromProduction([AudioClip]::new('Amb_MainRain_S_Loop'))
Check ([WindTextureClassify]::FromProduction($windClip)) 'Wind identity not recovered after rain clip switch'
$a='C:\Users\kajet\WireMods\ValheimSOURCE\ValheimProject\ExportedProject\Assets'
$meta=Join-Path $a 'Audio\Ambients\loops\SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only.ogg.meta'
$guid=([regex]::Match([IO.File]::ReadAllText($meta),'guid:\s*([a-f0-9]{32})')).Groups[1].Value
Check ($guid.Length -eq 32) 'Real authored wind recording GUID not found'
$env=Join-Path $a 'Systems\_Environment.prefab'
$lines=[IO.File]::ReadAllLines($env)
$start=-1
for($i=0;$i -lt $lines.Length;$i++){
 if($lines[$i].Trim() -eq '- m_name: Heath clear'){$start=$i;break}
}
Check ($start -ge 0) 'Heath clear environment missing'
$linked=''
for($i=$start+1;$i -lt [Math]::Min($start+110,$lines.Length);$i++){
 if($lines[$i] -match '^\s*- m_name: '){break}
 $matchGuid=[regex]::Match($lines[$i],'m_ambientLoop:.*guid:\s*([a-f0-9]{32})')
 if($matchGuid.Success){$linked=$matchGuid.Groups[1].Value;break}
}
Check ($linked -eq $guid) 'Plains clear weather no longer uses extracted wind-in-reeds recording'
$valheim=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\AudioMan.cs')
Check ($valheim.Contains('m_ambientLoopSource.spatialBlend = 0f;') -and
       $valheim.Contains('m_windLoopSource.spatialBlend = 0f;')) 'Vanilla ambient/wind layout changed'
foreach($fragment in @(
 'WindTexture(man, listener, windFrom, windAmount);',
 'float shelter = Mathf.Lerp(1f, Mathf.Lerp(.25f, 1f, s_exposure), s);',
 'f.SourceLevel = s_windModulation * shelter;',
 'f.SetDirection(listener.InverseTransformDirection(windFrom));',
 'float ambientCut = fieldActive ?',
 'Mathf.Min(s_cutoff, windCut) : s_cutoff;',
 'Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);',
 'f.SourceLevel = 1f;',
 'f.Hrtf = false;',
 'if (f.SavedAmbientPan)',
 'if (hrtf)',
 'f.Active = f.NeedsWork;')){
 Check ($code.Contains($fragment)) "Missing runtime branch: $fragment"
}
Check ($code.Contains('s_lastRainClip != null') -and
       $code.Contains('bool fieldActive = rainPlaying && EnvironmentalField.Ready;') -and
       $code.Contains('s_fieldGain :')) 'Rain texture protection missing'
Write-Output 'WIND TEXTURE TEST PASS: authored Heath clear reeds clip, classifier excluding rain/birds, direction, shelter, lowpass, clip-switch reset'
