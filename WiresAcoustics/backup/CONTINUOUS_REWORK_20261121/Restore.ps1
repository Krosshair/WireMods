$ErrorActionPreference = 'Stop'
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) {
  throw 'Close Valheim before restoring this backup.'
}
$root = 'C:\Users\kajet\WireMods\WiresAcoustics'
$backup = Join-Path $root 'backup\CONTINUOUS_REWORK_20261121'
Get-ChildItem (Join-Path $backup 'src') -Filter '*.cs' | ForEach-Object {
  Copy-Item $_.FullName (Join-Path $root ('src\' + $_.Name)) -Force
}
$live = Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $backup 'installed.dll') $live -Force
Copy-Item (Join-Path $backup 'package.dll') (Join-Path $root 'package\WiresAcoustics.dll') -Force
Write-Output 'Reverted continuous acoustics experiment.'
