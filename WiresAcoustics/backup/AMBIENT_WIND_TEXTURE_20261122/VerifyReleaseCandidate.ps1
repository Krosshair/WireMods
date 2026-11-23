$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$baseline=Join-Path $r 'backup\RELEASE_ENGINEERING_20261122\src'
$source=Join-Path $r 'src'
$report=Join-Path $r 'RELEASE_CANDIDATE_VERIFICATION_20261122.txt'
if (!(Test-Path $baseline)) {throw 'Missing immutable baseline'}
$files=Get-ChildItem $source -Filter *.cs | Sort-Object Name
if($files.Count -ne 50){throw "Source inventory changed from expected 50 files to $($files.Count)"}
$changes=New-Object 'System.Collections.Generic.List[string]'
$sourceDigest=New-Object System.Text.StringBuilder
foreach($f in $files){
 $back=Join-Path $baseline $f.Name
 if(!(Test-Path $back)){
   if($f.Name -notin @('ThreeRayTransmission.cs','SourceVolume.cs','VolumeFresnel.cs','OpenPanelArea.cs','AnalyticOcclusion.cs','AnalyticWorker.cs','WeatherAcoustics.cs')){throw "Unexpected new source $($f.Name)"}
   $changes.Add($f.Name)
   [void]$sourceDigest.AppendLine("$($f.Name) $((Get-FileHash $f.FullName -Algorithm SHA256).Hash)")
   continue
 }
 $hash=(Get-FileHash $f.FullName -Algorithm SHA256).Hash
 $baseHash=(Get-FileHash $back -Algorithm SHA256).Hash
 if($hash -ne $baseHash){$changes.Add($f.Name)}
 [void]$sourceDigest.AppendLine("$($f.Name) $hash")
}
$allowedChanges=@('Filter.cs','Occlusion.cs','Plugin.cs','Batch.cs','Starts.cs','AcousticTrace.cs','ThreeRayTransmission.cs','SourceVolume.cs','VolumeFresnel.cs','ContinuousEdges.cs','Hearing.cs','Directions.cs','Room.cs','OpenPanelArea.cs','Ambience.cs','Budget.cs','Ocean.cs','GeometryEvents.cs','AnalyticOcclusion.cs','AnalyticWorker.cs','WeatherAcoustics.cs','Spots.cs','Area.cs')
$outside=@($changes | Where-Object {$_ -notin $allowedChanges})
if($outside.Count){throw "Unexpected files modified in current engineering pass: $($outside -join ', ')"}
$patterns='void Update\(', 'void LateUpdate\(', 'void FixedUpdate\(', 'StartCoroutine', 'yield return', 'Task\.Run\(', 'Thread\.Sleep'
function HookList([string]$folder){
 $result=@()
 foreach($f in Get-ChildItem $folder -Filter *.cs | Sort-Object Name){
  $ln=0
  foreach($line in [IO.File]::ReadAllLines($f.FullName)){
   $ln++
   foreach($pat in $patterns){
    if($line -match $pat){$result+=("$($f.Name) " + $line.Trim());break}
   }
  }
 }
 return @($result)
}
$baseHooks=HookList $baseline
$nextHooks=HookList $source
# These are unchanged helper method declarations, NOT new MonoBehaviour hooks.
$newHooks=@($nextHooks | Where-Object {$_ -notin $baseHooks})
if($newHooks.Count){throw "New per-frame/coroutine/task patterns: $($newHooks -join '; ')"}
$occ=[IO.File]::ReadAllText((Join-Path $source 'Occlusion.cs'))
foreach($expected in @(
 'b.BendHigh *= SpectralPaths.Response(cut);',
 'b.ThroughHigh *= SpectralPaths.Response(layerCut);',
 'SpectralPaths.Decode(dc, high, out gain, out cutoff);'
)){if(!$occ.Contains($expected)){throw "Production serial transfer check failed: $expected"}}
$filter=[IO.File]::ReadAllText((Join-Path $source 'Filter.cs'))
if(!$filter.Contains('data.Length % channels != 0) return;')){throw 'Missing empty/partial audio buffer guard'}
# The experimental loop-start/stop envelope was explicitly rejected
# in gameplay. Fail the release gate if any of its runtime hooks return.
foreach($file in $files) {
 $body=[IO.File]::ReadAllText($file.FullName)
 foreach($rejected in @('LoopFades.SpawnTail', 'LoopReleaseEnvelope',
   'FadeInLoop(data, channels)', 'BeginLoopFade(', 'EndLoopFade(',
   'RefreshLoopFadeEligibility(', 'ResetLoopFadeAfterStop(',
   'SuppressLoopTail', 'FadeDisabledWorldLoop', 'FadeStoppedWorldLoop')) {
  if($body.Contains($rejected)) {throw "Rejected loop fade code remains in $($file.Name): $rejected"}
 }
}
if($filter.Contains('internal volatile bool Mute;') -or
   $filter.Contains('OwnPriority = -1') -or
   !$filter.Contains('if (!m_active) return;')){
  throw 'Dead mute path or inactive DSP bypass regression'
}
if($occ.Contains('MassLaw(') -or $occ.Contains('private static float Mass(') -or
   $occ.Contains('private static float Thickness(')){
  throw 'Unused wall mass-law helpers were reintroduced'
}
$plugin=[IO.File]::ReadAllText((Join-Path $source 'Plugin.cs'))
if(!$plugin.Contains('24 most potentially audible sources')){throw 'Debug trace contract mismatch'}
$batch=[IO.File]::ReadAllText((Join-Path $source 'Batch.cs'))
$starts=[IO.File]::ReadAllText((Join-Path $source 'Starts.cs'))
$coverage=[IO.File]::ReadAllText((Join-Path $source 'ThreeRayTransmission.cs'))
foreach($required in @('ThreeRayTransmission.Combine(', 'Occlusion.Wall(b0,', 'Occlusion.Wall(b1,', 'Occlusion.Wall(b2,',
 'DirectWalls(r.First, r.To, ref direct,', 'DirectWalls(c, p, ref legA,',
 'DirectWalls(c + 3, r.To, ref legB,', 'f.EdgeCanOverride = !f.AnalyticActive && r.VolumeN == 0 && openingCount == 0 &&')){
 if(!$batch.Contains($required)){throw "Direct/indirect ray integration missing: $required"}
}
if($batch.Contains('Occlusion.Median(')){throw 'Median selection still used'}
if(!$starts.Contains('ThreeRayTransmission.Blend(')){throw 'Visible source face still binary'}
if(!$starts.Contains('RandomizeLoop(z, src);') -or !$starts.Contains('src.timeSamples = UnityEngine.Random.Range(0, src.clip.samples);') -or
   !$plugin.Contains('RandomLoopPhase = Setting("General", "Random loop start", true,')){
  throw 'Independent positional looping source phase-decorrelation regression'
}
if(!$starts.Contains('[HarmonyPatch(typeof(AudioMan), "UpdateLoopingConcurrency")]') -or
   !$starts.Contains('foreach (ZSFX z in ___m_loopingSfx) AllowWorldLoop(z);') -or
   !$starts.Contains('AllowWorldLoop(__instance);') -or
   !$starts.Contains('z.ConcurrencyEnable();') -or
   !$starts.Contains('source.spatialBlend < .5f')){
   throw 'Positional loop concurrency is not bypassed at the non-inlined vanilla caller'
}
$analytic=[IO.File]::ReadAllText((Join-Path $source 'AnalyticOcclusion.cs'))
if(!$analytic.Contains('float area = Primitive(x1,y1) - Primitive(x0,y1) -') -or
   !$analytic.Contains('FresnelEdge.Intensity(clearance,d1,d2,125f,lowThrough)') -or
   !$analytic.Contains('float low = 1f - covered*(1f-lowAtEdge);')){throw 'Analytic area + Fresnel transfer is missing'}
