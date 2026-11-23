$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$inventory=@(Import-Csv "$r\WA_METHOD_INVENTORY_20261122.tsv" -Delimiter ([char]9))
$mapping=[System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$descriptions=[IO.File]::ReadAllLines("$r\WA_REVIEW_DESCRIPTIONS_20261122.txt")
foreach($l in $descriptions | Select-Object -Skip 1) {
 if([string]::IsNullOrWhiteSpace($l)) {continue}
 $s=$l.Split('|')
 if($s.Count -ne 5) {throw "Bad description: $l"}
 $key=$s[0]+'|'+$s[1]+'|'+$s[2]
 if($mapping.ContainsKey($key)) {throw "Duplicate description: $key"}
 $mapping.Add($key,[string[]]@($s[3],$s[4]))
}
$missing=@()
foreach($x in $inventory) {
 $key="$($x.File)|$($x.Type)|$($x.Method)"
 $generic="$($x.File)|*|$($x.Method)"
 if(!$mapping.ContainsKey($key) -and !$mapping.ContainsKey($generic)) {$missing+=$key}
}
if($missing.Count -ne 0) {throw "Missing reviewer notes: $($missing -join ', ')"}
if($inventory.Count -ne 421) {throw "Changed source inventory count: $($inventory.Count)"}
$sb=[System.Text.StringBuilder]::new(130000)
function L([string]$s='') {[void]$sb.AppendLine($s)}
L '# Wires Acoustics 0.7.0 — Complete Function Audit'
L ''
L 'Source inspected: C:\Users\kajet\WireMods\WiresAcoustics\src (snapshot 2026-11-22).'
L 'Exhaustive Roslyn inventory: 47 C# files and 421 defined method-like members (methods, hooks, constructors, local functions and accessors). Every row includes source location, quick implementation summary and judgement tag.'
L ''
L '## Priority findings'
L ''
L '| Priority | Source | Evidence from implementation | Assessment |'
L '|---|---|---|---|'
L '| P0 | Budget.cs:103-107, Filter.cs:554-583 | Audibility estimator mutes sources below 0.001 (-60 dB), restores above 0.002; audio callback explicitly zeroes gain <0.0001. Does not use actual measured audible RMS. | Confirmed hard-silence mechanism; relevant to muted machines. |'
L '| P0 | Batch.cs:978-983, 209,217 | Stopwatch gives whole audio update 0.5 ms, later audio ray batches are postponed if the time already elapsed. Under persistently heavy work they could starve. | Scheduling risk; actual frequency unprofiled. |'
L '| P1 | Occlusion.cs:710-725; Room.cs:203-204 | Even unchanged looping audio is forced into a resample after 2 seconds; global source scanning runs on a half-second cadence. | Violates comment claiming no time fallback; contrary to strictly event-driven intent. |'
L '| P1 | Directions.cs:77-161 | Each active audio frame consumes last transform job and schedules next one, calculating direction and Doppler even for stationary tracked sources. | Nonzero continuous CPU cost, not purely event driven. |'
L '| P1 | SourceVolume.cs:75-102 | Four quality tiers 4/8/16/32 source rays give discrete visibility, changing tiers at angular thresholds. | Plausible source of audible steps; perceptual test required. |'
L '| P1 | Filter.cs:702-820 | Binaural routine is handcrafted delay/headshade/notch, not a measured HRTF impulse-response dataset. | Psychoacoustic approximation marketed as HRTF. |'
L '| P1 | SpectralPaths.cs:20-25,95-112; Occlusion.cs:387-426 | Published Steam Audio 3 bands (400 Hz, 2.5 kHz, 15 kHz) are approximated with DC + 2 kHz filter, omitting the high band. | Genuine coefficients, incomplete spectral reproduction. |'
L '| P1 | Room.cs:443-635 | Room samples first collider per direction; open-panel weighting cannot see solid wall behind that collider; additional RT60 and reverb multipliers are authoring decisions. | Geometry/realism limitation. |'
L '| P1 | OpenPanelArea.cs:11-29 | Exactly seven prefab names use hardcoded aperture area constants. | Correct cheap runtime structure; numbers not yet measured. |'
L '| P1 | Occlusion.cs:575-588, Ambience.cs, Ocean.cs:123-164 | Soft-obstacle tables, wind/ocean shelter scaling and ambient swells are gameplay-authored, not documented physical laws. | Mark clearly as content design and test audibly. |'
L '| P2 | Ocean.cs:65-97; Room.cs:210-216 | Ocean rescans after four stationary seconds, shades four times per second; room runs reverb-zone checks on timer. | Periodic work despite event-driven design goal. |'
L '| P2 | Batch.cs:587-950; Room.cs:443-635 | Core apply function is 363 lines, 50 conditional branches; room solver 191 lines, 40 conditional branches. | Harder to reason about regressions. |'
L '| P2 | Occlusion.cs:381,544-574 | Mass law and wall thickness helpers remain after newer preset-material path superseded them. | Dead-code removal candidate. |'
L '| P2 | Plugin.cs:86-94 | Unity real voice limit raised 32 -> 128. | May reduce virtualization but increases worst-case mixer workload. |'
L ''
L '## Positive architecture'
L ''
L 'The implementation uses reuse of persistent NativeArrays, batched Unity physics casts, source lifecycle cleanup, change-generation validation, source provenance, shared edge caching, bounded histories and a sizeable deterministic regression suite. These are substantive, not merely comments. However none proves in-game audio fidelity or frame cost.'
L ''
L '## Method-by-method table'
L ''
$groups=$inventory | Group-Object File
foreach($g in $groups) {
 L ''
 L ("### "+$g.Name+" — "+$g.Count+" methods/accessors")
 L ''
 L '| Function | Source line · LOC | How it works | Assessment |'
 L '|---|---:|---|---|'
 foreach($x in $g.Group | Sort-Object {[int]$_.Line}) {
  $key="$($x.File)|$($x.Type)|$($x.Method)"
  $generic="$($x.File)|*|$($x.Method)"
  $v=if($mapping.ContainsKey($key)){$mapping[$key]}else{$mapping[$generic]}
  $name=($x.Type+'.'+$x.Method).Replace('|','/')
  $how=([string]$v[0]).Replace('|','/')
  $judgment=([string]$v[1]).Replace('|','/')
  L ("| "+$name+" | "+$x.Line+" · "+$x.LOC+" | "+$how+" | "+$judgment+" |")
 }
}
L ''
L '## Limitations and external references'
L ''
L 'This is a static source audit; no listening test, Valheim profiler capture, reliable end-to-end Steam Audio A/B test or updated log is part of this report. P0/P1/P2 rate risk or priority, not certainty that a given bug occurs under every runtime condition.'
L ''
L '- Steam Audio material energy fractions and frequency bands: https://valvesoftware.github.io/steam-audio/doc/capi/scene.html'
L '- Steam Audio volume-source and transmission behavior: https://valvesoftware.github.io/steam-audio/doc/unity/source.html'
L '- Unity audio callback: https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnAudioFilterRead.html'
L ''
L 'Reproduction/source integrity: WA_METHOD_INVENTORY_20261122.tsv and WA_REVIEW_DESCRIPTIONS_20261122.txt; generator dev/ReviewInventory/Program.cs. No src files, installed DLLs, config or game processes changed in this audit.'
$path="$r\WA_COMPLETE_FUNCTION_AUDIT_20261122.md"
[IO.File]::WriteAllText($path,$sb.ToString(),[Text.UTF8Encoding]::new($false))
$rows=($inventory|Measure-Object).Count
"REPORT=$path"
"ROWS=$rows"
"BYTES=$((Get-Item $path).Length)"
