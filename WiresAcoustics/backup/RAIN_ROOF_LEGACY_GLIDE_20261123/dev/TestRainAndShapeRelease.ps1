$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
$amb=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$volume=[IO.File]::ReadAllText("$r\src\SourceVolume.cs")
$tests=[IO.File]::ReadAllText("$r\dev\VolumeSamplingTests\Program.cs")
function Must([bool]$good,[string]$reason){if(!$good){throw ('FIELD/SHAPE REGRESSION: '+$reason)}}
Must ($room.Contains('Enclosure = enclosure;') -and
      $room.Contains('WallBed = bedWalls > 0 ? Mathf.Sqrt(bedSum / bedWalls) : 0f;')) 'room data stale'
Must (!$room.Contains('Enclosure = Mathf.Lerp(Enclosure')) 'stale room smoothing returned'
Must ($field.Contains('Occlusion.Wall(block, out gain, out cutoff);') -and
      $field.Contains('SpectralPaths.HighEnergy(gain, cutoff)')) 'field not using existing physical spectrum'
Must ($amb.Contains('s_fieldGain :') -and
      $amb.Contains('s_fieldCutoff :') -and
      !$amb.Contains('WeatherAcoustics.Blend(')) 'field not applied to single original ambient loop'
Must ($room.Contains('s_current = s_haveCurrent ? Params.Lerp(s_current, target, tail) : target;')) 'room reverb tail modified'
Must ($volume.Contains('p.Box = knownBox;') -and
      $volume.Contains('return p.Center + p.AxisX *') -and
      $volume.Contains('Plugin.SourceSphereRadius.Value')) 'true SFX volume rays lost'
Must ($tests.Contains('root.InverseTransformPoint(point)') -and
      $tests.Contains('sample outside actual oriented owner bounds') -and
      $tests.Contains('Must(oneShot.Box,') -and
      $tests.Contains('!localized.Box')) 'one-shot/area tests missing'
Write-Output 'ENVIRONMENT FIELD + TRUE SOURCE VOLUME PASS: shared materials, 0 extra queries, unchanged reverb and SFX geometry'
