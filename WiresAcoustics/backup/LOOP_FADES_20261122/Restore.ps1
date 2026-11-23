$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\LOOP_FADES_20261122'
$dst='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback'}
foreach($f in @('Filter.cs','Starts.cs','GeometryEvents.cs','Budget.cs','Plugin.cs')){
 Copy-Item -LiteralPath (Join-Path $b $f) -Destination (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item -LiteralPath (Join-Path $b $f) -Destination (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $r 'dev\TestLoopFades.ps1') -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $b 'WiresAcoustics.dll') -Destination $dst -Force
Write-Output 'Pre-fades sources/scripts and Enngard DLL restored. Rebuild from source before future installation.'