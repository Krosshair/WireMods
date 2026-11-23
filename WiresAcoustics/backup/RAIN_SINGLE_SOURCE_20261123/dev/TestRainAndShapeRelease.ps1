$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$amb=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$weather=[IO.File]::ReadAllText((Join-Path $r 'src\WeatherAcoustics.cs'))
$volume=[IO.File]::ReadAllText((Join-Path $r 'src\SourceVolume.cs'))
$tests=[IO.File]::ReadAllText((Join-Path $r 'dev\VolumeSamplingTests\Program.cs'))
function Must([bool]$good,[string]$reason){if(!$good){throw ('RAIN/SHAPE SOURCE REGRESSION: '+$reason)}}
Must ($room.Contains('Enclosure = enclosure;') -and
      $room.Contains('BuiltRoof = roofRays > 0 ? builtRoofRays / roofRays : 0f;') -and
      $room.Contains('WallBed = bedWalls > 0 ? Mathf.Sqrt(bedSum / bedWalls) : 0f;')) 'one-pass room data still leaves permanently stale partial weather occlusion'
Must (!$room.Contains('BuiltRoof = Mathf.Lerp(BuiltRoof') -and
      !$room.Contains('Enclosure = Mathf.Lerp(Enclosure')) 'one-pass under-roof smoothing unexpectedly reintroduced'
Must ($amb.Contains('s_rainInside = WeatherAcoustics.Approach(s_rainInside, s_insideTarget, dt);') -and
      $amb.Contains('s_rainBlend = WeatherAcoustics.Approach(s_rainBlend,') -and
      $amb.Contains('rainSelected ? 1f : 0f, dt);') -and
      $amb.Contains('WeatherAcoustics.Blend(rainBaseGain,rainBaseCut,')) 'rain playback does not follow fast spatial response'
Must ($weather.Contains('internal const float RainResponseSeconds = .25f;') -and
      $weather.Contains('Mathf.MoveTowards(current,target,Mathf.Max(0f,deltaTime) / RainResponseSeconds)')) 'rain fade does not have continuous bounded 250ms response'
Must ($amb.Contains('Mathf.MoveTowards(s_inside, s_insideTarget, dt);')) 'normal diffuse ambience fade was incorrectly accelerated'
Must ($room.Contains('s_current = s_haveCurrent ? Params.Lerp(s_current, target, tail) : target;')) 'room reverb tail was modified'
Must ($volume.Contains('p.Box = knownBox;') -and
      $volume.Contains('return p.Center + p.AxisX *') -and
      $volume.Contains('Plugin.SourceSphereRadius.Value')) 'known shape ray samples and fallback sphere not both present'
Must ($tests.Contains('root.InverseTransformPoint(point)') -and
      $tests.Contains('sample outside actual oriented owner bounds') -and
      $tests.Contains('Must(oneShot.Box,') -and
      $tests.Contains('!localized.Box')) 'shape test coverage for rotation and one-shots missing'
Write-Output 'ROOM RAIN + ORIENTED SOURCE VOLUME SOURCE CHECKS PASS: immediate geometry state, 250ms rain, retained general fade and reverb, real volume ray samples'
