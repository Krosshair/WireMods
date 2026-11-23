$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b=Join-Path $r 'backup\ROOF_IMPACTS_SMOOTH_20261123'
$installed='C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll'
if(Get-Process valheim -EA SilentlyContinue){throw 'Exit Valheim before restoring WA'}
if(!(Test-Path (Join-Path $b 'installed.dll'))){throw 'Backup DLL missing'}
foreach($f in @('Ambience.cs','Room.cs','EnvironmentalField.cs','Plugin.cs','Filter.cs','WiresAcoustics.csproj')){
 Copy-Item (Join-Path $b ('src\'+$f)) (Join-Path $r ('src\'+$f)) -Force
}
Remove-Item (Join-Path $r 'src\SurfaceImpacts.cs') -EA SilentlyContinue
foreach($f in @('README.md','CHANGELOG.md','WiresAcoustics.dll')){
 Copy-Item (Join-Path $b ('package\'+$f)) (Join-Path $r ('package\'+$f)) -Force
}
Copy-Item (Join-Path $b 'dev\VerifyReleaseCandidate.ps1') (Join-Path $r 'dev\VerifyReleaseCandidate.ps1') -Force
function Undo([string]$file,[string]$now,[string]$old){
 $t=Join-Path $r ('dev\'+$file)
 $s=[IO.File]::ReadAllText($t)
 if(!$s.Contains($now)){throw "Regression test restoration expected text not found in $file"}
 [IO.File]::WriteAllText($t,$s.Replace($now,$old))
}
Undo 'VerifyEveryImplementation.ps1' "'WeatherAcousticsTests','EnvironmentalFieldTests','SurfaceImpactsTests','SourceOwnershipTests'" "'WeatherAcousticsTests','EnvironmentalFieldTests','SourceOwnershipTests'"
$vr=Join-Path $r 'dev\VerifyEveryImplementation.ps1'
$t=[IO.File]::ReadAllText($vr)
$remove='& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestSurfaceImpactsIntegration.ps1"'+[Environment]::NewLine+'if($LASTEXITCODE -ne 0){throw ''General material-impact acoustics and field transition integration failed''}'+[Environment]::NewLine
if(!$t.Contains($remove)){throw 'Integration regression block not found'}
[IO.File]::WriteAllText($vr,$t.Replace($remove,''))
foreach($test in @('TestContinuousRainLayers.ps1','TestRainAndShapeRelease.ps1')){
 Undo $test "'s_fieldCutoff :'" "'EnvironmentalField.Cutoff :'"
 Undo $test "'s_fieldGain :'" "'EnvironmentalField.Gain :'"
}
Undo 'TestContinuousRainLayers.ps1' "'float ambientCut = fieldActive ?'" "'float ambientCut = rainPlaying && EnvironmentalField.Ready ?'"
Undo 'TestContinuousRainLayers.ps1' "'float diffuseBed = fieldActive ?'" "'float diffuseBed = rainPlaying && EnvironmentalField.Ready ?'"
Undo 'TestAmbientWindTexture.ps1' "'float ambientCut = fieldActive ?'" "'float ambientCut = rainPlaying && EnvironmentalField.Ready ?'"
Undo 'TestAmbientWindTexture.ps1' "'bool fieldActive = rainPlaying && EnvironmentalField.Ready;'" "'rainPlaying && EnvironmentalField.Ready ?'"
Undo 'TestAmbientWindTexture.ps1' "'s_fieldGain :'" "'EnvironmentalField.Gain :'"
Remove-Item (Join-Path $r 'dev\SurfaceImpactsTests') -Recurse -Force -EA SilentlyContinue
Remove-Item (Join-Path $r 'dev\TestSurfaceImpactsIntegration.ps1') -EA SilentlyContinue
Copy-Item (Join-Path $b 'installed.dll') $installed -Force
Write-Output 'ROLLBACK COMPLETE: previous environmental field DLL restored to Enngard, production source/tests/docs restored. Valheim was closed.'