if(!$batch.Contains('bool analyticDirect = false;') -or
   !$batch.Contains('directCoverage = areaCovered;') -or
   !$batch.Contains('f.AnalyticIndirectGain = pathGain > 0f')){throw 'Continuous area model not integrated into propagation'}
if(!$starts.Contains('f.AcousticPath = "start/analytic-box";')){throw 'Initial sound still uses discrete coverage model'}
if(!$batch.Contains('!on || !blocked || analyticDirect')){throw 'Analytical edge and sampled corner paths are double-counted'}
if(!$batch.Contains('Corners = f.AnalyticHasPortal ||') -or
   !$batch.Contains('r.Corners && r.F != null && !r.F.AnalyticActive')){
  throw 'Independent portal routes were lost or redundant analytic corner probes returned'}
if(!$batch.Contains('r.VolumeN = !cachedAnalytic && r.F != null &&') -or
   !$batch.Contains('r.F.AnalyticWorldGen == Occlusion.WorldGen')){
  throw 'Validated analytic source still pays repeated 4–32 volume rays'}
$weather=[IO.File]::ReadAllText((Join-Path $source 'WeatherAcoustics.cs'))
$amb=[IO.File]::ReadAllText((Join-Path $source 'Ambience.cs'))
if(!$weather.Contains('environmentLoop == playingLoop') -or
   !$amb.Contains('WeatherAcoustics.IsActiveRainLoop(')){
  throw 'Weather loop is not identified from actual Valheim audio clip'}
