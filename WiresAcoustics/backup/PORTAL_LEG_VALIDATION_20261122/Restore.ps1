$ErrorActionPreference='Stop'
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before rollback.' }
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\PORTAL_LEG_VALIDATION_20261122'
foreach($n in @('Batch.cs','SharedEdges.cs','Portals.cs','SpectralPaths.cs')) {
 Copy-Item (Join-Path $b $n) (Join-Path $r ('src\'+$n)) -Force
}
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Previous local version restored.'
