# 0.3.1

- Replace conflicting Hold R per-slot command with Shift+E, a focused swap on the precise armor-stand switch collider.
- Preserve vanilla R weapon hide/show, E taps, Alt+E alternate interactions.
- Rebuild item-stand and individual armor-stand hover text with actionable Hold E / Shift+E prompts.
- Handle empty hands / empty stands clearly; no nonexistent swap hover prompts.
- If Shift+E targets the armor stand but no individual slot is found, refuse instead of swapping an unintended full loadout.
- Support configurable main interaction key and modifier, still with smart short-hold duration.
- Focused body-piece action uses the corresponding equipped armor slot; no accidental weapon trade.
- Preserve network ownership / rollback, EAQS support, and equip animations.
- No inventory/scene scans every frame; key states only, on-demand validations.

Note: No Valheim in-game validation has yet been completed on 0.3.1.