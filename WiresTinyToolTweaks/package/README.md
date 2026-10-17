# Wire's Tiny Tool Tweaks

Small vanilla-style tool fixes.

In 1.0.2, **both features are independently configurable** in BepInEx's \`Features\` section. Both default to **true**:
- \`Dismantle empty carts and ships\`: makes empty carts/boats removable with the hammer, respecting the normal checks.
- \`Interact while holding build tool\`: restores normal interactions and animations with a build tool equipped.

- Interact normally with things while a build tool is equipped.
- Successful interactions still animate the held build tool.
- Dismantle empty boats and carts with the hammer when they are safe to remove and you are within the required crafting-station range.
- Vehicle dismantling plays the normal held-tool swing animation.
- Occupied/in-use vehicles, non-empty storage, wards and no-build areas are respected.
- Hover lookup while a build tool is equipped uses cached delegates instead of per-frame reflection allocations.
