using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        // The yellow "damage taken" trail on a nameplate shown by our own
        // reveals (a hit or a parry) rather than by the game.
        //
        // The game hides a nameplate whose hover timer has run out, every
        // frame; our reveal turned it back on every frame, and each time a
        // bar is switched on the game snaps its yellow trail to the current
        // health. Now, on the hit itself, the game's own hover timer is set
        // so the game keeps the nameplate shown for the reveal: no more
        // switching off and on. A nameplate that was hidden (or created by
        // the hit) gets its yellow trail set to the health before the hit,
        // once, and the game's normal draining takes it from there.
        // Nothing here runs per frame: only on hits, parries and creation.

        // Looked up on first use (static fields in other files of this
        // class may not be set up yet when this one is).
        private static FieldInfo _hoverTimerField;
        private static FieldInfo HoverTimerField
        {
            get
            {
                if (_hoverTimerField == null && HudDataType != null)
                    _hoverTimerField = AccessTools.Field(HudDataType, "m_hoverTimer");
                return _hoverTimerField;
            }
        }

        private static readonly AccessTools.FieldRef<GuiBar, float> SlowSmoothValue =
            AccessTools.FieldRefAccess<GuiBar, float>("m_smoothValue");
        private static readonly AccessTools.FieldRef<GuiBar, float> SlowDelayTimer =
            AccessTools.FieldRefAccess<GuiBar, float>("m_delayTimer");
        private static readonly AccessTools.FieldRef<GuiBar, float> SlowBarWidth =
            AccessTools.FieldRefAccess<GuiBar, float>("m_width");

        private static void RememberTrailBeforeHit(
            HealthSample sample, Character target,
            float previousHp, float currentHp, float now)
        {
            if (sample == null || target == null ||
                currentHp <= 0f || previousHp <= currentHp)
                return;

            float max = target.GetMaxHealth();
            if (max <= 0f)
                return;

            sample.TrailBeforeShare = Mathf.Clamp01(previousHp / max);
            sample.TrailSeedPending = true;
            sample.TrailSeedUntil = now + DamageShowSeconds;
            Instance?.RevealOnHit(target, sample);
        }

        // Keep the game itself showing this nameplate for the next
        // `seconds` (it shows nameplates whose hover timer is below its
        // show duration). Never shortens a longer time the game already has.
        private static void KeepShownByGame(EnemyUiState state, float seconds)
        {
            EnemyHud hud = EnemyHud.instance;
            if (hud == null || state == null || state.HudRecord == null ||
                HoverTimer == null || HoverTimerField == null)
                return;
            float wanted = hud.m_hoverShowDuration - seconds;
            if (HoverTimer(state.HudRecord) > wanted)
                HoverTimerField.SetValue(state.HudRecord, wanted);
        }

        // On a confirmed hit (and once when a hit creates the nameplate).
        private void RevealOnHit(Character c, HealthSample sample)
        {
            EnemyUiState state;
            if (c == null || sample == null || !_states.TryGetValue(c, out state) ||
                state == null || state.Gui == null || state.Kind != HudKind.Enemy)
                return; // no nameplate yet: done when the reveal creates it

            KeepShownByGame(state, DamageShowSeconds);

            if (!sample.TrailSeedPending)
                return;
            sample.TrailSeedPending = false;
            if (Time.unscaledTime > sample.TrailSeedUntil || c.GetHealth() <= 0f)
                return;

            if (!state.Gui.activeSelf)
            {
                ApplyLevelStars(state, c);
                state.Gui.SetActive(true); // the game snaps the bars here
            }

            Transform slowTransform = state.HealthRect != null
                ? state.HealthRect.Find("health_slow") : null;
            GuiBar slow = slowTransform != null ? slowTransform.GetComponent<GuiBar>() : null;
            if (slow == null || !slow.m_smoothDrain || slow.m_bar == null)
                return;

            float current = c.GetHealthPercentage();
            float old = sample.TrailBeforeShare;
            if (old <= current + 0.002f)
                return;
            // Already trailing (it was on screen): the game handles it.
            if (slow.GetSmoothValue() > current + 0.002f)
                return;

            SlowSmoothValue(slow) = old;
            SlowDelayTimer(slow) = Mathf.Max(0f, slow.m_changeDelay);
            slow.m_bar.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, SlowBarWidth(slow) * old);
        }

        // A parry reveal: the game keeps showing it too.
        private void RevealOnParry(Character c)
        {
            EnemyUiState state;
            if (c != null && _states.TryGetValue(c, out state) &&
                state != null && state.Kind == HudKind.Enemy)
                KeepShownByGame(state, DamageShowSeconds);
        }
    }
}
