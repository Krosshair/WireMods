$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\RAIN_GROUND_SIGHT_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before restoring previous WA DLL'}
foreach($f in @('Room.cs','RainField.cs','Ambience.cs','WeatherAcoustics.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('TestRainImpactField.ps1','TestRainDoorway.ps1',
 'TestContinuousRainLayers.ps1','VerifyEveryImplementation.ps1',
 'VerifyReleaseCandidate.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item (Join-Path $r 'dev\TestRainGroundSight.ps1') -EA SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored previous 0.7.0 WA source, tests, package and Enngard DLL. Rebuild before any further install.'
