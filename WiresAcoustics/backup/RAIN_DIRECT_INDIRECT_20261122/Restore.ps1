$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\RAIN_DIRECT_INDIRECT_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Exit Valheim before restoring WA DLL'}
foreach($f in @('RainField.cs','Ambience.cs','WeatherAcoustics.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('TestRainImpactField.ps1','TestRainDoorway.ps1','TestAmbientWindTexture.ps1','VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1','WeatherAcousticsTests\Program.cs')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $r 'dev\TestContinuousRainLayers.ps1') -EA SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored previous 0.7.0 rain source/tests/package and Enngard DLL. Rebuild any future release from the restored source.'
