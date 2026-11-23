$ErrorActionPreference='Stop'
$root='C:\Users\kajet\WireMods\WiresAcoustics\src'
$area=[IO.File]::ReadAllText((Join-Path $root 'Area.cs'))
$starts=[IO.File]::ReadAllText((Join-Path $root 'Starts.cs'))
$plugin=[IO.File]::ReadAllText((Join-Path $root 'Plugin.cs'))
$trace=[IO.File]::ReadAllText((Join-Path $root 'AcousticTrace.cs'))
function Require([bool]$ok,[string]$msg){if(!$ok){throw 'LOOP POLYPHONY REGRESSION: '+$msg}}
Require (-not $area.Contains('f.SourceLevel = FireLevel(size)')) 'arbitrary size attenuation suppresses smaller fires'
Require ($plugin.Contains('RandomLoopPhase = Setting("General", "Random loop start", true,')) 'phase-decorrelation config missing'
Require ($starts.Contains('RandomizeLoop(z, src);')) 'loop start missing phase decorrelation'
Require ($starts.Contains('if (src.loop)')) 'deferred and immediate loop starts not sharing phase-decorrelation path'
Require ($starts.Contains('src.timeSamples = UnityEngine.Random.Range(0, src.clip.samples);')) 'no individual sample phase for loops'
Require ($starts.Contains('f.WorldRandomAmbient') -and $starts.Contains('f.Kind == AcousticFilter.Kinds.Menu')) 'non-world loop exclusions missing'
Require ($trace.Contains('[acoustics.polyphony]') -and $trace.Contains('instances.Count') -and $trace.Contains('src.isVirtual') -and $trace.Contains('phaseSamples=')) 'per-source diagnostics incomplete'
Write-Output 'LOOP POLYPHONY STATIC REGRESSION PASS: independent instances, world-loop phases, no extra fire attenuation, full source census'
