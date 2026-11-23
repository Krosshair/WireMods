$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\ENVIRONMENT_FIELDS_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Exit Valheim before rollback'}
if(!(Test-Path (Join-Path $b 'installed.dll'))){throw 'Missing original installed DLL'}
foreach($f in @('Batch.cs','Room.cs','Ambience.cs','WeatherAcoustics.cs','RainField.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
Remove-Item (Join-Path $r 'src\EnvironmentalField.cs') -EA SilentlyContinue
foreach($f in @('TestRainImpactField.ps1','TestRainGroundSight.ps1',
 'TestContinuousRainLayers.ps1','TestRainDoorway.ps1',
 'TestRainAndShapeRelease.ps1','TestRoofVsFloor.ps1',
 'TestAmbientWindTexture.ps1','VerifyReleaseCandidate.ps1',
 'WeatherAcousticsTests\Program.cs')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored prior WA 0.7.0 ground-LOS source, tests, packaged assets and Enngard DLL. Rebuild before later modifications.'
