using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresLightOcclusion
{
    // Fire light coloured like fire, and the brightness slider.
    //
    // The game's fires light the world a pale salmon: a sconce's light is (1, 0.62, 0.48), about
    // 2,600 K but well off the colour any hot thing glows (toward magenta); a campfire's (1, 0.50,
    // 0.32) is as far off it. A Dvergr lantern's (1, 0.79, 0.29) is about 3,200 K and off the other way,
    // toward green: acid yellow, though its flame is a surtling core's fire. A flame's light is the
    // glow of hot soot, close to a black body's: a candle's is 1,850 K (Wikipedia, Color
    // temperature). So every fire light the game has (torches, sconces, braziers, hearths, campfires,
    // ovens, forges, candles, Dvergr lanterns, something burning) is given the colour of a black body
    // at 1,850 K, the curve of Kang et al. (2002), "Design of advanced color temperature control
    // system for HDTV applications", taken to sRGB. Wood, oil and wax are all taken as a candle's
    // flame: a judgement, no source gives one figure for each.
    // A deeper colour is darker for the same intensity, so the intensity makes up the difference:
    // each fire is exactly as bright to the eye as the game made it (same luminance, Rec. 709). Unity
    // here multiplies a light's colour by its intensity before taking it to linear, so that's done
    // through the same 2.2 power (an approximation of the sRGB curve).
    // Lights that aren't fire (magic, portals, blue and green torches, the hooded lantern) keep
    // their colour. Fires already as deep as this (kilns, smelters, the Ashlands pyre) too.
    //
    // Each light is changed once, as it wakes (before the game reads its intensity for its flicker
    // and fades), and changed back or again when a setting changes. Nothing runs per frame.
    internal static class LightColors
    {
        internal const float Kelvin = 1850f;
        private const float Gamma = 2.2f;

        // The game's own fire light colours (as stored, sRGB).
        private static readonly Color[] Fires =
        {
            new Color(1f, .621f, .482f),  // sconce, standing torches, braziers, hearth, torch, forge, lava lantern, jack-o-turnip
            new Color(1f, .504f, .324f),  // campfire, bonfire, oven, bathtub
            new Color(.838f, .527f, .413f), // the low glow under campfires, hearths and braziers
            new Color(1f, .785f, .288f),  // Dvergr lanterns (standing, pole, carried, picked)
            new Color(1f, .646f, .222f),  // candles, snow lantern
            new Color(1f, .673f, .324f),  // cauldron
            new Color(1f, .692f, .456f),  // something burning
            new Color(1f, .417f, .265f),  // surtling core
        };

        private sealed class Entry
        {
            internal Color Color;     // as the game made it
            internal bool Fire;
            internal float Factor = 1f; // the intensity multiplier now applied
        }

        private static readonly Dictionary<Light, Entry> Lights = new Dictionary<Light, Entry>();
        private static Color s_fire;
        private static float s_fireY;
        private static readonly AccessTools.FieldRef<LightFlicker, float> FlickerBase = AccessTools.FieldRefAccess<LightFlicker, float>("m_baseIntensity");
        private static readonly AccessTools.FieldRef<EffectFade, float> FadeBase = AccessTools.FieldRefAccess<EffectFade, float>("m_lightBaseIntensity");

        static LightColors()
        {
            s_fire = BlackBody(Kelvin);
            s_fireY = Luminance(s_fire);
        }

        private static bool On => Plugin.UseShader != null && Plugin.UseShader.Value;
        private static float Brightness => Plugin.Brightness != null ? Plugin.Brightness.Value : 1f;

        // A black body's colour at T kelvin as sRGB, brightest channel 1. Kang et al. (2002), valid
        // 1,667 to 25,000 K: the Planckian locus as CIE 1931 xy, then XYZ to linear sRGB (IEC 61966-2-1).
        internal static Color BlackBody(float t)
        {
            double T = t, x, y;
            if (T <= 4000) x = -0.2661239e9 / (T * T * T) - 0.2343589e6 / (T * T) + 0.8776956e3 / T + 0.179910;
            else x = -3.0258469e9 / (T * T * T) + 2.1070379e6 / (T * T) + 0.2226347e3 / T + 0.240390;
            if (T <= 2222) y = -1.1063814 * x * x * x - 1.34811020 * x * x + 2.18555832 * x - 0.20219683;
            else if (T <= 4000) y = -0.9549476 * x * x * x - 1.37418593 * x * x + 2.09137015 * x - 0.16748867;
            else y = 3.0817580 * x * x * x - 5.87338670 * x * x + 3.75112997 * x - 0.37001483;
            double X = x / y, Y = 1.0, Z = (1 - x - y) / y;
            double r = 3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z;
            double g = -0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z;
            double b = 0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z;
            double m = System.Math.Max(r, System.Math.Max(g, b));
            var lin = new Color((float)System.Math.Max(0, r / m), (float)System.Math.Max(0, g / m), (float)System.Math.Max(0, b / m));
            return lin.gamma;
        }

        // Rec. 709 luminance of an sRGB colour, in linear light.
        private static float Luminance(Color c)
        {
            Color l = c.linear;
            return .2126f * l.r + .7152f * l.g + .0722f * l.b;
        }

        private static bool IsFire(Color c)
        {
            foreach (Color f in Fires)
                if (Mathf.Abs(c.r - f.r) < .004f && Mathf.Abs(c.g - f.g) < .004f && Mathf.Abs(c.b - f.b) < .004f) return true;
            return false;
        }

        // Before the game reads the light (LightLod, LightFlicker or EffectFade waking up).
        internal static void Touch(Light l)
        {
            if (l == null || (l.type != LightType.Point && l.type != LightType.Spot) || Lights.ContainsKey(l)) return;
            if (Lights.Count > 4096) Prune();
            var e = new Entry { Color = l.color, Fire = IsFire(l.color) };
            Lights[l] = e;
            if (!On) return;
            float f = Wanted(e);
            if (e.Fire) l.color = new Color(s_fire.r, s_fire.g, s_fire.b, l.color.a);
            l.intensity *= f;
            e.Factor = f;
        }

        private static float Wanted(Entry e)
        {
            if (!On) return 1f;
            float f = Brightness;
            if (e.Fire) f *= Mathf.Pow(Luminance(e.Color) / s_fireY, 1f / Gamma);
            return f;
        }

        // A setting changed: every light already out there, again.
        internal static void Refresh()
        {
            Prune();
            foreach (KeyValuePair<Light, Entry> kv in Lights)
            {
                Light l = kv.Key;
                Entry e = kv.Value;
                if (e.Fire) l.color = On ? new Color(s_fire.r, s_fire.g, s_fire.b, l.color.a) : e.Color;
                float want = Wanted(e);
                float r = want / e.Factor;
                e.Factor = want;
                if (Mathf.Abs(r - 1f) < 1e-5f) continue;
                bool held = false;
                LightFlicker flicker = l.GetComponent<LightFlicker>();
                if (flicker != null) { FlickerBase(flicker) *= r; held = true; }
                EffectFade fade = l.GetComponentInParent<EffectFade>();
                if (fade != null) { FadeBase(fade) *= r; held = true; }
                if (!held) l.intensity *= r;
            }
        }

        private static readonly List<Light> s_dead = new List<Light>();
        private static void Prune()
        {
            s_dead.Clear();
            foreach (Light l in Lights.Keys) if (l == null) s_dead.Add(l);
            foreach (Light l in s_dead) Lights.Remove(l);
        }

        [HarmonyPatch(typeof(LightLod), "Awake")]
        private static class OnLod
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(LightLod __instance) => Touch(__instance.GetComponent<Light>());
        }

        [HarmonyPatch(typeof(LightFlicker), "Awake")]
        private static class OnFlicker
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(LightFlicker __instance) => Touch(__instance.GetComponent<Light>());
        }

        [HarmonyPatch(typeof(EffectFade), "Awake")]
        private static class OnFade
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(EffectFade __instance) => Touch(__instance.GetComponentInChildren<Light>());
        }
    }
}
