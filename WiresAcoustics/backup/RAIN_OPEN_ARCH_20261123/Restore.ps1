$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\RAIN_OPEN_ARCH_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Exit Valheim before restoring WA DLL'}
foreach($f in @('RainField.cs','Ambience.cs','WeatherAcoustics.cs','Room.cs')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('TestRainImpactField.ps1','TestContinuousRainLayers.ps1',
 'TestRainDoorway.ps1','VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$f)) (Join-Path $r ('dev\'+$f)) -Force
}
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored prior single-source roof-aware WA build, tests, docs and Enngard DLL. Rebuild before any future install.'
