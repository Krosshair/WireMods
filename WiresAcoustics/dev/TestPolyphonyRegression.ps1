$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics\src'
$area=[IO.File]::ReadAllText((Join-Path $root 'Area.cs'))
$starts=[IO.File]::ReadAllText((Join-Path $root 'Starts.cs'))
$plugin=[IO.File]::ReadAllText((Join-Path $root 'Plugin.cs'))
$trace=[IO.File]::ReadAllText((Join-Path $root 'AcousticTrace.cs'))
function Require([bool]$ok,[string]$msg){if(!$ok){throw 'LOOP POLYPHONY REGRESSION: '+$msg}}
Require (-not $area.Contains('f.SourceLevel = FireLevel(size)')) 'old arbitrary collider-size fire attenuation is still present'
Require ($area.Contains('f.SourceLevel = FireLevelFromRange(f.AuthoredMaxDistance);')) 'fire power is not sourced from original Valheim maxDistance'
Require ($area.Contains('ReferenceFireRange = 10f')) 'fire level normalization reference changed unexpectedly'
$filter=[IO.File]::ReadAllText((Join-Path $root 'Filter.cs'))
Require ($filter.Contains('m_physical ? m_origMax')) 'authored game range lost when physical falloff is enabled'
Require ($plugin.Contains('RandomLoopPhase = Setting("General", "Random loop start", true,')) 'phase-decorrelation config missing'
Require ($starts.Contains('RandomizeLoop(z, src);')) 'loop start missing phase decorrelation'
Require ($starts.Contains('if (src.loop)')) 'deferred and immediate loop starts not sharing phase-decorrelation path'
Require ($starts.Contains('src.timeSamples = UnityEngine.Random.Range(0, src.clip.samples);')) 'no individual sample phase for loops'
Require ($starts.Contains('f.WorldRandomAmbient') -and $starts.Contains('f.Kind == AcousticFilter.Kinds.Menu')) 'non-world loop exclusions missing'
Require ($trace.Contains('[acoustics.polyphony]') -and $trace.Contains('instances.Count') -and $trace.Contains('src.isVirtual') -and $trace.Contains('phaseSamples=') -and $trace.Contains('authoredRange=')) 'per-source diagnostics incomplete'
Write-Output 'LOOP POLYPHONY STATIC REGRESSION PASS: independent instances, world-loop phases, no extra fire attenuation, full source census'
