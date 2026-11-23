$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$src=Join-Path $r 'src'
function Read([string]$name){[IO.File]::ReadAllText((Join-Path $src $name))}
function Require([bool]$ok,[string]$message){if(!$ok){throw "AUDIT CORRECTION FAILED: $message"}}
$ambient=Read 'Ambience.cs'
$start=$ambient.IndexOf('internal static void Pan(')
$end=$ambient.IndexOf('private static void Lean(', $start)
Require ($start -ge 0 -and $end -gt $start) 'Wind Pan source bounds'
$pan=$ambient.Substring($start,$end-$start)
Require (![regex]::IsMatch($pan,'\bw\.volume\s*(?:\*=|/=|\+=|-=|=)')) 'Vanilla wind volume mutated in render Update'
Require ($pan.Contains('windFilter.SourceLevel = s_windModulation;')) 'Wind amplitude not routed through filter'
Require ($ambient.Contains('c.F.SourceLevel = s_windModulation;')) 'Wind source copies missing equivalent modulation'
Require ($ambient.Contains('f.SourceLevel = 1f;')) 'Wind reset does not restore neutral gain'
$filter=Read 'Filter.cs'
Require ($filter.Contains('SourceLevel < .999f || SourceLevel > 1.001f')) 'Wind positive gains do not enable audio DSP'
$vanillaPath='C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\AudioMan.cs'
Require (Test-Path $vanillaPath) 'ValheimSOURCE audio implementation missing'
$vanilla=[IO.File]::ReadAllText($vanillaPath)
Require ($vanilla.Contains('UpdateWindAmbience(fixedDeltaTime)') -and
 $vanilla.Contains('m_windLoopSource.volume = Mathf.Lerp(m_windMinVol')) 'Vanilla wind-reset cadence changed'
$budget=Read 'Budget.cs'
Require (!$budget.Contains('f.Heard < .001f') -and !$budget.Contains('f.Heard < .002f')) 'Estimated wall gain still causes hard mute'
Require ($budget.Contains('if (f.Mute) Silence(f, false);')) 'Previously muted sources not restored'
$batch=Read 'Batch.cs'
Require ($batch.Contains('if (Queue.Count == 0) return;')) 'Batch can be starved by previous audio work'
Require (!$batch.Contains('if (Queue.Count == 0 || Frame.Over)')) 'Frame deadline still suppresses all pending raycasts'
Require ($batch.Contains('if (m > 0 && Frame.Over)')) 'No limit on overdue batch size'
$occ=Read 'Occlusion.cs'
Require (!$occ.Contains('LoopFallback') -and $occ.Contains('return !ChangedNear(f.CheckedGen')) 'Elapsed-time loop fallback still active'
$room=Read 'Room.cs'
Require (!$room.Contains('StationaryResample') -and $room.Contains('if (s_haveCast && !s_roomDirty')) 'Room still scans every stationary second'
$ocean=Read 'Ocean.cs'
Require (!$ocean.Contains('now - s_lastCast') -and $ocean.Contains('internal static void Invalidate(')) 'Ocean still rescans stationary shore'
$events=Read 'GeometryEvents.cs'
Require ($events.Contains('if (everywhere) Ocean.Invalidate();')) 'World edits do not invalidate ocean shoreline'
'WA AUDIT CORRECTION TESTS PASS: vanilla cadence, no volume mutation, no -60dB muting, fair batch, no idle recasts, terrain invalidation'
