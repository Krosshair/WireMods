$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\WA_EVENT_SHAPES_20261122'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
$config='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\config\wire.valheim.acoustics.cfg'
if(Get-Process valheim -EA SilentlyContinue){throw 'Close Valheim before rollback'}
foreach($file in @('SourceVolume.cs','Spots.cs','Follow.cs','Filter.cs','Plugin.cs','Area.cs','AnalyticOcclusion.cs','AnalyticWorker.cs')){
 Copy-Item (Join-Path $b ('src\'+$file)) (Join-Path $r ('src\'+$file)) -Force
}
foreach($file in @('VolumeSamplingTests\Program.cs','TestSourceVolumeIntegration.ps1','TestStandardListenerSplit.ps1','VerifyEveryImplementation.ps1','VerifyReleaseCandidate.ps1','TestRainAndShapeRelease.ps1')){
 Copy-Item (Join-Path $b ('dev\'+$file)) (Join-Path $r ('dev\'+$file)) -Force
}
Remove-Item (Join-Path $r 'dev\TestEventSourceExtent.ps1') -EA SilentlyContinue
$directions=Join-Path $r 'src\Directions.cs'
$s=[IO.File]::ReadAllText($directions)
if(!$s.Contains('float physicalWidth = !f.LocalizedEvent && f.AreaRadius > 0f')){throw 'Directions changed since backup; manual review required'}
[IO.File]::WriteAllText($directions,$s.Replace('float physicalWidth = !f.LocalizedEvent && f.AreaRadius > 0f','float physicalWidth = f.AreaRadius > 0f'))
$occ=Join-Path $r 'src\Occlusion.cs'
$s=[IO.File]::ReadAllText($occ)
$pattern='f != null && !f\.LocalizedEvent &&\s+f\.ShapeRoot != null && f\.AreaRadius > 0f \?\s+FacingPoint\(f, Hearing\.At\) : \(Vector3\?\)null'
if(![regex]::IsMatch($s,$pattern)){throw 'Occlusion changed since backup; manual review required'}
$s=[regex]::Replace($s,$pattern,'f != null && f.ShapeRoot != null && f.AreaRadius > 0f ? FacingPoint(f, Hearing.At) : (Vector3?)null')
[IO.File]::WriteAllText($occ,$s)
foreach($file in @('CHANGELOG.md','README.md','manifest.json','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$file)) (Join-Path $r ('package\'+$file)) -Force
}
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Copy-Item (Join-Path $b 'config\wire.valheim.acoustics.cfg') $config -Force
Write-Output 'Restored prior source, tests, package, Enngard DLL and config. Rebuild before any subsequent install.'
