$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$f=[IO.File]::ReadAllText((Join-Path $r 'src\RainField.cs'))
$room=[IO.File]::ReadAllText((Join-Path $r 'src\Room.cs'))
$amb=[IO.File]::ReadAllText((Join-Path $r 'src\Ambience.cs'))
$heightmap=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\Heightmap.cs')
$zone=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\ZoneSystem.cs')
function Must([bool]$yes,[string]$reason){if(!$yes){throw ('GROUND VIEW RAIN REGRESSION: '+$reason)}}
# All azimuths, both ranges, plus direct rays toward inferred windows.
Must ($f.Contains('internal const int GroundBearings = 24;') -and
       $f.Contains('internal const int GroundRings = 2;') -and
       $f.Contains('private const int PortalGroundProbes = 4;') -and
       $f.Contains('GroundBearings * GroundRings + PortalGroundProbes;') -and
       $f.Contains('GroundSamples * 2;')) 'bounded 360-degree sampling / portal refinement missing'
Must ($f.Contains('bearing + (near ? .25f : .75f)') -and
       $f.Contains('aperture.Point - ear;')) 'oblique windows not covered independent of viewing direction'
# Actual outside-ground location from Valheim's cached terrain mesh, not
# the expensive ZoneSystem synchronous physics helper.
Must ($heightmap.Contains('public static bool GetHeight(Vector3 worldPos, out float height)') -and
       $heightmap.Contains('heightmap.GetWorldHeight(worldPos, out height)')) 'cached terrain height method changed'
Must ($zone.Contains('public bool GetGroundHeight(Vector3 p, out float height)') -and
       $zone.Contains('Physics.Raycast(p, Vector3.down')) 'avoid accidentally using synchronous ZoneSystem height casts'
Must ($f.Contains('Heightmap.GetHeight(point, out float ground)') -and
       !$f.Contains('ZoneSystem.instance.GetGroundHeight') -and
       !$f.Contains('Physics.Raycast(')) 'terrain placement is doing independent synchronous physics'
# LOS is from actual ears to OUTSIDE AIR above sampled terrain. The rain
# distance is to the real ground, not the window or proxy air location.
Must ($f.Contains('Vector3 air = new Vector3(point.x,') -and
       $f.Contains('Mathf.Max(ear.y, ground + 1.2f)') -and
       $f.Contains('GroundPoints[i] = point;') -and
       $f.Contains('ear, to / length, query,') -and
       $f.Contains('air + Vector3.up * .25f') -and
       $f.Contains('GroundAccess((GroundPoints[i] - listener).magnitude)')) 'ground-proximity gain and sight-through-window model incorrect'
Must ($f.Contains('if (direct.colliderInstanceID != 0)') -and
       $f.Contains('if (s == null || (!s.Tree && (s.Built || s.Structural)))')) 'direct wall path or building roof above sampled ground not rejected, or tree canopy blocks all rain'
Must ($f.Contains('s_windowAccess = Mathf.Max(') -and
       $f.Contains('CombineAccess(s_openFraction, nearestOpening),') -and
       $f.Contains('groundAccess);')) 'verified ground view fails to override sparse missing window rays'
# No additional frame/coroutine or game audio source. These are batched
# within the existing Room.Run asynchronous job only in wet weather.
Must ($room.Contains('RayJobs.EnsureFixed(ref s_cmds, ref s_hits,') -and
       $room.Contains('Rays * 3 + RainField.GroundCommands') -and
       $room.Contains('RainField.ScheduleGroundSight(from, s_cmds,') -and
       $room.Contains('(rainSelected ? RainField.GroundCommands : 0)') -and
       $room.Contains('s_cmds.GetSubArray(0, count)') -and
       $room.Contains('RainField.ReadGroundSight(s_from, s_hits, Rays * 3);')) 'probes are not part of the existing bounded async room survey'
Must ($room.Contains('if (rainSelected != s_groundRainSelected)') -and
       $room.Contains('s_roomDirty = true;')) 'rain onset while listener stands stationary does not trigger needed sight survey'
Must (!$f.Contains('AddComponent<AudioSource>') -and
       !$f.Contains('new GameObject(') -and
       !$f.Contains('void Update(') -and
       !$f.Contains('void LateUpdate(') -and
       !$f.Contains('StartCoroutine(') -and
       !$f.Contains('new NativeArray<')) 'additional rain sound sources, per-frame checks or native allocations'
Must ($amb.Contains('RainField.Access(Hearing.At)') -and
       $amb.Contains('Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);')) 'single existing original rain recording not filtered by new outdoor-ground access'
Write-Output 'GROUND-SIGHT RAIN INTEGRATION PASS: 24 bearings x 2 offset rings + 4 window rays; 104 wet-only room-job commands; ear-to-outdoor-air LOS; sky shelter; distance to actual terrain, 1 vanilla rain source'
