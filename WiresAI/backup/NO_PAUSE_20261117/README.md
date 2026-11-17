# Wire's AI

Smarter, more individual creatures, built on Valheim's own AI.

Feedback and bug reports: on Hexium, or on Discord (.krosshair).

## Individuals
Every creature is its own individual, the same one every time it loads (saved with the creature, so it survives relogs and multiplayer handoffs).

- **Body:** health, damage, attack frequency, move speed, turn speed, stagger and size each vary a little. Strong in one area usually means weaker in another, so overall difficulty stays close to vanilla (on average about 5% tougher). Starred creatures have higher possible variation margins.
- **Mind:** aggression, courage, tactics, grudges and pack loyalty, biased by species lore. Wolves are pack hunters, skeletons don't fear anything, greydwarfs are a bit cowardly, and so on.
- **Sight:** each sees a little further or less than its kind, separate from everything else. A dim one can have sharp eyes.
- Animals like deer and hares get the body part too.
- Tames and summons are individuals as well.
- Bosses and named characters keep their vanilla stats and are the same every time.

## Fighting
- **Postures:** commit, probe, hesitate or briefly back off, based on personality, mood (rage, fear, confidence), health and who's winning. Running away is a last resort; getting hurt makes them angry more than scared.
- **Packs:** creatures that spawned together fight together. They have a leader, take turns instead of all swinging at once, spread fear or courage to each other, regroup at a packmate when hurt, and react when a packmate dies.
- **Positioning:** flank around a blocking player, come from the other side when a packmate is already on you, back off and come back at a better angle, wait out of reach when they can't get to you, hover at the edge of reach waiting for a whiff. Archers keep their distance and step sideways for a clear shot, casters stay behind their friends.
- **Physicality:** they size you up. A creature that can overpower you (bigger, hits harder, breaks through your block) stops dancing around and just presses in. They can't tell how strong your gear is though, so strong creatures stay confident even when you outclass them.
- **Openings:** rolling, eating or drinking, whiffing a swing, or getting staggered can get punished. Clever ones learn within a fight: roll a lot and they wait it out, parry a lot and they start delaying their swings.
- **Pacing:** no long pauses. Aggressive ones chain attacks, mixing them up, and get the next one ready sooner. Attacks that land get used more, ones that miss or get parried less, and clever ones nearby learn from it. Only dim creatures circle around you the vanilla way; the rest face you or come back round to your front.
- **Chargers** back off to their run-up distance and charge again. **Flyers** weave in front of you and dive; big ones like Moder make wide, slow passes.

## Finding you
- They give up a chase they can't win (calm and timid ones sooner, the hateful ones not at all) and walk home.
- Lost you: they spread out to search where you were, clever ones where you were heading.
- No path: they try a way round first. Standing on a rock: archers keep shooting, clever melee ones wait out of sight for you to come down.
- Shot from out of sight: only the really clever work out where from; the rest just know roughly which way.

## Bosses
- Escalate smoothly as they lose health, push harder if you kite them, take a short breather after a big hit, and get desperate near the end.
- They also count as clever and fearless: they read openings and position like the smartest enemies, but never retreat.
- Past half way they stop circling and keep to your front; Moder's passes get tighter and lower.

## Multiplayer
Required on the server and every client, same version. The server's settings apply to everyone (lockable). Openings are detected on each player's own client.

## Configuration
- **General:** master switch, individuals on/off, faction-aware encounters, boss escalation, config lock.
- **Tuning:** stat spread (0 = vanilla stats) and behaviour strength (0 = vanilla pacing).
- **Debug:** spawn/encounter log and a verbose behaviour log, for bug reports.

ServerSync by Tykea (MIT license included).

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
