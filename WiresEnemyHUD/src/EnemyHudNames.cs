using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Names and their colors.
    public sealed partial class EnemyHudReworkPlugin
    {
        // The game turns its awareness icons on and off itself every frame.
        // Switching them off after it would make the UI rebuild each time,
        // so instead their pictures are made invisible once, and the game
        // can switch the empty objects as much as it likes.
        private static void SetAwarenessIconsHidden(EnemyUiState state, bool hidden)
        {
            if (state.IconsHidden == hidden) return;
            state.IconsHidden = hidden;
            if (state.IconGraphics == null)
            {
                var list = new List<Graphic>();
                if (state.AlertedIcon != null) list.AddRange(state.AlertedIcon.GetComponentsInChildren<Graphic>(true));
                if (state.AwareIcon != null) list.AddRange(state.AwareIcon.GetComponentsInChildren<Graphic>(true));
                state.IconGraphics = list.ToArray();
            }
            foreach (Graphic g in state.IconGraphics)
                if (g != null) g.enabled = !hidden;
        }

        private void UpdateStatusName(EnemyUiState state, bool isEnemy)
        {
            if (state == null || state.Kind != HudKind.Enemy)
                return;

            if (state.NameText != null)
            {
                // The creature's name as the game has it now: a tame renamed shows its new name
                // (the game writes it every frame from GetHoverName; the one kept from when the
                // nameplate was made was written over it). Localized only when it changes. The
                // debug boss keeps the name it was given.
                if (!state.IsDebug && state.Character != null && Localization.instance != null)
                {
                    string raw = state.Character.GetHoverName();
                    if (!string.Equals(raw, state.RawName, System.StringComparison.Ordinal))
                    {
                        state.RawName = raw;
                        state.OriginalNameText = Localization.instance.Localize(raw);
                    }
                }
                string source = state.OriginalNameText ?? state.NameText.text ?? "";
                string wanted = source;
                if (_uppercaseNames.Value)
                {
                    if (!ReferenceEquals(state.UpperSource, source))
                    {
                        state.UpperSource = source;
                        state.UpperValue = source.ToUpperInvariant();
                    }
                    wanted = state.UpperValue;
                }
                if (!string.Equals(state.NameText.text, wanted, System.StringComparison.Ordinal))
                    state.NameText.text = wanted;
            }

            if (state.NameGraphic == null)
                return;

            // Icons: vanilla shows and hides them itself.
            if (state.Ai == null || _nameColorMode.Value == NameColorMode.Icons)
            {
                SetAwarenessIconsHidden(state, false);
                state.NameGraphic.color = state.NormalNameColor;
                return;
            }

            SetAwarenessIconsHidden(state, true);

            if (_nameColorMode.Value == NameColorMode.None)
            {
                state.NameGraphic.color = state.NormalNameColor;
                return;
            }

            bool pastel = _nameColorMode.Value == NameColorMode.Pastel;
            Color alertedColor;
            Color awareColor;

            if (isEnemy)
            {
                alertedColor = (pastel
                    ? new Color32(0xFF, 0x7A, 0x76, 255)
                    : new Color32(0xF2, 0x10, 0x0C, 255));
                awareColor = (pastel
                    ? new Color32(0xFF, 0xE5, 0x80, 255)
                    : new Color32(0xFE, 0xDB, 0x00, 255));
            }
            else
            {
                alertedColor = (pastel
                    ? new Color32(0x83, 0xE5, 0xA7, 255)
                    : new Color32(0x18, 0xC9, 0x62, 255));
                awareColor = (pastel
                    ? new Color32(0x86, 0xBE, 0xFF, 255)
                    : new Color32(0x1E, 0x86, 0xFF, 255));
            }

            if (state.Ai.IsAlerted())
                state.NameGraphic.color = alertedColor;
            else if (state.Ai.HaveTarget())
                state.NameGraphic.color = awareColor;
            else
                state.NameGraphic.color = state.NormalNameColor;
        }

        private void ApplyBossNameColor(EnemyUiState state)
        {
            if (state == null || state.Kind != HudKind.Boss || state.NameGraphic == null)
                return;

            switch (_bossNameColorMode.Value)
            {
                case BossNameColorMode.White:
                    state.NameGraphic.color = Color.white;
                    return;

                case BossNameColorMode.Vanilla:
                    state.NameGraphic.color = state.NormalNameColor;
                    return;
            }

            Color color = new Color32(0xFF, 0x5A, 0xA7, 255);

            // The bar is looked up once, not every frame.
            if (!state.BossFastLooked && state.HealthRect != null)
            {
                state.BossFastLooked = true;
                Transform fastTransform = state.HealthRect.Find("health_fast");
                state.BossFastBar = fastTransform != null ? fastTransform.GetComponent<GuiBar>() : null;
            }

            if (state.BossFastBar != null)
            {
                Color sampled = state.BossFastBar.GetColor();
                bool nearlyWhite = sampled.r > 0.95f && sampled.g > 0.95f && sampled.b > 0.95f;
                if (!nearlyWhite) color = sampled;
            }

            color.a = state.NormalNameColor.a;
            state.NameGraphic.color = color;
        }
    }
}
