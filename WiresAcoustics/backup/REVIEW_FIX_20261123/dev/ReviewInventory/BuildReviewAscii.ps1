$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$inventory=@(Import-Csv "$r\WA_METHOD_INVENTORY_20261122.tsv" -Delimiter ([char]9))
$mapping=@{}
$descriptions=[IO.File]::ReadAllLines("$r\WA_REVIEW_DESCRIPTIONS_20261122.txt")
foreach($l in $descriptions | Select-Object -Skip 1) {
 if([string]::IsNullOrWhiteSpace($l)){continue}
 $s=$l.Split('|')
 if($s.Count -ne 5){throw "Bad description field"}
 $key=$s[0]+'|'+$s[1]+'|'+$s[2]
 if($mapping.ContainsKey($key)){throw "Duplicate description key"}
 $mapping[$key]=@($s[3],$s[4])
}
if($inventory.Count -ne 421){throw "Source inventory changed"}
foreach($x in $inventory){
 $key=$x.File+'|'+$x.Type+'|'+$x.Method
 $generic=$x.File+'|*|'+$x.Method
 if(!$mapping.ContainsKey($key) -and !$mapping.ContainsKey($generic)){throw "Missing note for "+$key}
}
$sb=New-Object System.Text.StringBuilder
function L([string]$s=''){[void]$script:sb.AppendLine($s)}
L '# Wires Acoustics 0.7.0: Complete Source Function Audit'
L ''
L 'Source inspected: C:\Users\kajet\WireMods\WiresAcoustics\src on 2026-11-22.'
L '47 C# files; 421 method-like members. Every row has a line number, summary from inspected implementation, and judgement.'
L ''
L '## Prioritized code findings'
L ''
L '| Priority | File and line | Source-level finding |'
L '|---|---|---|'
L '| P0 | Ambience.cs:87-131 versus ValheimSOURCE AudioMan.cs:290-332,633-645 | WA multiplies wind volume every render Update; vanilla assigns it only in FixedUpdate. Consecutive Updates compound gain. During pause FixedUpdate stops and gain may drift. Source-confirmed mismatch; audible output needs testing. |'
L '| P0 | Budget.cs:103-107 and Filter.cs:554-583 | Estimated gain below 0.001 causes hard mute; filter zeroes audio under 0.0001. This can make an obstructed loud source completely silent. |'
L '| P0 | Batch.cs:978-983; 209,217 | A whole audio tick shares 0.5 ms stopwatch budget before Batch can start new rays. Heavy or consistent overload could starve propagation. |'
L '| P1 | Occlusion.cs:710-725 | Unchanged sources are still forced to recast after 2 seconds. 0.5-second Recheck traversal exists. This is periodic polling despite source comments. |'
L '| P1 | Directions.cs:77-161 | Per-frame transform job schedules even when active sources stand still. Nonzero fixed CPU overhead; cost needs profiler. |'
L '| P1 | SourceVolume.cs:75-102 | Finite 4/8/16/32 rays, count selected by angular thresholds; visibility may transition in audible steps. |'
L '| P1 | Filter.cs:702-820 | HRTF has handmade interaural delay/head shadow/back-notch model rather than measured HRTF/Steam Audio convolution. |'
L '| P1 | SpectralPaths.cs:20-25,95-112 and Occlusion.cs:387-426 | Real Steam Audio 3-band presets at 400Hz/2.5kHz/15kHz are mapped to DC+2kHz low-pass; third band discarded. |'
L '| P1 | Room.cs:443-635 | First hit per room sample means opening fraction cannot account for a solid wall behind a known open panel. Room/reverb also has authored multipliers. |'
L '| P1 | OpenPanelArea.cs:11-29 | Static table for seven prefab IDs: correct efficient design; aperture percentages are authored estimates. |'
L '| P1 | Occlusion.cs:575-588; Ambience.cs; Ocean.cs:123-164 | Soft-material muffling, rain/ocean/wind subjective levels remain tuning rather than established physical law. |'
L '| P2 | Ocean.cs:65-97; Room.cs:210-216 | Ocean rescans even when stationary every 4 sec; room scans Unity reverb zones periodically. |'
L '| P2 | Batch.cs:587-950 | 363-line path solver with 50 branches, substantial change and stale-result state; high regression risk. |'
L '| P2 | Occlusion.cs:381,544-574 | Old mass-law and collider-thickness helper methods no longer used by production wall path. |'
L '| P2 | Plugin.cs:86-94 | Unity real voices increased from 32 to 128, trading virtualization for additional mixer work. |'
L ''
L '## Positive engineering'
L ''
L 'Reused persistent native buffers, batched physics jobs, change-generation checks, source lifecycle cleanup, source provenance, shared edge caching and repeatable regression tests are meaningful positives. They do not establish in-game fidelity or CPU benefit on their own.'
L ''
L '## All functions and explicit property bodies'
L ''
foreach($g in ($inventory|Group-Object File)){
 L ''
 L ('### '+$g.Name+' ('+$g.Count+' methods)')
 L ''
 L '| Function | Source line / LOC | How it works | Assessment |'
 L '|---|---:|---|---|'
 foreach($x in ($g.Group|Sort-Object {[int]$_.Line})){
  $key=$x.File+'|'+$x.Type+'|'+$x.Method
  $generic=$x.File+'|*|'+$x.Method
  $v=if($mapping.ContainsKey($key)){$mapping[$key]}else{$mapping[$generic]}
  $name=$x.Type+'.'+$x.Method
  $how=[string]$v[0]
  $status=[string]$v[1]
  L ('| '+$name+' | '+$x.Line+' / '+$x.LOC+' | '+$how+' | '+$status+' |')
 }
}
L ''
L '## Anonymous function bodies (4 lambda expressions)'
L ''
L '| Function | Source line | Mechanism | Assessment |'
L '|---|---:|---|---|'
L '| Batch.Loudest comparator | Batch.cs:170 | Sorts pending requests by urgent change, probe status, expected loudness, then distance. | Hot sorting path. |'
L '| Budget.Levels.Sort comparator | Budget.cs:178 | Sorts diagnostic gain changes in ascending dB order to report worst attenuation first. | Debug only. |'
L '| Portals.List.Sort comparator | Portals.cs:83 | Ranks detected portal openings by their estimated aperture share. | Finite topology. |'
L '| Portals.neighbour comparator | Portals.cs:110 | Sorts directional neighbors by angular proximity with index tie-break. | One-time topology setup. |'
L ''
L '## Limits'
L ''
L 'This audit is static code review, not execution inside Valheim. No performance trace, listening verification or exhaustive mod-compatibility test was completed in this audit.'
L 'Reference for material 3-band energy transmission: https://valvesoftware.github.io/steam-audio/doc/capi/scene.html'
L 'Source inventory: WA_METHOD_INVENTORY_20261122.tsv'
L 'Human function summaries: WA_REVIEW_DESCRIPTIONS_20261122.txt'
L 'Roslyn extractor: dev/ReviewInventory/Program.cs'
L 'No production source or installed DLL was changed as part of this source review.'
$path="$r\WA_COMPLETE_FUNCTION_AUDIT_20261122.md"
[IO.File]::WriteAllText($path,$sb.ToString(),[Text.UTF8Encoding]::new($false))
"REPORT="+$path
"METHODS="+$inventory.Count
"BYTES="+((Get-Item $path).Length)
