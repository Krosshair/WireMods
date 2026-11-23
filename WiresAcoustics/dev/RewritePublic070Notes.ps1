$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics'
$backup=Join-Path $r 'backup\PUBLIC_NOTES_CLEANUP_20261123'
if(!(Test-Path $backup)){New-Item -ItemType Directory -Path $backup|Out-Null}
$changelogPath=Join-Path $r 'package\CHANGELOG.md'
$readmePath=Join-Path $r 'package\README.md'
$nexusPath=Join-Path $r 'NEXUS_DESCRIPTION.txt'
foreach($name in @('CHANGELOG.md','README.md')){
 $p=Join-Path $r ('package\'+$name)
 $b=Join-Path $backup $name
 if(!(Test-Path $b)){Copy-Item $p $b}
}
if(!(Test-Path "$backup\NEXUS_DESCRIPTION.txt")){Copy-Item $nexusPath "$backup\NEXUS_DESCRIPTION.txt"}
function ReplaceSection([string]$text,[string]$heading,[string]$replacement,[bool]$nexus=$false){
 if($nexus){$pattern='(?ms)^\[size=4\]\[b\]'+[regex]::Escape($heading)+'\[/b\]\[/size\]\r?\n.*?(?=^\[size=[45]\]|\z)'}
 else{$pattern='(?ms)^### '+[regex]::Escape($heading)+'\r?\n.*?(?=^#{2,3} |\z)'}
 $matches=[regex]::Matches($text,$pattern)
 if($matches.Count -ne 1){throw "Expected exactly one section $heading, found $($matches.Count)"}
 $m=$matches[0]
 return $text.Substring(0,$m.Index)+$replacement.TrimEnd()+[Environment]::NewLine+[Environment]::NewLine+$text.Substring($m.Index+$m.Length)
}
$old=[IO.File]::ReadAllText("$backup\CHANGELOG.md")
$i=$old.IndexOf('## 0.6.0')
if($i -lt 0){throw '0.6 section not found'}
$tail=$old.IndexOf('### 0.7.0 RC roof continuity',$i)
if($tail -lt 0){throw 'RC appendix not found'}
$history=$old.Substring($i,$tail-$i).TrimEnd()
$release = @'
## 0.7.0

- Sounds change more naturally as you move behind a wall or past its edge. The more of a sound is hidden, the more it comes through the wall or round it, instead of jumping straight from clear to muffled.
- Building or removing a wall, or opening and closing a door, updates nearby sounds promptly. Walking round the same wall still changes what you hear smoothly.
- Sounds find their way through more windows, doorways and other openings. When several paths are open, their loudness and direction blend instead of a sound jumping from one window to another. Small gaps between building pieces are less likely to sound like open windows.
- Rain stays audible indoors instead of disappearing behind the walls. An open doorway lets in more of the outside than a narrow window, and being under an actual roof changes the rain's sound without making an ordinary floor count as a roof. The original Valheim rain recording is used throughout.
- Wind affects distant sounds differently depending on whether they're upwind or downwind, how far away they are and the ground between you. Buildings, rocks and hills shelter you from it. Clear-weather wind recordings also follow the wind's direction.
- The sea is heard from the shoreline around you, not from one arbitrary point. Buildings can block part of the shore without silencing all of it, and the sound no longer stays behind at an old shoreline when you move away.
- Weapon swings sound from the moving weapon itself, not just the hand holding it. Other sounds keep their actual sources: a chest's lid, a door's hinge, a creature's mouth or feet, and the part of a crafting station you use. Rotated pieces and larger sound sources are handled more accurately.
- Several fires or machines playing the same looping sound can now all be heard. Each starts at a different point in its recording instead of sounding perfectly synchronized. Small fires are quieter than large ones, based on their original sound settings.
- Sound direction is steadier when you turn the camera, hear several paths at once or move past a corner. Doppler pitch changes also behave more consistently at different frame rates.
- Birds and insects placed randomly in the world are less likely to end up in the wrong spot when the first sampled patch of ground is empty.
- Reduced unnecessary sound and room checks, improved audio-job scheduling and fixed several cases where sound processing could keep stale geometry or try to play a disabled source.
- Main-menu sound processing now stays with the menu's own positional sounds instead of affecting audio sources left over from the world.
'@
$newChangelog=$release+[Environment]::NewLine+[Environment]::NewLine+$history+[Environment]::NewLine
if([regex]::Matches($newChangelog,'(?m)^## 0\.7\.0$').Count -ne 1){throw 'Invalid 0.7 heading'}
if($release -match '(?i)release candidate|experimental|failed experiment|patching history|profiler|old implementation|ray job'){throw 'Internal terminology in public changelog'}
if(!$newChangelog.Contains('## 0.6.0') -or !$newChangelog.Contains('## 0.1.0')){throw 'Historical version missing'}
[IO.File]::WriteAllText($changelogPath,$newChangelog,[Text.UTF8Encoding]::new($false))
$md=[IO.File]::ReadAllText("$backup\README.md")
$main=$md.IndexOf('Sound physics.')
if($main -lt 0){throw 'README narrative missing'}
$md="# Wire's Acoustics"+[Environment]::NewLine+[Environment]::NewLine+$md.Substring($main)
$md=ReplaceSection $md 'Material transmission and diffraction' @'
### Walls block sound like real walls
A door lets through more sound than a plank wall, and stone keeps out more than wood. The next room is muffled, not silent. How much of a sound you can hear also changes as you move past the edge of a wall: a little bit hidden shouldn't sound the same as being completely behind it. Sounds can still reach you over a wall or round its corners.
'@
$md=ReplaceSection $md 'Sound goes round things' @'
### Sound goes round things
A sound behind a wall also reaches you through openings: a window, a door, a hole in the roof, or round a corner. More than one opening can carry the same sound. When you move, the sound changes direction gradually instead of jumping between windows. The sound through the wall arrives first, but a much louder arrival through an opening can take over, as your hearing does in real life. Hills, rocks, trees and furniture muffle sound by their size: a trunk barely matters, a boulder takes the highs off, and a hill blocks more the higher it rises.
'@
$md=ReplaceSection $md 'Distance and air' @'
### Distance and air
Sounds fade with distance the way real sound does, instead of cutting out after a few metres: a quiet sound still reaches far, just quietly. Sound takes time to travel, so far sounds arrive late, and sounds coming towards you or going away shift in pitch. A small fire crackles more softly than a large one. The air takes the highs off distant sounds, with warm, cold and freezing weather matched to the real Norwegian weather Valheim is modelled on.
'@
$md=ReplaceSection $md 'Wind' @'
### Wind
Downwind, a distant sound carries further; upwind, it can become quieter. The effect depends on the wind, the distance and the ground around you, and falls away when you're sheltered behind a wall, a rock or a hill. The wind itself is heard from where it comes round and over things: behind a wall it drops to a low rumble, and it gusts and lulls rather than blowing as one even loop. Wind in the Plains reeds follows the wind too; birds and rain don't get mistaken for wind.
'@
$md=ReplaceSection $md 'Sounds come from where they''re made' @'
### Sounds come from where they're made
The game plays a lot of sounds at a character's feet or at the base of a piece. Here, a weapon swing comes from the moving weapon itself, a bow's draw from the bow, holstering from your back, eating from your head. Creatures roar from their head, claw with their arm and stomp with their feet. A chest sounds from its lid, a door from its hinge, a fire from its flame, crafting from the part of the station you use. Rotated pieces stay in the right place. Bigger things can sound wider, while a specific hit or action still comes from the spot where it happened.
'@
$md=ReplaceSection $md 'Weather and the sea' @'
### Weather and the sea
The rain, wind and birds are outside, not in the room with you. Windows, doors and gaps let more of them in; the walls round you let through a quieter, duller sound. Rain stays audible under a roof, and the roof gives it a little more presence without treating a floor between storeys as the roof. It's still Valheim's original rain recording, not added raindrop sounds. The sea is heard from the actual shoreline, including more than one direction where the coast surrounds you. A house blocks the part of the shore behind it, but a post barely matters. Under water, the world above is about 30 dB quieter, as the physics of the water surface says.
'@
if($md -match '(?i)release candidate|experimental loop|256 multi-hit|Fresnel|source-linked|before publishing|not a claim of completed listening'){throw 'Development wording remains in README'}
[IO.File]::WriteAllText($readmePath,$md,[Text.UTF8Encoding]::new($false))
$nx=[IO.File]::ReadAllText("$backup\NEXUS_DESCRIPTION.txt")
$nx=ReplaceSection $nx 'Walls block sound like real walls' @'
[size=4][b]Walls block sound like real walls[/b][/size]
Walls, floors, roofs and doors block sound differently by what they're made of. A door lets through more than a plank wall, and stone keeps out more than wood. The next room is muffled, not silent. As you walk behind the edge of a wall, the sound gradually loses what's in the open and comes through or round the wall instead. Being just behind an edge isn't the same as standing in the middle of its shadow.
'@ $true
$nx=ReplaceSection $nx 'Sound goes round things' @'
[size=4][b]Sound goes round things[/b][/size]
A sound behind a wall also reaches you through openings: a window, a door, a hole in the roof, or round a corner. More than one opening can carry the same sound, and as you walk the direction shifts rather than hopping from window to window. The sound through the wall arrives first and helps set its direction; a much louder arrival through an opening can take over, as hearing does in real life. Hills, rocks, trees and furniture in the way muffle by their size: a trunk barely matters, a boulder takes the highs off, a hill cuts a sound depending on how high it rises.
'@ $true
$nx=ReplaceSection $nx 'Distance and air' @'
[size=4][b]Distance and air[/b][/size]
Sounds fade with distance the way real sound does, instead of cutting out after a few metres: a quiet sound still reaches far, just quietly. Sound takes time to travel, so distant sounds arrive late; sounds coming towards you or moving away shift in pitch. A small fire is quieter than a hearth. The air takes the highs off far sounds as the international standard says it does, with the weather: warm, cold and freezing are matched to the real Norwegian weather Valheim is modelled on.
'@ $true
$nx=ReplaceSection $nx 'Wind' @'
[size=4][b]Wind[/b][/size]
Downwind, distant sounds carry further; upwind, they can get quieter. How much depends on the wind, distance and ground, and it drops away when you're sheltered behind a wall, rock or hill. The wind itself is shaped by what's round you: in the lee of something it drops to a low rumble, it's heard from where it comes round and over what's in the way, and it gusts and lulls instead of blowing as one even loop. The reeds in the Plains follow the wind too, without making birds and rain come from one direction.
'@ $true
$nx=ReplaceSection $nx 'Sounds come from where they''re made' @'
[size=4][b]Sounds come from where they're made[/b][/size]
The game plays a lot of sounds at a character's feet or at the base of a piece. Here, weapon swings come from the moving weapon, a bow's draw from the bow, holstering from your back, eating from your head. Creatures roar from their head, claw with their arm and stomp with their feet. Chests sound from the lid, doors from their hinges, fires from the flame, crafting from the station. Rotated things keep their sounds in the right place, and big sources sound as wide as they look. A specific hit or action still comes from where it actually happened.
'@ $true
$nx=ReplaceSection $nx 'Weather and the sea' @'
[size=4][b]Weather and the sea[/b][/size]
Rain, wind and birds sound like they're outside, not in the room with you. Windows, doors and gaps let more of them in, while walls let through a quieter, duller sound. The rain stays audible indoors, and an actual roof gives the original Valheim recording a little more presence. A floor between storeys doesn't count as a roof. The sea comes from the real shoreline around you: a house blocks the part behind it without silencing the whole coast. Under water, the world above is about 30 dB quieter, as the physics of the water surface says.
'@ $true
$nx=$nx.Replace('how much a wall takes (the first one 12 to 22 dB by how heavy it is, each one after it 6 dB more and duller: measured walls made the next room silent), ','how partial source coverage and several walls are blended, ')
$nx=$nx.Replace('upwind sounds held to 10 dB quieter at most (measured: 20 dB and more), ','the limits applied to wind propagation outside the measured range, ')
$nx=$nx.Replace('fires as loud as how much is burning (by flame width against a 1.5 m campfire, down to 18 dB quieter), ',"fires' relative loudness based on the original sound range, ")
if($nx -match 'fires as loud as how much is burning|first one 12 to 22 dB|upwind sounds held to 10 dB'){throw 'Outdated Nexus model claim remains'}
[IO.File]::WriteAllText($nexusPath,$nx,[Text.UTF8Encoding]::new($false))
if(!([IO.File]::ReadAllText($changelogPath)).Contains($history)){throw 'Previous release history altered'}
foreach($p in @($changelogPath,$readmePath,$nexusPath)){Write-Output ('UPDATED '+$p+' '+(Get-Item $p).Length+' bytes')}
Write-Output 'PUBLIC CONTENT REWRITE PASS'
