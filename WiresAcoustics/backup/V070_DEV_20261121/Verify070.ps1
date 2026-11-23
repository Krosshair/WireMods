param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$s="$r\src"
$b="$r\backup\V070_DEV_20261121\src"
$n=0
function Check([bool]$ok,[string]$msg){
 if(!$ok){throw "FAIL $msg"}
 $script:n++
 Write-Output "PASS $msg"
}
function T([string]$file) {return [IO.File]::ReadAllText("$s\$file")}
function Before([string]$file) {return [IO.File]::ReadAllText("$b\$file")}
$area=T 'Area.cs';$batch=T 'Batch.cs';$direction=T 'Directions.cs'
$portal=T 'Portals.cs';$room=T 'Room.cs';$ocean=T 'Ocean.cs'
$filt=T 'Filter.cs';$geom=T 'GeometryEvents.cs'
Check ($area.Contains('owner.InverseTransformPoint(world)') -and $area.Contains('AddCollider(owner, c')) 'geometry represents oriented colliders in owner-local space'
Check ($area.Contains('bc.center, bc.size') -and $area.Contains('mc.sharedMesh.bounds')) 'box and mesh geometry use authored collider bounds'
Check ($area.Contains('owner.TransformPoint(s.Center + Vector3.up * s.Half.y)')) 'lid source uses oriented local top'
Check ((T 'Occlusion.cs').Contains('owner.InverseTransformPoint(eye)') -and (T 'Occlusion.cs').Contains('return owner.TransformPoint(c + new Vector3(')) 'visible source-face uses matching local coordinates'
Check ($area.Contains('internal static void Invalidate(Transform owner)') -and $geom.Contains('Area.Invalidate(__instance.transform)')) 'cached geometry invalidated by build and door events'
Check ($geom.Contains('if (everywhere) Area.InvalidateAll()')) 'full geometry rebuild invalidates shape caches'
Check ($direction.Contains('1f - Mathf.Exp(-Dt / tau)')) 'Doppler smoothing derives from elapsed time'
Check ($direction.Contains('Dirs[i] = new Vector4(p.x, p.y, p.z')) 'direction job returns world positions'
Check ($direction.Contains('Vector3 direction = worldSource - lp;') -and $direction.Contains('inv * (direction / currentDist)')) 'HRTF uses current listener coordinates'
Check ($portal.Contains('cosHalf <= 0f ? dist * 4f')) 'large openings cannot generate negative radii'
Check ($portal.Contains('internal const int Routes = 4;') -and $batch.Contains('P0, P1, P2, P3')) 'all four detected opening candidates can be checked'
Check ($batch.Contains('if (r.Routes > 3) Cast3(r.P3.Point, r.To, q, ref c);')) 'fourth route has real verification rays'
Check ($batch.Contains('if (r.Routes > 3) r.P3 = Portals.List[3];')) 'fourth route retains physical opening identity'
Check ($batch.Contains('f.RouteSpread = pathDispersion * share;') -and $direction.Contains('Mathf.Max(physicalWidth, f.RouteSpread)')) 'multi-opening angular dispersion reaches HRTF'
Check ($batch.Contains('float onset = Mathf.SmoothStep') -and $batch.Contains('float late = Mathf.SmoothStep')) 'precedence onset and separation have no hard switching'
Check ($ocean.Contains('ShoreWeight[d] = w;') -and $ocean.Contains('Room.Exposure(ShoreDir[i], out Vector3 via)')) 'ocean shelter evaluated for each real water-bearing direction'
Check ($ocean.Contains('if (total > 1e-8f)') -and $ocean.Contains('open /= total;')) 'ocean directional shelter combines continuously by detected energy'
Check (-not $ocean.Contains('new AudioSource(') -and -not $ocean.Contains('ocean_direction_')) 'no duplicated ocean audio emitters'
Check ($room.Contains('RoomScale = .5f') -and $room.Contains('LivedIn = .1f')) 'Valheim effective-room scaling and furnished-room adaptation preserved'
Check ($filt.Contains('private void Spatialize(float[] dataArr, int ch') -and $filt.Contains('private void SmoothOcclusion(int frames')) 'HRTF and filter pipeline retained'
$start='        private void OnAudioFilterRead(float[] data, int channels)'
$baselineF=Before 'Filter.cs'
$oldAt=$baselineF.IndexOf($start)
$newAt=$filt.IndexOf($start)
Check ($oldAt -ge 0 -and $newAt -ge 0) 'DSP comparison anchors found'
Check ($baselineF.Substring($oldAt) -ceq $filt.Substring($newAt)) 'audio callback and DSP code identical to current 0.6.0 baseline'
$beforeAll=(Get-ChildItem $b -Filter *.cs |ForEach-Object {[IO.File]::ReadAllText($_.FullName)}) -join [Environment]::NewLine
$afterAll=(Get-ChildItem $s -Filter *.cs |ForEach-Object {[IO.File]::ReadAllText($_.FullName)}) -join [Environment]::NewLine
$baselinePatch=([regex]::Matches($beforeAll,'\[HarmonyPatch')).Count
$currentPatch=([regex]::Matches($afterAll,'\[HarmonyPatch')).Count
Check ($currentPatch -eq $baselinePatch) "Harmony patch declarations unchanged ($currentPatch)"
$values=@()
foreach($fps in @(30,60,120)){
 $v=0.0;$dt=1.0/$fps;$tau=.07469
 for($i=0;$i -lt $fps;$i++){$a=1.0-[Math]::Exp(-$dt/$tau);$v+=(1.0-$v)*$a}
 $values+=$v
}
Check ([Math]::Abs($values[0]-$values[1]) -lt 1e-9 -and [Math]::Abs($values[1]-$values[2]) -lt 1e-9) 'Doppler velocity reaches same result after 1 sec at 30/60/120 FPS'
$w=2.0;$h=1.0;$angle=[Math]::PI/4
$oldWorldWidth=[Math]::Abs($w*[Math]::Cos($angle))+[Math]::Abs($h*[Math]::Sin($angle))
Check ([Math]::Abs($oldWorldWidth-$w) -gt .01) 'world-axis bounding volume does not preserve rotated local dimensions'
foreach($deg in @(1,15,45,75,90,100,130,170,179)){
 $c=[Math]::Cos($deg * [Math]::PI/180.0)
 $rad=if($c -le 0){4.0}else{[Math]::Min(4.0,[Math]::Sqrt(1-$c*$c)/[Math]::Max(0.000001,$c))}
 if([double]::IsNaN($rad) -or $rad -lt 0 -or $rad -gt 4.00001){throw "Invalid portal radius $deg => $rad"}
}
Check $true 'nine representative opening angles produce finite nonnegative radius'
if(!$NoBuild){
 dotnet build "$s\WiresAcoustics.csproj" -c Release -t:Rebuild --no-restore -v:q
 if($LASTEXITCODE -ne 0){throw 'Release compilation failed'}
}
Write-Output "RESULT: $n checks passed; runtime acoustic/performance testing is still required"
