# Wire's Forge of Goodtential

Reworks the Forge of Potential into a risk-and-reward system. Choose an idol, optionally offer a trophy, and gamble for extra quality. **Forge refinements take half their former crafting time**; normal crafting is unchanged.

## Refinement

Each success gives **+1 quality**, with a hidden **20% jackpot chance** to gain +2 to +5 instead. Each additional jackpot level has a 20% chance to continue.

The base success chance depends on extra quality already gained above the item's natural maximum (**R**):

```
Success = max(65%, 90% - 5% × R)
R:        0   1   2   3   4   5+
Success: 90% 85% 80% 75% 70% 65%
```

A failed roll may **ignore** the attempt (R below 2), **fail** (lose exactly 1 quality level, never below the natural cap), or **reset** to the natural cap (from R = 8). Ordinary failures never lose multiple levels or destroy the item. A reset can never raise an item below its natural cap: in that exceptional case, it sets quality to 1 and always consumes the idol.

## Idols and risky gambles

An item's **native tier** comes from its upgrade recipe. The **minimum safe idol tier** starts there and rises for every **3 extra quality levels** (for a Q4 item: native tier for Q5–Q7, +1 for Q8–Q10, etc.), capped at Bloodgold. Stronger idols improve success by reducing bad outcomes **relative to the current minimum safe tier**, not the item's native tier. The minimum safe tier itself grants no bonus.

Weaker idols are allowed. Each tier below the safe requirement:

- Reduces success by **2.5 percentage points**.
- Increases the jackpot trigger by **10 percentage points**.

At **R = 8+**, using an idol **3 or more tiers too weak** carries a small risk of **actual item destruction** (up to 7%). **Under-tier idols cannot use trophies**, so this risk cannot be protected against by an offering.

**Idols have a 50% chance to survive any ordinary attempt**, including successes, failures and destruction. A strong trophy can improve this to **67.5%** at most. The exceptional below-cap reset always consumes its idol. A saved idol plays a distinct chime.

## Trophy offerings

Offer **one non-boss trophy** when using a safe idol. **Under-tier idols cannot use trophies.** Boss trophies and named Hildir miniboss trophies are excluded. A trophy is always consumed when offered.

On safe attempts, stronger trophies reduce ordinary failures and resets based on their prestige relative to the item. A fresh, equal-prestige trophy reduces ordinary failure chance by about **90.7%** and reset chance by about **98.6%**.

Repeatedly offering the same trophy type causes **fatigue**, temporarily weakening its bonuses. Full recovery takes **1 in-game day** after one use, **2.5 days** after two consecutive uses, and **4.5 days** after three. Recovery is gradual and saves between sessions, separately for each character, world, and trophy type. Fatigue gradually reduces a trophy's benefits while it recovers.

## Forge controls

```
[Idol] [I] [Trophy] [T]
```

**I** cycles owned idols; **T** cycles available trophies and the no-trophy option. The Forge selects your **highest owned matching idol** by default. If none is available, the idol frame is empty and both buttons are disabled. Buttons also disable when there is nothing to cycle through.

Refinable gear is listed even if you only have under-tier idols, sorted by higher success chance and safer failures. The Forge shows **success / ignore / fail / reset / destruction** chances when applicable; jackpot chance stays hidden. No extra hover tooltips or consumption/fatigue rows are added.

Disclaimer: the code is AI assisted since I barely know C#. Not fully AI nor human.
https://ko-fi.com/krosshair
