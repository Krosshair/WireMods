$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\ENVIRONMENT_FIELD_LIGHT_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Exit Valheim before restoring an older DLL'}
foreach($f in @('EnvironmentalField.cs','Room.cs','Ambience.cs','Batch.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('VerifyReleaseCandidate.ps1','TestRainImpactField.ps1','TestRainGroundSight.ps1',
'TestContinuousRainLayers.ps1','TestRainDoorway.ps1','TestRainAndShapeRelease.ps1','TestRoofVsFloor.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
$f=Join-Path $r 'dev\VerifyEveryImplementation.ps1'
$s=[IO.File]::ReadAllText($f)
$s=$s.Replace("'WeatherAcousticsTests','EnvironmentalFieldTests','SourceOwnershipTests'","'WeatherAcousticsTests','SourceOwnershipTests'")
[IO.File]::WriteAllText($f,$s)
Remove-Item (Join-Path $r 'dev\EnvironmentalFieldTests') -Recurse -Force -EA SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'RESTORED earlier 0.7.0 multi-patch environmental-field RC to Enngard and sources. Game was closed.'
