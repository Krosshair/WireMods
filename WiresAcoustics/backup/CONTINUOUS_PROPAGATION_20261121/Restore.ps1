$ErrorActionPreference = 'Stop'
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) {
    throw 'Close Valheim before restoring the old build.'
}
$root = 'C:\Users\kajet\WireMods\WiresAcoustics'
$backup = Join-Path $root 'backup\CONTINUOUS_PROPAGATION_20261121'
foreach ($name in 'Filter.cs','Batch.cs','Directions.cs','Occlusion.cs','Corners.cs') {
    Copy-Item (Join-Path $backup ('src\' + $name)) (Join-Path $root ('src\' + $name)) -Force
}
Remove-Item (Join-Path $root 'src\ContinuousEdges.cs') -ErrorAction SilentlyContinue
$live = Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $backup 'installed.dll') $live -Force
Copy-Item (Join-Path $backup 'package.dll') (Join-Path $root 'package\WiresAcoustics.dll') -Force
Write-Output 'Restored previous local build and sources.'
