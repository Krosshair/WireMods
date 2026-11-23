$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$source=Join-Path $r 'src\Room.cs'
$weather=Join-Path $r 'src\WeatherAcoustics.cs'
$amb=Join-Path $r 'src\Ambience.cs'
$room=[IO.File]::ReadAllText($source)
$rain=[IO.File]::ReadAllText($weather)
$mixer=[IO.File]::ReadAllText($amb)
function Must([bool]$pass,[string]$why){if(!$pass){throw ('ATTIC BASEMENT ROOF REGRESSION: '+$why)}}
Must ($room.Contains('internal bool Built, Structural, Tree, Ground, Wall, Static, AcousticProxy, Roof;')) 'actual exposed-roof attribution not cached'
Must ($room.Contains('s.Roof = s.Built && p != null &&') -and
       $room.Contains('WeatherAcoustics.IsRoofPieceName(p.gameObject.name);')) 'building roof name is not checked at collider-cache creation'
Must ($room.Contains('if (hitSurface != null && hitSurface.Roof)') -and
       $room.Contains('WeatherAcoustics.RoofImpactWeight(') -and
       $room.Contains('Dist[i] * Dirs[i].y);')) 'roof trickle still triggered by any overhead floor instead of first visible roof / vertical distance'
Must ($rain.Contains('name.IndexOf("wall_roof", System.StringComparison.OrdinalIgnoreCase) < 0')) 'gable wall falsely classified as an exposed roof'
Must ($room.Contains('BuiltRoof = roofRays > 0 ? builtRoofRays / roofRays : 0f;')) 'measured roof presence not committed'
Must ($mixer.Contains('Room.BuiltRoof,rainIndoor,') -and
       $mixer.Contains('RainField.Access(Hearing.At)')) 'one-source rain mixer not using real roof / window data'
Must (!$room.Contains('new RaycastCommand(from + Vector3.up') -and
       !$rain.Contains('Physics.Raycast(') -and
       !$rain.Contains('void Update(')) 'new roof overhead raycasts or frame callbacks'
$base='C:\Users\kajet\WireMods\ValheimSOURCE\ValheimProject\ExportedProject\Assets\Editor\GameElements\Pieces'
foreach($roof in @('wood_roof.prefab','darkwood_roof_45.prefab','piece_grausten_roof_45.prefab')){
 Must (Test-Path (Join-Path $base $roof)) "reference roof prefab missing: $roof"
}
foreach($floor in @('wood_floor.prefab','stone_floor_2x2.prefab')){
 Must (Test-Path (Join-Path $base $floor)) "reference intermediate floor prefab missing: $floor"
}
Write-Output 'ROOF VS FLOOR SOURCE INTEGRATION PASS: real Valheim roof/floor prefabs, cached authored surface, first overhead roof and distance weighting, one-source mixer, zero extra raycasts'
