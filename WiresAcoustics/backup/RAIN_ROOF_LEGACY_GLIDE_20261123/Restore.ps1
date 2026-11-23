$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\RAIN_ROOF_LEGACY_GLIDE_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before restoring WA'}
foreach($f in @('Ambience.cs','WeatherAcoustics.cs','Room.cs','EnvironmentalField.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('VerifyReleaseCandidate.ps1','TestRoofVsFloor.ps1',
'VerifyEveryImplementation.ps1','TestContinuousRainLayers.ps1',
'TestRainAndShapeRelease.ps1','TestAmbientWindTexture.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
Copy-Item (Join-Path $b 'dev\WeatherAcousticsTests_Program.cs') (Join-Path $r 'dev\WeatherAcousticsTests\Program.cs') -Force
foreach($f in @('README.md','CHANGELOG.md','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'RESTORED: previous single-voice exterior-field WA (without legacy roof boost), tests, docs, package and Enngard DLL. Valheim was closed.'
