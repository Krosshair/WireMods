$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\WA_RAIN_FIELD_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Exit Valheim before rollback'}
foreach($f in @('Ambience.cs','Room.cs','WeatherAcoustics.cs')){
 Copy-Item (Join-Path $b $f) (Join-Path $r ('src\'+$f)) -Force
}
Remove-Item (Join-Path $r 'src\RainField.cs') -ErrorAction SilentlyContinue
foreach($f in @('VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1','TestRainDoorway.ps1','TestAmbientWindTexture.ps1','WeatherAcousticsTests\Program.cs')){
 Copy-Item (Join-Path $b ($f.Replace('\','_'))) (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item (Join-Path $r 'dev\TestRainImpactField.ps1') -EA SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package_'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'WA 0.7.0 previous rain/doorway implementation restored. Rebuild before further installation.'
