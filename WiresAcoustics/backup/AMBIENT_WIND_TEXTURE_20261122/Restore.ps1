$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\AMBIENT_WIND_TEXTURE_20261122'
$plugin='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before rollback'}
Copy-Item -LiteralPath (Join-Path $b 'Ambience.cs') -Destination (Join-Path $r 'src\Ambience.cs') -Force
foreach($f in @('VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item -LiteralPath (Join-Path $b $f) -Destination (Join-Path $r ('dev\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $r 'dev\TestAmbientWindTexture.ps1') -ErrorAction SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item -LiteralPath (Join-Path $b $f) -Destination (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item -LiteralPath (Join-Path $b 'installed.dll') -Destination $plugin -Force
Write-Output 'Restored pre-ambient-wind patch sources, tests, package, and Enngard DLL. Rebuild before future installations.'
