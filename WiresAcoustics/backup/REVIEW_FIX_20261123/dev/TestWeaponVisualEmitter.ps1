$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$area=[IO.File]::ReadAllText((Join-Path $r 'src\Area.cs'))
$spots=[IO.File]::ReadAllText((Join-Path $r 'src\Spots.cs'))
$follow=[IO.File]::ReadAllText((Join-Path $r 'src\Follow.cs'))
$filter=[IO.File]::ReadAllText((Join-Path $r 'src\Filter.cs'))
$volume=[IO.File]::ReadAllText((Join-Path $r 'src\SourceVolume.cs'))
$vis=[IO.File]::ReadAllText('C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\VisEquipment.cs')
function Must([bool]$ok,[string]$reason){if(!$ok){throw 'WEAPON VOLUME REGRESSION: '+$reason}}
foreach($field in @('m_rightItemInstance','m_leftItemInstance','m_leftHand','m_rightHand')){Must ($vis.Contains($field)) "Valheim weapon prefab field missing: $field"}
Must ($spots.Contains('WeaponVisual(h, list)')) 'Weapon visual not resolved from current attack'
Must ($spots.Contains('k == Kind.Limb && weaponArea')) 'Only physically swept weapon audio should choose the visual extent'
Must ($spots.Contains('Area.WeaponCenterOf(weaponVisual, out weaponMid)')) 'Weapon midpoint not sourced from visible model'
Must ($spots.Contains('MoveOne(go, root, weaponMid, weaponVisual);')) 'Moving weapon visual not the acoustic source parent'
foreach($required in @('af.WeaponSource = true;','af.LocalizedEvent = false;','af.ShapeRoot = weaponVisual;','af.AreaKnown = false;')){
 Must ($spots.Contains($required)) "Weapon event geometry assignment missing: $required"
}
Must ($spots.Contains('return shown != null ? shown.transform : null;')) 'Unarmed or unresolved model should fall back to original hand'
Must ($spots.Contains('if (audio != null) AcousticFilter.For(audio).LocalizedEvent = true;') -or
       $spots.Contains('marker.LocalizedEvent = true;')) 'Localized hand/impact events must remain point sources'
Must ($filter.Contains('internal bool WeaponSource;')) 'Weapon attribution not persisted'
Must ($follow.Contains('if (!a.WeaponSource) a.ShapeRoot = given;')) 'Follow assigned entire character body over visual weapon'
Must ($follow.Contains('!(go.GetComponent<AcousticFilter>()?.WeaponSource ?? false)')) 'Off-pivot one-shot incorrectly clobbers weapon source'
Must ($area.Contains('if (!f.WeaponSource) Voice(sound, owner);')) 'Generic static prefab placement may recenter moving blade'
Must ($area.Contains('f.WeaponSource ? WeaponCached(owner, out s) : Cached(owner, out s)')) 'Area.Measure not using actual visible weapon bounds'
Must ($area.Contains('WeaponShapes[id] = shape;') -and $area.Contains('WeaponShapes.Clear();')) 'Per-visual mesh caching missing'
Must ($area.Contains('mesh.sharedMesh.bounds') -and
       $area.Contains('skin.localBounds') -and
       $area.Contains('AddCorners(weapon, mesh.transform') -and
       $area.Contains('AddCorners(weapon, skin.transform')) 'Visible model mesh shape and its local transformations missing'
Must ($area.Contains('Mathf.Max(size.x, Mathf.Max(size.y, size.z))')) 'Blade projected dimensions not reflected in width'
Must ($volume.Contains('!f.LocalizedEvent && f.AreaKnown') -and
       !$volume.Contains('f.Source.loop &&')) 'Shape extent incorrectly controlled by loop state'
Must ($spots.Contains('k == Kind.Ground ? null')) 'Ground impacts must retain their contact placement'
# Explicitly located ground impact and vocal positions must still
# use their existing separate placement branches.
Must ($spots.Contains('k == Kind.Voice ? head')) 'Voice location changed'
Must ($spots.Contains('k == Kind.Ground ? null')) 'Ground contact position changed'
foreach($file in @('Area.cs','Spots.cs','Follow.cs','Filter.cs')) {
 $src=[IO.File]::ReadAllText((Join-Path $r ('src\'+$file)))
 Must (!$src.Contains('StartCoroutine(') -and !$src.Contains('void LateUpdate(')) "New per-frame engine hook in $file"
}
Write-Output "WEAPON VISUAL EMITTER SOURCE CHECKS PASS: true equipped model/mesh extent and cache, separate impact/hand attribution, no loop assumption, voice and ground separation"
