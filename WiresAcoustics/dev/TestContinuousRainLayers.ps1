$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$a=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$f=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('SINGLE SOURCE AMBIENCE: '+$why)}}
Must ($a.Contains('AudioSource outdoor = AmbientLoop(man);') -and
      $a.Contains('Bed(outdoor, gain: diffuseBed, cutoff: ambientCut, weatherBed: true);')) 'original Valheim ambient source replaced'
Must ($a.Contains('float ambientCut = rainPlaying ?') -and
      $a.Contains('rainCut :') -and
      $a.Contains('float diffuseBed = rainPlaying ?') -and
      $a.Contains('rainGain :')) 'shared field gain/cutoff absent'
Must ($a.Contains('s_lastRainClip = env.m_ambientLoop;') -and
      $a.Contains('outdoor != null && outdoor.clip == s_lastRainClip;')) 'must track the actual rain clip, not m_isWet alone'
Must ($a.Contains('Play(slot, 0, amb, rainNow ? 0f :')) 'duplicate rain voices through windows'
Must (!$f.Contains('AddComponent<AudioSource>') -and
      !$f.Contains('new GameObject(') -and
      !$f.Contains('new RaycastCommand(') -and
      !$a.Contains('RainField.Access(') -and
      !$a.Contains('WeatherAcoustics.Blend(')) 'old bespoke rain/audio system remains'
Must ($room.Contains('EnvironmentalField.Measure(Dirs, Picked, Hit);') -and
      $room.Contains('EnvironmentalField.Reset();')) 'shared field missing room lifecycle'
Write-Output 'SINGLE SOURCE AMBIENCE PASS: one vanilla weather recording, shared geometry/energy, no extra rain jobs'
