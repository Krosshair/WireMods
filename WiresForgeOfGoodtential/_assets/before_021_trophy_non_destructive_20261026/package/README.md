# Wire's Forge of Goodtential

Forge of Goodtential changes how the Forge of Potential calculates refinement risk.

The selected idol controls the power and risk of the attempt. An optional trophy can make a safe attempt more reliable. Lower-tier idols can also be used as a high-risk, high-reward option.

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

Before the item has gained 2 extra quality levels, a failed roll is an **ignore** result: the idol is consumed and the item is unchanged.

At 2 or more extra quality levels, ordinary failure can lower the item's quality.

At 4 or more extra quality levels, a failure can remove multiple gained quality levels.

At 5 or more extra quality levels, a **reset** outcome becomes possible. Reset returns the item to its normal maximum quality instead of destroying it.

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
M = clamp(e ^ (-0.11 × (delta + 9)), 0.03, 0.98)
```

Then:

```
Ordinary failure ×= M
Reset chance     ×= M ^ 1.8
```

A stronger trophy therefore has a much larger effect on a weaker item, while an outdated trophy has very little effect on a highly refined item.

Trophies cannot be used with an under-tier idol.

## Under-tier idols

An idol below the minimum safe tier can still be used.

This changes the attempt into a destruction gamble.

Let:

```
D = minimum safe idol tier - selected idol tier
```

For every tier below the minimum:

```
Success chance   -= 2.5 percentage points
Jackpot trigger  += 10 percentage points
```

All non-success probability becomes **destruction**.

Example:

If the minimum safe idol would give:

```
85% success
10% ignore
5% reset
```

then one tier below becomes:

```
82.5% success
17.5% destruction
30% jackpot trigger on a successful attempt
```

Two tiers below becomes:

```
80% success
20% destruction
40% jackpot trigger on a successful attempt
```

A destruction result destroys the item and returns its recoverable crafting materials.

Because destruction can return materials, under-tier attempts require enough free inventory space for those returned items.

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
destruction
```

Example:

```
82.5% success   17.5% destruction
```

The jackpot chance is not displayed.

Disclaimer: the code is AI assisted since I barely know C#. Not fully AI nor human.
https://ko-fi.com/krosshair
