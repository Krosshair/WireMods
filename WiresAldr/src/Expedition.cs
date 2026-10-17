using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Expedition: session-only, per-player XP bonus that grows linearly
    // with active time away from bases. Active = earning any skill XP.
    // Uses vanilla's own base detection (the
    // same player "base value" raids use, refreshed by vanilla every ~2 s),
    // and runs on that vanilla refresh, so there is no extra Update loop.
    //  - The bonus only applies off-base. At a base it fades gradually
    //    instead of resetting, so a short rest at home keeps most of it.
    //  - Standing around off-base slowly loses it. Death ends it.
    internal static class Expedition
    {
        private static float bonus;           // current bonus in percent
        private static float lastXp = -999f;  // last skill XP earned away from a base
        private static float lastTick = -1f;
        private static bool atBase;
        private static int lastMilestone;

        private const float ActiveWindow = 15f;      // XP within this many seconds = active
        private const float IdleGrace = 90f;         // no XP off-base for this long starts a slow fade
        private const float BaseFadeFactor = 1.25f;  // fades a bit faster at base than it grows

        private static float PerMinute => Plugin.VenturePerKm != null ? Plugin.VenturePerKm.Value : 0f;
        private static float Cap => Plugin.VentureCap != null ? Plugin.VentureCap.Value : 0f;
        internal static bool Enabled => PerMinute > 0f && Cap > 0f &&
            Plugin.Enabled != null && Plugin.Enabled.Value && Plugin.XpEnabled.Value;
        internal static bool AtBase => atBase;
        internal static float StoredPercent => bonus;
        internal static float ActivePercent => atBase ? 0f : bonus;

        internal static void Reset(string why)
        {
            if (bonus > 0f && AldrDiagnostics.Active)
                AldrDiagnostics.Log("EXPEDITION", "reset reason=" + why +
                    " lost_percent=" + bonus.ToString("0.#"));
            bonus = 0f; lastXp = -999f; lastTick = -1f; atBase = false; lastMilestone = 0;
        }

        private static void Fade(float seconds, float scale)
        {
            bonus = Mathf.Max(0f, bonus - seconds / 60f * PerMinute * BaseFadeFactor * scale);
        }

        internal static void Tick(Player p)
        {
            if (!Enabled || !p || p.IsDead()) return;
            float now = Time.time;
            float dt = lastTick < 0f ? 2f : Mathf.Clamp(now - lastTick, 0f, 10f);
            lastTick = now;

            bool home = p.GetBaseValue() >= 3; // same threshold vanilla raids use
            if (home != atBase)
            {
                atBase = home;
                if (AldrDiagnostics.Active)
                    AldrDiagnostics.Log("EXPEDITION", (home ? "entered_base" : "left_base") +
                        " bonus_percent=" + bonus.ToString("0.#"));
            }

            if (home)
                Fade(dt, 1f);
            else if (now - lastXp <= ActiveWindow)
                // Active expedition time: earning XP away from a base.
                bonus = Mathf.Min(Cap, bonus + dt / 60f * PerMinute);
            else if (now - lastXp > IdleGrace)
                Fade(dt, 0.5f);
            Milestone();
        }

        // Any skill XP earned away from a base makes this moment active.
        internal static void ObserveXp()
        {
            if (Enabled && !atBase) lastXp = Time.time;
        }

        internal static float Multiplier()
        {
            if (!Enabled || atBase) return 1f;
            return 1f + bonus / 100f;
        }

        private static void Milestone()
        {
            if (!AldrDiagnostics.Active) return;
            int m = Mathf.FloorToInt(bonus / 5f); // log every 5 percentage points
            if (m == lastMilestone) return;
            lastMilestone = m;
            AldrDiagnostics.Log("EXPEDITION", "bonus_percent=" + bonus.ToString("0.#") + " at_base=" + atBase);
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
                Expedition.Tick(__instance);
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
