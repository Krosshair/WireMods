$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$pre=Join-Path $r 'backup\LOOP_FADES_20261122'
$audit=Join-Path $r 'backup\PRE_RELEASE_AUDIT_20261122'
$dest='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback'}
foreach($n in @('Filter.cs','Budget.cs','Starts.cs','GeometryEvents.cs')){
 Copy-Item -LiteralPath (Join-Path $pre $n) -Destination (Join-Path $r ('src\'+$n)) -Force
}
foreach($n in @('Occlusion.cs','Batch.cs')){
 Copy-Item -LiteralPath (Join-Path $audit ('src\'+$n)) -Destination (Join-Path $r ('src\'+$n)) -Force
}
Copy-Item -LiteralPath (Join-Path $audit 'dev\TestAuditCorrections.ps1') -Destination (Join-Path $r 'dev\TestAuditCorrections.ps1') -Force
foreach($n in @('VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1')){
 Copy-Item -LiteralPath (Join-Path $pre $n) -Destination (Join-Path $r ('dev\'+$n)) -Force
}
foreach($n in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item -LiteralPath (Join-Path $audit ('package\'+$n)) -Destination (Join-Path $r ('package\'+$n)) -Force
}
Copy-Item -LiteralPath (Join-Path $pre 'WiresAcoustics.dll') -Destination $dest -Force
Write-Output 'Restored prior no-fade source, verification scripts, old package and installed DLL.'
Write-Output 'Build output and staged RC ZIP not reverted; rebuild before any subsequent installation.'
