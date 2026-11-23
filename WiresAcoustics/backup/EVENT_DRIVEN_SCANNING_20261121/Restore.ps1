$ErrorActionPreference='Stop'
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before restoring.' }
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\EVENT_DRIVEN_SCANNING_20261121'
foreach ($n in 'Room.cs','Occlusion.cs','GeometryEvents.cs') { Copy-Item (Join-Path $b $n) (Join-Path $r ('src\'+$n)) -Force }
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Previous build restored'
