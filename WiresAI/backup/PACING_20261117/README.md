# Wire's AI

Smarter, more individual creatures, built on Valheim's own AI.

> **Status: done in theory, not fully tested.** Every feature is in and it builds clean, but it hasn't had *enough* real playtime yet. Nothing is known to be broken, but I don't recommend yet if you're expecting a 100% sure clean experience. Bug reports and balance feedback is very welcome. Hexium users can simply put feedback on hexium itself but for you Thunderstore fellas, if you've got feedback either go on Hexium or contact me on discord (.krosshair) for any feedback you might have.

## Individuals
Every creature is its own individual, the same one every time it loads (saved with the creature, so it survives relogs and multiplayer handoffs).

- **Body:** health, damage, attack frequency, move speed, turn speed, stagger and size each vary a little. Strong in one area usually means weaker in another, so overall difficulty stays close to vanilla (on average about 5% tougher). Starred creatures have higher possible variation margins.
- **Mind:** aggression, courage, tactics, grudges and pack loyalty, biased by species lore. Wolves are pack hunters, skeletons don't fear anything, greydwarfs are a bit cowardly, and so on.
- Animals like deer and hares get the body part too.
- Tames and summons are individuals as well.
- Bosses and named characters keep their vanilla stats and are the same every time.

## Fighting
- **Postures:** commit, probe, hesitate or briefly back off, based on personality, mood (rage, fear, confidence), health and who's winning. Running away is a last resort; getting hurt makes them angry more than scared.
- **Packs:** creatures that spawned together fight together. They have a leader, take turns instead of all swinging at once, spread fear or courage to each other, regroup at a packmate when hurt, and react when a packmate dies.
- **Positioning:** flank around a blocking player, come from the other side when a packmate is already on you, back off and come back at a better angle, wait out of reach when they can't get to you, hover at the edge of reach waiting for a whiff. Archers keep their distance and step sideways for a clear shot, casters stay behind their friends.
- **Physicality:** they size you up. A creature that can overpower you (bigger, hits harder, breaks through your block) stops dancing around and just presses in. They can't tell how strong your gear is though, so strong creatures stay confident even when you outclass them.
- **Openings:** rolling, eating or drinking, whiffing a swing, or getting staggered can get punished. Clever ones learn within a fight: roll a lot and they wait it out, parry a lot and they start delaying their swings.

## Bosses
- Escalate smoothly as they lose health, push harder if you kite them, take a short breather after a big hit, and get desperate near the end.
- They also count as clever and fearless: they read openings and position like the smartest enemies, but never retreat.

## Multiplayer
Required on the server and every client, same version. The server's settings apply to everyone (lockable). Openings are detected on each player's own client.

## Configuration
- **General:** master switch, individuals on/off, faction-aware encounters, boss escalation, config lock.
- **Tuning:** stat spread (0 = vanilla stats) and behaviour strength (0 = vanilla pacing).
- **Debug:** spawn/encounter log and a verbose behaviour log, for bug reports.

ServerSync by Tykea (MIT license included).

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
