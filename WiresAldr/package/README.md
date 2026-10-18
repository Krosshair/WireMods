# Wire's Aldr

*Aldr* is Old Norse for age or lifetime. Your character and your world grow older together: every Forsaken you defeat makes its biome richer, and risk is highly rewarded.

Every bonus is shown in the Compendium under **Aldr**, with where it comes from.

### World

Each Forsaken defeated improves its own biome. For stars, harvest chance, fishing and later-Forsaken yields, later boss kills add 50% of the previous step each, and a biome whose Forsaken is still alive gets 15% of what later Forsaken would give.

- **Creature stars:** +8 percentage points star chance for the biome's native creatures, scaled with world difficulty (relative to the 10% base). Max 95%.
- **Wild foraging:** +25 percentage points bonus harvest chance for the biome's wild plants (total capped at 80%). Later Forsaken add items per pick: +20%, then half the previous step each (max about +40%).
  - Black Forest mushrooms: +8 after the Elder. Swamp thistle: +10 after the Elder, +7.5 after Bonemass.
- **Natural materials:** extra drops from trees, rocks and deposits.
  - Wood: +10% Eikthyr, +10% Elder, +5% each later Forsaken.
  - Stone: +20% Elder, +5% each later Forsaken.
  - Core wood +20% Elder. Fine wood +10% Elder, +20% Yagluth. Ashwood +10% Fader.
  - Regional materials (copper, tin, iron, silver, obsidian, tar, ...): +10% after their biome's Forsaken; later Forsaken add +25%, then half the previous step each (max about +50%).
- **Wild fishing:** +15% chance of an extra fish per rod catch of the biome's fish. Their spawn chance, spawn rate and population rise by the same share, and quality rolls by 0.35 x that in percentage points.

### Experience

All skills. Bonuses are additive; only raids multiply.

**XP = base XP x (1 + location + catch-up + Expedition + stars + extras) x raid**

## Location
| Biome | Bonus |
|---|---|
| Meadows | +0% |
| Black Forest | +10% |
| Swamp, Ocean | +25% |
| Mountains | +45% |
| Plains | +70% |
| Mistlands | +100% |
| Ashlands | +135% |

Plus 20% of the bonus of your **progress biome** (the biome after the last Forsaken you defeated), wherever you are.
Fighting a hostile creature from a later biome counts as its biome. Fish count as their native biome.

## Catch-up
- **Any skill** below 8 + 6 x your tier: up to **+15%**, as 15% x (threshold - skill) / threshold. Tier = the higher of your progress biome and your current biome (Meadows 1, Black Forest 2, ...).
- **Combat skills** (weapons, unarmed and magic) below 3/4 of your best one (once it's 20+): up to **+100%**, same formula. Not for rewards.
- Only the larger of the two applies.

## Expedition
A bonus that grows while you adventure away from base.
- **Bonus = 0.3% x T + 0.067% x T^1.5**, T = expedition minutes. About +20% after 30 minutes in the Swamp.
- Each XP gain adds the time since the previous one (max 30 s), at a biome pace: Meadows x0.8, Black Forest x0.9, Swamp x1.0, then +0.1 per biome.
- Only grows while you keep moving (20 m within 6 minutes). Riding counts; sailing at 2 m/s or more grows it at 1/5 pace.
- **Paused at a base**, decaying 2 minutes per minute. Away from a base it decays 0.5 minutes per minute after 90 s without XP. Resting doubles both. Sailing and riding hold it.
- Saved on your character. Lost on death.

## Challenge
- **Stars:** +25% combat and dodge XP per star.
- **Raids:** x1.5 combat XP against raiders while you're inside the raid.
- **Extras**, shared cap +60% (+75% ranged):
  - **Danger:** same biome as your last conquered one +10%, +4% per biome beyond (max 18%), one older 4.5%, older 1.2%. Up to 1.4x more at skill 90 against current threats.
  - **Aim:** ranged hits +3% (moving target +8%) + 0.3% per meter (max 12%). Melee hits on moving targets past 12 m: 0.2% per meter beyond, max 7%. Unaware targets +7%. Max 25% ranged, 12% melee. +3% per extra target hit at once.
  - **Combos:** +12% per landed chain step, max +36%. **Counters:** +6% on the first hit within 2.25 s of a parry or perfect dodge.
  - **Blocking:** danger + 40% x the share of your stagger bar the hit filled.
  - **Perfect dodges:** danger + up to 25% for attackers with 4x your health.
  - **Travel:** Run, Swim and Ride up to +40% for steady straight-line progress.
  - **Sneak:** +35% near unaware enemies.
  - **Crafting, cooking and placing pieces:** +2.5% per ingredient type (max 12%), upgrades +3.5%.
  - **Cooking:** 50% x (health + stamina + eitr of the dish - 40) / 120, max +50%.
  - **Farming:** up to +10% for crops of later biomes.

## Rewards
Flat XP for accomplishments. They get half the location bonus.
| Action | XP |
|---|---|
| Kill, creature of your last conquered biome | 2.5, +1 per biome beyond (max +3) |
| Kill, older creatures | halved per biome older (1.25 / 0.63 / 0.31) |
| Stars on a kill | +50% per star |
| Sneak attack kill (crouched, target unaware) | +3 Sneak, halved per biome older |
| Summon kill | 60% of a kill, full if you fought in the last 20 s |
| Tree felled | 2 + tool tier |
| Regional deposit, per chunk | (4 + tool tier) / (2 x sqrt(chunks)) |
| Regional deposit, mined out | 4 + tool tier |
| Fish caught with a rod | 3 + 1.5 per quality above 1 |
| Parry (timed, not against bosses) | 0.15 x (1 + 9 x stagger^4) x (0.2 + 0.8 x hit size), max 1.5. Stagger = your stagger bar after the parry, hit size = damage / 35% of your health |
| Perfect dodge streak | 0.05 per dodge in the streak, max 0.3 |
| Blood Magic barrier | 4 per unskilled barrier's worth of damage absorbed |
| First craft (2+ ingredient types) | 1.5 + 0.5 per type, max 5 |
| Upgrade | 1 + 0.5 x new quality, per level |
| First station dish | 2 Cooking |
| Crop harvested | 0.5 Farming |

Bosses never give challenge bonuses or rewards.

## Multiplayer
Required on the server and every client, same version. The server's settings apply to everyone (lockable). Raids in player-based mode follow each player's own Forsaken.

## Configuration
Every number above is a setting, and every part has its own toggle, server-synced. `12 - Diagnostics` writes `BepInEx/Aldr-Diagnostics.log` for bug reports; it's local and off by default.

ServerSync by Tykea (MIT license included).

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
