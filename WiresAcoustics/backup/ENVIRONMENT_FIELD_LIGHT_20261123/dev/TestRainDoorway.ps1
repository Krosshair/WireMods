$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$amb=[IO.File]::ReadAllText("$r\src\Ambience.cs")
$field=[IO.File]::ReadAllText("$r\src\EnvironmentalField.cs")
$batch=[IO.File]::ReadAllText("$r\src\Batch.cs")
function Must([bool]$ok,[string]$why){if(!$ok){throw ('ENVIRONMENT PORTAL PROPAGATION: '+$why)}}
Must ($amb.Contains('Play(slot, 0, amb, rainNow ? 0f :') -and
       $amb.Contains('Play(slot, 1, wind, apertureGain * s_inside, width, listener);')) 'old ambience/wind portal routing broken or rain duplicated'
Must ($field.Contains('Portals.Portal portal = Portals.List[i];') -and
       $field.Contains('Vector3 at = portal.Point + direction.normalized * 5f;') -and
       $field.Contains('Heightmap.GetHeight(at, out float h)')) 'windows wrongly treated as rain sources rather than routing to real terrain'
Must ($field.Contains('Ways[i] = Portals.List[i];') -and
       $field.Contains('RayJobs.Cast3(s_cmds, listener, Ways[k].Point, q, ref c);') -and
       $field.Contains('RayJobs.Cast3(s_cmds, Ways[k].Point, source, q, ref c);')) 'two actual route legs must be measured'
Must ($batch.Contains('internal static float PortalTransfer(') -and
       $batch.Contains('SpectralPaths.Cascade(exitGain, exitCut, entranceGain, entranceCut,') -and
       $batch.Contains('ThreeRayTransmission.IndirectAmplitude(directCoverage);')) 'legacy SFX and environment use different portal transfer models'
Must ($batch.Contains('openingGain = PortalTransfer(r.Listener, r.To, p,') -and
       $field.Contains('float pg = Batch.PortalTransfer(s_from, patch.At,')) 'shared portal model not called by both audio classes'
Must ($field.Contains('s_world != Occlusion.WorldGen') -and
       $field.Contains('s_door != Occlusion.DoorGen')) 'building/door changes do not invalidate field'
Write-Output 'SHARED SFX/ENVIRONMENT PORTAL PARITY PASS: same serial material/frequency/diffraction transfer, no window-as-emitter'
