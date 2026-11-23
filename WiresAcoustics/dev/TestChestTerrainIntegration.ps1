$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$f=[IO.File]::ReadAllText("$r\src\Follow.cs")
$s=[IO.File]::ReadAllText("$r\src\Stackify.cs")
$v=[IO.File]::ReadAllText("$r\src\SourceVolume.cs")
$t=[IO.File]::ReadAllText("$r\src\TerrainAudio.cs")
function Must([bool]$ok,[string]$msg){if(!$ok){throw ('CHEST/TERRAIN SOURCE FAIL: '+$msg)}}
Must ($f.Contains('s_playingChest = chest;') -and $f.Contains('ReferenceEquals(__instance, chest.m_openEffects)') -and $f.Contains('ReferenceEquals(__instance, chest.m_closeEffects)')) 'chest context missing'
Must ($f.Contains('private static class ChestPlayback') -and $f.Contains('f.SurfaceEmitter = true;') -and $f.Contains('if (Area.TopOf(chest.transform, out Vector3 lid)) basePos = lid;')) 'initial chest playback is not shell-aware'
Must ($s.Contains('s_chest = s_active ? container : null;') -and $s.Contains('private static class ChestPlay') -and $s.Contains('f.SurfaceEmitter = true;')) 'Stackify opening is not shell-aware'
Must ($v.Contains('p.Surface = knownBox && f.SurfaceEmitter;') -and $v.Contains('float face = Halton(i, 7);') -and $v.Contains('p.AxisY * (p.SignY * inset)')) 'no real listener-facing sample surfaces'
Must ($t.Contains('!ReferenceEquals(list, op.m_onPlacedEffect)') -and $t.Contains('Heightmap.GetHeight(atOperation, out float ground)') -and $t.Contains('at.y = Mathf.Max(at.y, RaisedSurfaceHeight(')) 'raised ground is not properly attributed'
Must ($f.Contains('TerrainAudio.Place(__instance, ref basePos);')) 'terrain origin not relocated before playback'
foreach($name in @('Follow.cs','Stackify.cs','SourceVolume.cs','TerrainAudio.cs')){
 $file=[IO.File]::ReadAllText("$r\src\$name")
 Must (!$file.Contains('StartCoroutine(') -and !$file.Contains('void LateUpdate(') -and !$file.Contains('void Update(')) ('per-frame update introduced in '+$name)
}
Write-Output 'CHEST AND TERRAIN SOURCE CONTRACT PASS: first play, geometric shell, raised earth, no frame polling'
