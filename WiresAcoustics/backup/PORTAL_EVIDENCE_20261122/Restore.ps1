$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback.'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\PORTAL_EVIDENCE_20261122'
foreach($name in @('Portals.cs','Room.cs'))
{ Copy-Item (Join-Path $b $name) (Join-Path $r ('src\'+$name)) -Force }
Copy-Item (Join-Path $b 'PortalTopologyTests.cs') (Join-Path $r 'dev\PortalTopologyTests\Program.cs') -Force
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Restored pre-portal-evidence state.'
