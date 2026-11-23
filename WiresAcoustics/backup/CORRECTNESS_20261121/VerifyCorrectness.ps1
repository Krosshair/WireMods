$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$s="$r\src"
$b="$r\backup\CORRECTNESS_20261121\src"
$oc=[IO.File]::ReadAllText("$s\Ocean.cs")
$di=[IO.File]::ReadAllText("$s\Diffraction.cs")
$oldO=[IO.File]::ReadAllText("$b\Ocean.cs")
$oldD=[IO.File]::ReadAllText("$b\Diffraction.cs")
$starts=[IO.File]::ReadAllText("$s\Starts.cs")
$oldStarts=[IO.File]::ReadAllText("$b\Starts.cs")
$n=0
function Verify([bool]$ok, [string]$name) {
 if(-not $ok){throw "FAIL: $name"}
 $script:n++
 "PASS: $name"
}
Verify ($oldO.Contains('if (s_pending) { s_job.Complete(); s_pending = false; Read(); }')) 'reference ocean had stale route when read lost sea'
Verify ($oc.Contains('if (wasLocated && !s_have) RestoreUnlocated(src);')) 'detected loss of shoreline clears audio route'
Verify ($oc.Contains('f.Routed = false;') -and $oc.Contains('f.RoutePoint = Vector3.zero;')) 'stale HRTF route cleared'
Verify ($oc.Contains('f.Set(1f, 22000f, false);') -and $oc.Contains('f.Physical(false);')) 'old filter and synthetic distance rolloff restored'
Verify ($oc.Contains('!ReferenceEquals(s_zoneSystem, ZoneSystem.instance)')) 'world identity checked'
Verify ($oc.Contains('s_zoneSystem = null;') -and ([regex]::Matches($oc,'SeaZones.Clear\(\)')).Count -ge 2) 'sea-zone classifications reset on world transitions'
Verify ($oc.Contains('RayJobs.Submit(s_cmds, s_hits, 16, 1)') -and $oldO.Contains('RayJobs.Submit(s_cmds, s_hits, 16, 1)')) 'shoreline ray scheduling unchanged'
Verify ($oc.Contains('if (s_have) src.transform.position = Vector3.Lerp(src.transform.position, s_point, Follow);')) 'shoreline position interpolation unchanged'
Verify ($oc.Contains('Shade(f, l.transform.position, src.transform.position)') -and $oldO.Contains('Shade(f, l.transform.position, src.transform.position)')) 'original wind/obstruction shade remains'
Verify ($oc.Contains('if (s_pending) { s_job.Complete(); s_pending = false; }')) 'outstanding ray job drained on world transition'
Verify ($di.Contains('while (low < high)') -and $di.Contains('if (Gains[mid] <= gain) high = mid;')) 'ordered binary search used'
Verify ($oldD.Contains('for (int i = 0; i < Gains.Length; i++) if (Gains[i] <= gain) return i * Step;')) 'reference diffraction linear semantics captured'
Verify ($oldStarts.Contains('if (top < 0) return;') -and $starts.Contains('if (top < 0) continue;')) 'open-air placement tries later candidates after an empty terrain sample'
Verify ($starts.Contains('if (any) t.position = last;') -and $starts.Contains('if (flat.sqrMagnitude < Occlusion.NotOver * Occlusion.NotOver')) 'placement still retains fallback and building avoidance'
Verify (([regex]::Matches($starts,'new RaycastCommand')).Count -eq ([regex]::Matches($oldStarts,'new RaycastCommand')).Count) 'open-air ray work unchanged'
# Run the exact original and replacement compare algorithms against a
# generated monotone decaying gain lookup with flat tail. Test NaN as well.
[float[]]$gains = New-Object float[] 1001
for($i=0;$i -lt $gains.Length;$i++){
 $gains[$i] = [single][Math]::Pow(10,[Math]::Max(-20,-5-(0.25*[Math]::Sqrt($i)))/20)
}
for($i=1;$i -lt $gains.Length;$i++){
 if($gains[$i] -gt $gains[$i-1]){throw 'Test table not monotone'}
}
$candidates=[System.Collections.Generic.List[float]]::new()
foreach($f in @([float]0,[float]0.01,[float]0.1,[float]0.3,[float]0.5,[float]1,[float]1.5,[float]::NaN)){$candidates.Add([float]$f)}
foreach($v in $gains){$candidates.Add($v);$candidates.Add([float]($v*0.9999))}
$testCount=0
foreach($v in $candidates){
 $linear=$gains.Length
 for($i=0;$i -lt $gains.Length;$i++){
  if($gains[$i] -le $v){$linear=$i;break}
 }
 $low=0;$high=$gains.Length
 while($low -lt $high){
  $mid=$low+[int][Math]::Floor(($high-$low)/2)
  if($gains[$mid] -le $v){$high=$mid}else{$low=$mid+1}
 }
 if($low -ne $linear){throw "Lookup differs for input $v old=$linear new=$low"}
 $testCount++
}
Verify ($testCount -gt 2000) "$testCount monotonic lookup regression cases matched exactly"
$originalFiles=Get-ChildItem $b -Filter '*.cs'
foreach($f in $originalFiles){
 if($f.Name -in @('Ocean.cs','Diffraction.cs','Starts.cs')){continue}
 if([IO.File]::ReadAllText($f.FullName) -cne [IO.File]::ReadAllText("$s\$($f.Name)")){
  throw "Unexpected non-target code change: $($f.Name)"
 }
}
Verify ($true) 'all other C# files unchanged from before this correction pass'
if(-not (Get-Process valheim -ErrorAction SilentlyContinue)) {Verify $true 'game is closed for deployment'}
else {throw 'Valheim still running'}
"PASS: $n checks; $testCount numerical test inputs; runtime listening not conducted."
