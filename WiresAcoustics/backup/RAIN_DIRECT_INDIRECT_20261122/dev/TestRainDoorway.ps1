$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$amb=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$weather=[IO.File]::ReadAllText((Join-Path $r 'src\WeatherAcoustics.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$portals=[IO.File]::ReadAllText((Join-Path $r 'src\Portals.cs'))
$game=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\AudioMan.cs')
function Must([bool]$ok,[string]$reason) { if(!$ok){throw ('RAIN DOORWAY REGRESSION: '+$reason)} }
# The original game changes clips in AudioMan.UpdateAmbientLoop even if
# WA's listener ray survey remains static. A changed Game AudioClip must
# rebind the active doorway copies, without scheduling new room raycasts.
Must ($game.Contains('m_ambientLoopSource.clip = m_queuedAmbientLoop;')) 'Valheim ambient-loop transition mechanism changed'
Must ($room.Contains('if (s_haveCast && !s_roomDirty &&')) 'room survey should still be stationary-cache gated'
Must ($amb.Contains('!ReferenceEquals(outdoor.clip, s_routedAmbientClip)') -and
       $amb.Contains('Update(man, Room.Enclosure);') -and
       $amb.Contains('s_routedAmbientClip = amb != null && amb.isPlaying')) 'stationary weather clip transition does not refresh opening copies'
Must ($amb.Contains('outdoor.volume > .0001f') -and
       $amb.Contains('Room.WindSurveyReady')) 'rebind should wait for active playback and a valid cached room'
Must ($amb.Contains('src.clip = game.clip;') -and
       $amb.Contains('src.timeSamples = Mathf.Min(game.timeSamples,')) 'copy loses actual changed Valheim recording and phase'
# Spatial room opening geometry: choose the nearest audible source rather
# than the largest aperture regardless of distance.
Must ($portals.Contains('Found.Sort((a, b) => b.Share.CompareTo(a.Share));')) 'existing room portal order unexpectedly changed'
Must ($amb.Contains('for (int k = 0; k < Portals.List.Count; k++)') -and
       $amb.Contains('if (strength > gain0)') -and
       $amb.Contains('best1 = best0; gain1 = gain0;') -and
       $amb.Contains('int picked = op == 0 ? best0 : best1;')) 'opening routing is still blind to listener distance'
Must ($weather.Contains('internal static float OpeningGain(float share, float radius, float distance)')) 'distance-and-size-derived aperture gain missing'
Must ($amb.Contains('Play(slot, 0, amb, rainNow ? 0f :') -and
       $amb.Contains('float impactMix = RainField.Glide(outdoor, rain,')) 'rain must be routed through the ground field, not one/two doorway copies'
Must ($amb.Contains('Play(slot, 1, wind, apertureGain * s_inside, width, listener);')) 'generic outside wind transition lost'
# Directional copies and original outdoor bed use the same recording;
# avoid summing both at full level when the open door is directly audible.
Must ($amb.Contains('openingEnergy += ambientCopy.Target * ambientCopy.Target;') -and
       $amb.Contains('s_openingMixTarget = Mathf.Sqrt(Mathf.Clamp01(openingEnergy));') -and
       $amb.Contains('float directionalMix = rain ? impactMix : s_openingMix;') -and
       $amb.Contains('float diffuseBed = s_rainGain * (1f - Mathf.Clamp01(directionalMix));') -and
       $amb.Contains('Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);')) 'directional ground-field rain not traded against duplicated diffuse 2D rain bed'
Must ($amb.Contains('(w == 0 && s_rainBlend > .01f ? 4f : 2f) * dt')) 'rain opening samples still use slow 500ms fade'
Must ($amb.Contains('s_routedAmbientClip = null;') -and
       $amb.Contains('s_openingMixTarget = s_openingMix = 0f;')) 'cross-world/disabled-state reset missing'
Must (!$amb.Contains('new RaycastCommand(') -and !$weather.Contains('new RaycastCommand(') -and
       !$amb.Contains('StartCoroutine(') -and !$amb.Contains('void LateUpdate(')) 'new per-frame worker or raycast introduced'
Write-Output 'RAIN/AMBIENCE ROUTING PASS: stationary audible clip rebind; generic portals preserved; rain ground-field handoff; no 2D/3D double-gain'
