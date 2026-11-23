$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\EMPIRICAL_WIND_FHWA_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before rollback'}
foreach($f in @('Directions.cs','Room.cs','Plugin.cs')){
 Copy-Item (Join-Path $b $f) (Join-Path $r ('src\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $r 'src\WindPropagation.cs') -ErrorAction SilentlyContinue
foreach($f in @('VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1','TestWindCarryShelter.ps1')){
 Copy-Item (Join-Path $b $f) (Join-Path $r ('dev\'+$f)) -Force
}
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package_'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'Restored previous cheap +3/-10 wind model, source, package and Enngard DLL. Rebuild before future installation.'
