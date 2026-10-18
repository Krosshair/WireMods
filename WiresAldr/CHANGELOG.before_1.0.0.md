# 0.3.2 — Wire's Aldr (clean code / performance)

- Keep every existing progression formula, resource yield percentage and server-sync configuration key.
- Replace leftover internal WiresHeimvoxtr namespace, project filename and Compendium class name with Aldr names; retain historical BepInEx plugin GUID intentionally for save/config compatibility.
- Merge duplicate melee and area attack Harmony scopes: preserve skill technique, resource-hit and perfect-dodge attacker contexts with fewer patches and correct restoration of nested context.
- Cache reflection field accessors for attack ownership, projectile ownership, parry timer and confirmed dodge state. Remove per-event Traverse allocations for those hot hooks.
- Find the local player's Skills directly rather than GetComponent<Player> on every skill XP event.
- Remove unused per-event XP tier calculations and unused helper method.
- Construct debug messages for resource, fishing, combat and creature events only when diagnostic mode is actually enabled.
- Buffer diagnostic writes and flush at initialization checkpoint, summary and shutdown, instead of forcing disk I/O on every sample.
- Default newly created diagnostic config to OFF. Existing config remains compatible; deliberately turn diagnostics off in the installed profile after backing up its previous config.
- Avoid allocating an extra loot list when a natural source drops no bonus items.
- Preserve source code and installed DLL backups for rollback.
- 238 existing progression/XP/forage/resource/compendium assertions pass, 0 build warnings/errors.
- Gameplay callbacks and runtime performance need live revalidation; benchmarks not fabricated.

# 0.3.1 — Wire's Aldr

- Rename Wire's Living Progression to **Wire's Aldr**, reflecting the character and world growing older together. Keep existing plugin GUID and all saved config keys for compatibility.
- Rename Compendium entry to "Aldr", plugin display name, release DLL and package.
- Add local (non-synchronized) **Enable diagnostics**, ON by default for this validation release.
- Write a dedicated `BepInEx/Aldr-Diagnostics.log` with bounded size and rotation at 2 MB. Each play session gets its own timestamped divider.
- Startup checks: configuration switches, growth settings, loaded companion plugins, and audit of key Harmony patches. PATCH_OK is explicitly not counted as gameplay verification.
- On world entry, report player-based raid mode, biome, earned Forsaken flags and the calculated XP baseline.
- Instrument smelter ore/fuel queueing, ore processing and finished output, without changing smelting, to provide genuine activity during ordinary base play.
- Sample actual skill XP before/after multipliers and activated forage, creature star roll, resource loot, rod fishing, recipe familiarity, parry and dodge events.
- At every 1st, 5th and 10th smelter conversion, write a concise summary of observed signals. Further summaries at shutdown.
- No per-frame pollers or new UI. Output limited to first five and every 20th category event, with XP samples also throttled.
- Preserve the prior biome-first XP balance, all skill-specific mechanisms, creature growth, resources and fishing unchanged.
- The patch audit and instrumentation need to be validated by an actual Valheim session; compilation alone is not runtime proof.

