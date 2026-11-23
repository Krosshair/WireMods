$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$backup=Join-Path $root 'backup\STUTTER_THREAD_JOBS_20261122'
$profile='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before restoring WA'}
foreach($file in @('Room.cs','Directions.cs','Occlusion.cs','AnalyticOcclusion.cs','AcousticTrace.cs','Plugin.cs')){
    Copy-Item -LiteralPath (Join-Path $backup ('src\'+$file)) -Destination (Join-Path $root ('src\'+$file)) -Force
}
Remove-Item -LiteralPath (Join-Path $root 'src\AnalyticWorker.cs') -ErrorAction SilentlyContinue
foreach($file in @('Program.cs','AnalyticOcclusionTests.csproj')){
    Copy-Item -LiteralPath (Join-Path $backup ('dev\AnalyticOcclusionTests\'+$file)) -Destination (Join-Path $root ('dev\AnalyticOcclusionTests\'+$file)) -Force
}
Copy-Item -LiteralPath (Join-Path $backup 'dev\VerifyReleaseCandidate.ps1') -Destination (Join-Path $root 'dev\VerifyReleaseCandidate.ps1') -Force
Copy-Item -LiteralPath (Join-Path $backup 'installed\WiresAcoustics.dll') -Destination (Join-Path $profile 'BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll') -Force
Copy-Item -LiteralPath (Join-Path $backup 'wire.valheim.acoustics.cfg') -Destination (Join-Path $profile 'BepInEx\config\wire.valheim.acoustics.cfg') -Force
Write-Host 'WA pre-stutter-threading sources, installed DLL, diagnostics config and verification scripts restored.'
Write-Host 'Source build outputs were not reverted. Run dotnet build again before installing from source.'
