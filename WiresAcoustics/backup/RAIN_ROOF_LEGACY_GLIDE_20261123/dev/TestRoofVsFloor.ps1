$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
$weather=[IO.File]::ReadAllText("$r\src\WeatherAcoustics.cs")
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('ROOF AND FLOOR BOUNDARY: '+$why)}}
Must ($room.Contains('internal bool Built, Structural, Tree, Ground, Wall, Static, AcousticProxy, Roof;') -and
      $room.Contains('WeatherAcoustics.IsRoofPieceName(p.gameObject.name);')) 'actual roof classification lost'
Must ($weather.Contains('name.IndexOf("wall_roof",') -and
      $weather.Contains('System.StringComparison.OrdinalIgnoreCase) < 0;')) 'gable wall incorrectly roof'
Must ($field.Contains('material.Ground') -and
      $field.Contains('gain = 0f;') -and
      $field.Contains('if (material.Wall)') -and
      $field.Contains('material.First;')) 'interior ground incorrectly assumed wet or authored roof material lost'
Must ($room.Contains('if (hitSurface != null && hitSurface.Roof)') -and
      $room.Contains('WeatherAcoustics.RoofImpactWeight(')) 'regular room geometry roof classification lost'
Must (!$field.Contains('RoofRainPresence') -and !$field.Contains('RainRoofGain')) 'bespoke roof rain boost returned'
Write-Output 'ROOF/FLOOR GENERAL BOUNDARY PASS: one physical material transmission, no artificially boosted attic rain'
