param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
function Assert([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL: $name" }
    Write-Output "PASS: $name"
}
$amb = [IO.File]::ReadAllText((Join-Path $src 'Ambience.cs'))
$ocean = [IO.File]::ReadAllText((Join-Path $src 'Ocean.cs'))
$room = [IO.File]::ReadAllText((Join-Path $src 'Room.cs'))
$batch = [IO.File]::ReadAllText((Join-Path $src 'Batch.cs'))
$corners = [IO.File]::ReadAllText((Join-Path $src 'Corners.cs'))
$occ = [IO.File]::ReadAllText((Join-Path $src 'Occlusion.cs'))
$model = [IO.File]::ReadAllText((Join-Path $src 'AcousticModel.cs'))
$filter = [IO.File]::ReadAllText((Join-Path $src 'Filter.cs'))
$plugin = [IO.File]::ReadAllText((Join-Path $src 'Plugin.cs'))
$portals = [IO.File]::ReadAllText((Join-Path $src 'Portals.cs'))
$gen = [IO.File]::ReadAllText((Join-Path $src 'GeometryEvents.cs'))
Assert (!(Test-Path (Join-Path $src 'OpeningRegistry.cs'))) 'removed unstable acoustic source registry'
Assert (!(Test-Path (Join-Path $src 'ApertureGeometry.cs'))) 'removed aperture reconstruction and collider-scan overhead'
Assert (!$amb.Contains('OpeningRegistry') -and !$amb.Contains('new GameObject(') -and !$amb.Contains('src.Play()')) 'environmental rain does not spawn or play duplicate loop sources'
Assert ($amb.Contains('AmbientLoop(man)') -and $amb.Contains('Glide(man, dt)')) 'ambient changes use original Valheim playback and continuous filtering'
Assert ($amb.Contains('AmbientWallFloor = .64f') -and $amb.Contains('AmbientWallCutoff = 7200f')) 'indoor rain bed remains present and tonally detailed'
Assert ($room.Contains('float shortRoom = Mathf.SmoothStep') -and $room.Contains('Mathf.Min(decay, 1.2f)')) 'small rooms have bounded reverb tails'
Assert ($room.Contains('reflectionPresence') -and $room.Contains('reverbLevel * .45f')) 'early reflections are separately balanced'
Assert ($room.Contains('reverbLevel * .75f') -and $room.Contains('float response = Mathf.Abs(change)')) 'late reverb and transition settings remain controlled'
Assert (!$amb.Contains('s_windowMix') -and !$amb.Contains('HasBuildingRoof(')) 'weather is independent of discrete roof/window detection'
Assert (!$ocean.Contains('new GameObject(') -and !$ocean.Contains('RaycastCommand') -and !$ocean.Contains('ScheduleBatch(')) 'ocean uses vanilla emitter without shoreline scans or duplicate voices'
Assert ($ocean.Contains('Room.Exposure(') -and $ocean.Contains('Room.Enclosure')) 'ocean shelter uses existing room measurement'
Assert ($ocean.Contains('internal static void Shutdown()')) 'ocean resets filters on world transition or disable'
Assert ($room.Contains('Portals.NeighboursOf(Dirs)') -and !$room.Contains('Portals.Find(')) 'room sensor retains seam filtering without constructing audio portals'
Assert (!$gen.Contains('OpeningRegistry')) 'building changes no longer invoke obsolete registry'
Assert (!$batch.Contains('OpeningRegistry') -and !$batch.Contains('r.Routes') -and !$batch.Contains('Portals.')) 'batch no longer schedules noisy aperture routes'
Assert ($batch.Contains('Cast3(listener, r.To') -and $batch.Contains('Diffraction.Round(')) 'material-aware direct rays and verified edge diffraction retained'
Assert ($corners.Contains('Spacing = .2f, Reach = 3.2f')) 'narrow edge probe spacing retained with bounded reach'
Assert ($batch.Contains('ProbeBudget = 256')) 'secondary probe budget bounded per batch'
Assert ($occ.Contains('Occlusion.Block') -or $occ.Contains('internal struct Block')) 'material-dependent blocking implementation remains'
Assert ($model.Contains('internal static void Combine(') -and $batch.Contains('AcousticModel.Combine(')) 'path gain, cutoff and direction combined in shared acoustic model'
Assert ($filter.Contains('Spatialize(') -and $plugin.Contains('Hrtf = Setting(')) 'headphone HRTF implementation retained'
Assert ($room.Contains('s_current.ApplyTo(s_zone)')) 'room-material acoustic reverb retained'
Assert (!$portals.Contains('internal static void Find(') -and $portals.Contains('NeighboursOf(')) 'portals module reduced to static room-ray topology'
Assert ($plugin.Contains('"Profiler logging", false') -and $room.Contains('[acoustics profiler]')) 'profiling remains optional and available'
Assert ($room.Contains('Ambience.ProfilerSummary()') -and $room.Contains('Cost.Report()')) 'profiler reports ambience state and measured cost'
Assert ((Get-Content (Join-Path $root 'package\manifest.json') -Raw | ConvertFrom-Json).version_number -eq '0.6.0') 'release version unchanged'
$hooks = Get-ChildItem $src -Filter '*.cs' | Select-String -Pattern '^\s*(public|private|protected|internal)\s+void\s+(Update|LateUpdate|FixedUpdate)\s*\(\s*\)'
Assert ($null -eq $hooks) 'no independent per-frame Unity components'
if (-not $NoBuild) {
    dotnet build (Join-Path $src 'WiresAcoustics.csproj') -c Release --no-restore -v:q
    if ($LASTEXITCODE -ne 0) { throw 'FAIL: Release build' }
}
Write-Output 'PASS: Lean Acoustics regression assertions'