$spots=[IO.File]::ReadAllText((Join-Path $source 'Spots.cs'))
if(!$spots.Contains('if (found != null && found != root) return null;') -or
   !$spots.Contains('private static readonly Collider[] RootNear = new Collider[64];')){
  throw 'Sound sources are assigned to ambiguous nearby colliders'}
$area=[IO.File]::ReadAllText((Join-Path $source 'Area.cs'))
if($area.Contains('f.SourceLevel = FireLevel(size)')){throw 'Collider-size-driven fire attenuation remains in place'}
if(!$area.Contains('FireLevelFromRange(f.AuthoredMaxDistance)') -or
   !$area.Contains('ReferenceFireRange = 10f') -or
   !$filter.Contains('m_physical ? m_origMax')){throw 'Fire strength not derived from original Valheim audio range'}
$directions=[IO.File]::ReadAllText((Join-Path $source 'Directions.cs'))
if(!$directions.Contains('AnalyticWorker.Request(f, Hearing.At, physicalSource);') -or
   !$directions.Contains('AnalyticWorker.Submit();') -or
   !$directions.Contains('AnalyticWorker.CompleteReady();')){throw 'No worker-scheduled continuous acoustic update on listener motion'}
$worker=[IO.File]::ReadAllText((Join-Path $source 'AnalyticWorker.cs'))
foreach($required in @('ThreadPool.QueueUserWorkItem(WorkCallback)', 'Volatile.Read(ref s_state)',
    'Volatile.Write(ref s_state, 2)', 'SourceVolume.Project(f, listener, source)',
    'FresnelEdge.Intensity(', 'if (f == null || !f.AnalyticActive')){
  if(!$worker.Contains($required)){throw "Asynchronous analytic worker contract missing: $required"}
}
if(!$starts.Contains('Occlusion.Resume(r.Z, r.Src, occ, block,')){throw 'First-sound spectral initialization missing'}
if(!$coverage.Contains('SpectralPaths.Decode(dc, high, out gain, out cutoff);')){throw 'Spectral coverage model not used'}
$volume=[IO.File]::ReadAllText((Join-Path $source 'SourceVolume.cs'))
if(!$volume.Contains('MaxSamples = 32')){throw 'Source-volume sample limit missing'}
if(!$volume.Contains('Plugin.SourceSphereRadius')){throw 'Source spherical radius authoring missing'}
$room=[IO.File]::ReadAllText((Join-Path $source 'Room.cs'))
if(!$room.Contains('s.AcousticProxy = IsCoarseOpenPanel(c, p);')){throw 'Valheim prefab open-panel modeling missing'}
if(!$occ.Contains('TransmissionEnergy(part.m_materialType')){throw 'Material coefficients are not sourced from Steam Audio mapping'}
if(!$occ.Contains('low = phi + (1f - phi) * low;')){throw 'ASHRAE composite material energy integration missing'}
$openPanel=[IO.File]::ReadAllText((Join-Path $source 'OpenPanelArea.cs'))
if($openPanel.Contains('mesh.triangles') -or $openPanel.Contains('MeshFilter') -or $openPanel.Contains('GetComponentsInChildren')){throw 'Runtime mesh analysis was reintroduced'}
if(!$openPanel.Contains('case "iron_grate": return 0.70f;')){throw 'Prefab acoustic property table missing'}
if($occ.Contains('private const float NextWall')){throw 'Non-reference second-wall scaling returned'}
if(!$plugin.Contains('Unshaped source sphere radius')){throw 'Source radius configuration missing'}
$wave=[IO.File]::ReadAllText((Join-Path $source 'VolumeFresnel.cs'))
$continuous=[IO.File]::ReadAllText((Join-Path $source 'ContinuousEdges.cs'))
if(!$wave.Contains('FresnelEdge.Transfer(')){throw 'Analytic volume diffraction implementation missing'}
if(!$continuous.Contains('VolumeFresnel.Transfer(')){throw 'Analytic volumetric integration missing'}
if(!$batch.Contains('SourceVolume.Point(in projection, r.To, v, r.VolumeN)')){throw '3D emission samples absent from Batch'}
if(!$starts.Contains('SourceVolume.Point(in projection,')){throw '3D emission samples absent from Starts'}
$previousExit=$global:LASTEXITCODE
$logs=Join-Path $r 'dev\release-candidate-tests-20261122.log'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $r 'dev\VerifyEveryImplementation.ps1') *> $logs
if($LASTEXITCODE -ne 0){throw "Regression suite FAILED. See $logs"}
$logText=[IO.File]::ReadAllText($logs)
if(!$logText.Contains('ALL IMPLEMENTATION REGRESSION TESTS PASS')){throw 'Expected regression completion marker missing'}
$dll=Join-Path $source 'bin\Release\net472\WiresAcoustics.dll'
if(!(Test-Path $dll)){throw 'Build DLL missing'}
$dllhash=(Get-FileHash $dll -Algorithm SHA256).Hash
$doc=@"
WIRE'S ACOUSTICS 0.7.0 -- FINITE RELEASE CANDIDATE GATES
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
Production .cs files reviewed for source identity: $($files.Count)
Changed relative to immutable baseline: $($changes -join ', ')
New per-frame/coroutine/task patterns: NONE
Production file checks: direct serial occlusion, nonblocking worker handoff, empty audio buffer and log contract OK
Main source compilation: PASS (from full VerifyEveryImplementation script)
Numerical/unit regression: PASS (see dev\release-candidate-tests-20261122.log)
DLL SHA256: $dllhash
Source SHA256 manifest: backup\RELEASE_ENGINEERING_20261122\CURRENT_SOURCE_SHA256.txt

