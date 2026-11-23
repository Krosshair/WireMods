$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$backup=Join-Path $root 'backup\LOOP_POLYPHONY_20261122'
$profile='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before restoring WA'}
foreach($f in @('Area.cs','Starts.cs','Plugin.cs','AcousticTrace.cs')) {
 Copy-Item -LiteralPath (Join-Path $backup $f) -Destination (Join-Path $root ('src\'+$f)) -Force
}
Copy-Item -LiteralPath (Join-Path $backup 'AcousticTraceTests.Program.cs') -Destination (Join-Path $root 'dev\AcousticTraceTests\Program.cs') -Force
foreach($f in @('VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1')){
 Copy-Item -LiteralPath (Join-Path $backup $f) -Destination (Join-Path $root ('dev\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $root 'dev\TestPolyphonyRegression.ps1') -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $backup 'WiresAcoustics.dll') -Destination (Join-Path $profile 'BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll') -Force
Write-Output 'Restored pre-polyphony source, verification scripts and installed Enngard DLL. Rebuild source before next installing.'
