using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Expedition: per-character XP bonus for adventuring away
    // from bases. Uses vanilla's own base detection (the player "base value"
    // raids use) on vanilla's ~2 s refresh, so no extra Update loop.
    // The expedition clock (minutes) runs on XP: each XP event away from a
    // base credits the time since the previous one (up to 30 s), sped up in
    // more dangerous biomes. A ship under way adds a fifth of that pace. The bonus grows with the clock and
    // accelerates: bonus = a*T + b*T^1.5, so long adventures pay off hugely.
    // What kind or how much XP doesn't matter, only that you are doing things
    // while actually out roaming: the clock only grows while you have moved
    // at least 20 m within the last 6 minutes, so camping one spot (an AFK
    // run into a wall, a trapped-creature farm) never builds it. The bonus
    // you already have still applies there. Saved on the character (logging
    // out keeps it); death resets it.
    internal static class Expedition
    {
        private static float clock;            // expedition minutes
        private static float lastXp = -999f;
        private static float lastTick = -1f;
        private static int biomeTier = 1;
        private static bool atBase, resting;
        private static float lastTravel = -999f;  // sailing/riding: freezes fading
        private static float lastStatus = -999f;
        private static int lastMilestone;
        private static Player owner;              // character the clock belongs to
        private static Vector3 roamAnchor;
        private static Vector3 lastTickPosition;
        private static float lastRoam = -999f;    // last time you moved RoamRadius from the anchor
        private const string SaveKey = "Wire.Aldr.Expedition";

        // Tunable in the config (04 - Expedition).
        private static float RoamRadius => Plugin.ExpeditionRoamRadius.Value;
        private static float RoamWindow => Plugin.ExpeditionRoamMinutes.Value * 60f;
        private static float MaxCredit => Plugin.ExpeditionCredit.Value;
        private static float IdleGrace => Plugin.ExpeditionIdleGrace.Value;   // halved while resting
        private static float IdleFade => Plugin.ExpeditionIdleDecay.Value;    // clock minutes per minute, off-base idle
        private static float BaseFade => Plugin.ExpeditionBaseDecay.Value;    // clock minutes per minute at a base
        private static float Curve => Plugin.ExpeditionCurve.Value;           // the T^1.5 part
        private static float SailShare => Plugin.ExpeditionSailShare.Value / 100f;
        private const float SailMinSpeed = 2f;    // m/s: actually under way, not drifting at anchor

        private static readonly float[] TierPace = { 0.8f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f };
        private static float Pace => TierPace[Mathf.Clamp(biomeTier, 0, TierPace.Length - 1)];

        private static float PerMinute => Plugin.VenturePerKm != null ? Plugin.VenturePerKm.Value : 0f;
        internal static bool Enabled => Plugin.ExpeditionEnabled != null && Plugin.ExpeditionEnabled.Value &&
            Plugin.Enabled != null && Plugin.Enabled.Value && Plugin.XpEnabled.Value;
        internal static bool AtBase => atBase;
        internal static float Minutes => clock;
        // Bonus % = linear x T + curve x T^1.5.
        internal static float StoredPercent => PerMinute * clock + Curve * clock * Mathf.Sqrt(clock);
        private static bool Roaming => Time.time - lastRoam <= RoamWindow;
        internal static float ActivePercent => atBase ? 0f : StoredPercent;

        internal static void Reset(string why)
        {
            if (clock > 0f && AldrDiagnostics.Active)
                AldrDiagnostics.Log("EXPEDITION", "reset reason=" + why + " lost_percent=" + StoredPercent.ToString("0.#"));
            Clear();
            if (owner && owner.m_customData != null) owner.m_customData.Remove(SaveKey);
        }

        // New world or character: forget the in-memory state; the clock is
        // read back from the character on its first refresh.
        internal static void Clear()
        {
            clock = 0f; lastXp = -999f; lastTick = -1f; lastTravel = -999f; atBase = false; resting = false; lastMilestone = 0;
            lastRoam = -999f;
        }

        private static void Ensure(Player p)
        {
            if (!p || p == owner) return;
            owner = p;
            Clear();
            string raw;
            float saved;
            if (p.m_customData != null && p.m_customData.TryGetValue(SaveKey, out raw) &&
                float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out saved) &&
                saved > 0f && saved < 100000f)
                clock = saved;
            lastMilestone = Mathf.FloorToInt(StoredPercent / 5f);
            if (clock > 0f && AldrDiagnostics.Active)
                AldrDiagnostics.Log("EXPEDITION", "restored bonus_percent=" + StoredPercent.ToString("0.#") +
                    " clock_min=" + clock.ToString("0.#"));
        }

        // Written into the character save by vanilla Player.Save.
        internal static void Save(Player p)
        {
            if (!p || p != owner || p.m_customData == null) return;
            if (clock > 0.01f) p.m_customData[SaveKey] = clock.ToString("0.###", CultureInfo.InvariantCulture);
            else p.m_customData.Remove(SaveKey);
        }

        private static void Fade(float seconds, float perMinute)
        {
            clock = Mathf.Max(0f, clock - seconds / 60f * perMinute * (resting ? 2f : 1f));
        }

        internal static void Tick(Player p)
        {
            if (!Enabled || !p) return;
            Ensure(p);
            float now = Time.time;
            float dt = lastTick < 0f ? 2f : Mathf.Clamp(now - lastTick, 0f, 10f);
            lastTick = now;
            if (p.IsDead() || p.IsTeleporting()) return;

            // Roaming: moved RoamRadius from the last anchor (portals count).
            Vector3 position = p.transform.position;
            if (lastRoam < -998f || (position - roamAnchor).sqrMagnitude >= RoamRadius * RoamRadius)
            {
                roamAnchor = position;
                lastRoam = now;
            }

            resting = p.InBed() || p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectResting);
            bool home = p.GetBaseValue() >= 3; // same threshold vanilla raids use
            if (home != atBase)
            {
                atBase = home;
                // Time spent at base is never credited to the next XP event.
                if (!home && lastXp > 0f) lastXp = now;
                if (AldrDiagnostics.Active)
                    AldrDiagnostics.Log("EXPEDITION", (home ? "entered_base" : "left_base") +
                        " bonus_percent=" + StoredPercent.ToString("0.#"));
            }
            if (home) Fade(dt, BaseFade);
            else
            {
                // Sailing or riding to the next island isn't idling: hold the
                // bonus. A ship under way also builds it, slowly.
                Ship ship = p.GetControlledShip();
                if (ship == null) ship = p.GetStandingOnShip();
                if (ship != null || p.IsAttachedToShip() || p.IsRiding())
                    lastTravel = now;
                // Riding counts like any XP event at full pace (vanilla only
                // gives Ride XP at a gallop), as long as the mount is moving.
                if (p.IsRiding() && (position - lastTickPosition).sqrMagnitude >= 4f)
                    Credit(DynamicExperience.BiomeTier(p.GetCurrentBiome()), now);
                if (ship != null && Roaming && Mathf.Abs(ship.GetSpeed()) >= SailMinSpeed)
                {
                    biomeTier = Mathf.Max(1, DynamicExperience.BiomeTier(p.GetCurrentBiome()));
                    clock += dt / 60f * Pace * SailShare;
                }
                float lastActive = Mathf.Max(lastXp, lastTravel);
                if (now - lastActive > (resting ? IdleGrace * 0.5f : IdleGrace)) Fade(dt, IdleFade);
            }
            lastTickPosition = position;
            Milestone();
            if (AldrDiagnostics.Active && now - lastStatus > 120f && clock > 0f)
            {
                lastStatus = now;
                AldrDiagnostics.Log("EXPEDITION", "status bonus_percent=" + StoredPercent.ToString("0.#") +
                    " clock_min=" + clock.ToString("0.#") + " at_base=" + atBase + " resting=" + resting +
                    " seconds_since_xp=" + (now - lastXp).ToString("0") + " roaming=" + Roaming);
            }
        }

        internal static void ObserveXp(int currentBiomeTier)
        {
            if (!Enabled) return;
            Ensure(Player.m_localPlayer);
            if (atBase) return;
            Credit(currentBiomeTier, Time.time);
            Milestone();
        }

        private static void Credit(int currentBiomeTier, float now)
        {
            biomeTier = Mathf.Max(1, currentBiomeTier);
            if (lastXp > 0f && Roaming) clock += Mathf.Min(MaxCredit, now - lastXp) / 60f * Pace;
            lastXp = now;
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
        private static void Postfix() => Expedition.Clear();
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class ExpeditionSave
    {
        private static void Prefix(Player __instance) => Expedition.Save(__instance);
    }
}
