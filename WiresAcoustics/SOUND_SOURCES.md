# Where each sound comes from (proposal, 2026-11-23)

Status 2026-11-23: the "proposed" spots below are built (wards, map tables, feasts to their top; turret to its head; ship sail; incinerator lever; trader/NPC heads). Several others were already handled by Acoustics (chest item-moved -> lid, fire lit -> flame, portal/trap/piece damage -> middle). Machine loops checked in the prefabs: furnace fire loops (smelter, kiln, blast furnace) were at the pivot and now go to their fire light; windmill, spinning wheel, fermenter and blast furnace machinery were already at their parts; the Eitr refinery hum stays at the piece middle.

From Valheim's code (`ValheimSOURCE\csharp\assembly_valheim`, every `EffectList.Create` outside characters, UI and status effects) and what Wire's Acoustics already does (`Spots.cs`, `Follow.cs`, `Area.Voice`, `TerrainAudio.cs`). "Pivot" = the object's `transform.position`, usually its base on the ground.

Rule for the new version: every sound at its real spot; its own object never muffles it; what you're using is clear within reach. (Redirecting a sound out through its own object's opening was dropped: at a real spot the shift is a few degrees and it would double the route search.)

## Already at the real spot (the game does it)
- Hits on pieces, trees, logs, rocks, destructibles, archery targets: `hit.m_point`.
- Cooking station: food added, done, overcooked at the slot (`m_slots[i]`).
- Fermenter, smelter output, beehive, sap collector: their spawn/output points.
- Item stand item placed: the attach point. Armor/item stand destroyed: drop point.
- Catapult load/shoot/arm return: load point. Siege machine: its part/effect point.
- Shield generator shield start/stop/hit: the dome / hit position.
- Footsteps, projectiles, equipping: already placed.

## Already moved by Acoustics
- Weapon swings/trails: the visible weapon. Creature idle/alert/voice: head (`m_eye`); bites/spits at head, claws/swipes at hand.
- Holstering: the back slot; drop: right hand; eat/drink: head; pick up: where the item was.
- Door open/close/locked: the hinge, half way up (`Spots`).
- Chest open/close: the lid (top of its box) (`Follow`).
- Fire sounds (hearth, brazier, sconce, campfire): the flame (its light) (`Area.Voice`).
- Smelter coal/ore intakes and output, fermenter intake and tap, cooking station fuel intake (0.5.12).
- Crafting station crafting sounds (played at the player): the station.
- Terrain raise placement sound: above the new ground (0.7.0).
- Anything else a piece plays at its pivot: the middle of the piece (`Area.Voice`), a fallback, not a real spot.

## Still at the pivot: proposed real spot (please check)
| Source (game code) | Now | Proposed |
|---|---|---|
| Smelter/kiln/blast furnace/windmill/spinning wheel running loop (prefab AudioSource) | where the prefab puts it (to check per prefab) | the fire/firebox or the moving part (the light for fire, the rotating part for mills) |
| Smelter `m_produceEffects` (done, item out) | pivot | `m_outputPoint` |
| Fermenter `m_addedEffects` | pivot | intake (as its other sounds) |
| Fireplace `m_toggleOnEffects` (lit) | pivot | the flame (its light) |
| Fireplace `m_fireworksEffects` | pivot | the flame, upward |
| Turret shoot/reload/target sounds | pivot | the turret body/eye (`m_turretBody`, `m_eye`) |
| Ship `m_changeSailPosEffect` | ship pivot | the sail (`m_sailObject` / `m_sailBottomTransform`) |
| Ship `m_waterImpactEffect` | ship pivot | the hull's waterline at the bow |
| Catapult leg up/down | pivot (one uses each leg) | each leg (`m_legs[i]`) |
| Incinerator `m_leverEffects` | pivot | the lever |
| Map table `m_writeEffects` | pivot | the table top (middle of its box, top face) |
| Trap arm/trigger | pivot | the jaws (middle of the trap) |
| Ward (PrivateArea) activate/flash/permit | pivot (base) | its glowing head (top of the piece) |
| Portal (TeleportWorld) connected | pivot | the portal ring centre (`m_proximityRoot`) |
| Trader greet/talk/buy/sell (Haldor, Hildir, Bog Witch) | feet | the head |
| Raven/Odin/NPC talk, RandomSpeak | pivot (feet) | the head/beak |
| Feast eat | pivot | the table top |
| Egg hatch, Plant grow | pivot | the egg / the plant's middle (pivot is close already) |
| Container `m_moveItemEffects` (item moved in) | chest pivot | the lid (as open/close) |
| WearNTear switch (damage stage) / snow damage | pivot | middle of the piece |
| Fishing float nibble/line break | float | fine as is |

Loops placed by prefabs (not in code): need a pass over the prefabs (`ValheimProject\ExportedProject\Assets`) to list each AudioSource's transform against where the sound really happens.