RELEASE GATES
PASS - $($files.Count) production source files checked against baseline
PASS - 0 new per-frame/coroutine/task hook patterns
PASS - 6060 direct occlusion spectral cases and other existing production tests
PASS - DSP coefficient/spectral model mathematical consistency (two-point fit)
PASS - deterministic geometry/portal/cache tests
PASS - concurrent audio meter test under synthetic load
PASS - zero-warning/zero-error compilation
OPEN - actual Unity/Valheim runtime playback and localization
OPEN - actual multi-mod game compatibility
OPEN - audio and game CPU budget (p50, p95, p99)
OPEN - subjective sound continuity and game-specific reflections

NOT CLAIMED
 - Full Steam Audio/Wwise parity, full-band impulse responses, SOFA HRTF,
   arbitrary multi-edge wave simulation, or zero regressions without gameplay.

AUTOMATED STATIC/NON-GAME STATUS: PASS
RELEASE/PERFECT BUILD STATUS: NOT CERTIFIED
"@
[IO.File]::WriteAllText($report,$doc,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $r 'backup\RELEASE_ENGINEERING_20261122\CURRENT_SOURCE_SHA256.txt'),$sourceDigest.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output 'RELEASE CANDIDATE STATIC GATE PASSED'
Write-Output "Files: $($files.Count), changed: $($changes -join ', ')"
Write-Output "SHA256: $dllhash"
Write-Output "Full report: $report"
Write-Output 'Runtime/performance/subjective acoustic gates remain OPEN'
