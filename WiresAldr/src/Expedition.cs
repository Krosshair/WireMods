using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Expedition: session-only, per-player XP bonus for adventuring away
    // from bases. Uses vanilla's own base detection (the player "base value"
    // raids use) on vanilla's ~2 s refresh, so no extra Update loop.
    // The expedition clock (minutes) only runs on XP: each XP event away
    // from a base credits the time since the previous one (up to 20 s),
    // sped up in more dangerous biomes. The bonus grows with the clock and
    // accelerates: bonus = a*T + b*T^1.5, so long adventures pay off hugely.
    // What kind or how much XP doesn't matter, only that you are doing things.
    internal static class Expedition
    {
        private static float clock;            // expedition minutes
        private static float lastXp = -999f;
        private static float lastTick = -1f;
        private static int biomeTier = 1;
        private static bool atBase, resting;
        private static float lastTravel = -999f;  // sailing/riding: freezes fading, builds nothing
        private static float lastStatus = -999f;
        private static int lastMilestone;

        private const float MaxCredit = 30f;      // seconds credited per XP event
        private const float IdleGrace = 90f;      // halved while resting
        private const float IdleFade = 0.5f;      // clock minutes lost per minute, off-base idle
        private const float BaseFade = 2f;        // clock minutes lost per minute at a base
        private const float Curve = 0.067f;       // the T^1.5 part: Swamp, 30 min ~ +20%

        private static readonly float[] TierPace = { 0.8f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f };
        private static float Pace => TierPace[Mathf.Clamp(biomeTier, 0, TierPace.Length - 1)];

        private static float PerMinute => Plugin.VenturePerKm != null ? Plugin.VenturePerKm.Value : 0f;
        internal static bool Enabled => PerMinute > 0f &&
            Plugin.Enabled != null && Plugin.Enabled.Value && Plugin.XpEnabled.Value;
        internal static bool AtBase => atBase;
        internal static float StoredPercent => PerMinute * clock + Curve * clock * Mathf.Sqrt(clock);
        internal static float ActivePercent => atBase ? 0f : StoredPercent;
        internal static float ClockMinutes => clock;

        internal static void Reset(string why)
        {
            if (clock > 0f && AldrDiagnostics.Active)
                AldrDiagnostics.Log("EXPEDITION", "reset reason=" + why + " lost_percent=" + StoredPercent.ToString("0.#"));
            clock = 0f; lastXp = -999f; lastTick = -1f; lastTravel = -999f; atBase = false; resting = false; lastMilestone = 0;
        }

        private static void Fade(float seconds, float perMinute)
        {
            clock = Mathf.Max(0f, clock - seconds / 60f * perMinute * (resting ? 2f : 1f));
        }

        internal static void Tick(Player p)
        {
            if (!Enabled || !p) return;
            float now = Time.time;
            float dt = lastTick < 0f ? 2f : Mathf.Clamp(now - lastTick, 0f, 10f);
            lastTick = now;
            if (p.IsDead() || p.IsTeleporting()) return;

            resting = p.InBed() || p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectResting);
            bool home = p.GetBaseValue() >= 3; // same threshold vanilla raids use
            if (home != atBase)
            {
                atBase = home;
                if (AldrDiagnostics.Active)
                    AldrDiagnostics.Log("EXPEDITION", (home ? "entered_base" : "left_base") +
                        " bonus_percent=" + StoredPercent.ToString("0.#"));
            }
            if (home) Fade(dt, BaseFade);
            else
            {
                // Sailing or riding to the next island isn't idling: hold the bonus.
                if (p.GetControlledShip() != null || p.IsAttachedToShip() ||
                    p.GetStandingOnShip() != null || p.IsRiding())
                    lastTravel = now;
                float lastActive = Mathf.Max(lastXp, lastTravel);
                if (now - lastActive > (resting ? IdleGrace * 0.5f : IdleGrace)) Fade(dt, IdleFade);
            }
            Milestone();
            if (AldrDiagnostics.Active && now - lastStatus > 120f && clock > 0f)
            {
                lastStatus = now;
                AldrDiagnostics.Log("EXPEDITION", "status bonus_percent=" + StoredPercent.ToString("0.#") +
                    " clock_min=" + clock.ToString("0.#") + " at_base=" + atBase + " resting=" + resting +
                    " seconds_since_xp=" + (now - lastXp).ToString("0"));
            }
        }

        internal static void ObserveXp(int currentBiomeTier)
        {
            if (!Enabled || atBase) return;
            float now = Time.time;
            biomeTier = Mathf.Max(1, currentBiomeTier);
            if (lastXp > 0f) clock += Mathf.Min(MaxCredit, now - lastXp) / 60f * Pace;
            lastXp = now;
            Milestone();
        }

        internal static float Multiplier()
        {
            if (!Enabled || atBase) return 1f;
            return 1f + StoredPercent / 100f;
        }

        private static void Milestone()
        {
            if (!AldrDiagnostics.Active) return;
            int m = Mathf.FloorToInt(StoredPercent / 5f);
            if (m == lastMilestone) return;
            lastMilestone = m;
            AldrDiagnostics.Log("EXPEDITION", "bonus_percent=" + StoredPercent.ToString("0.#") +
                " clock_min=" + clock.ToString("0.#") + " at_base=" + atBase + " resting=" + resting + " biome_tier=" + biomeTier);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateBaseValue")]
    internal static class ExpeditionTick
    {
        private static readonly AccessTools.FieldRef<Player, float> Timer =
            AccessTools.FieldRefAccess<Player, float>("m_baseValueUpdateTimer");
        private static void Postfix(Player __instance)
        {
            // Vanilla resets its timer to 0 when it refreshes the base value.
            if (__instance == Player.m_localPlayer && Timer(__instance) == 0f)
            {
                Accomplishments.Tick(__instance);
                Expedition.Tick(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class ExpeditionDeath
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) Expedition.Reset("death");
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class ExpeditionWorldStart
    {
        private static void Postfix() => Expedition.Reset("world_load");
    }
}
