$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$tests=@('TestRendererParity.ps1','TestDiffractionParity.ps1','TestFresnelEdge.ps1','TestSpectralPaths.ps1','TestUnderwaterFraction.ps1')
dotnet build "$r\src\WiresAcoustics.csproj" -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'Build failed'}
foreach($t in $tests){
 & powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\$t"
 if($LASTEXITCODE -ne 0){throw "FAILED $t"}
}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestFresnelGeometryParity.ps1"
if($LASTEXITCODE -ne 0){throw 'Fresnel incidence and transmission parity failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestStandardListenerSplit.ps1"
if($LASTEXITCODE -ne 0){throw 'Industry-reference listener and spherical volume contract failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestValheimMaterialGeometry.ps1"
if($LASTEXITCODE -ne 0){throw 'ValheimSOURCE prefab/material parity failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestSourceVolumeIntegration.ps1"
if($LASTEXITCODE -ne 0){throw 'Source-volume batch/new-sound integration failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestCornerRouteParity.ps1"
if($LASTEXITCODE -ne 0){throw 'Corner path spectral parity failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\SourceOwnershipTests\GenerateSourceMethod.ps1"
if($LASTEXITCODE -ne 0){throw 'Valheim production sound owner extractor failed'}
foreach($project in @('SharedEdgesTests','MeterHistoryTests','AcousticTraceTests','ObstacleTests','PortalTopologyTests','PartialOcclusionTests','VolumeSamplingTests','OpenPanelAreaTests','AnalyticOcclusionTests','WeatherAcousticsTests','SourceOwnershipTests')){
 dotnet run --project "$r\dev\$project\$project.csproj" -c Release --no-restore | Select-Object -Last 1
 if($LASTEXITCODE -ne 0){throw "FAILED $project"}
}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\OcclusionSerialTests\RunSourceTest.ps1"
if($LASTEXITCODE -ne 0){throw 'Occlusion serial path source tests failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\AreaInvalidationTests\RunSourceTest.ps1"
if($LASTEXITCODE -ne 0){throw 'Area source extraction test failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestAuditCorrections.ps1"
if($LASTEXITCODE -ne 0){throw 'WA audit corrections source integration failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestMotionVsWorldChange.ps1"
if($LASTEXITCODE -ne 0){throw 'Motion vs world change transition separation failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestPolyphonyRegression.ps1"
if($LASTEXITCODE -ne 0){throw 'Loop polyphony regression failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestFireRangeLoudness.ps1"
if($LASTEXITCODE -ne 0){throw 'Fire range loudness regression failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestWorldLoopConcurrency.ps1"
if($LASTEXITCODE -ne 0){throw 'Positional world-loop concurrency source test failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestAmbientWindTexture.ps1"
if($LASTEXITCODE -ne 0){throw 'Authored ambient wind-texture direction/shelter regression failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestWindCarryShelter.ps1"
if($LASTEXITCODE -ne 0){throw 'World-sound wind carriage and shelter regression failed'}
& powershell -NoProfile -ExecutionPolicy Bypass -File "$r\dev\TestRainAndShapeRelease.ps1"
if($LASTEXITCODE -ne 0){throw 'Rain responsiveness and true source-volume shape regression failed'}
Write-Output 'ALL IMPLEMENTATION REGRESSION TESTS PASS'
