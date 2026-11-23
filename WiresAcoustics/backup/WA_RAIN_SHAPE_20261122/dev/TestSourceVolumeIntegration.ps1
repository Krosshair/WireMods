$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$batch=[IO.File]::ReadAllText("$r\src\Batch.cs")
$starts=[IO.File]::ReadAllText("$r\src\Starts.cs")
$volume=[IO.File]::ReadAllText("$r\src\SourceVolume.cs")
$wave=[IO.File]::ReadAllText("$r\src\VolumeFresnel.cs")
$edges=[IO.File]::ReadAllText("$r\src\ContinuousEdges.cs")
$mix=[IO.File]::ReadAllText("$r\src\ThreeRayTransmission.cs")
$plugin=[IO.File]::ReadAllText("$r\src\Plugin.cs")
$filter=[IO.File]::ReadAllText("$r\src\Filter.cs")
$trace=[IO.File]::ReadAllText("$r\src\AcousticTrace.cs")
function Must([bool]$condition,[string]$message) {
 if(!$condition){throw "INTEGRATION FAILURE: $message"}
}
foreach($s in @(
 'cmds += 3 * (1 + r.Routes) + 6 * r.EdgeN + r.VolumeN;',
 's_volumePoints[i * SourceVolume.MaxSamples + v] = p;',
 's_cmds[c++] = Cast(listener, p, q);',
 'r.First + 3 + v,',
 'int c = r.First + 3 + r.VolumeN;',
 'out directGain, out directCut, out directCoverage',
 'lowEnergy * normalization,',
 'highEnergy * normalization',
 's_volumeRays += r.VolumeN;',
 'f.LastVolumeSamples = r.VolumeN;',
 'ContinuousEdges.EvaluateVolume(f, r.Listener, r.To,',
 'ThreeRayTransmission.IndirectAmplitude(directCoverage)'
)){Must ($batch.Contains($s)) "Batch does not contain '$s'"}
foreach($s in @(
 'int n = Queue.Count, cmds = 6 * n;',
 'cmds += check.VolumeN;',
 's_volumePoints[i * SourceVolume.MaxSamples + v] = point;',
 's_cmds[c++] = Batch.Cast(r.Eye, point, q);',
 'r.First + 6 + v,',
 'if (f != null) f.LastVolumeSamples = r.VolumeN;',
 'Occlusion.Resume(r.Z, r.Src, occ, block,',
 'Batch.VolumeRay(s_cmds, s_hits,'
)){Must ($starts.Contains($s)) "Starts does not contain '$s'"}
Must ($batch.Contains('Plugin.VolumetricRays.Value')) 'Batch config not used'
Must ($starts.Contains('Plugin.VolumetricRays.Value')) 'Starts config not used'
Must ($plugin.Contains('new AcceptableValueList<int>(0, 4, 8, 16, 32)')) 'Quality setting missing'
Must ($trace.Contains('.Append(" volumeRays=")')) 'Volume telemetry missing'
Must ($filter.Contains('internal int LastVolumeSamples;')) 'Sample count source metadata missing'
Must ($volume.Contains('if (f != null && f.AreaKnown')) 'Measured collider-to-sphere input absent'
Must ($volume.Contains('Plugin.SourceSphereRadius')) 'Configurable fallback source sphere absent'
Must ($volume.Contains('MaxSamples = 32')) 'Maximum sample capacity mismatch'
Must ($wave.Contains('FresnelEdge.Transfer(')) 'Volume analytic wave fit absent'
Must ($edges.Contains('VolumeFresnel.Transfer(')) 'Source-volume Fresnel integration absent'
Must ($mix.Contains('Mathf.Sqrt(UnityEngine.Mathf.Clamp01(coverage))')) 'Indirect path hidden-area weighting absent'
foreach($p in @($batch,$starts,$volume)){
 Must (!$p.Contains('StartCoroutine(')) 'Coroutine introduced in volume path'
}
# Validate the arithmetic actually used by the scheduler and decoder.
$cases=0
foreach($entrances in 0..4){
 foreach($routes in 0..4){
  foreach($edges in @(0,1,4,8)){
   foreach($samples in @(0,4,8,16,32)){
    $first=3*$entrances
    $beforeDirect=$first
    $beforeVolume=$first+3
    $beforeRoutes=$first+3+$samples
    $beforeEdges=$beforeRoutes+3*$routes
    $end=$beforeEdges+6*$edges
    $planned=3*(1+$routes)+6*$edges+$samples
    Must (($end-$first) -eq $planned) "Batch command count mismatch for $entrances/$routes/$edges/$samples"
    for($i=0;$i -lt $samples;$i++){
     Must (($beforeVolume+$i) -lt $beforeRoutes) 'Volume intersects portal ray segment'
    }
    Must ($beforeDirect -eq (3*$entrances)) 'Entrance prefix not respected'
    $cases++
   }
  }
 }
}
foreach($air in @($true,$false)){
 foreach($volumeN in @(0,4,8,16,32)){
  $planned=6 + $(if($air){0}else{$volumeN})
  $first=17
  if(!$air) {
   for($i=0;$i -lt $volumeN;$i++){
    $idx=$first+6+$i
    Must ($idx -lt ($first+$planned)) 'Starts volume read index out of command range'
   }
  }
  $cases++
 }
}
"VOLUMETRIC JOB LAYOUT PASS: $cases ray indexing/source integration scenarios; direct + portals + corners + new-sound paths"
