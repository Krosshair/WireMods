using System.Collections.Generic;
using System.ComponentModel;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Shape and color of each damage type's icon, plus one resistance
    // color. Everything else uses the game's own colors.
    public sealed partial class EnemyHudReworkPlugin
    {
        // The shapes the damage type icons can use, in the order of their
        // pictures (each damage type's own shape by default).
        private enum IconShape
        {
            [Description("Circle")] Circle,
            [Description("Diamond")] Diamond,
            [Description("Chevron")] Chevron,
            [Description("Triangle")] Triangle,
            [Description("Hexagon")] Hexagon,
            [Description("Slanted rhombus")] SlantedRhombus,
            [Description("Upside-down triangle")] UpsideDownTriangle,
            [Description("Square")] Square,
            [Description("Upside-down chevron")] UpsideDownChevron,
            [Description("Sideways triangle")] SidewaysTriangle
        }

        private static readonly string[] DamageNames =
            { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit", "Pickaxe", "Chop" };

        private enum IconStyle
        {
            [Description("Shapes")] Shapes,
            [Description("Game icons (not recommended)")] GameIcons
        }

        private ConfigEntry<IconStyle> _iconStyle;
        private readonly ConfigEntry<IconShape>[] _iconShape = new ConfigEntry<IconShape>[DamageKinds];
        private readonly ConfigEntry<Color>[] _iconColor = new ConfigEntry<Color>[DamageKinds];
        private ConfigEntry<Color> _resistColor;

        // Read into plain arrays when a setting changes, never per frame.
        private readonly int[] _shapeOf = new int[DamageKinds];
        private readonly Color[] _tintOf = new Color[DamageKinds];
        private Color _resistTint = ResistTint;

        private void BindColorSections()
        {
            _order = 1000;
            _iconStyle = Bind(SecIconColors, "Icon style", IconStyle.Shapes,
                "Shapes are made to be told apart at a glance at this size. Game icons uses the game's own skill and status effect icons instead (axe for chop, clubs for blunt, the poison effect for poison, ectoplasm for spirit...). NOT recommended: they are detailed pictures, hard to tell apart this small. The shapes below don't apply to them; weaknesses keep the icon's own colors and resistances use the resistance color.",
                null, null);
            _iconStyle.SettingChanged += (s, e) => RefreshIconLook();
            for (int i = 0; i < DamageKinds; i++)
            {
                _iconShape[i] = Bind(SecIconColors, DamageNames[i] + " icon", (IconShape)i,
                    "Shape of the " + DamageNames[i].ToLowerInvariant() + " icon.", null, null);
                _iconColor[i] = Bind(SecIconColors, DamageNames[i] + " color", DamageTint[i],
                    "Color of the " + DamageNames[i].ToLowerInvariant() + " icon when the creature is weak to it.", null, null);
                _iconShape[i].SettingChanged += (s, e) => RefreshIconLook();
                _iconColor[i].SettingChanged += (s, e) => RefreshIconLook();
            }
            _resistColor = Bind(SecIconColors, "Resistance color", ResistTint,
                "Color of every resistance icon.", null, null);
            _resistColor.SettingChanged += (s, e) => RefreshIconLook();
            RefreshIconLook();
        }

        private void RefreshIconLook()
        {
            for (int i = 0; i < DamageKinds; i++)
            {
                int shape = _iconShape[i] != null ? (int)_iconShape[i].Value : i;
                _shapeOf[i] = shape >= 0 && shape < DamageKinds ? shape : i;
                _tintOf[i] = _iconColor[i] != null ? _iconColor[i].Value : DamageTint[i];
            }
            _resistTint = _resistColor != null ? _resistColor.Value : ResistTint;
            MarkAllWeaknessDirty();
        }

        private void ResetIconLook()
        {
            for (int i = 0; i < DamageKinds; i++)
            {
                _iconShape[i].Value = (IconShape)i;
                _iconColor[i].Value = DamageTint[i];
            }
            _resistColor.Value = ResistTint;
        }

        // ---- Bar frames, for the flashes ----

        private sealed class NativePalette
        {
            public Image HealthFrame, StaggerFrame;
            public Color HealthFrameOriginal, StaggerFrameOriginal;
        }

        private readonly Dictionary<EnemyUiState, NativePalette>
            _nativePalettes = new Dictionary<EnemyUiState, NativePalette>();

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
            p.HealthFrame = Child(state.HealthRect, "darken");
            p.StaggerFrame = Child(state.StaggerRect, "darken");
            Capture(p.HealthFrame, out p.HealthFrameOriginal);
            Capture(p.StaggerFrame, out p.StaggerFrameOriginal);
            _nativePalettes[state] = p;
        }

        // The game's flash is not a ring around a bar: the bar's dark frame
        // (the padding behind everything) turns white, and the bar's own
        // background in front of it makes the inside a darker white. So the
        // frame image itself is blended toward white. Only touched while a
        // flash runs or just ended.
        private void ApplyFrameFlash(EnemyUiState state)
        {
            NativePalette p;
            if (!_nativePalettes.TryGetValue(state, out p)) return;
            BlendFrame(p.HealthFrame, p.HealthFrameOriginal, state.CritFlashAlpha, ref state.CritFlashShown);
            BlendFrame(p.StaggerFrame, p.StaggerFrameOriginal, state.StaggerFlashAlpha, ref state.StaggerFlashShown);
        }

        private static void BlendFrame(Image frame, Color rest, float alpha, ref float shown)
        {
            if (frame == null || (alpha <= 0f && shown <= 0f)) return;
            Color target = alpha > 0f ? Color.Lerp(rest, Color.white, alpha) : rest;
            if (frame.color != target) frame.color = target;
            shown = alpha;
        }

        private void ForgetNativePalette(EnemyUiState state)
        {
            if (state != null) _nativePalettes.Remove(state);
        }

        private Color BarBackground(Color original)
        {
            return _transparentBackground.Value ? TransparentBackground : original;
        }
    }
}
