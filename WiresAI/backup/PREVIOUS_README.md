# Wire's Enemy Variation — experimental playtest 0.1.0

## Status
Build-tested and mathematically tested; NOT yet playtested inside Valheim. This build REPLACES the installed WBA DLL (the original is backed up and disabled, not deleted). It is an integrated behavioral overhaul and persistent-physiology experiment, not a release.

## Core rules
- Ordinary untamed, grounded monsters: persistent individual health ±15%, outgoing damage ±10%, attack cadence/rate ±20%, walk/run speed ±5%, turning speed ±10%, stagger threshold ±15% vs vanilla (HP-induced threshold change is removed before applying the independent stagger trait).
- Each axis has mathematically centered vanilla expectation (1.0) and a hard cap. A bounded odd tanh transform makes extremes rare rather than common.
- Six latent physical axes have approximately -0.185 cross-correlation. An unusually strong attribute softly biases every other strength toward the weak side, but extreme combinations remain possible.
- Personality is correlated with physiology and species (Greydwarf, Troll, Wolf, Fuling, Skeleton, Seeker and neutral fallback), including aggression, courage, tactical propensity, retaliation and cohesion.
- Size is a terminal output, not a source trait: approximately ±2.5% strict bound, based on physique plus independent appearance noise.
- Size physically affects the root capsule/hitboxes and all child transforms; attack range, ray width, attack height and Character.GetHeight scale as well. It does not modify original rolled attributes or perceived strength.
- Stars apply base game's own HP/damage scaling, with only 1.5% per-star narrowing of trait rolls up to 5 stars. Stars don't determine a personality.
- Encounter factions: exactly same faction counts as allies. Hostile other factions and players count as adversaries using BaseAI.IsEnemy. Tamed/players do not become ordinary enemy allies.
- When an ally dies, nearby individuals may develop rage or fear using the species/individual traits. Reactions decay analytically on existing AI decisions, no new timers.
- When alone 1v1 and at full HP, the neutral *population expectation* for modifications is vanilla. Individuals still differ! A finite sample will have tiny sampling error.
- Bosses NEVER roll physiology, size, or permanent psychology. Boss attack/circling delays instead change smoothly toward greater aggression as HP decreases. Boss normal state: vanilla at full HP, 1v1. Health 75/50/25/10% thresholds are log categories only, not sudden stat changes.
- Attack cadence means attempted attack frequency: +-20% ATTACKS/SECOND from physiology (Valheim minAttackInterval = vanillaInterval / cadenceRate); NOT animation playback speed. Existing attack telegraphs are preserved.
- Default debug enabled: [WEV spawn] once on locally owned spawn; [WEV level] only on actual star update; [WEV encounter] first encounter, changed counts, and boss phase. Group refresh uses existing BaseAI.FindEnemy and MonsterAI.OnDamaged, with snapshots cached 1.25 seconds ON DEMAND; nearby ally-death scan only on actual Character.OnDeath. No added per-frame callbacks/coroutines.

## Config
BepInEx/config/kajetan.valheim.enemyvariation.cfg auto-created on first launch.
Debug: Log spawn and encounter = true (default).
General: Enabled, Individual physiology and psychology, Faction-aware encounters, Boss escalating aggression.

## Maths and tests
- Six independent Gaussian draws transformed with lambda=.28 and divisor=sqrt(1-(2*lambda-lambda^2)/6).
- All six axis multipliers are 1 + cap*tanh(z*(1-.015*min(stars,5))/1.65).
- Correlation i!=j = -(2lambda-lambda²)/(6-(2lambda-lambda²)), about -0.087.
- One-way visual scale = 1 + 0.025*tanh((.42*z_health+.27*z_damage+.24*z_stagger+.25*appearanceNoise)/1.8).
- 200,000 deterministic C# individual generations: no cap violations, all six sample means close to 1.0, HP-D corr ~ -0.087, lower-than-average HP associated with higher damage.
- Full health 1v1 with no transient context has exactly zero situational contribution and odd (centered) personal contribution; bosses exactly vanilla.
- Run the tests: dotnet run --project math-tests/Test.csproj -c Release.

## Risk and limitations
- Live Harmony patches, persistent HP changes, and multiplayer synchronization are NOT yet verified in a running game. This is a playtest build.
- Dynamic disabling in config is not intended as a complete live stat restoration; restart and use Rollback.ps1.
- Existing creatures with modified persistent max HP may retain their ZDO max-health value if the DLL is removed; respawn/reload or later cleanup may be needed.
- Owner-authoritative damage and HP updates. Other clients derive exactly the same traits from the persistent ZDO ID/seed. Scale visual/network compatibility requires an actual multiplayer test.
- Monsters that fly or use special serpent movement are exempt from changing WBA combat decisions; physiology is still applied to normal MonsterAI characters, except bosses. New modded behaviors may require species-specific exceptions.
- The code intentionally does not inject frame loops or independent polling.

## Install/Rollback
Install.ps1 / Rollback.ps1 refuse to run while Valheim is open. WBA is backed up at backups/WiresBrainActivity_before_WEV.dll and disabled in its existing plugin directory; the new DLL lives in plugins/Wire-WiresEnemyVariation/.

## Probability retune 2026-11-02
Physical latent correlation reduced from ~-0.185 to ~-0.087 so extraordinary enemies exist without allowing most traits to stack frequently. With 200,000 unstarred samples: 7,220 (3.61%) >=+10% aggregate physical challenge, 485 (0.243%) >=+15%, 3 (0.0015%) >=+20%. This is a heuristic proxy, not game-combat difficulty. Same fixed test seeds, no changes to game spawning or stars.
