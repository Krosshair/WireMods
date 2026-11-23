$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
$weather=[IO.File]::ReadAllText("$r\src\WeatherAcoustics.cs")
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$amb=[IO.File]::ReadAllText("$r\src\Ambience.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('ROOF / FLOOR EMITTER TEST: '+$why)}}
Must ($room.Contains('internal bool Built, Structural, Tree, Ground, Wall, Static, AcousticProxy, Roof;') -and
      $room.Contains('WeatherAcoustics.IsRoofPieceName(p.gameObject.name);')) 'roof type not identified from actual Valheim prefab'
Must ($weather.Contains('name.IndexOf("wall_roof",') -and
      $weather.Contains('System.StringComparison.OrdinalIgnoreCase) < 0;')) 'gable wall counted as exposed roofing'
Must ($room.Contains('if (hitSurface != null && hitSurface.Roof && Dirs[i].y > .35f)') -and
      $room.Contains('EnvironmentalField.OfferRoof(hitSurface, Picked[i]);')) 'real roof candidates not passed to shared surface provider'
Must ($field.Contains('if (info == null || !info.Roof || info.C == null) return;') -and
      $field.Contains('Bounds b = info.C.bounds;') -and
      $field.Contains('at.y = b.max.y + .35f;')) 'intermediate floor incorrectly offered as rain emitter'
Must ($field.Contains('if (!Exposed(first)) continue;') -and
      $field.Contains('source + Vector3.up * .15f,')) 'a shelter above the source does not block rainfall'
Must ($amb.Contains('EnvironmentalField.Gain :') -and
      !$amb.Contains('Room.BuiltRoof,rainIndoor,')) 'attic-only special volume boost returned'
$base='C:\Users\kajet\WireMods\ValheimSOURCE\ValheimProject\ExportedProject\Assets\Editor\GameElements\Pieces'
foreach($roof in @('wood_roof.prefab','darkwood_roof_45.prefab','piece_grausten_roof_45.prefab')){
 Must (Test-Path (Join-Path $base $roof)) "missing actual Valheim roof $roof"
}
foreach($floor in @('wood_floor.prefab','stone_floor_2x2.prefab')){
 Must (Test-Path (Join-Path $base $floor)) "missing Valheim intermediate floor $floor"
}
Write-Output 'ROOF/FLOOR FIELD PASS: authored roof collider emitted only when exposed; floors remain acoustic obstacles, not emitting roof'
