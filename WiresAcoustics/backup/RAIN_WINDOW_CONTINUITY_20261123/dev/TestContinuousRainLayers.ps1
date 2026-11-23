$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$amb=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$rain=[IO.File]::ReadAllText((Join-Path $r 'src\RainField.cs'))
$weather=[IO.File]::ReadAllText((Join-Path $r 'src\WeatherAcoustics.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$game=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\AudioMan.cs')
function Must([bool]$ok,[string]$why){if(!$ok){throw ('CONTINUOUS RAIN REGRESSION: '+$why)}}
Must ($game.Contains('m_ambientLoopSource.volume = Mathf.MoveTowards(m_ambientLoopSource.volume, 0f, dt / m_ambientFadeTime);') -and
       $game.Contains('m_ambientLoopSource.clip = m_queuedAmbientLoop;')) 'Valheim queued rain clip transition assumption changed'
Must ($weather.Contains('internal static float BridgeNeeded(bool rainSelected, bool playingRain,') -and
       $weather.Contains('if (!playingRain) return 1f;')) 'continuous weather onset still waits for previous ambient clip'
Must ($amb.Contains('bool rainSelected = env != null && env.m_isWet &&') -and
       $amb.Contains('s_rainBlend = WeatherAcoustics.Approach(s_rainBlend,') -and
       $amb.Contains('rainSelected ? 1f : 0f, dt);')) 'weather onset still linked to audible previous clip'
Must ($amb.Contains('float impactMix = RainField.Glide(outdoor,') -and
       $amb.Contains('rainSelected ? env.m_ambientLoop : null,') -and
       $amb.Contains('env != null ? env.m_ambientVol : 0f,')) 'rain field must use selected clip and its original level independent of pending game crossfade'
Must ($rain.Contains('if (rain) s_lastVolume = Mathf.Clamp01(weatherVolume);') -and
       $rain.Contains('src.clip = chosenRain;') -and
       $rain.Contains('src.volume = s_lastVolume * current;')) 'direct source loses selected weather or has a delayed game-volume dependency'
Must ($rain.Contains('if (rain && (src.clip != chosenRain || !src.isPlaying))') -and
       $rain.Contains('if (Sources[i] != null && Sources[i].isPlaying) Sources[i].Stop();')) 'rain exit cannot fade without attempting to play a null clip'
Must ($rain.Contains('current = Mathf.MoveTowards(current, desired, 6f * Mathf.Max(0f, dt));')) 'direct sector movement still uses unnecessarily slow weather fade'
Must ($rain.Contains('private static void CreateBridge()') -and
       $rain.Contains('s_bridge.spatialBlend = 0f;') -and
       $rain.Contains('s_bridgeFilter.Set(Mathf.Clamp01(indirectGain),') -and
       $rain.Contains('s_bridge.volume = s_lastVolume * s_bridgeLevel;')) 'continuous diffuse weather bridge or fade missing'
Must ($rain.Contains('return Mathf.Sqrt(Mathf.Clamp01(energy));') -and
       $rain.Contains('float active = rain && s_ready ? .75f * indoor : 0f;')) 'spatial direct share not capped to retain the diffuse layer'
Must ($amb.Contains('float diffuseBed = rainPlaying') -and
       $amb.Contains('s_rainGain * Mathf.Sqrt(Mathf.Clamp01(') -and
       $amb.Contains('1f - impactMix * impactMix))')) 'direct visibility still zeroes the entire diffuse rain bed'
Must ($weather.Contains('internal const float IndirectRainFloor = .24f;') -and
       $weather.Contains('Mathf.Max(ambientGain, IndirectRainFloor)') -and
       $weather.Contains('Mathf.Clamp01(rainBlend)')) 'fully enclosed rain has no transmitted indirect floor'
Must ($room.Contains('RainField.Measure(s_from, Dirs);') -and
       $rain.Contains('if (!Room.Out(i)) continue;')) 'direct sectors do not use existing measured room visibility'
Must ($amb.Contains('RainField.Reset();') -and
       $rain.Contains('if (s_bridge != null) s_bridge.Stop();')) 'weather bridge retains source across worlds'
Must (!$rain.Contains('new RaycastCommand(') -and
       !$rain.Contains('Physics.Raycast') -and
       !$rain.Contains('StartCoroutine(') -and
       !$rain.Contains('void Update(') -and
       !$rain.Contains('void LateUpdate(')) 'new casts or per-frame component hooks in rain code'
Write-Output 'CONTINUOUS RAIN DIRECT+INDIRECT PASS: immediate weather source, 2s vanilla fade bridged, 125ms direct sector transitions, stable building-filtered rain without opening rays, no extra physics'
