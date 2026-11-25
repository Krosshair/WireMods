using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Item tooltips: the damage type icon (your shapes and colors, or the
    // game's icons with that style) in front of each damage line ("Slash:
    // 20"). Cosmetic only: the lines get a little room at their start and
    // the icons sit in it, laid out again whenever the tooltip changes
    // size, never per frame.
    public sealed partial class EnemyHudReworkPlugin
    {
        private BepInEx.Configuration.ConfigEntry<bool> _tooltipIcons;

        private static readonly string[] TooltipTokens =
            { "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_fire", "$inventory_frost",
              "$inventory_lightning", "$inventory_poison", "$inventory_spirit" };
        private static readonly int[] TooltipKinds =
            { (int)DamageKind.Blunt, (int)DamageKind.Slash, (int)DamageKind.Pierce, (int)DamageKind.Fire, (int)DamageKind.Frost,
              (int)DamageKind.Lightning, (int)DamageKind.Poison, (int)DamageKind.Spirit };
        private const string TooltipRoom = "<space=1.3em>";

        private static readonly AccessTools.FieldRef<GameObject> TooltipObject =
            AccessTools.StaticFieldRefAccess<GameObject>(AccessTools.Field(typeof(UITooltip), "m_tooltip"));

        // The text was just (re)set: make room in front of damage lines and
        // hand the positions to the tooltip's icon helper.
        internal void DecorateTooltip()
        {
            if (_tooltipIcons == null || !_tooltipIcons.Value || Localization.instance == null) return;
            GameObject tip = TooltipObject();
            if (tip == null) return;
            Transform t = Utils.FindChild(tip.transform, "Text");
            TMP_Text text = t != null ? t.GetComponent<TMP_Text>() : null;
            if (text == null || string.IsNullOrEmpty(text.text)) return;
            string s = text.text;
            var kinds = new List<int>();
            var labels = new List<string>();
            for (int i = 0; i < TooltipTokens.Length; i++)
            {
                string label = Localization.instance.Localize(TooltipTokens[i]);
                if (string.IsNullOrEmpty(label) || label.StartsWith("[")) continue;
                string line = "\n" + label + ":";
                if (s.IndexOf(line, System.StringComparison.Ordinal) < 0) continue;
                s = s.Replace(line, "\n" + TooltipRoom + label + ":");
                kinds.Add(TooltipKinds[i]);
                labels.Add(label + ":");
            }
            if (kinds.Count == 0) return;
            text.text = s;
            TooltipDamageIcons helper = text.GetComponent<TooltipDamageIcons>();
            if (helper == null) helper = text.gameObject.AddComponent<TooltipDamageIcons>();
            helper.Set(this, text, kinds, labels);
        }

        // The look of one icon, the same as on nameplates.
        internal void StyleTooltipIcon(Image back, Image fill, int kind)
        {
            Sprite game = _iconStyle.Value == IconStyle.GameIcons ? FindGameIcon(kind) : null;
            if (game != null)
            {
                back.sprite = game; back.preserveAspect = true; back.color = Color.white;
                fill.enabled = false;
                return;
            }
            LoadDamageSprites();
            int shape = _shapeOf[kind];
            back.sprite = DamageBack[shape]; back.preserveAspect = false; back.color = Color.black;
            fill.sprite = DamageFill[shape]; fill.enabled = fill.sprite != null; fill.color = _tintOf[kind];
        }

        [HarmonyPatch(typeof(UITooltip), "UpdateTextElements")]
        private static class TooltipTextPatch
        {
            private static void Postfix() { Instance?.DecorateTooltip(); }
        }
    }

    internal sealed class TooltipDamageIcons : MonoBehaviour
    {
        private EnemyHudReworkPlugin _plugin;
        private TMP_Text _text;
        private readonly List<int> _kinds = new List<int>();
        private readonly List<string> _labels = new List<string>();
        private readonly List<Image[]> _icons = new List<Image[]>();
        private bool _busy;

        internal void Set(EnemyHudReworkPlugin plugin, TMP_Text text, List<int> kinds, List<string> labels)
        {
            _plugin = plugin;
            _text = text;
            _kinds.Clear(); _kinds.AddRange(kinds);
            _labels.Clear(); _labels.AddRange(labels);
            Layout();
        }

        private void OnEnable() { Layout(); }
        private void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Layout(); }

        private Image[] Icon(int i)
        {
            while (_icons.Count <= i)
            {
                GameObject go = new GameObject("WireDamageIcon", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                Image back = go.GetComponent<Image>();
                back.raycastTarget = false;
                GameObject f = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                f.transform.SetParent(go.transform, false);
                Image fill = f.GetComponent<Image>();
                fill.raycastTarget = false;
                RectTransform br = back.rectTransform, fr = fill.rectTransform;
                br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0.5f);
                fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0.5f);
                _icons.Add(new[] { back, fill });
            }
            return _icons[i];
        }

        private void Layout()
        {
            if (_busy || _text == null || _plugin == null) return;
            _busy = true;
            try
            {
                _text.ForceMeshUpdate(true);
                TMP_TextInfo info = _text.textInfo;
                string parsed = _text.GetParsedText();
                int used = 0;
                for (int k = 0; k < _kinds.Count; k++)
                {
                    int at = -1;
                    for (int from = 0; ;)
                    {
                        int i = parsed.IndexOf(_labels[k], from, System.StringComparison.Ordinal);
                        if (i < 0) break;
                        if (i == 0 || parsed[i - 1] == '\n') { at = i; break; }
                        from = i + 1;
                    }
                    if (at < 0 || at >= info.characterCount) continue;
                    TMP_CharacterInfo c = info.characterInfo[at];
                    TMP_LineInfo line = info.lineInfo[c.lineNumber];
                    float height = line.ascender - line.descender;
                    float size = height * 0.85f;
                    Image[] icon = Icon(used++);
                    _plugin.StyleTooltipIcon(icon[0], icon[1], _kinds[k]);
                    RectTransform r = icon[0].rectTransform;
                    r.sizeDelta = new Vector2(size, size);
                    icon[1].rectTransform.sizeDelta = new Vector2(size, size) * (12f / 15f);
                    r.localPosition = new Vector3(c.bottomLeft.x - size * 0.5f - size * 0.1f,
                        (line.ascender + line.descender) * 0.5f, 0f);
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                }
                for (int i = used; i < _icons.Count; i++)
                    if (_icons[i][0].gameObject.activeSelf) _icons[i][0].gameObject.SetActive(false);
            }
            finally { _busy = false; }
        }
    }
}
