# Wire's Forge of Goodtential

Forge of Goodtential changes how the Forge of Potential calculates refinement risk.

The selected idol controls the power and risk of the attempt. An optional trophy can make a safe attempt much more reliable. Lower-tier idols trade stability for a higher jackpot chance.

**Refinement never destroys your equipment or its crafting materials.** The worst outcome is a reset to the item's normal maximum quality. Idols and offered trophies are still consumable costs.

## Quality and base success chance

Let:

- **Qmax** = the item's normal maximum quality.
- **Q** = the item's current quality.
- **R** = extra refinement already gained above the normal cap.

```
R = max(0, Q - Qmax)
```

The base success chance is:

```
Success = max(65%, 90% - 5% × R)
```

This gives:

| Extra refinement R | Base success |
| ---: | ---: |
| 0 | 90% |
| 1 | 85% |
| 2 | 80% |
| 3 | 75% |
| 4 | 70% |
| 5+ | 65% |

The displayed **success** percentage includes both normal +1 results and jackpot results.

## Minimum safe idol

Every item has a minimum safe idol tier for its next refinement.

For an item with a normal maximum quality of 4:

| Target quality | Minimum safe idol |
| ---: | --- |
| Q5-Q7 | Item's native idol tier |
| Q8-Q10 | +1 idol tier |
| Q11-Q13 | +2 idol tiers |
| Q14-Q16 | +3 idol tiers |
| Q17-Q19 | +4 idol tiers |
| Q20-Q22 | +5 idol tiers |

The progression continues until Bloodgold, which remains the highest required tier.

The default selected idol is always the lowest safe idol available for the item.

## Safe idols

A safe idol is an idol at or above the minimum safe tier.

Using the minimum safe idol keeps the base success/failure distribution for that refinement level.

Using a stronger idol reduces bad outcomes.

For each idol tier above the minimum:

```
Ordinary failure multiplier = 0.72 ^ tiers_above
Reset multiplier            = 0.45 ^ tiers_above
```

The probability removed from those outcomes is added to **success**.

### Failure types

Before the item has gained 2 extra quality levels, a failed roll is an **ignore** result: the item remains unchanged, and the idol has a 50% chance to be spared.

At 2 or more extra quality levels, ordinary failure can lower the item's quality.

At 4 or more extra quality levels, a failure can remove multiple gained quality levels.

At 8 or more extra quality levels, a **reset** replaces the old item-destruction outcome. The item survives at its natural maximum quality; none of its original crafting materials are lost or returned.

Without extra idol/trophy protection, reset chance on a safe attempt is:

```
Reset chance = min(6%, 10% × base failure chance)
```

At 8 or more extra levels, the base failure chance is 35%, so reset chance is **3.5%** before protection. All other failures lower quality; quality never falls below the natural maximum.

## Trophy offerings

One trophy may be offered on a safe attempt.

Trophies reduce bad outcomes. Their effect depends on the strength of the defeated creature relative to the item being refined.

Each trophy has an internal prestige score. The item also has a reference prestige based on its progression.

For an item above its normal quality cap:

```
Item tier reference = native tier + (extra quality / 3)
```

Tier prestige anchors are:

| Tier | Prestige |
| --- | ---: |
| Wooden | 6 |
| Bronze | 20 |
| Iron | 30 |
| Silver | 40 |
| Black Metal | 48 |
| Black Marble | 58 |
| Flametal | 68 |
| Bloodgold | 82 |

Let:

```
delta = trophy prestige - item prestige
M = clamp(e ^ (-0.17 × (delta + 14)), 0.005, 0.995)
```

Then:

```
Ordinary failure ×= M
Reset chance     ×= M ^ 1.8
```

A trophy from an equally prestigious enemy now multiplies ordinary failure chance by about **0.093** (a **90.7% reduction**) and reset chance by about **0.014** (a **98.6% reduction**). Stronger trophies protect even more, while outdated trophies have little effect.

The risk removed by trophy protection is added to success.

Trophies cannot be used with an under-tier idol.

## Under-tier idols

An idol below the minimum safe tier can still be used.

This increases the risk of losing several extra quality levels, while making a jackpot more likely. No idol tier can ever destroy the item.

Let:

```
D = minimum safe idol tier - selected idol tier
```

For every tier below the minimum:

```
Success chance   -= 2.5 percentage points
Jackpot trigger  += 10 percentage points
```

All non-success probability becomes **ignore** (before +2 extra quality), **quality loss** (from +2 onward), or a rare **reset** (from +8 onward).

Example with **R = 3** (3 extra quality levels):

```
Minimum safe idol:
75% success
25% quality loss

One idol tier below:
72.5% success
27.5% quality loss
30% jackpot trigger on a successful roll

Two idol tiers below:
70% success
30% quality loss
40% jackpot trigger on a successful roll
```

From R = 4 onward, individual failures can cascade across multiple gained quality levels. Lower-tier idols increase that cascade chance:

```
Loss cascade continuation chance = base failure chance × (1 + 0.35 × D)
```

From R = 8 onward, a low-tier idol also has a rare reset outcome, replacing a small part of ordinary failure:

```
Reset = min(7%, 10% × (1 - success) + 0.5% × (R - 8))
Other failure = (1 - success) - reset
```

A reset returns the **same item** to its natural maximum quality. Its normal crafting materials are not lost or returned. Under-tier attempts no longer require free inventory slots for hypothetical returned materials.

Selecting an under-tier idol clears the selected trophy and disables trophy cycling.

## Jackpot refinement

Jackpot chance is hidden in the Forge UI.

On a safe attempt, a successful refinement has a **20%** chance to become a jackpot.

For each idol tier below the minimum safe tier, the jackpot trigger increases by **10 percentage points**.

When a jackpot triggers, the item gains at least +2 quality instead of +1.

After that, each additional quality level has a 20% continuation chance, up to +5 total quality.

If **J** is the jackpot trigger chance, the quality gain on a successful attempt is:

```
+1 = 1 - J
+2 = J × 0.80
+3 = J × 0.16
+4 = J × 0.032
+5 = J × 0.008
```

For the normal 20% jackpot trigger:

```
+1 = 80%
+2 = 16%
+3 = 3.2%
+4 = 0.64%
+5 = 0.16%
```

## Forge UI

The four Forge slots are:

```
[Idol] [CI] [Trophy] [CT]
```

- **CI** cycles through owned idols.
- **CT** cycles through owned trophies.
- Cycling is disabled while a refinement is in progress.
- CT is disabled while an under-tier idol is selected.
- If there is nothing to cycle through, the corresponding button is disabled.
- Idol names show the material on the top line and **Battle** or **Protection** below it.
- Trophy names omit the redundant word **Trophy**.
- The orange status line shows only non-zero outcomes.

Possible displayed outcomes are:

```
success
ignore
fail
reset
```

Example:

```
72.5% success   27.5% fail
```

The jackpot chance is not displayed.

Disclaimer: the code is AI assisted since I barely know C#. Not fully AI nor human.
https://ko-fi.com/krosshair
