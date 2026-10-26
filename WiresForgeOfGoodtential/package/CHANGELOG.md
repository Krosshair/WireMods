# Changelog

## 0.5.0
- Replaced the idol and trophy cycle controls with I and T on the original wooden buttons; they disable when there is nothing to cycle through.
- Selection defaults to the strongest owned idol of the item's matching Battle/Protection family, and items remain listed even when only under-tier idols are available. Items sort by success and severity of possible outcomes.
- Forge of Potential refining now takes half as long, including the increase per quality level. Normal crafting is unchanged.
- Ordinary failed refinements lose at most one quality; multilevel "anti-jackpots" are gone.
- Only extremely risky attempts (3+ tiers below the minimum safe idol at 8+ extra quality) can destroy equipment. Under-tier idols cannot accept trophies.
- Boss and Hildir miniboss trophies cannot be sacrificed.
- Trophy favor diminishes through repeated sacrifices of the same trophy, recovers gradually over in-game days, and persists between play sessions.
- An idol can survive success, jackpot, ignore, failure, reset or destruction; a saved idol plays its own reward chime.
- Added below-cap reset protection: an impossible reset instead drops the item to quality 1 and consumes its idol, never upgrading an item accidentally.
- Kept the status display to a single line of result chances. Removed all added idol/trophy hover tooltips, consumption text and fatigue rows.
- Shortened the README and kept the original disclaimer and Ko-fi link. Updated the NexusMods-ready BBCode description.
- No change to the Forge's established native-item-tier versus minimum-safe-idol progression. The latter determines risk bonuses.
## 0.4.0
- Every outcome can now spare the selected idol: successful upgrades and jackpots included, alongside ignore, quality loss and reset. Without a trophy, the idol survives with a 50% chance.
- Offering a trophy increases idol preservation based on its prestige relative to the item. `favor = clamp((1 - M) / 0.995, 0, 1)`; `idol survival = 50% × (1 + 0.35 × favor)`, where `M` is the existing trophy bad-outcome multiplier.
- Equal-prestige trophies increase idol preservation to approximately 66%; sufficiently strong trophies reach a maximum of 67.5% (35% relative boost).
- A trophy is still always consumed and may only be offered with safe idols. No trophy means 50% preservation, regardless of refinement outcome.
- No changes to refinement success/reset odds, under-tier idol restrictions, jackpot probabilities, trophy loot rates or Haldor's stock.
- Updated README to explain universal idol preservation and trophy scaling.

## 0.3.0
- Trophy offerings are substantially more powerful. Ordinary failure multiplier is now `clamp(exp(-0.17 × (prestige_delta + 14)), 0.005, 0.995)`; reset uses the multiplier to the power of 1.8. Equal-prestige trophies cut ordinary failure by approximately 90.7% and reset chance by 98.6%.
- Removed item destruction entirely, including under-tier idol attempts. No failed refinement deletes equipment, destroys its original crafting materials, or triggers vanilla item destruction/material-return logic.
- Under-tier attempts now mostly lose gained quality instead of destroying the item; below +2 refinement failures ignore it, +2 and higher lose quality, and +4 and higher may cascade through multiple gained levels. Lower-tier idols increase cascade risk while retaining their increased jackpot chance.
- Full reset risk begins later, at +8 refinement (previously +5), and resets only to the item's natural maximum quality. Under-tier resets remain relatively rare: `min(7%, 10% × failure + 0.5% × (extra_quality - 8))`.
- Safe-idol base reset chance is at most 6% and uses 10% of the base failure pool, reduced further by idol/trophy protection.
- No empty-inventory-slot reserve is required for under-tier refinements now that material-return/destruction outcomes are gone. Ordinary crafting inventory-space rules are unchanged.
- The Forge chance display now shows success, ignore, quality loss and reset only. README updated to reflect exact new probabilities.
- Existing selected idol/trophy costs and the 50% chance to spare the idol on failed refinement remain unchanged.

## 0.2.0
- Failed refinements (ignore, quality loss, reset or destruction) have a 50% chance not to consume the idol. Trophy offerings are still always consumed.

## 0.1.16
- The crafting panel no longer creates garbage every frame while it's open.

## 0.1.15
- Protection idol type text now displays the full word Protection instead of clipping to Protect.
- Renamed the displayed risky outcome from destroy to destruction.
- Rewrote the README as a direct functionality reference with the refinement, idol, trophy, destruction and jackpot formulas shown explicitly.

## 0.1.14
- Added deliberate under-tier idol gambling: each idol tier below the current safe minimum trades 2.5 percentage points of success for destruction and adds 10 percentage points to the hidden jackpot trigger chance.
- Under-tier attempts replace ignore/fail/reset outcomes with full vanilla destruction and ingredient return.
- Trophy offerings are unavailable with under-tier idols; selecting one clears the current trophy and disables CT.
- Safe attempts continue to bypass vanilla's empty-slot reserve, while under-tier destructive attempts restore the vanilla inventory-space safeguard.
- The idol and trophy cycle buttons are now labeled CI and CT.
- Forge percentages now preserve half-percent values such as 82.5% instead of rounding them away.

