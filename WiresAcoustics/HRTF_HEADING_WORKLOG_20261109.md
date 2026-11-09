# Wire's Acoustics — sounds keeping their place as you turn, 2026-11-09

## User report
Sounds keep the left/right they started with when you turn.

## Source finding
- Placed 3D sounds: direction is recomputed every frame (`Directions.Step`) against the listener's rotation and read by the HRTF on every callback; these follow your head.
- The frozen case: `Occlusion.Run` turned the game's random ambience (birds, gusts, insects: `AudioMan.UpdateRandomAmbient`, tagged by `AudioProvenance`) into a positionless 2D sound when indoors (`Ambience.PlaceAsAmbient`: spatialBlend 0). A 2D sound's left/right is `panStereo`, set once when it starts (`ZSFX.Play` random pan, plus our lean toward openings in `Ambience.Apply`), so it stayed on the same side however you turned.

## Change
- `src/Occlusion.cs`: indoor random ambience is no longer made 2D; it keeps its 3D spot in the world (Unity's own panning, HRTF off as before) and keeps the indoor muffling from `Ambience.Apply` (room gain/cutoff, refreshed as you move between rooms). `PlaceAsAmbient` is no longer called (left in Ambience.cs, restored by RestorePlaced for already-converted sources).
- Removed the short-lived direction probe (Filter.cs, Budget.cs back to before it).
- Build 0 errors/0 warnings. Installed SHA256 in this log's next line.

## Rollback
backup/DIRECTION_PROBE_20261109/Restore.cmd (DLL from before the probe, i.e. the wood-walls build) and RestoreSource.cmd.

## Needs in-game test
A bird or insect call indoors should stay put in the world as you turn the camera.
- Installed SHA256: 502C21313633905797EF63F1CF35CA1FE8ECEF96E8E62AE87A68B9D951DB4271

## 2. Same issue on darkwood gates (and, by the same cause, every HRTF sound)
- Darkwood gate sounds (sfx_darkwood_door_open/close, ExportedProject Editor/GameElements/Pieces/_res/door/fx): mono clips (Objects_Gate_Darkwood_*.ogg, 1 channel), spatial blend 1, logarithmic 5-30 m, ZSFX m_minPan -1 / m_maxPan 1.
- Cause (Unity pipeline): an AudioSource's OnAudioFilterRead runs BEFORE Unity's own 3D panning (the filter gets the clip upmixed to the speaker channels; Unity pans and attenuates the filter's output, which is also how procedural OnAudioFilterRead audio gets positioned). Our HRTF's two ears were therefore panned again by Unity: with spread 180 each output channel is placed a quarter turn either side of the sound's direction: correct with the sound in front, both ears to the middle with it at your side, ears swapped behind you. The audible result didn't stay with the sound as you turned. (Probably also why direction felt weak earlier.)
- Fix: while our HRTF pans a source (Hrtf, processed), Unity plays it flat (spatialBlend 0, panStereo 0); the distance fade Unity no longer applies is computed here from the source's own rolloff (logarithmic min/clamp(d), linear, or its custom curve over d/max) per frame in Directions and applied in the filter gain (Filter.Rolloff); voice priority raised by -20*log10(heard level) over its own so Unity's 32 real voices still go to the nearer/louder sounds. Restored (blend, pan, priority) as soon as our HRTF stops for it. For flat sources the Occlusion postfix undoes what ZSFX decides for 2D (reverb bypass, random pan). All game-side checks of "is it placed" now use the original blend (AcousticFilter.BlendOf / SpatialBlend): Occlusion, Travel, Menu, PlayerSounds.
- Known side effects: Unity's own Doppler doesn't apply to flat sources (ours does for placed sounds with Realistic distance). Debug "level after processing" now includes the distance fade.
- Build 0/0. Live before matched backup/FLAT_PANNING_20261109/before.dll. Installed SHA256 34DC2AE949BA2B5FB0CBD8C7156AA44CC34D0BD58B7B3DCDFD3F58E144C2B0D7.
- Rollback: backup/FLAT_PANNING_20261109/Restore.cmd, RestoreSource.cmd.
- Needs in-game test: gate opening on your left, turn the camera: it must stay where the gate is. Also check nothing near you goes missing in busy places (voice priority).

## 3. Flat-panning patch REVERTED (user: didn't fix, audio weirder)
- backup/FLAT_PANNING_20261109 sources and before.dll restored (live DLL byte-identical to before.dll). The double-panning theory was wrong for this bug; not pursued further.

## 4. Actual cause: proximity "follow the player" (Follow.cs)
- Follow's EffectList.Create postfix gave any effect sound without an owner to the nearest player within 2.5 m (PlayerSounds.At) and moved it every frame to owner.TransformPoint(offset): an offset in the PLAYER's local space. A sound made by something else next to you (Door.SetState's open/close effects are created with no parent; Attack hit effects at the hit point; pieces) was glued to your character and turned with it: a gate opened on your left stayed on your left as your character turned. Bug dates from my 0.2.0 work, not ChatGPT's.
- Vanilla source check (Character/Humanoid/Player/Attack .Create calls): a character's attack start/trigger/trail, jump and hit effects already pass the character or a joint as parent (followed through 'given'). The owner-less ones that are really the character's: equip (Humanoid.EquipItem, UseItem), unequip (UnequipItem), consume (UseItem), pickup (Pickup), drop (DropItem).
- Fix: removed the proximity owner. Equip.cs now sets Follow.Owner around Humanoid.EquipItem, UnequipItem, UseItem, Pickup, DropItem (outermost call wins). Everything else stays where it was made.
- Build 0/0. Live before matched backup/OWNED_FOLLOW_20261109/before.dll. Installed SHA256 68B72BF3B2443E28D92DA377A77B7984D06DD2076EE3BD3F5FD65411F4860C7A. Rollback: Restore.cmd / RestoreSource.cmd there.
- Needs in-game test: open the darkwood gate and turn: the sound stays at the gate. Equip/eat/pick up still follow you.
