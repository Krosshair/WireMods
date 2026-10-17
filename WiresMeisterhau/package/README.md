# \### Wire's Meisterhau



\# Counterattacks and attack interrupting in Valheim!
YouTube video explaining the mod; https://youtu.be/Ub90k9mEJZM



A small combat mod adding an offensive parrying system to the game, turning your opponents attacks against them.

## \## How the meisterhau works;

Hit an enemy during its vanilla parry window for melee attacks, or during the wind-up before a ranged attack, to trigger a Meisterhau.



A successful Meisterhau:

1\. deals your normal 1× attack damage staggers the enemy in the same way as a vanilla blocking parry grants 3× normal weapon XP

2\. skips your next melee attack straight to the final hit of the weapon’s combo, allowing for big damage



\# How to

Think of it a bit like parrying in Elden Ring: you have to anticipate the attack.



* With melee weapons, start your attack early enough to hit the enemy during its counter window. Keep holding the attack button and, if the Meisterhau succeeds, your combo jumps straight to the final attack for a heavy follow-up while the enemy is staggered.



* With ranged weapons, simply land a shot during the enemy’s counter window to interrupt and stagger them.



Enemy melee attack counter windows are just the parry windows from vanilla.

Enemy ranged attack counter windows are the the windup time of the enemy before launching the projectile (Skeleton pulling the bow before shooting, for example).



\## The math



Vanilla defensive parries:

(weapon or shield block power \* parry bonus) and scaled with the "blocking" skill (Skill at 100 means +50%)



Melee offensive parries:

((weapon block power \* parry bonus)\*1.5x) and scaled with weapon skill, scaling math is identical to vanilla's blocking skill (Skill at 100 means +50%)



\# Ranged attack interrupting:
(ranged weapon damage \* 0.5) and scaled with the weapon skill, scaling math is identical to vanilla's blocking skill (Skill at 100 means +50%)



MAGIC CANNOT PARRY OR INTERRUPT!



Donations: ko-fi.com/krosshair
## Multiplayer

Wire's Meisterhau is a gameplay mod and is required on both the server and every client.
A client with Meisterhau cannot join a server that does not have Meisterhau installed, and a Meisterhau server rejects clients without the required version.

Gameplay config is server-authoritative. The server's Meisterhau settings are synchronized to connecting clients and locked for normal clients while connected.
Enemy ranged-attack windups are synchronized from the peer that owns the enemy, so ranged interrupts use the real owner-side attack state in multiplayer.

