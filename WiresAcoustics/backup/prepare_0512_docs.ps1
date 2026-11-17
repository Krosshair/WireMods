$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$src=Join-Path $r 'src\Plugin.cs'
$pkg=Join-Path $r 'package'
$bk=Join-Path $r 'backup\SMELTER_BOUNDS_20261116'
foreach($n in @('README.md','CHANGELOG.md','manifest.json')){
 Copy-Item (Join-Path $pkg $n) (Join-Path $bk ($n+'.before')) -Force
}
Copy-Item $src (Join-Path $bk 'Plugin.cs') -Force
$s=[IO.File]::ReadAllText($src)
if(!$s.Contains('public const string Version = "0.5.11"')){throw 'Unexpected source version'}
$s=$s.Replace('public const string Version = "0.5.11"','public const string Version = "0.5.12"')
[IO.File]::WriteAllText($src,$s,[Text.UTF8Encoding]::new($false))
$manifest=Join-Path $pkg 'manifest.json'
$s=[IO.File]::ReadAllText($manifest)
if(!$s.Contains('"version_number": "0.5.11"')){throw 'Unexpected manifest version'}
[IO.File]::WriteAllText($manifest,$s.Replace('"version_number": "0.5.11"','"version_number": "0.5.12"'),[Text.UTF8Encoding]::new($false))
$nl=[string][char]10
$read=Join-Path $pkg 'README.md'
$s=[IO.File]::ReadAllText($read).Replace(([string][char]13+[char]10),$nl)
$anchor='### Sounds come from where they'+"'re made"
if(!$s.Contains($anchor)){throw 'README heading missing'}
$body=@'
### Clear paths to larger sound sources
A sound made by a large object is not just one point: if any sampled part
of its sound-producing box is clearly exposed to you, it is heard clearly
without wall muffling or being routed through a different opening. The mod
tests the six face centres and eight corners of the source bounds with its
existing three-ray seam-resistant wall checks. The extra checks run only
when a source is blocked, through the existing audio worker-ray batch.
The sound stays exactly where the game/other sound-placement fixes put it;
the bounds only decide whether the object is obstructed.

On a smelter, adding ore or fuel is heard from the actual ore or fuel input,
rather than the centre or base of the entire smelter. The active part is used
as the sound's small bounding volume, so an unrelated exposed section of
the machine cannot make an obstructed input sound appear unobstructed.
Other smelter sounds (such as its normal operation) keep their existing
placement.

'@.Replace(([string][char]13+[char]10),$nl)
$s=$s.Replace($anchor,$body+$anchor)
[IO.File]::WriteAllText($read,$s,[Text.UTF8Encoding]::new($false))
$ch=Join-Path $pkg 'CHANGELOG.md'
$s=[IO.File]::ReadAllText($ch).Replace(([string][char]13+[char]10),$nl)
if(!$s.StartsWith('## 0.5.11')){throw 'Unexpected changelog header'}
$entry=@'
## 0.5.12
- Smelter interaction sounds now come from the part being used: ore from the ore input, fuel from the fuel input. Normal machine sounds keep their original placement.
- Larger sound sources are no longer muffled or redirected if a clear path reaches any tested part of their bounds. The sampled six faces and eight corners use the existing three-ray seam checks, so tiny gaps between building pieces do not falsely open a sound.
- A source's bounds now affect only whether it is obstructed. Its original sound position, stereo direction and HRTF are not shifted to the open edge.
- Blocked large sources are checked using the existing worker-thread batches and frame budget, not extra per-frame polling or synchronous bursts. Clear sources add no bounds rays.

'@.Replace(([string][char]13+[char]10),$nl)
[IO.File]::WriteAllText($ch,$entry+$s,[Text.UTF8Encoding]::new($false))
'PREPARED SOURCE VERSION, MANIFEST, README, CHANGELOG: 0.5.12'
