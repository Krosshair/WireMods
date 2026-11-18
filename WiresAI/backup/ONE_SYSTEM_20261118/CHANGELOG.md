## 0.3.1
- Noticing: creatures look for you by how close you are: twice a second up close, once a second nearby, slower than vanilla far away. Anything right next to a creature is noticed whichever way it faces, so they no longer walk right past you.
- Chasing: closing in on you they go straight at you instead of where you'll be, so they don't overrun you (they still lead you when you're far and getting away).
- Stuck: one that's running after you but hasn't got any closer tries a way round.
- Searching is less random: a pack only spreads out if you're really gone from where they lost you, each to its own fixed side, and after that they follow the way you went for a few steps before wandering.
- Swinging early (and closing in on a retreating target) works from a creature's very first attack: until an attack has been seen to hit, when it hits is read from the creature's own animations.
- They read how you're really moving, not every twitch: tapping left and right no longer sends them the wrong way or makes them whiff. Only movement you keep up counts.
- Misjudging an attack is now a small error (about 5% of its reach), like a player's, instead of whiffing at air.

## 0.3.0
- No more long pauses: when not chaining, the next attack is ready sooner, by aggression. Only dim creatures circle the vanilla way; the rest face you, or walk back round to your front.
- Attacking comes first: the moment they reach you they attack, no wait. In reach with an attack ready they always swing instead of repositioning or circling, with any attack that reaches (not just the one in hand), and clever ones turn to you in time. Aggressive ones also reposition and feint less overall.
- More attacks, like a player: they swing early while closing in when they reckon you'll be in reach as the hit lands, and read where you're turning. Their read can be wrong (more so for dim ones), so they can whiff or hesitate. Everyone waits less between attacks and chains more often.
- Better timed hits: clever ones close in first when you're backing off and would be out of reach by the time the hit lands. Feints and steps are dropped when you're mid-swing or turned away.
- Chains: the small step between attacks only happens sometimes, mostly when repeating the same attack. Parried attacks get used less.
- Chargers too close for their charge back off to run-up distance and charge again (the clever ones nearly always).
- Flyers weave in front of you instead of circling and dive when ready. Big ones (Moder) make wide, slow passes, climbing into each turn.
- Chases: calm and timid creatures give up sooner (as little as a third of vanilla's time), or after a few seconds with no path to you. The hateful ones keep chasing. Not mid-fight, not raids or bosses.
- Pace, chains and chases follow the pack's personality as well as their own.
- Sight: each creature sees a little further or less than its kind, independent of its other traits.
- Lost targets are searched for spread out, clever ones ahead along where you were heading.
- No path: they try a way round first. Out of reach above them: clever melee ones wait out of your sight.
- Shot from out of sight: only the really clever know where from; the rest only know roughly which way.
- Bosses: past half way they stop circling and keep to your front. Moder's passes get tighter and lower as she weakens.
- Fixed: lag with many small creatures at once (e.g. the Queen's brood).

## 0.2.0
- Attack chains: aggressive, confident enemies can go straight into another attack instead of attack, pause, attack, up to 4 in a row.
- Between chained attacks they take a very small step, and switch to a different attack when they have one.
- Melee attacks that land get used more, ones that miss less.
- Smart enemies of the same kind nearby learn from it too.
## 0.1.2
- Wire's Aldr gives its extra leader star to the pack's own leader.

## 0.1.1
- Balance: enemies are far less likely to run away. Fleeing now needs a real reason (badly hurt, outnumbered or truly scared), and anger cancels it.
- Balance: enemies can't tell how strong you are. Your weapon and health no longer make them doubt themselves, so strong enemies stay confident, even trolls when you're in the Ashlands. Body size still counts.
- Balance: getting hit or losing an ally now causes much more rage and much less fear. Losing the pack leader shakes them half as much.
- Balance: fear fades faster (5 s instead of 8), weighs less when choosing to press in, and spreads less from pack members who run. Lower fear sensitivity for all species.

## 0.1.0
- First upload. Feature complete, not fully tested in real play yet.
