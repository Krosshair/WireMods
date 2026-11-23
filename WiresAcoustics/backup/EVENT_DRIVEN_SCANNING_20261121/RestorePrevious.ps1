$ErrorActionPreference='Stop'
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before restoring.' }
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$backup=Join-Path $root 'backup\EVENT_DRIVEN_SCANNING_20261121'
foreach ($name in 'Room.cs','Occlusion.cs','GeometryEvents.cs') {
  Copy-Item (Join-Path $backup $name) (Join-Path $root ('src\'+$name)) -Force
}
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $backup 'installed.dll') $live -Force
Copy-Item (Join-Path $backup 'package.dll') (Join-Path $root 'package\WiresAcoustics.dll') -Force
Write-Output 'Previous local build restored.'
