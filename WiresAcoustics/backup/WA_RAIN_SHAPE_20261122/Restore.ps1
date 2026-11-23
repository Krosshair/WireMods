$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\WA_RAIN_SHAPE_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before rollback'}
foreach($f in @('Room.cs','Ambience.cs','WeatherAcoustics.cs','SourceVolume.cs','Area.cs','Filter.cs')){
 Copy-Item -LiteralPath (Join-Path $b ('src\'+$f)) -Destination (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('VolumeSamplingTests\Program.cs','TestSourceVolumeIntegration.ps1','TestStandardListenerSplit.ps1','VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1','WeatherAcousticsTests\Program.cs')){
 Copy-Item -LiteralPath (Join-Path $b ('dev\'+$f)) -Destination (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $r 'dev\TestRainAndShapeRelease.ps1') -ErrorAction SilentlyContinue
foreach($f in @('CHANGELOG.md','README.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item -LiteralPath (Join-Path $b ('package\'+$f)) -Destination (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item -LiteralPath (Join-Path $b 'installed.dll') -Destination $installed -Force
Write-Output 'WA pre-rain/shape source, tests, package and Enngard DLL restored. Build output and staged ZIP still refer to the later release candidate; rebuild before reinstallation.'
