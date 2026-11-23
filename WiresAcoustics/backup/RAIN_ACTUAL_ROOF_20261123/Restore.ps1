$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\RAIN_ACTUAL_ROOF_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before replacing the mod DLL'}
foreach($f in @('Room.cs','WeatherAcoustics.cs','Ambience.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('WeatherAcousticsTests\Program.cs','TestRainAndShapeRelease.ps1','VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item (Join-Path $r 'dev\TestRoofVsFloor.ps1') -ErrorAction SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Previous single-loop rain RC restored in source, tests, package and Enngard. Rebuild after rollback before any further install.'
