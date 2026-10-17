using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        // Empty = use the original Valheim UI color. Only user-supplied,
        // valid hexadecimal colors override the existing bar images.
        private ConfigEntry<string> _colorHealth;
        private ConfigEntry<string> _colorPlayer;
        private ConfigEntry<string> _colorEnemy;
        private ConfigEntry<string> _colorFriendly;
        private ConfigEntry<string> _colorStagger;
        private ConfigEntry<string> _colorRecovery;

        private sealed class NativePalette
        {
            public Image Health;
            public Image Friendly;
            public Image Stagger;
            public Image Recovery;
            public Color HealthOriginal;
            public Color FriendlyOriginal;
            public Color StaggerOriginal;
            public Color RecoveryOriginal;
            public bool HealthColored;
            public bool FriendlyColored;
            public bool StaggerColored;
            public bool RecoveryColored;
        }

        private readonly Dictionary<EnemyUiState, NativePalette>
            _nativePalettes = new Dictionary<EnemyUiState, NativePalette>();

        private void BindColorConfig()
        {
            _colorHealth = ColorEntry("Health bars",
                "Default health-fill color for enemies, bosses, players, and friendly creatures. Specific bar color settings take priority.", 600);
            _colorPlayer = ColorEntry("Player health bars",
                "Health-fill color for multiplayer player nameplates.", 500);
            _colorEnemy = ColorEntry("Enemy health bars",
                "Health-fill color for hostile enemy nameplates.", 400);
            _colorFriendly = ColorEntry("Friendly health bars",
                "Health-fill color for friendly and tamed creature nameplates.", 300);
            _colorStagger = ColorEntry("Stagger bar",
                "Fill color of the enemy stagger meter before and during a stagger.", 200);
            _colorRecovery = ColorEntry("Recovery overlay",
                "Color of the recovery layer that shrinks across the stagger meter while an enemy recovers.", 100);
        }

        private ConfigEntry<string> ColorEntry(
            string name, string description, int order)
        {
            return Config.Bind("Colors", name, string.Empty,
                new ConfigDescription(
                    description + " Enter a hex color such as #E75D16 or #E75D16FF. Leave empty for the original color.",
                    null,
                    new ConfigurationManagerAttributes { Order = order }));
        }

        private static Image GetFillImage(RectTransform fill)
        {
            if (fill == null) return null;
            var bar = fill.GetComponent<GuiBar>();
            if (bar != null && bar.m_bar != null)
                return bar.m_bar.GetComponent<Image>();
            return fill.GetComponent<Image>();
        }

        private void CaptureNativePalette(EnemyUiState state)
        {
            if (state == null) return;
            var original = new NativePalette();
            original.Health = GetFillImage(state.HealthFastFill);
            original.Friendly = GetFillImage(state.HealthFriendlyFill);
            original.Stagger = state.StaggerFast != null &&
                state.StaggerFast.m_bar != null
                ? state.StaggerFast.m_bar.GetComponent<Image>() : null;
            original.Recovery = state.RecoveryOverlay;

            if (original.Health != null)
                original.HealthOriginal = original.Health.color;
            if (original.Friendly != null)
                original.FriendlyOriginal = original.Friendly.color;
            if (original.Stagger != null)
                original.StaggerOriginal = original.Stagger.color;
            if (original.Recovery != null)
                original.RecoveryOriginal = original.Recovery.color;

            _nativePalettes[state] = original;
        }

        private static bool ParseHexColor(string input, out Color result)
        {
            result = default(Color);
            if (string.IsNullOrWhiteSpace(input)) return false;
            string hex = input.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
                hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8) return false;
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                if (!((c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F')))
                    return false;
            }
            return ColorUtility.TryParseHtmlString("#" + hex, out result);
        }

        private static void ApplyColor(
            Image image, string hex, Color original, ref bool applied)
        {
            if (image == null) return;
            if (string.IsNullOrWhiteSpace(hex))
            {
                if (applied)
                {
                    image.color = original;
                    applied = false;
                }
                return;
            }

            Color color;
            // Invalid codes are ignored: retain the last valid color.
            if (!ParseHexColor(hex, out color)) return;
            if (!applied || image.color != color)
                image.color = color;
            applied = true;
        }

        private void UpdateNativePalette(EnemyUiState state, bool isEnemy)
        {
            NativePalette palette;
            if (!_nativePalettes.TryGetValue(state, out palette))
                return;

            string generic = _colorHealth.Value;
            string main = generic;

            if (state.Kind == HudKind.Player &&
                !string.IsNullOrWhiteSpace(_colorPlayer.Value))
                main = _colorPlayer.Value;
            else if (state.Kind == HudKind.Enemy && isEnemy &&
                !string.IsNullOrWhiteSpace(_colorEnemy.Value))
                main = _colorEnemy.Value;
            else if (state.Kind == HudKind.Enemy && !isEnemy &&
                !string.IsNullOrWhiteSpace(_colorFriendly.Value))
                main = _colorFriendly.Value;

            ApplyColor(palette.Health, main, palette.HealthOriginal,
                ref palette.HealthColored);

            string friendly = !string.IsNullOrWhiteSpace(
                _colorFriendly.Value)
                ? _colorFriendly.Value : generic;
            ApplyColor(palette.Friendly, friendly, palette.FriendlyOriginal,
                ref palette.FriendlyColored);

            ApplyColor(palette.Stagger, _colorStagger.Value,
                palette.StaggerOriginal, ref palette.StaggerColored);
            ApplyColor(palette.Recovery, _colorRecovery.Value,
                palette.RecoveryOriginal, ref palette.RecoveryColored);
        }

        private void ForgetNativePalette(EnemyUiState state)
        {
            if (state != null) _nativePalettes.Remove(state);
        }

        private void ClearColorOverrides()
        {
            if (_colorHealth == null) return;
            _colorHealth.Value = string.Empty;
            _colorPlayer.Value = string.Empty;
            _colorEnemy.Value = string.Empty;
            _colorFriendly.Value = string.Empty;
            _colorStagger.Value = string.Empty;
            _colorRecovery.Value = string.Empty;
        }
    }
}
