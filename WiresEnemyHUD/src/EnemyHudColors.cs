using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Every color the HUD shows, vanilla ones included, as an optional hex
    // override. Empty keeps the default. Parsed once when a setting changes,
    // never per frame; per-state images are only touched when needed.
    public sealed partial class EnemyHudReworkPlugin
    {
        private sealed class HexColor
        {
            public ConfigEntry<string> Entry;
            public bool Has;
            public Color Value;

            public void Refresh()
            {
                Color c;
                Has = ParseHexColor(Entry.Value, out c);
                Value = Has ? c : default(Color);
            }

            public Color Or(Color fallback)
            {
                return Has ? Value : fallback;
            }
        }

        private readonly List<HexColor> _hexColors = new List<HexColor>();
        private int _colorVersion = 1;

        // Bars
        private HexColor _cHealth, _cEnemy, _cFriendly, _cBoss, _cPlayer, _cTrail,
            _cBackground, _cFrame, _cStagger, _cRecovery, _cStaggerFlash, _cCritFlash;
        // Text
        private HexColor _cEnemyName, _cEnemyAware, _cEnemyAlerted, _cFriendlyAware,
            _cFriendlyAlerted, _cBossName, _cPlayerName, _cHpText, _cDpsText;
        // Icons
        private HexColor _cStars, _cStarOutline, _cAwareIcon, _cAlertedIcon, _cResist, _cIconOutline;
        private readonly HexColor[] _cDamage = new HexColor[DamageKinds];

        private static Color _staggerFlashBase = Color.white;
        private static Color _critFlashBase = Color.white;

        // Defaults the mod itself uses (vanilla ones are read from the game).
        private static readonly Color StaggerOrange = new Color32(0xE7, 0x5D, 0x16, 255);
        private static readonly Color RecoveryYellow = new Color32(255, 243, 75, 255); // vanilla stamina yellow
        private static readonly Color StarGold = new Color(1f, 0.817f, 0.338f, 1f);

        private void BindColorSections()
        {
            string barDefault = "Empty keeps the game's own color.";

            _order = 1000;
            _cHealth = HexEntry(SecBarColors, "Health bars",
                "Health fill for every bar. The specific bar colors below take priority. " + barDefault,
                SecOldColors, "Health bars");
            _cEnemy = HexEntry(SecBarColors, "Enemy health bars", "Health fill of hostile enemies. " + barDefault,
                SecOldColors, "Enemy health bars");
            _cFriendly = HexEntry(SecBarColors, "Friendly health bars",
                "Health fill of friendly and tamed creatures. " + barDefault,
                SecOldColors, "Friendly health bars");
            _cBoss = HexEntry(SecBarColors, "Boss health bar", "Health fill of boss bars. " + barDefault);
            _cPlayer = HexEntry(SecBarColors, "Player health bars", "Health fill of other players. " + barDefault,
                SecOldColors, "Player health bars");
            _cTrail = HexEntry(SecBarColors, "Damage trail",
                "The lighter part that trails behind a bar as health drops. " + barDefault);
            _cBackground = HexEntry(SecBarColors, "Bar background",
                "Behind the health and stagger fill. Default #0000007B with Transparent bar background on, the game's gray with it off.");
            _cFrame = HexEntry(SecBarColors, "Bar frame", "The dark frame around the bars. " + barDefault);
            _cStagger = HexEntry(SecBarColors, "Stagger bar", "Fill of the stagger meter. Default #E75D16.",
                SecOldColors, "Stagger bar");
            _cRecovery = HexEntry(SecBarColors, "Recovery overlay",
                "Layer that recedes across the stagger meter during a stagger. Default #FFF34B.",
                SecOldColors, "Recovery overlay");
            _cStaggerFlash = HexEntry(SecBarColors, "Stagger flash",
                "The stagger meter's frame flash when an enemy gets staggered. Default #FFFFFF.");
            _cCritFlash = HexEntry(SecBarColors, "Critical hit flash",
                "The health bar's frame flash on a backstab or a hit on a staggered enemy. Default #FFFFFF.");

            _order = 1000;
            _cEnemyName = HexEntry(SecTextColors, "Creature names",
                "Names of creatures that have not noticed you. Empty keeps the game's color.");
            _cEnemyAware = HexEntry(SecTextColors, "Enemy aware",
                "Name of an enemy that noticed you. Default #FFE580 (Pastel) or #FEDB00 (Normal).");
            _cEnemyAlerted = HexEntry(SecTextColors, "Enemy alerted",
                "Name of an enemy hunting you. Default #FF7A76 (Pastel) or #F2100C (Normal).");
            _cFriendlyAware = HexEntry(SecTextColors, "Friendly aware",
                "Name of a friendly creature that noticed a target. Default #86BEFF (Pastel) or #1E86FF (Normal).");
            _cFriendlyAlerted = HexEntry(SecTextColors, "Friendly alerted",
                "Name of a friendly creature in a fight. Default #83E5A7 (Pastel) or #18C962 (Normal).");
            _cBossName = HexEntry(SecTextColors, "Boss names",
                "Boss name color. Overrides the Boss Bar name color setting while set.");
            _cPlayerName = HexEntry(SecTextColors, "Player names",
                "Names above other players. Empty keeps the game's color.");
            _cHpText = HexEntry(SecTextColors, "HP text", "Health numbers on all bars. Default #FFFFFF.");
            _cDpsText = HexEntry(SecTextColors, "Boss DPS text", "The boss DPS counter. Default #FFFFFF.");

            _order = 1000;
            _cStars = HexEntry(SecIconColors, "Stars", "Level stars. Default #FFD056.");
            _cStarOutline = HexEntry(SecIconColors, "Star outlines", "The dark edge around level stars. Default #000000.");
            _cAwareIcon = HexEntry(SecIconColors, "Aware icon",
                "Vanilla awareness icon (Awareness display: Icons). Empty keeps the game's color.");
            _cAlertedIcon = HexEntry(SecIconColors, "Alerted icon",
                "Vanilla alerted icon (Awareness display: Icons). Empty keeps the game's color.");
            string[] names = { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit", "Pickaxe", "Chop" };
            for (int i = 0; i < DamageKinds; i++)
                _cDamage[i] = HexEntry(SecIconColors, names[i] + " weakness",
                    names[i] + " weakness icon. Default #" + ColorUtility.ToHtmlStringRGB(DamageTint[i]) + ".");
            _cResist = HexEntry(SecIconColors, "Resistances",
                "All resistance icons. Default #99999973 (gray, 45% opacity).");
            _cIconOutline = HexEntry(SecIconColors, "Icon outlines",
                "The dark edge around weakness and resistance icons. Default #000000.");
        }

        private HexColor HexEntry(string section, string key, string description,
            string oldSection = null, string oldKey = null)
        {
            var hex = new HexColor();
            hex.Entry = oldSection != null
                ? Bind(section, key, string.Empty,
                    description + " Hex like #E75D16, or #E75D1680 with opacity.",
                    null, null, oldSection, oldKey, "08 - Colors", oldKey)
                : Bind(section, key, string.Empty,
                    description + " Hex like #E75D16, or #E75D1680 with opacity.",
                    null, null);
            hex.Refresh();
            hex.Entry.SettingChanged += (s, e) =>
            {
                hex.Refresh();
                _colorVersion++;
                _staggerFlashBase = _cStaggerFlash != null ? _cStaggerFlash.Or(Color.white) : Color.white;
                _critFlashBase = _cCritFlash != null ? _cCritFlash.Or(Color.white) : Color.white;
            };
            _hexColors.Add(hex);
            return hex;
        }

        private void RefreshFlashColors()
        {
            _staggerFlashBase = _cStaggerFlash.Or(Color.white);
            _critFlashBase = _cCritFlash.Or(Color.white);
        }

        private sealed class NativePalette
        {
            public Image Health, Friendly, Slow, Stagger, Recovery, HealthFrame, StaggerFrame;
            public Color HealthOriginal, FriendlyOriginal, SlowOriginal, StaggerOriginal,
                RecoveryOriginal, HealthFrameOriginal, StaggerFrameOriginal;
            public bool HealthColored, FriendlyColored, SlowColored, StaggerColored,
                RecoveryColored, HealthFrameColored, StaggerFrameColored;

            // Applied only when a color setting changes.
            public int Version;
            public readonly List<Image> StarFill = new List<Image>();
            public readonly List<Image> StarEdge = new List<Image>();
            public readonly List<Image> AwareIcon = new List<Image>();
            public readonly List<Image> AlertedIcon = new List<Image>();
            public readonly List<Color> StarFillOriginal = new List<Color>();
            public readonly List<Color> StarEdgeOriginal = new List<Color>();
            public readonly List<Color> AwareOriginal = new List<Color>();
            public readonly List<Color> AlertedOriginal = new List<Color>();
        }

        private readonly Dictionary<EnemyUiState, NativePalette>
            _nativePalettes = new Dictionary<EnemyUiState, NativePalette>();

        private static Image GetFillImage(RectTransform fill)
        {
            if (fill == null) return null;
            var bar = fill.GetComponent<GuiBar>();
            if (bar != null && bar.m_bar != null)
                return bar.m_bar.GetComponent<Image>();
            return fill.GetComponent<Image>();
        }

        private static Image Child(RectTransform parent, string name)
        {
            Transform t = parent != null ? parent.Find(name) : null;
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static void Capture(Image image, out Color original)
        {
            original = image != null ? image.color : default(Color);
        }

        private void CaptureNativePalette(EnemyUiState state)
        {
            if (state == null) return;
            var p = new NativePalette();
            p.Health = GetFillImage(state.HealthFastFill);
            p.Friendly = GetFillImage(state.HealthFriendlyFill);
            p.Slow = GetFillImage(state.HealthSlowFill);
            p.Stagger = state.StaggerFast != null && state.StaggerFast.m_bar != null
                ? state.StaggerFast.m_bar.GetComponent<Image>() : null;
            p.Recovery = state.RecoveryOverlay;
            p.HealthFrame = Child(state.HealthRect, "darken");
            p.StaggerFrame = Child(state.StaggerRect, "darken");

            Capture(p.Health, out p.HealthOriginal);
            Capture(p.Friendly, out p.FriendlyOriginal);
            Capture(p.Slow, out p.SlowOriginal);
            Capture(p.Stagger, out p.StaggerOriginal);
            Capture(p.Recovery, out p.RecoveryOriginal);
            Capture(p.HealthFrame, out p.HealthFrameOriginal);
            Capture(p.StaggerFrame, out p.StaggerFrameOriginal);

            // Stars: a dark backing image with the gold star on top.
            CaptureStars(state.Level2, p);
            CaptureStars(state.Level3, p);
            CaptureImages(state.AwareIcon, p.AwareIcon, p.AwareOriginal);
            CaptureImages(state.AlertedIcon, p.AlertedIcon, p.AlertedOriginal);

            _nativePalettes[state] = p;
        }

        private static void CaptureStars(RectTransform level, NativePalette p)
        {
            if (level == null) return;
            foreach (Image img in level.GetComponentsInChildren<Image>(true))
            {
                Color c = img.color;
                bool dark = c.r + c.g + c.b < 0.6f;
                (dark ? p.StarEdge : p.StarFill).Add(img);
                (dark ? p.StarEdgeOriginal : p.StarFillOriginal).Add(c);
            }
        }

        private static void CaptureImages(GameObject root, List<Image> images, List<Color> originals)
        {
            if (root == null) return;
            foreach (Image img in root.GetComponentsInChildren<Image>(true))
            {
                images.Add(img);
                originals.Add(img.color);
            }
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

        // Only touches an image while an override is set, or once to put
        // the original back, so vanilla and other mods keep control otherwise.
        private static void ApplyColor(Image image, HexColor hex, Color original, ref bool applied)
        {
            if (image == null) return;
            if (hex == null || !hex.Has)
            {
                if (applied)
                {
                    image.color = original;
                    applied = false;
                }
                return;
            }
            if (!applied || image.color != hex.Value)
                image.color = hex.Value;
            applied = true;
        }

        private static HexColor First(HexColor specific, HexColor general)
        {
            return specific != null && specific.Has ? specific : general;
        }

        private void UpdateNativePalette(EnemyUiState state, bool isEnemy)
        {
            NativePalette p;
            if (!_nativePalettes.TryGetValue(state, out p))
                return;

            HexColor main =
                state.Kind == HudKind.Player ? First(_cPlayer, _cHealth) :
                state.Kind == HudKind.Boss ? First(_cBoss, _cHealth) :
                isEnemy ? First(_cEnemy, _cHealth) : First(_cFriendly, _cHealth);

            ApplyColor(p.Health, main, p.HealthOriginal, ref p.HealthColored);
            ApplyColor(p.Friendly, First(_cFriendly, _cHealth), p.FriendlyOriginal, ref p.FriendlyColored);
            ApplyColor(p.Slow, _cTrail, p.SlowOriginal, ref p.SlowColored);
            ApplyColor(p.Stagger, _cStagger, p.StaggerOriginal, ref p.StaggerColored);
            ApplyColor(p.Recovery, _cRecovery, p.RecoveryOriginal, ref p.RecoveryColored);
            ApplyColor(p.HealthFrame, _cFrame, p.HealthFrameOriginal, ref p.HealthFrameColored);
            ApplyColor(p.StaggerFrame, _cFrame, p.StaggerFrameOriginal, ref p.StaggerFrameColored);

            if (state.Kind == HudKind.Player && state.NameGraphic != null)
            {
                Color want = _cPlayerName.Or(state.NormalNameColor);
                if (state.NameGraphic.color != want) state.NameGraphic.color = want;
            }

            if (p.Version == _colorVersion) return;
            p.Version = _colorVersion;
            Recolor(p.StarFill, p.StarFillOriginal, _cStars);
            Recolor(p.StarEdge, p.StarEdgeOriginal, _cStarOutline);
            Recolor(p.AwareIcon, p.AwareOriginal, _cAwareIcon);
            Recolor(p.AlertedIcon, p.AlertedOriginal, _cAlertedIcon);
        }

        private static void Recolor(List<Image> images, List<Color> originals, HexColor hex)
        {
            for (int i = 0; i < images.Count; i++)
                if (images[i] != null)
                    images[i].color = hex.Or(originals[i]);
        }

        private void ForgetNativePalette(EnemyUiState state)
        {
            if (state != null) _nativePalettes.Remove(state);
        }

        private void ClearColorOverrides()
        {
            foreach (HexColor hex in _hexColors)
                hex.Entry.Value = string.Empty;
        }

        // ---- Lookups used by the rest of the HUD ----
        private Color HpTextColor() { return _cHpText.Or(Color.white); }

        private Color BarBackground(Color original)
        {
            return _cBackground.Or(_transparentBackground.Value ? TransparentBackground : original);
        }
    }
}
