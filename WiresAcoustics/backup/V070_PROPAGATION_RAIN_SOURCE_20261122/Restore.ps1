$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue) {
    throw 'Close Valheim before restoring Wire Acoustics'
}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\V070_PROPAGATION_RAIN_SOURCE_20261122'
$before=Join-Path $b 'src'
$src=Join-Path $r 'src'
$files=@(Get-ChildItem $before -Filter '*.cs')
if($files.Count -ne 48) {throw "Incomplete source backup: $($files.Count) instead of 48"}
if(!(Test-Path (Join-Path $b 'installed.dll'))){throw 'Original DLL backup missing'}
Remove-Item (Join-Path $src 'WeatherAcoustics.cs') -Force -ErrorAction SilentlyContinue
foreach($f in $files){Copy-Item $f.FullName (Join-Path $src $f.Name) -Force}
Copy-Item (Join-Path $b 'WiresAcoustics.csproj') (Join-Path $src 'WiresAcoustics.csproj') -Force
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
if((Get-FileHash $live -Algorithm SHA256).Hash -ne
   (Get-FileHash (Join-Path $b 'installed.dll') -Algorithm SHA256).Hash) {
    throw 'Restored DLL hash mismatch'
}
'Restored prior 48 production source files and original Enngard/package DLL.'
'Developer regression test scripts and package docs were not rolled back.'
