$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
$amb=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$volume=[IO.File]::ReadAllText("$r\src\SourceVolume.cs")
$tests=[IO.File]::ReadAllText("$r\dev\VolumeSamplingTests\Program.cs")
function Must([bool]$good,[string]$reason){if(!$good){throw ('FIELD/SHAPE RELEASE REGRESSION: '+$reason)}}
Must ($room.Contains('Enclosure = enclosure;') -and
      $room.Contains('WallBed = bedWalls > 0 ? Mathf.Sqrt(bedSum / bedWalls) : 0f;')) 'cached room geometry still stale'
Must (!$room.Contains('Enclosure = Mathf.Lerp(Enclosure')) 'room one-pass measurement changed'
Must ($amb.Contains('EnvironmentalField.Gain :') -and
      $amb.Contains('EnvironmentalField.Cutoff :') -and
      !$amb.Contains('WeatherAcoustics.Blend(')) 'field transfer is not the sole weather sound filter'
Must ($field.Contains('Batch.DirectWalls(s_cmds, s_hits, first + 1,') -and
      $field.Contains('Batch.PortalTransfer(')) 'field wall and opening paths not shared with regular SFX'
Must ($room.Contains('s_current = s_haveCurrent ? Params.Lerp(s_current, target, tail) : target;')) 'room reverb tail modified'
Must ($volume.Contains('p.Box = knownBox;') -and
      $volume.Contains('return p.Center + p.AxisX *') -and
      $volume.Contains('Plugin.SourceSphereRadius.Value')) 'true source-volume sampling lost'
Must ($tests.Contains('root.InverseTransformPoint(point)') -and
      $tests.Contains('sample outside actual oriented owner bounds') -and
      $tests.Contains('Must(oneShot.Box,') -and
      $tests.Contains('!localized.Box')) 'real emitter shape regression tests missing'
Write-Output 'FIELD + TRUE SOURCE VOLUME PASS: shared geometric propagation, unchanged reverb, oriented/one-shot source extents'
