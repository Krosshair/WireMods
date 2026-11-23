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
Must (!$field.Contains('RoofRainPresence') -and !$field.Contains('RainRoofGain')) 'general exterior field must not contain roof-specific processing'
Must ($weather.Contains('internal const float RainRoofGain = .72f;') -and
      $weather.Contains('internal const float RainRoofCutoff = 6500f;') -and
      $weather.Contains('internal static float RoofRainPresence(') -and
      $weather.Contains('internal static void BlendRoof(')) 'legacy original-audio roof treatment not restored'
$amb=[IO.File]::ReadAllText("$r\src\Ambience.cs")
Must ($amb.Contains('WeatherAcoustics.BlendRoof(') -and
      $amb.Contains('s_roofPresence = Mathf.Lerp(') -and
      $amb.Contains('RoofAttackTau = .42f, RoofReleaseTau = 1.25f;')) 'roof treatment does not glide asymmetrically through missed geometry surveys'
Write-Output 'ROOF/FLOOR GENERAL BOUNDARY PASS: real roofs only, legacy one-loop roof coloration smoothly restored'
