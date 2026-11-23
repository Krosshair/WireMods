$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$room=[IO.File]::ReadAllText("$r\src\Room.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('NO-EXTRA-RAYS SURVEY: '+$why)}}
Must ($room.Contains('RayJobs.EnsureFixed(ref s_cmds, ref s_hits, Rays * 3);') -and
      $room.Contains('s_job = RayJobs.Submit(s_cmds, s_hits, 16, 1);')) 'original 192-ray room job not restored'
Must ($room.Contains('EnvironmentalField.Measure(Dirs, Picked, Hit);') -and
      $room.Contains('internal static int AcousticBoundary(int i) =>') -and
      $room.Contains('SealedBy[i] >= 0 ? SealedBy[i] : i;')) 'incident field not using cached real openings and seam boundary'
Must ($field.Contains('bool open = Room.Out(i);') -and
      $field.Contains('Solids.Of(picked[boundary])') -and
      $field.Contains('Occlusion.Wall(block, out gain, out cutoff);')) 'actual material from room probe not reused'
foreach($word in @('new RaycastCommand(','RayJobs.Submit(','NativeArray<','Heightmap.GetHeight(',
'Physics.Raycast(','AddComponent<AudioSource>','new AudioSource','void Update(','void LateUpdate(','StartCoroutine(')){
 Must (!$field.Contains($word)) "forbidden additional field cost: $word"
}
Must ($field.Contains('EXTRA RAYS 0')) 'field ray savings not reflected in diagnostics'
Write-Output 'NO-EXTRA-RAYS ENVIRONMENT FIELD PASS: exactly existing room physics, no independent jobs or audio sources'
