param([switch]$DryRun)
$ErrorActionPreference='Stop'
if((Get-Process valheim -ErrorAction SilentlyContinue) -and !$DryRun){
 throw 'Close Valheim before restoring the previous development DLL.'
}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\SOURCE_VOLUME_20261122'
$baseline=Join-Path $r 'backup\RELEASE_ENGINEERING_20261122\src'
$src=Join-Path $r 'src'
$modified=@{}
foreach($f in @('Plugin.cs','Filter.cs','AcousticTrace.cs','ThreeRayTransmission.cs')){
 $modified[$f]=[IO.File]::ReadAllText((Join-Path $src $f))
}
function Replace-Once([string]$file,[string]$old,[string]$new) {
 $value=$script:modified[$file]
 if(!$value.Contains($old)){throw "Rollback source mismatch: $file '$old'"}
 $script:modified[$file]=$value.Replace($old,$new)
}
Replace-Once 'Plugin.cs' "        internal static ConfigEntry<int> VolumetricRays;"+[Environment]::NewLine '' 
# Handle CRLF and LF; PowerShell regex uses multiline exact endpoint.
$p=$modified['Plugin.cs']
$pattern='(?ms)^\s*VolumetricRays = Setting\("Tuning", "Source-volume rays", 16,.*?new AcceptableValueList<int>\(0, 4, 8, 16, 32\), true\);\r?\n'
if(![regex]::IsMatch($p,$pattern)){throw 'Rollback cannot locate volume config'}
$modified['Plugin.cs']=[regex]::Replace($p,$pattern,'')
Replace-Once 'Plugin.cs' 'with their geometric coverage and source-volume ray count, path, target and actual gain/filter' 'with their geometric coverage, path, target and actual gain/filter'
$modified['Filter.cs']=[regex]::Replace($modified['Filter.cs'],'(?m)^        internal int LastVolumeSamples;\r?\n','')
$modified['AcousticTrace.cs']=[regex]::Replace($modified['AcousticTrace.cs'],'(?m)^                    \.Append\(" volumeRays="\)\.Append\(f\.LastVolumeSamples\)\r?\n','')
$mix=$modified['ThreeRayTransmission.cs']
$pattern='(?ms)^        // Indirect routes carry only the obstructed fraction.*?^            UnityEngine\.Mathf\.Sqrt\(UnityEngine\.Mathf\.Clamp01\(coverage\)\);\r?\n\r?\n'
if(![regex]::IsMatch($mix,$pattern)){throw 'Rollback cannot locate source-volume indirect scale'}
$modified['ThreeRayTransmission.cs']=[regex]::Replace($mix,$pattern,'')
foreach($f in @('Batch.cs','Starts.cs')){
 if(!(Test-Path (Join-Path $b ('src\'+$f)))){throw "Missing snapshot $f"}
}
if(!(Test-Path (Join-Path $baseline 'ContinuousEdges.cs'))){throw 'Missing independent ContinuousEdges baseline'}
if(!(Test-Path (Join-Path $b 'installed.dll')) -or !(Test-Path (Join-Path $b 'package.dll'))){throw 'Missing old binary'}
if(!$DryRun){
 foreach($f in @('Batch.cs','Starts.cs')){
  Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $src $f) -Force
 }
 Copy-Item (Join-Path $baseline 'ContinuousEdges.cs') (Join-Path $src 'ContinuousEdges.cs') -Force
 foreach($entry in $modified.GetEnumerator()){
  [IO.File]::WriteAllText((Join-Path $src $entry.Key),$entry.Value,[Text.UTF8Encoding]::new($false))
 }
 Remove-Item (Join-Path $src 'SourceVolume.cs') -Force -ErrorAction SilentlyContinue
 Remove-Item (Join-Path $src 'VolumeFresnel.cs') -Force -ErrorAction SilentlyContinue
 $live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
 Copy-Item (Join-Path $b 'installed.dll') $live -Force
 Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
 'Restored the previous installed and source state (before source-volume implementation).'
} else {
 'DRY RUN PASS: previous DLLs present, source-inverse patches verified. No files changed.'
}
