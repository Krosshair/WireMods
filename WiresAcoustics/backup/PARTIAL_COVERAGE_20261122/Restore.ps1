$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback.'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\PARTIAL_COVERAGE_20261122'
foreach($n in @('Batch.cs','Occlusion.cs','Starts.cs','Filter.cs','AcousticTrace.cs')){
 Copy-Item (Join-Path $b ('src\'+$n)) (Join-Path $r ('src\'+$n)) -Force
}
Remove-Item (Join-Path $r 'src\ThreeRayTransmission.cs') -Force -ErrorAction SilentlyContinue
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Restored pre-partial-occlusion development build sources and DLL.'
