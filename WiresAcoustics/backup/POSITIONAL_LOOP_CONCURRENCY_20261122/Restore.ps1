$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\POSITIONAL_LOOP_CONCURRENCY_20261122'
$plugin='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback'}
Copy-Item -LiteralPath (Join-Path $b 'Starts.cs') -Destination (Join-Path $r 'src\Starts.cs') -Force
Copy-Item -LiteralPath (Join-Path $b 'VerifyEveryImplementation.ps1') -Destination (Join-Path $r 'dev\VerifyEveryImplementation.ps1') -Force
Copy-Item -LiteralPath (Join-Path $b 'VerifyReleaseCandidate.ps1') -Destination (Join-Path $r 'dev\VerifyReleaseCandidate.ps1') -Force
Remove-Item -LiteralPath (Join-Path $r 'dev\TestWorldLoopConcurrency.ps1') -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $b 'WiresAcoustics.dll') -Destination $plugin -Force
Write-Output 'Restored pre-concurrency-fix source, scripts, installed Enngard DLL.'
Write-Output 'Rebuild source before subsequent DLL installations.'