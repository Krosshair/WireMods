$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\LOOP_CONCURRENCY_CALLSITE_20261122'
$dest='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Valheim must be closed'}
Copy-Item -LiteralPath (Join-Path $b 'Starts.cs') -Destination (Join-Path $r 'src\Starts.cs') -Force
foreach($f in @('TestWorldLoopConcurrency.ps1','TestPolyphonyRegression.ps1','VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item -LiteralPath (Join-Path $b $f) -Destination (Join-Path $r ('dev\'+$f)) -Force
}
Copy-Item -LiteralPath (Join-Path $b 'WiresAcoustics.dll') -Destination $dest -Force
Write-Output 'Original pre-callsite-fix source and DLL restored. Rebuild before reinstallation.'