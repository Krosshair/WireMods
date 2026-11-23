$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$a=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$f=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('ONE-LOOP FIELD MIXER: '+$why)}}
Must ($a.Contains('AudioSource outdoor = AmbientLoop(man);') -and
       $a.Contains('Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);')) 'original game ambient source not preserved'
Must ($a.Contains('float ambientCut = rainPlaying && EnvironmentalField.Ready ?') -and
       $a.Contains('EnvironmentalField.Cutoff :') -and
       $a.Contains('float diffuseBed = rainPlaying && EnvironmentalField.Ready ?') -and
       $a.Contains('EnvironmentalField.Gain :')) 'emitter field transfer not applied to the one original filter'
Must ($a.Contains('s_lastRainClip = env.m_ambientLoop;') -and
       $a.Contains('outdoor != null && outdoor.clip == s_lastRainClip;')) 'source identity switched before the original game recording'
Must ($a.Contains('Play(slot, 0, amb, rainNow ? 0f :')) 'extra doorway rain recording reintroduced'
Must (!$f.Contains('AddComponent<AudioSource>') -and
       !$f.Contains('AddComponent<AudioSource>') -and
       !$f.Contains('src.Play(') -and
       !$f.Contains('Source.volume =') -and
       !$a.Contains('RainField.Access(') -and
       !$a.Contains('WeatherAcoustics.Blend(')) 'old multi-voice or rain-only gain/roof scheme still active'
Must ($field=[bool]($room.Contains('EnvironmentalField.Step(from, Mask, environmentSelected);') -and
       $room.Contains('EnvironmentalField.Reset();'))) 'field resource lifecycle missing'
Must ($f.Contains('RayJobs.Dispose(ref s_cmds, ref s_hits);') -and
       $f.Contains('if (s_pending) { s_job.Complete(); s_pending = false; }')) 'world cleanup leaks jobs/native arrays'
Write-Output 'ONE ORIGINAL RAIN PLAYBACK PASS: generic field transfer filters only original AudioMan ambient loop, no patches/bridge creating audio'
