$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$a=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$f=[IO.File]::ReadAllText("$r\src\RainField.cs")
$w=[IO.File]::ReadAllText("$r\src\WeatherAcoustics.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('SINGLE RAIN SOURCE: '+$why)}}
Must ($a.Contains('AudioSource outdoor = AmbientLoop(man);') -and
       $a.Contains('Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);')) 'original game ambient source must be rain playback and filter target'
Must ($a.Contains('RainField.Access(Hearing.At)') -and
       $a.Contains('Mathf.Lerp(s_rainGain, 1f, windowAccess)') -and
       $a.Contains('AcousticMath.LogLerp(s_rainCutoff, 22000f, windowAccess)')) 'window exposure must change a single loop gain AND spectrum'
Must ($w.Contains('internal const float IndirectRainFloor = .24f;') -and
       $w.Contains('internal const float RainRoofGain = .72f;')) 'indirect building and roof attenuation missing'
Must ($a.Contains('s_lastRainClip = env.m_ambientLoop;') -and
       $a.Contains('outdoor != null && outdoor.clip == s_lastRainClip;')) 'rain identity must be the actual currently playing source'
Must ($a.Contains('s_rainBlend = WeatherAcoustics.Approach(s_rainBlend,') -and
       $a.Contains('rainPlaying ? 1f : 0f, dt);')) 'rain filter applied to unrelated ambient recording'
Must ($a.Contains('Play(slot, 0, amb, rainNow ? 0f :')) 'legacy portal clones still duplicate rain'
Must ($room.Contains('RainField.Measure(s_from, Dirs);') -and
       $f.Contains('if (Room.Out(i)) outside += w;')) 'window measurement not tied to cached room probe'
Must (!$f.Contains('AddComponent<AudioSource>') -and !$f.Contains('AddComponent<AcousticFilter>') -and
       !$f.Contains('src.Play(') -and !$f.Contains('src.clip') -and
       !$f.Contains('CreateBridge(') -and !$f.Contains('new GameObject(')) 'rain field still creates secondary audio'
Must (!$w.Contains('BridgeNeeded(') -and !$a.Contains('RainField.Glide(') -and
       !$a.Contains('impactMix * impactMix')) 'old three-source crossfade still present'
Must (!$f.Contains('Physics.Raycast(') -and
       $f.Contains('internal const int GroundCommands = GroundSamples * 2;') -and
       $f.Contains('NativeArray<RaycastCommand> cmds') -and
       !$f.Contains('void Update(') -and !$f.Contains('void FixedUpdate(') -and
       !$f.Contains('void LateUpdate(') -and !$f.Contains('StartCoroutine(')) 'rain sight must use bounded asynchronous physics, never synchronous or new per-frame loops'
Write-Output 'SINGLE-RAIN-SOURCE INTEGRATION PASS: one original rain recording, no audio clones or bridge, bounded rainy async ground visibility only'
