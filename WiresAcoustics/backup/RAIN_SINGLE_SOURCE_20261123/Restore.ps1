$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\RAIN_SINGLE_SOURCE_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before rolling back the WA DLL'}
foreach($f in @('RainField.cs','Ambience.cs','WeatherAcoustics.cs','Room.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('TestRainImpactField.ps1','TestContinuousRainLayers.ps1',
 'TestRainDoorway.ps1','TestRainAndShapeRelease.ps1',
 'TestAmbientWindTexture.ps1','VerifyReleaseCandidate.ps1',
 'WeatherAcousticsTests\Program.cs')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored previous multi-source rain candidate into source, tests, package and Enngard. Rebuild before any later installation.'
