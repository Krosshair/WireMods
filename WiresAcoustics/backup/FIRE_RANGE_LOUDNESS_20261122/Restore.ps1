$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics'
$backup=Join-Path $root 'backup\FIRE_RANGE_LOUDNESS_20261122'
$profile='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard'
if (Get-Process valheim -ErrorAction SilentlyContinue) {throw 'Close Valheim before restoring WA'}
foreach($f in @('Area.cs','Filter.cs','AcousticTrace.cs')){
 Copy-Item -LiteralPath (Join-Path $backup $f) -Destination (Join-Path $root ('src\'+$f)) -Force
}
foreach($f in @('TestPolyphonyRegression.ps1','VerifyReleaseCandidate.ps1','VerifyEveryImplementation.ps1')){
 Copy-Item -LiteralPath (Join-Path $backup $f) -Destination (Join-Path $root ('dev\'+$f)) -Force
}
Remove-Item -LiteralPath (Join-Path $root 'dev\TestFireRangeLoudness.ps1') -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $backup 'WiresAcoustics.dll') -Destination (Join-Path $profile 'BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll') -Force
Write-Output 'Pre-fire-range version restored: installed DLL and source/verification scripts.'
Write-Output 'Build output was not reverted. Rebuild from restored source before any subsequent installation.'
