$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$a=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$f=[IO.File]::ReadAllText("$r\src\RainField.cs")
$portals=[IO.File]::ReadAllText("$r\src\Portals.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('ONE LOOP WINDOW ROUTING: '+$why)}}
Must ($a.Contains('int n = s_insideTarget > .001f ?') -and
       $a.Contains('Play(slot, 1, wind, apertureGain * s_inside, width, listener);')) 'nonrain ambience and wind portal system broken'
Must ($a.Contains('Play(slot, 0, amb, rainNow ? 0f :')) 'rain still sent through extra doorway copies'
Must ($a.Contains('s_lastRainClip != null && amb != null &&') -and
       $a.Contains('amb.clip == s_lastRainClip;')) 'rain still duplicated when weather transitions out'
Must ($f.Contains('private static readonly Aperture[] Openings') -and
       $f.Contains('internal static float Access(Vector3 listener)') -and
       $f.Contains('WindowAccess(a.Radius, (a.Point - listener).magnitude)')) 'window gain not continuously derived from known physical openings'
Must ($f.Contains('s_worldGen != world || s_doorGen != door') -and
       $f.Contains('s_count = 0;')) 'door closing fails to invalidate cached windows'
Must ($f.Contains('if (incoming + 4f >= farthest) continue;') -and
       $f.Contains('match = farthestIndex;')) 'new close windows cannot replace distant ones'
Must ($a.Contains('float diffuseBed = rainPlaying ?') -and
       $a.Contains('Mathf.Lerp(s_rainGain, 1f, windowAccess)')) 'window does not brighten one filtered loop'
Must (!$f.Contains('AddComponent<AudioSource>') -and !$f.Contains('src.Play(')) 'rain source copies reintroduced'
Write-Output 'ONE-LOOP WINDOW ROUTING PASS: rain is filtered at cached openings, nonrain wind/ambience portal routing remains'
