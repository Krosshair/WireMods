$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback.'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\SHARED_PATHS_20261122'
Get-ChildItem (Join-Path $b 'src') -Filter '*.cs' | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $r ('src\'+$_.Name)) -Force
}
foreach($name in 'SharedEdges.cs','SpectralPaths.cs') {
    Remove-Item (Join-Path $r ('src\'+$name)) -Force -ErrorAction SilentlyContinue
}
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Previous installed DLL and all original .cs files restored.'
