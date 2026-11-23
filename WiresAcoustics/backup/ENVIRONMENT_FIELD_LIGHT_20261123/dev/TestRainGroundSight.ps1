$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
$batch=[IO.File]::ReadAllText("$r\src\Batch.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('ENVIRONMENTAL SURFACE FIELD: '+$why)}}
Must ($field.Contains('internal enum EmitterDomain : byte { Surface, Point, Volume }') -and
       $field.Contains('internal enum EmitterSurface : byte { Terrain, Roof, Water, Vegetation }') -and
       $field.Contains('internal struct Patch')) 'source geometry/domain abstraction missing'
Must ($field.Contains('Heightmap.GetHeight(at, out float h)') -and
       $field.Contains('Surface = material,') -and
       $field.Contains('private static void Collect(Vector3 listener)')) 'physical terrain patch sampling unavailable'
Must ($field.Contains('OfferRoof(Solids.Info info, RaycastHit hit)') -and
       $room.Contains('EnvironmentalField.OfferRoof(hitSurface, Picked[i]);')) 'real roof emitter samples not gathered from existing collider survey'
Must ($field.Contains('new RaycastCommand(source + Vector3.up * .15f,') -and
       $field.Contains('if (!Exposed(first)) continue;')) 'source-side exposure must be tested at surface, never at listener'
Must ($field.Contains('RayJobs.Cast3(s_cmds, listener, source, q, ref c);') -and
       $field.Contains('RayJobs.Cast3(s_cmds, listener, Ways[k].Point, q, ref c);') -and
       $field.Contains('RayJobs.Cast3(s_cmds, Ways[k].Point, source, q, ref c);')) 'physical source must test direct and portal detour legs'
Must ($field.Contains('Batch.DirectWalls(s_cmds, s_hits, start,') -and
       $field.Contains('Batch.DirectWalls(s_cmds, s_hits, start + 3,') -and
       $batch.Contains('Diffraction.Opening(ear, portal.Point, portal.Dir, portal.Radius,')) 'shared WA diffraction and material transfer missing'
Must ($field.Contains('s_job = RayJobs.Submit(') -and
       $field.Contains('if (!s_job.IsCompleted) return;') -and
       $field.Contains('(listener - s_from).sqrMagnitude < Moved * Moved')) 'event/movement gated async job missing'
Must ($room.Contains('EnvironmentalField.Step(from, Mask, environmentSelected);') -and
       $room.Contains('RayJobs.EnsureFixed(ref s_cmds, ref s_hits, Rays * 3);')) 'room job still carries old 104 rain-only rays'
Must (!$field.Contains('Physics.Raycast(') -and
       !$field.Contains('StartCoroutine(') -and
       !$field.Contains('void Update(') -and
       !$field.Contains('void LateUpdate(') -and
       !$field.Contains('new NativeArray<')) 'synchronous physics, independent frame hooks or per-frame native allocations introduced'
Write-Output 'GENERAL SURFACE FIELD INTEGRATION PASS: source-side exposure, terrain/roof, existing WA material/portal diffraction, bounded async surveys'
