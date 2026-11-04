# Wire's Enemy Variation - decision AI experiment (2026-11-02)

This experiment is NOT validated with actual Valheim combat yet. Compiles against the installed Valheim and BepInEx, with zero C# warnings or errors. It replaces the previous WEV DLL in the Gale Default profile, leaving WBA separately disabled as before.

## Behavior (real gameplay choices)
- Each ordinary enemy retains a stable 5-axis personality (aggression, courage, tactics, retaliation, group cohesion).
- At existing BaseAI.FindEnemy events the owner independently chooses a combat posture using deterministic bounded random draws. No new Update, LateUpdate, FixedUpdate, AI polling, or coroutine.
- Commit: sharply reduces circling so the creature pursues vanilla offensive behavior.
- Probe: more of Valheim's native repositioning/circling.
- Hesitate: short extra minimum-attack delay (even when the original was zero), coupled with vanilla circling.
- Retreat: for approx 1.2 to 3.6 seconds temporarily enables vanilla MonsterAI low-health flee logic. A one-shot Unity Invoke restores the ORIGINAL flee settings; no custom movement/pathfinding and no continuously updating helper.
- Every 2.0 to 4.8 seconds (per individual decision interval), a NEW decision is possible on the NEXT native target-search event. It doesn't poll to force decisions.
- Taking damage, seeing an ally die, and successfully dealing damage to a player can trigger earlier reconsideration.
- Damage IN: raises temporary fear and rage, slightly reduces confidence.
- Damage OUT on an actual player's HP: raises the enemy's confidence, slightly increases rage and reduces fear; repeated hits stack and confidence decays with a 12-second half-life. Works through a routed owner RPC for multiplayer.
- Perceived target injuries also make the enemy somewhat more likely to commit. Hostile groups and relative numbers influence decisions as before.
- Bosses have no randomized personality or physique; existing low-health aggression increases smoothly, and successful player hits can additionally raise short-term offensive pressure. Bosses never use the new retreat postures.
- Physiology and correlation math are unchanged from the previous probability retune.

## Telemetry
[WEV spawn]: persistent individual stats.
[WEV level]: late star update with stable seed.
[WEV encounter]: faction counts, actual modified attack / circle timing, health, boss phase.
[WEV reaction]: actual incoming damage, actual player HP lost, or an ally death, with fear/rage/confidence.
[WEV behavior]: action-to-action transition, trigger, personality, assessed retreat chance, health, confidence.

## Limitations
- It is still Valheim's existing combat system: no custom formation logic, different attacks, active shield defense, or tactical obstacle routing.
- If the original weapon has a longer cooldown, a commitment cannot bypass it.
- Native flee may fail when no route is available, and vanilla's special movement cases remain exempt.
- Owner-authoritative AI decisions are supported; routed damage RPC needs an actual multiplayer game check.
- Individual action distributions are not yet confirmed to produce precisely vanilla-average *observed* action frequency in an actual healthy solo duel. Physical-attribute distributions still center on vanilla.
- No in-game testing yet; this is a build-tested prototype. Monitor logs and be prepared to rollback if needed.

## Install and rollback
The previous active WEV DLL is backed up in this project's backup directory before deployment.
Run Rollback.ps1 with PowerShell -ExecutionPolicy Bypass only while Valheim is closed.

Star spawning policy: WEV has NO override of star spawn caps or star-unlock functionality. It only observes Character.SetLevel to apply persistent individual stats to the star level set by Valheim/other mods. Any 3+ star spawning/unlock should be handled separately by SLS. Do not remove SetLevel handling, which is essential to preserve WEV HP and traits correctly.
