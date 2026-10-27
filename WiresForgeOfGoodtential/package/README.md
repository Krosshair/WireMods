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

An item's **native tier** comes from its upgrade recipe. The **safe tier** starts there and rises for every **3 extra quality levels** (for a Q4 item: native tier for Q5–Q7, +1 for Q8–Q10, etc.), capped at Bloodgold. Stronger idols improve success by reducing bad outcomes **relative to the safe tier**, not the item's native tier. The safe tier itself grants no bonus.

Weaker idols are allowed. Each tier below the safe tier:

* Reduces success by **2.5 percentage points**.
* Increases the jackpot trigger by **10 percentage points**.

At **R = 8+**, using an idol **3 or more tiers too weak** carries a small risk of **actual item destruction** (up to 7%). **Under-tier idols cannot use trophies**, so this risk cannot be protected against by an offering.

## Keeping your idol

Any attempt can spare its idol. How likely depends on what happened and what you brought:

```
After a success or jackpot:   45%
After an ignore or a fail:    65%
After a reset:                85%
After destruction:            50%
```

* Each idol tier **above** the safe tier adds **8 points**; each tier **below** takes 8 away.
* A trophy adds up to **25 points**, depending on how well it suits the item.
* **Idol luck:** every idol lost in a row adds **4 points** to the next attempt, up to **12**. Keeping an idol resets it. It lasts for the play session.
* The chance always stays between **15% and 90%**. With the safe idol and no trophy it averages about 50%.

The exceptional below-cap reset always consumes its idol and doesn't count toward idol luck. A saved idol plays a distinct chime.

## Trophy offerings

Offer **one non-boss trophy** when using a safe idol. Boss trophies and named Hildir miniboss trophies are excluded. A trophy is always consumed when offered.

Stronger trophies reduce ordinary failures and resets based on their prestige relative to the item. A fresh, equal-prestige trophy reduces ordinary failure chance by about **90.7%** and reset chance by about **98.6%**.

Offering the same trophy type again soon after weakens it while its magic recovers. Full strength returns after **1 in-game day** after one use, **2.5 days** after two in a row, and **4.5 days** after three. Recovery is gradual and saves between sessions, separately for each character, world, and trophy type.

## At the Forge

```
[Idol] [Swap] [Trophy] [Swap]
```

* The Forge picks your **weakest idol that can't cost the item a level**, or your best one if every idol carries that risk.
* **Swap** opens a popup like the shield style one, listing the idols or trophies you can use. Hover one to see what it does, click it to pick it. Clicking the trophy you're already offering takes it back off.
* **Hover the idol** to see whether it's strong enough, the odds, how often it's kept and the current safe tier.
* **Hover the trophy** to see how much it helps, how much more often your idol is kept with it, and whether its magic was drawn on recently.
* The odds line shows **success / ignore / fail / reset / destruction** when they apply; the jackpot chance stays hidden.

Refinable gear is listed even if you only have under-tier idols, sorted by higher success chance and safer failures.

Disclaimer: the code is AI assisted since I barely know C#. Not fully AI nor human.
https://ko-fi.com/krosshair
