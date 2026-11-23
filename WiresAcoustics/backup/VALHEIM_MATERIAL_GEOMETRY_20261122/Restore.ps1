$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before restoring Wire Acoustics'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\VALHEIM_MATERIAL_GEOMETRY_20261122'
$src=Join-Path $r 'src'
$files=@(Get-ChildItem (Join-Path $b 'src') -Filter '*.cs')
if($files.Count -ne 46){throw "Incomplete source backup: $($files.Count)"}
foreach($f in $files){Copy-Item $f.FullName (Join-Path $src $f.Name) -Force}
Copy-Item (Join-Path $b 'WiresAcoustics.csproj') (Join-Path $src 'WiresAcoustics.csproj') -Force
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Restored pre-material/Valheim-geometry source and binary. Valheim was closed.'
