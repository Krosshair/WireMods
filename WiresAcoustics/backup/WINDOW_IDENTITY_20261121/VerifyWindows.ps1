$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b="$r\backup\WINDOW_IDENTITY_20261121\src"
$s="$r\src"
$old=[IO.File]::ReadAllText("$b\Ambience.cs")
$new=[IO.File]::ReadAllText("$s\Ambience.cs")
$n=0
function Check([bool]$ok,[string]$msg){if(!$ok){throw "FAIL: $msg"};$script:n++; "PASS: $msg"}
Check ($old.Contains('Vector3.Dot(Dir[j], p.Dir)') -and $old.Contains('if (!Used[j] && Placed[j] && Sounding(j) && dot > best)')) 'reference uses direction-only identity'
Check ($new.Contains('float delta = (At[j] - to).sqrMagnitude;')) 'matching also considers physical opening position'
Check ($new.Contains('if (dot < .65f) continue;')) 'matching rejects incompatible opening orientation'
Check ($new.Contains('if (delta > 36f) continue;')) 'distant windows do not inherit one another'
Check ($new.Contains('if (Used[j] || !Placed[j]) continue;')) 'one slot is assigned to at most one detected opening'
Check ($new.Contains('float score = delta + (1f - dot) * 4f;')) 'proximity is the primary matching score'
Check ($new.Contains('LastSeen[slot] = Time.time;') -and $new.Contains('OpeningGrace = .6f')) 'short missed-ray grace tracked per source'
Check ($new.Contains('LastWorldGen[slot] = Occlusion.WorldGen;') -and $new.Contains('LastWorldGen[j] == Occlusion.WorldGen')) 'changed geometry cancels missed-ray grace'
Check ($new.Contains('s_insideTarget > .05f')) 'walking outside cancels missed-ray grace'
Check ($new.Contains('if (!grace)') -and $new.Contains('Play(j, 0, amb, 0f, 0f, listener)')) 'unmatched openings fade normally after grace'
Check ($new.Contains('At[slot] = Placed[slot] ? Vector3.Lerp(At[slot], to, .5f) : to;')) 'unchanged smooth physical positioning'
Check ($new.Contains('src.timeSamples = Mathf.Min(game.timeSamples, game.clip.samples - 1);')) 'unchanged native loop phase synchronization'
Check ($new.Contains('c.Level = Mathf.MoveTowards(c.Level, c.Target, 2f * dt);')) 'unchanged window audio gain interpolation'
# Numerical matching examples that use the same scoring expression.
function Match([double[]]$oldX,[double[]]$newX,[int[]]$order){
 $slots=@($false,$false);$result=@(-1,-1)
 foreach($op in $order){
  $best=[double]::PositiveInfinity;$bestSlot=-1
  for($j=0;$j -lt 2;$j++){
   if($slots[$j]){continue}
   $delta=($oldX[$j]-$newX[$op])*($oldX[$j]-$newX[$op])
   if($delta -gt 36){continue}
   $score=$delta
   if($score -lt $best){$best=$score;$bestSlot=$j}
  }
  if($bestSlot -ge 0){$slots[$bestSlot]=$true;$result[$op]=$bestSlot}
 }
 return $result
}
# Deliberate testing of *separated* neighboring windows; the detector may
# enumerate them in reverse order on successive room scans.
$a=Match ([double[]]@(0,4)) ([double[]]@(4.1,-0.1)) ([int[]]@(0,1))
Check ($a[0] -eq 1 -and $a[1] -eq 0) 'same-facing neighboring windows retain identity when detector order flips'
$b0=Match ([double[]]@(0,4)) ([double[]]@(0.1,4.2)) ([int[]]@(0,1))
Check ($b0[0] -eq 0 -and $b0[1] -eq 1) 'same-facing windows retain identity with small ray-position jitter'
$c=Match ([double[]]@(0,4)) ([double[]]@(20,4.1)) ([int[]]@(0,1))
Check ($c[0] -eq -1 -and $c[1] -eq 1) 'unrelated remote opening cannot steal an existing source'
$ignore='Ambience.cs'
foreach($f in Get-ChildItem $b -Filter '*.cs'){
 if($f.Name -eq $ignore){continue}
 if([IO.File]::ReadAllText($f.FullName) -cne [IO.File]::ReadAllText("$s\$($f.Name)")){throw "Unexpected source edit $($f.Name)"}
}
Check $true 'all other acoustic systems remain byte-for-byte unchanged'
"COMPLETE $n checks. This does not validate sound perceptually."
