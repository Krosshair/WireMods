$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$volume=[IO.File]::ReadAllText((Join-Path $r 'src\SourceVolume.cs'))
$filter=[IO.File]::ReadAllText((Join-Path $r 'src\Filter.cs'))
$follow=[IO.File]::ReadAllText((Join-Path $r 'src\Follow.cs'))
$spots=[IO.File]::ReadAllText((Join-Path $r 'src\Spots.cs'))
$directions=[IO.File]::ReadAllText((Join-Path $r 'src\Directions.cs'))
$occ=[IO.File]::ReadAllText((Join-Path $r 'src\Occlusion.cs'))
$plugin=[IO.File]::ReadAllText((Join-Path $r 'src\Plugin.cs'))
function Must([bool]$ok,[string]$message){if(!$ok){throw ('EVENT SOURCE EXTENT REGRESSION: '+$message)}}
Must ($volume.Contains('f != null && !f.LocalizedEvent && f.AreaKnown') -and
       !$volume.Contains('f.Source.loop &&')) 'known collider extent cannot be gated on loop'
Must ($filter.Contains('internal bool LocalizedEvent;')) 'event origin identity not retained per source'
Must ($spots.Contains('AcousticFilter.For(audio).LocalizedEvent = true;')) 'action-specific placement does not mark point emitter'
Must ($spots.Contains('t.position = at;')) 'actual event origin lost'
Must ($follow.Contains('go.transform.position = lid;') -and
       $follow.Contains('AcousticFilter.For(audio).LocalizedEvent = true;')) 'chest-lid event no longer point localized'
Must ($follow.Contains('if (audio != null && !audio.loop &&')) 'offset one-shot event not classified'
Must ($directions.Contains('!f.LocalizedEvent && f.AreaRadius > 0f')) 'localized event still inherits owner HRTF width'
Must ($occ.Contains('f != null && !f.LocalizedEvent &&')) 'localized event still sends its occlusion face ray across full owner'
Must ($plugin.Contains('"Unshaped source sphere radius", 0f,') -and
       $plugin.Contains('new AcceptableValueRange<float>(0f, 3f)')) 'arbitrary nonzero default extent restored'
Must ($volume.Contains('if (!p.Box && p.Radius <= .010001f) return 0;')) 'point source still schedules spurious volume rays'
Must ($volume.Contains('f != null && f.LocalizedEvent ? 0f :')) 'localized events inherit optional nonzero radius'
Must ($volume.Contains('p.Box = knownBox;') -and
       $volume.Contains('return p.Center + p.AxisX *')) 'true owner box sampling replaced'
Must ($volume.Contains('Mathf.Pow(Halton(i, 5), 1f / 3f)')) 'optional sphere sampling disappeared'
foreach($f in @('SourceVolume.cs','Follow.cs','Spots.cs','Filter.cs','Directions.cs','Occlusion.cs')){
 $s=[IO.File]::ReadAllText((Join-Path $r ('src\'+$f)))
 Must (!$s.Contains('StartCoroutine(') -and !$s.Contains('void LateUpdate(')) ('new update/coroutine path in '+$f)
}
Write-Output 'EVENT SOURCE OWNERSHIP PASS: one-shot/loop area parity, authored event points, HRTF/initial visibility, zero default volume rays'
