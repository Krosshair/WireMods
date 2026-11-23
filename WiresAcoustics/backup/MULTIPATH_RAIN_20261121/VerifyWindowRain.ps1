param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$b="$r\backup\MULTIPATH_RAIN_20261121\src"
$s="$r\src"
$n=0
function Check([bool]$ok,[string]$label) {
 if(!$ok){throw "FAIL: $label"}
 $script:n++
 Write-Output "PASS: $label"
}
$batch=Get-Content "$s\Batch.cs" -Raw
$ambient=Get-Content "$s\Ambience.cs" -Raw
$room=Get-Content "$s\Room.cs" -Raw
$baseline=Get-Content "$b\Batch.cs" -Raw
Check ($batch.Contains('openingDirection += towardOpening.normalized * energy')) 'each opening contributes an energy-weighted direction'
Check ($batch.Contains('pathGain = Mathf.Sqrt(openingEnergy)')) 'multiple distinct paths add acoustic energy'
Check ($batch.Contains('pathCutoff = Mathf.Exp(openingCutLog / openingEnergy)')) 'multiple paths blend spectral cutoffs'
Check ($batch.Contains('pathOcc = openingOccSum / openingEnergy')) 'multiple paths blend obstruction'
Check ($batch.Contains('float coherence = Mathf.Clamp01(combined / openingEnergy)')) 'opposing directions do not arbitrarily choose one'
Check ($batch.Contains('openingPrecedence / openingEnergy')) 'per-opening precedence participates in localization'
Check ($batch.Contains('openingCount > 1')) 'single opening preserves prior behavior'
Check ($batch.Contains('if (Consider(total, leg, Corners.LengthGain')) 'original verified corner diffraction retained'
Check ($batch.Contains('Diffraction.Opening(') -and $batch.Contains('Portals.LengthGain(')) 'original diffraction and path lengths retained'
Check (([regex]::Matches($batch,'Cast3\(')).Count -eq ([regex]::Matches($baseline,'Cast3\(')).Count) 'unchanged number of raycast construction calls'
Check (([regex]::Matches($batch,'RayJobs.Submit\(')).Count -eq ([regex]::Matches($baseline,'RayJobs.Submit\(')).Count) 'unchanged number of raycast batch submission sites'
Check ($ambient.Contains('EnvMan.IsWet()') -and $ambient.Contains('RainWallGain = .72f') -and $ambient.Contains('RainWallCutoff = 6500f')) 'wet-weather transmission floor enabled'
Check ($ambient.Contains('Room.BuiltRoof') -and $room.Contains('if (Built[i]) builtRoofRays++')) 'rain gain is conditioned on measured built shelter, not caves'
Check ($ambient.Contains('Mathf.MoveTowards(s_rainBlend,') -and $ambient.Contains('s_rainBlend = 0f;')) 'weather transmission fades and resets'
Check ($ambient.Contains('Bed(AmbientLoop(man), weatherBed: true)') -and $ambient.Contains('Bed(WindLoop(man), windGain, windCut)')) 'weather and wind remain separately processed'
Check ($ambient.Contains('src.Play();') -and $ambient.Contains('Copies[o, which]') -and $ambient.Contains('Portals.List[op]')) 'native window ambience source pipeline retained'
Check ($room.Contains('Portals.Find(s_from, Dirs, enclosure, path)')) 'original opening detection remains'
Check ((Get-Content "$r\package\manifest.json" -Raw |ConvertFrom-Json).version_number -eq '0.6.0') 'release number unchanged'
# Simple independent numerical sanity checks for the same energy-weighted
# vector formula. These DO NOT constitute a live in-game audio test.
function Mean([float]$x1,[float]$z1,[float]$g1,[float]$x2,[float]$z2,[float]$g2){
 $e1=$g1*$g1; $e2=$g2*$g2
 $x=$x1*$e1+$x2*$e2; $z=$z1*$e1+$z2*$e2
 $total=$e1+$e2
 $mag=[Math]::Sqrt($x*$x+$z*$z)
 [pscustomobject]@{X=$x;Z=$z;Coherence=if($total -gt 0){$mag/$total}else{0};Gain=[Math]::Sqrt($total)}
}
$d=Mean 1 0 1 0 1 1
Check ([Math]::Abs($d.X-$d.Z) -lt 1e-6) 'equal-strength perpendicular paths center between windows'
$d2=Mean 0 1 1 1 0 1
Check ([Math]::Abs($d.X-$d2.X) -lt 1e-6 -and [Math]::Abs($d.Z-$d2.Z) -lt 1e-6) 'path ordering does not affect direction'
$o=Mean 1 0 1 -1 0 1
Check ($o.Coherence -lt 1e-6) 'opposite windows produce diffuse localization'
$a=Mean 1 0 1 0 1 .1
Check ($a.X -gt 50*$a.Z) 'weak second window cannot dominate localization'
Check ([Math]::Abs($d.Gain-[Math]::Sqrt(2)) -lt 1e-6) 'two independent equal paths gain 3.01 dB before downstream limiter'
Check ($null -eq (Get-Process valheim -ErrorAction SilentlyContinue)) 'Valheim not currently running'
if(!$NoBuild) {
 dotnet build "$s\WiresAcoustics.csproj" -c Release --no-restore -v:q
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
}
Write-Output "PASS: $n source-level / numerical sanity checks. In-game audio fidelity untested."