## 0.1.13
- Removed the Forge of Potential's vanilla empty-slot reserve check. Goodtential never destroys the item, so refinement no longer requires spare inventory slots for hypothetical returned ingredients.
- Normal crafting keeps Valheim's original inventory-space checks unchanged.

## 0.1.12
- Removed Goodtential's OnCraftPressed patch entirely so the Forge of Potential uses Valheim's untouched craft-button start path.
- The selected idol/trophy are now read and validated only when Valheim reaches DoCrafting, while cycle controls remain locked during the craft timer.

## 0.1.11
- Fixed refinement clicks being swallowed by Goodtential's pre-craft validation; the mod now snapshots the prepared offering and lets Valheim start the craft normally.
- Forge-only cycle controls are stripped before vanilla renders a non-upgrader crafting menu, preventing them from contaminating Workbench/Forge/etc. requirement UI.

## 0.1.10
- Rebuilt the cycle controls from the same vanilla button template used by Quick Stack/Store, with a normal UI-font C instead of the pixel-art upgrade arrow.
- Cycle buttons use the button's real disabled state when there is nothing to cycle through or while refinement is running.
- Forge-only cycle controls are removed from view when using normal crafting stations.
- Idol labels now show only the material name, with Battle/Protection in the lower line.
- Trophy labels drop the redundant Trophy suffix; an empty offering simply reads Trophy.

## 0.1.9
- Replaced the cycle-arrow icon with a plain centered C using Valheim's UI font.
- Cycle controls are now hidden outside the Forge of Potential, so normal crafting stations keep their vanilla requirement slots.
- Cycle buttons grey out when there is nothing else to cycle to, as well as while refinement is in progress.

## 0.1.8
- Replaced vanilla trophy substring matching with an exact prefab table covering all 72 current trophy prefabs in the installed game; fuzzy matching now applies only to unknown/modded trophies.
- Corrected special and variant trophies including Moder, Seeker Soldier, Fuling Berserker/Shaman, Hildir minibosses, Rancid Remains, Lava Blob, Pulp, Vile, Krigen, Hexen, Eyeless One, Writhan and Kvastur.
- Reworked the four Forge requirement slots into Idol / cycle button / Trophy / cycle button.
- Cycle buttons move only forward through stronger idols or higher-prestige trophies and wrap back to the first idol / empty trophy offering.
- Removed direct clicking from idol and trophy slots and removed their always-one quantity labels.
- Cycle controls are disabled while a refinement is already in progress.

## 0.1.7
- The Forge now shows only non-zero outcome percentages in its orange status line.
- Removed hover item tooltips from the idol and trophy selectors.
- Fixed Ashlands Charred trophy prefab recognition; Charred Warlock trophies now use the correct high-tier value instead of falling back to the generic unknown-trophy value.
- Raised Charred Warlock trophy prestige to reflect its rarity and threat.

## 0.1.6
- Added exact success, ignore, fail and reset percentages beneath the Forge warning text.

## 0.1.5
- Simplified refinement warnings to match Valheim's terse Forge wording and clarified complete loss as returning the item to its base form.
- Refined result, idol and trophy text for consistency.
- Moved Forge requirement tooltips to a dedicated top-left anchor on the crafting panel.

## 0.1.4
- Rebalanced trophy favor around the item's actual progression stage so strong later-biome kills remain meaningful for fully upgraded previous-biome gear.
- Cycling idols or trophies now plays a selection sound.
- Hover tooltips update immediately when the selection changes without moving the cursor away and back.

## 0.1.3
- Rebalanced idol progression so an item's native idol remains valid for its first few refinements beyond the normal quality cap, then advances one idol tier every three further quality levels.
- Removed the idea of "Potential" as a named upgrade stat; extra quality is simply further refinement beyond the item's natural limit.
- Tightened risk, result, idol and trophy wording to better match Valheim's terse forge/item style.

## 0.1.2
- Returned idol and trophy selection to left-click/right-click cycling.
- Reworked Forge wording and hover text around Valheim's own item tooltips.

## 0.1.1
- Fixed the refinement button incorrectly requiring the item's normal crafting materials in addition to the Forge offering.
- Replaced click-to-cycle requirements with dedicated previous/next arrow buttons on both the idol and trophy offering.
- Idol and trophy hover information now uses Valheim's proper item-tooltip presentation with a background, item-style layout and contextual colored text.

## 0.1.0
- Released as the standalone Wire's Forge of Goodtential.
- Idol requirements rise as an item is refined beyond its normal quality cap instead of remaining tied forever to its original recipe.
- Any stronger idol can replace the minimum requirement and provides additional stability.
- Added one optional trophy offering per refinement, with effectiveness based on the defeated creature's prestige relative to the item.
- Added interactive idol and trophy selection directly to the Forge requirement UI with contextual tooltips.
- Replaced destructive failure with complete refinement loss: the item survives and returns to its normal maximum quality.
- Preserved unlimited refinement, escalating level-loss risk and hidden jackpot refinements.
- Prepared refinements use qualitative risk/favor wording without exposing exact probabilities.
- Selected idol and trophy are validated and consumed as one Forge transaction.
