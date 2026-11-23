$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\WA_RAIN_DOORWAY_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before restoring WA DLL'}
foreach($file in @('Ambience.cs','WeatherAcoustics.cs')){
 Copy-Item (Join-Path $b ('src\'+$file)) (Join-Path $r ('src\'+$file)) -Force
}
foreach($file in @('WeatherAcousticsTests\Program.cs','VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$file)) (Join-Path $r ('dev\'+$file)) -Force
}
# Restore the existing wind-texture test's old expected bed call.
$f=Join-Path $r 'dev\TestAmbientWindTexture.ps1'
$s=[IO.File]::ReadAllText($f)
$old='Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);'
if(!$s.Contains($old)){throw 'Wind test changed since backup; review it before rollback'}
[IO.File]::WriteAllText($f,$s.Replace($old,'Bed(outdoor, cutoff: ambientCut, weatherBed: true);'))
Remove-Item (Join-Path $r 'dev\TestRainDoorway.ps1') -EA SilentlyContinue
foreach($file in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$file)) (Join-Path $r ('package\'+$file)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Prior WA 0.7.0 source/test/package and Enngard DLL restored. Rebuild before later install. The newer staged ZIP is not touched.'
