$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\WIND_CARRY_SHELTER_20261122'
$dst='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before rollback'}
foreach($f in @('Ambience.cs','Directions.cs','Room.cs','Plugin.cs')){
 Copy-Item (Join-Path $b $f) (Join-Path $r ('src\'+$f)) -Force
}
foreach($f in @('VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item (Join-Path $b $f) (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item (Join-Path $r 'dev\TestWindCarryShelter.ps1') -ErrorAction SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package_'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $dst -Force
Write-Output 'Restored pre-wind-carry source, package, and Enngard DLL. Rebuild source before further installs.'
