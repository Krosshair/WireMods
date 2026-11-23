$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$a=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$f=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
$portals=[IO.File]::ReadAllText("$r\src\Portals.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('WINDOW/DOOR FIELD CONTRACT: '+$why)}}
Must ($f.Contains('bool open = Room.Out(i);') -and
      $f.Contains('Room.AcousticBoundary(i)')) 'general ambient opening geometry not used'
Must ($room.Contains('if (Opening[i] < 0f) continue;') -and
      $room.Contains('SealedBy[i] = by;') -and
      $room.Contains('SealedBy[i] >= 0 ? SealedBy[i] : i;')) 'thin false seams not treated like an actual wall'
Must ($portals.Contains('Room.Out(i) && Room.PortalEvidence(i)') -and
      $room.Contains('Portals.Find(s_from, Dirs, enclosure, path);')) 'existing WA portal topology has changed'
Must ($a.Contains('Play(slot, 0, amb, rainNow ? 0f :') -and
      $a.Contains('Play(slot, 1, wind, apertureGain * s_inside, width, listener);')) 'original one-rain recording or wind/biome routes regressed'
Must ($f.Contains('out float outputCutoff') -and
      $f.Contains('if (open) directEnergy += energy;')) 'wide openings do not contribute actual diffuse energy'
Must ($room.Contains('RaycastHit side = s_hits[Rays + 2 * i];') -and
      $room.Contains('RaycastHit up = s_hits[Rays + 2 * i + 1];') -and
      $room.Contains('side.colliderInstanceID == 0 ||') -and
      $room.Contains('side.distance > wallDepth ||') -and
      $room.Contains('up.colliderInstanceID == 0 ||') -and
      $room.Contains('up.distance > wallDepth')) 'real narrow opening must survive seam suppression via preexisting side rays'
Write-Output 'WINDOW/DOOR FIELD PASS: measured open directions and actual wall-proxy materials, no new weather copies'
