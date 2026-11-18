namespace WiresAI
{
    // Every value in Wire's AI that was picked for the game rather than taken from Valheim's own data
    // or a source, in one place, each with why. Values from the game (reach, speeds, intervals,
    // animation timings, camera field of view) are read where they're used, not copied here.
    //
    // How the mod is laid out, one stage feeding the next:
    //   Perception (Perception.cs): what a creature knows. Its sight, noticing what's right by it,
    //     how often it looks, how it reads your movement, its small misjudgements. Nothing later
    //     reads your exact position or input around this.
    //   Mind (Individual.cs, Mind.cs): who it is. Body and personality, its pack's personality mixed
    //     in, mood (rage, fear, confidence), and the three knobs everything reads: Pressure, Wits, Nerve.
    //   Decisions (Decisions.cs, Chase.cs, Maneuver.cs): on each look. Posture, tactics, giving up, searching,
    //     getting unstuck, ways round.
    //   The moment (Moment.cs, Chains.cs): attacking first, early swings, closing in, chains, feints,
    //     openings, and the pack's turns at you (Turns.cs).
    //   Movement (Movement.cs, Maneuver.cs): straight chase, maneuvers, run-ups, flyers.
    internal static class Tune
    {
        // ---- Perception ----
        // Looking for a target, by the nearest player's distance (m) -> seconds between looks. Vanilla:
        // 2 s within 50 m, else 6. Faster up close to react in time, slower far off to even out the cost.
        internal const float LookNear = 10f, LookMid = 25f, LookFar = 50f;
        internal const float LookNearEvery = .5f, LookMidEvery = 1f, LookFarEvery = 3f, LookAwayEvery = 8f;
        // Noticed whichever way it faces within this (m): about two arm's lengths, heard and felt.
        internal const float Feel = 3f;
        // Reading your movement: what you've kept doing over this long (s). Long enough to ignore a tap,
        // short enough to see a real turn.
        internal const float ReadTime = .4f;
        // Misjudging an attack moment, as a share of its reach: clever to dim. About a player's 5%.
        internal const float JudgeClever = .03f, JudgeDim = .07f;
        // Sight: up to this much better or worse than its kind.
        internal const float SightSpread = .25f;
        // "Really clever" (wits above this on -1..1): works out where an unseen shot came from, hides.
        internal const float ReallyClever = .5f;

        // ---- Decisions ----
        // Repositioning considered this often (s), however often it looks: vanilla's own look rate.
        internal const float TacticsEvery = 2f;
        // Giving up a chase: the calmest give up in a third of vanilla's time (BetterEnemyAI's 10 s of
        // vanilla's 30); above this aggression they keep vanilla's; never within this long (s) of a hit
        // either way (BetterEnemyAI's 5 s).
        internal const float GiveUpShortest = 1f / 3f, Hateful = .5f, MidFight = 5f;
        // Looks in a row with no path before giving up (about BetterEnemyAI's 6 s at 2 s looks).
        internal const int NoPathLooks = 3;
        // Lost you: steps it follows the way you went before vanilla's wander.
        internal const int SearchSteps = 3;
        // Stuck: moved and closed less than this share of what running would cover since the last look.
        internal const float StuckShare = .25f;
        // Hiding from someone up high (s) before looking for a spot again.
        internal const float HideFor = 4f;

        // ---- The moment ----
        // Not in reach for this long (s) before: it just got there, and attacks at once.
        internal const float Arrived = .5f;
        // Not chaining: the next attack is ready this much sooner (share of its interval), plus this
        // much more for full pressure. Vanilla waits the whole interval.
        internal const float HurryBase = .3f, HurryPressure = .6f;
        // Chains: every creature this much likelier to start one than its aggression alone says.
        internal const float ChainBase = .15f;
        // The small step between chained attacks: usual when repeating itself, now and then when switching.
        internal const float StepSame = .75f, StepSwitch = .25f;
        // Within this (s) of reach: runs straight at you, no leading.
        internal const float StraightWithin = .5f;

        // ---- Pack turns (Turns.cs) ----
        // How many of a pack attack one target at once: this many, one more for an aggressive pack,
        // one fewer for a clever one (they hold back and step in when there's room); at least one.
        // Doom (2016) did the same with attack tokens.
        internal const int TurnsBase = 2;
        // "Aggressive pack" and "clever" for that: pack aggression and wits above these.
        internal const float TurnsAggressive = .3f, TurnsClever = .65f, TurnsTimid = .3f; // and "timid": nerve below this
        // Angry enough not to wait for anyone (rage 0..1).
        internal const float TurnsRage = .6f;
        // Waiting its turn this far (m) past its reach: one step in.
        internal const float TurnsMargin = .5f;
    }
}
