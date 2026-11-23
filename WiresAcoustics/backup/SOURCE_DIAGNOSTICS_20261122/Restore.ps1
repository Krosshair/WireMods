$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback.'}
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\SOURCE_DIAGNOSTICS_20261122'
foreach($name in @('Room.cs','Plugin.cs','Filter.cs','Occlusion.cs','ContinuousEdges.cs','Batch.cs','Starts.cs','Budget.cs')) {
    Copy-Item (Join-Path $b $name) (Join-Path $r ('src\'+$name)) -Force
}
Remove-Item (Join-Path $r 'src\AcousticTrace.cs') -Force -ErrorAction SilentlyContinue
$live=Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
Copy-Item (Join-Path $b 'installed.dll') $live -Force
Copy-Item (Join-Path $b 'package.dll') (Join-Path $r 'package\WiresAcoustics.dll') -Force
'Restored pre-diagnostics sources and prior installed/package DLL.'
