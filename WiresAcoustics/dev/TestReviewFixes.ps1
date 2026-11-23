$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics\src'
function Code([string]$file) { [IO.File]::ReadAllText((Join-Path $r $file)) }
function Must([bool]$condition, [string]$why) {
    if(!$condition) { throw "REVIEW CORRECTION FAILED: $why" }
}
$room=Code 'Room.cs'
$field=Code 'EnvironmentalField.cs'
$weather=Code 'WeatherAcoustics.cs'
$starts=Code 'Starts.cs'
$ocean=Code 'Ocean.cs'
$worker=Code 'AnalyticWorker.cs'
Must ($field.Contains('reference += AngularWeight(directions[i].y);')) 'environment weighting mismatch'
Must (!$field.Contains('if (directions[i].y >= 0f)')) 'old hemisphere normalization restored'
Must ($room.Contains('WeatherAcoustics.VerifiedNarrowAperture(') -and
      $weather.Contains('sideDistance > wallDepth + .10f &&') -and
      $weather.Contains('upDistance > wallDepth + .10f;')) 'seam promoted to window from a single missed offset ray'
Must ($room.Contains('(Solids.Of(a)?.Built ?? false)') -and
      $room.Contains('(Solids.Of(b)?.Built ?? false)') -and
      $room.Contains('Mathf.Abs(a.distance - b.distance) < .25f')) 'distant center hit bypasses near-wall seam consensus'
Must ($room.Contains('192/30 = 6.4 room rays') -and
      $room.Contains('if (Plugin.Occlusion.Value || Plugin.RoomReverb.Value)')) '30s safety refresh for unannounced external geometry'
Must ($starts.Contains('if (!s_job.IsCompleted) return;')) 'new-sound physics blocks game thread'
Must ($ocean.Contains('if (s_pending && s_job.IsCompleted)') -and
      $ocean.Contains('if (!s_pending && now >= s_next)')) 'shore physics blocks or overwrites outstanding job'
Must ($starts.Contains('!src.isActiveAndEnabled') -and
      $starts.Contains('r.Src.isActiveAndEnabled')) 'disabled AudioSource gets replayed'
Must ($worker.Contains('Occlusion.ChangedNear(m.Generation, m.Listener,') -and
      $worker.Contains('f.AnalyticWorldGen = Occlusion.WorldGen;') -and
      !$worker.Contains('Occlusion.WorldGen != m.Generation')) 'unrelated world edits invalidate continuous analytic solution'
Write-Output 'REVIEW CORRECTIONS SOURCE CONTRACT PASS: normalized field, windows/seams, 30s fallback, nonblocking jobs, disabled sound, analytic generation'
