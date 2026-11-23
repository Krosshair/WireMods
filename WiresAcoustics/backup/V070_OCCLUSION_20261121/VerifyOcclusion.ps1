param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$s='C:\Users\kajet\WireMods\WiresAcoustics\src'
$b='C:\Users\kajet\WireMods\WiresAcoustics\backup\V070_OCCLUSION_20261121'
$n=0
function Check([bool]$ok,[string]$msg) {
  if(!$ok){throw "FAILED $msg"}
  $script:n++
  "PASS $msg"
}
function Source([string]$f){[IO.File]::ReadAllText("$s\$f")}
$batch=Source 'Batch.cs'
$occ=Source 'Occlusion.cs'
$filter=Source 'Filter.cs'
$corners=Source 'Corners.cs'
$diff=Source 'Diffraction.cs'
$geom=Source 'GeometryEvents.cs'
Check ((Test-Path "$b\installed_before.dll") -and (Test-Path "$b\src\Batch.cs")) 'pre-change backup exists'
Check ($batch.Contains('internal static void GeometryChanged(')) 'geometry events enqueue audible sources'
Check ($occ.Contains('Batch.GeometryChanged(at, everywhere)')) 'world edit connects to audio refresh'
Check ($batch.Contains('Occlusion.NearLine(changed, listener, at,')) 'only nearby sound paths rechecked'
Check ($batch.Contains('f.FastGeometryResponse = true')) 'geometry-triggered gain transitions requested'
Check ($filter.Contains('m_quickOcclusion ? 1f : Glide(frames)')) 'world edit result reached within next audio buffer'
Check ($filter.Contains('m_quickOcclusion = false;')) 'fast mode is single-shot'
Check ($batch.Contains('Urgent') -eq $false -and $batch.Contains('urgentA != urgentB')) 'geometry changes receive priority over speculative probes'
Check ($batch.Contains('r.Gen = Occlusion.WorldGen')) 'job records the geometry generation'
Check ($batch.Contains('if (r.Gen != Occlusion.WorldGen')) 'stale raycast results are rejected'
Check ($batch.Contains('direct.Walls > 0 || direct.Terrain || direct.Soft > 0f')) 'geometry rather than arbitrary occlusion threshold classifies blocked paths'
Check (-not $batch.Contains('occ >= .3') -and -not $batch.Contains('occ < .3') -and -not $batch.Contains('LastOcc >= .3')) 'batch propagation no longer switches by 0.3 obstruction weight'
Check (-not $occ.Contains('occ >= .3')) 'new sounds no longer gate edge routing by occlusion weight'
Check (-not $batch.Contains('if (on && r.Started && r.EdgeN == 0')) 'no old level hold while corner probes start'
Check (-not $batch.Contains('if (on && r.Waiting && occ')) 'no old level hold while corner probes finish'
Check ($batch.Contains('Queue.Add(new Req { F = f, To = r.To') -and $batch.Contains('Corners = true')) 'newly blocked sources request edge search'
Check ($occ.Contains('EdgeMoved = .08f') -and $occ.Contains('Corners.Reach * Corners.Reach')) 'near-wall motion samples continuously computed paths more closely'
Check ($corners.Contains('internal static bool Illuminated(')) 'clear-side diffraction evaluates physical edge geometry'
Check ($batch.Contains('Corners.Illuminated(f.Records')) 'clear side diffraction integrated into direct path'
Check ($diff.Contains('internal static void LitEdge(') -and $diff.Contains('Lit(detour, 125f)')) 'clear side uses same existing Fresnel diffraction law'
Check ($corners.Contains('Occlusion.ChangedNear(rec.Gen')) 'old edge records invalidated if their wall changed'
Check ($batch.Contains('if (!on || !blocked) continue;')) 'indirect corner paths use geometric obstruction'
Check ($geom.Contains('Area.Invalidate(')) 'build/door source shape invalidation preserved'
Check ((Source 'Room.cs').Contains('RoomScale = .5f')) 'deliberate Valheim scaled-room model retained'
Check ((Source 'Plugin.cs').Contains('Version = "0.7.0"')) 'development version remains 0.7.0'
# 125Hz diffraction should be 5 dB at edge, then continuously approach 0 as clearance grows.
function Loss([double]$delta,[double]$freq) {
 $N=2.0*$delta*$freq/343.0
 if($N -le 1e-6){return 5.0}
 if($N -ge .2){return 0.0}
 $x=[Math]::Sqrt(2.0*[Math]::PI*$N)
 return [Math]::Max(0.0,5.0+20.0*[Math]::Log10($x/[Math]::Tan($x)))
}
$last=0.0
foreach($d in @(0.0,.00001,.0001,.001,.005,.01,.05,.1,.2,.3,.5)){
 $gain=[Math]::Pow(10.0,-(Loss $d 125.0)/20.0)
 if($gain -lt $last - 0.000001 -or $gain -gt 1.000001){throw "Non-monotonic illuminated-side gain at $d"}
 $last=$gain
}
Check $true 'illuminated-side gain is monotonic and tends to unity'
Check ([Math]::Abs((Loss 0.0 125.0)-5.0) -lt 0.000001 -and [Math]::Abs((Loss .5 125.0)) -lt .000001) 'lit edge transitions from -5dB at boundary to no loss at large clearance'
# All test cases only inspect code and pure math. Unity behavioral validation remains necessary.
if(!$SkipBuild){
 dotnet build "$s\WiresAcoustics.csproj" -c Release -t:Rebuild --no-restore -v:q
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
}
"RESULT $n checks passed; in-game listening and CPU profiling not yet verified"
