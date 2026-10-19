using System;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace EnemyHudRework
{
    // Optional: the damage type icons in item tooltips, before each damage
    // line ("<icon> Pierce: 55"). The icons are composed once into a small
    // TextMeshPro sprite sheet with the current colors, and rebuilt only
    // when a color setting changes. Off by default.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _tooltipIcons;

        private const string TipAssetName = "WireDamageIcons";
        private const int TipSize = 32;
        // Damage lines vanilla writes in tooltips, by DamageKind index.
        private static readonly string[] TipTokens =
        {
            "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_fire",
            "$inventory_frost", "$inventory_lightning", "$inventory_poison", "$inventory_spirit"
        };
        private static readonly string[] TipFind = new string[TipTokens.Length];
        private static readonly string[] TipReplace = new string[TipTokens.Length];

        private TMP_SpriteAsset _tipAsset;
        private Texture2D _tipAtlas;
        private int _tipVersion = -1;
        private bool _tipFailed;
        private Texture2D[] _tipBack, _tipFill;

        private bool EnsureTipSprites()
        {
            if (_tipFailed) return false;
            if (_tipAsset == null && !BuildTipAsset())
            {
                _tipFailed = true;
                return false;
            }
            if (_tipVersion != _colorVersion)
            {
                ComposeTipAtlas();
                _tipVersion = _colorVersion;
            }
            return true;
        }

        private bool BuildTipAsset()
        {
            try
            {
                _tipBack = new Texture2D[DamageKinds];
                _tipFill = new Texture2D[DamageKinds];
                for (int i = 0; i < DamageKinds; i++)
                {
                    _tipBack[i] = LoadReadable(DamageAsset[i]);
                    _tipFill[i] = LoadReadable(DamageAsset[i] + "_16");
                }

                Shader shader = Shader.Find("TextMeshPro/Sprite");
                if (shader == null && TMP_Settings.defaultSpriteAsset != null &&
                    TMP_Settings.defaultSpriteAsset.material != null)
                    shader = TMP_Settings.defaultSpriteAsset.material.shader;
                if (shader == null)
                {
                    Logger.LogWarning("Tooltip icons: no TextMeshPro sprite shader found; option disabled.");
                    return false;
                }

                _tipAtlas = new Texture2D(TipSize * DamageKinds, TipSize, TextureFormat.RGBA32, false);
                _tipAtlas.filterMode = FilterMode.Bilinear;
                _tipAtlas.wrapMode = TextureWrapMode.Clamp;
                _tipAtlas.hideFlags = HideFlags.HideAndDontSave;

                var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                asset.name = TipAssetName;
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(TipAssetName);
                asset.spriteSheet = _tipAtlas;
                asset.material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                asset.material.mainTexture = _tipAtlas;

                for (int i = 0; i < DamageKinds; i++)
                {
                    // Sits on the baseline, sized to the font's ascent by TMP.
                    var glyph = new TMP_SpriteGlyph((uint)i,
                        new GlyphMetrics(TipSize, TipSize, 0, TipSize * 0.86f, TipSize),
                        new GlyphRect(i * TipSize, 0, TipSize, TipSize), 0.95f, 0);
                    asset.spriteGlyphTable.Add(glyph);
                    var character = new TMP_SpriteCharacter(0xE100u + (uint)i, glyph);
                    character.name = DamageAsset[i];
                    asset.spriteCharacterTable.Add(character);
                }
                asset.UpdateLookupTables();
                MaterialReferenceManager.AddSpriteAsset(asset);
                _tipAsset = asset;

                for (int i = 0; i < TipTokens.Length; i++)
                {
                    TipFind[i] = "\n" + TipTokens[i] + ":";
                    TipReplace[i] = "\n<sprite=\"" + TipAssetName + "\" name=\"" + DamageAsset[i] + "\"> " + TipTokens[i] + ":";
                }
                return true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Tooltip icons could not be set up: " + e.Message);
                return false;
            }
        }

        private static Texture2D LoadReadable(string name)
        {
            using (Stream s = typeof(EnemyHudReworkPlugin).Assembly.GetManifestResourceStream("EnemyHudRework.Assets." + name + ".png"))
            {
                if (s == null || LoadImage == null) return null;
                byte[] data = new byte[s.Length];
                int read = 0;
                while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                if (!(bool)LoadImage.Invoke(null, new object[] { tex, data, false })) return null;
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
        }

        // Same construction as the HUD icons: dark backing, colored fill
        // at 12/15 of the size on top.
        private void ComposeTipAtlas()
        {
            Color[] px = new Color[_tipAtlas.width * _tipAtlas.height];
            Color edge = _cIconOutline.Or(Color.black);
            float inset = TipSize * 1.5f / 15f, inner = TipSize * 12f / 15f;
            for (int k = 0; k < DamageKinds; k++)
            {
                Texture2D back = _tipBack[k], fill = _tipFill[k];
                Color tint = IconTint(k, true);
                for (int y = 0; y < TipSize; y++)
                    for (int x = 0; x < TipSize; x++)
                    {
                        float u = (x + 0.5f) / TipSize, v = (y + 0.5f) / TipSize;
                        float ba = back != null ? back.GetPixelBilinear(u, v).a * edge.a : 0f;
                        Color outC = new Color(edge.r, edge.g, edge.b, ba);
                        float fu = (x + 0.5f - inset) / inner, fv = (y + 0.5f - inset) / inner;
                        if (fill != null && fu >= 0f && fu <= 1f && fv >= 0f && fv <= 1f)
                        {
                            Color f = fill.GetPixelBilinear(fu, fv);
                            float fa = f.a * tint.a;
                            float a = fa + ba * (1f - fa);
                            if (a > 0f)
                            {
                                Color fc = new Color(f.r * tint.r, f.g * tint.g, f.b * tint.b);
                                outC = new Color(
                                    (fc.r * fa + edge.r * ba * (1f - fa)) / a,
                                    (fc.g * fa + edge.g * ba * (1f - fa)) / a,
                                    (fc.b * fa + edge.b * ba * (1f - fa)) / a, a);
                            }
                        }
                        px[y * _tipAtlas.width + k * TipSize + x] = outC;
                    }
            }
            _tipAtlas.SetPixels(px);
            _tipAtlas.Apply(false, false);
        }

        private string AddTipIcons(string text)
        {
            for (int i = 0; i < TipFind.Length; i++)
                if (text.IndexOf(TipFind[i], StringComparison.Ordinal) >= 0)
                    text = text.Replace(TipFind[i], TipReplace[i]);
            return text;
        }

        private static void TipPostfix(ref string __result)
        {
            EnemyHudReworkPlugin p = Instance;
            if (p == null || p._tooltipIcons == null || !p._tooltipIcons.Value ||
                string.IsNullOrEmpty(__result) ||
                __result.IndexOf("$inventory_", StringComparison.Ordinal) < 0)
                return;
            if (!p.EnsureTipSprites()) return;
            __result = p.AddTipIcons(__result);
        }

        [HarmonyPatch(typeof(HitData.DamageTypes), nameof(HitData.DamageTypes.GetTooltipString), new Type[] { typeof(Skills.SkillType) })]
        private static class WeaponDamageTooltipPatch
        {
            private static void Postfix(ref string __result) { TipPostfix(ref __result); }
        }

        [HarmonyPatch(typeof(HitData.DamageTypes), nameof(HitData.DamageTypes.GetTooltipString), new Type[0])]
        private static class DamageTooltipPatch
        {
            private static void Postfix(ref string __result) { TipPostfix(ref __result); }
        }
    }
}
