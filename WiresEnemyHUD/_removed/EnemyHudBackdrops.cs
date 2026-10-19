using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Optional split-off backgrounds, in the health bar's own style (its
    // soft dark frame plus the bar background color): behind the damage
    // type icons, the level stars, the name and the alert icon. Built and
    // laid out only when something changes, never per frame.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _bgIcons, _bgStars, _bgName, _bgAlert;

        // The bar's frame reaches 4 units past the bar; contents get 2 units
        // of room inside the fill.
        private const float BackdropRoom = 2f, BackdropFrame = 4f;
        private static readonly Color VanillaBarBkg = new Color(0.1764706f, 0.1764706f, 0.1764706f, 1f);
        private const string BackdropName = "WireBackdrop";

        private sealed class Backdrop
        {
            public RectTransform Root;
            public Image Frame;
            public Image Fill;
        }

        private sealed class BackdropSet
        {
            public Backdrop IconsA, IconsB, Name, Alert, Star2, Star3;
            public BackdropFollower AlertFollower;
        }

        internal static bool IsBackdrop(Component c)
        {
            return c != null && c.gameObject.name.StartsWith(BackdropName, System.StringComparison.Ordinal);
        }

        private void BindBackdropConfig(string section)
        {
            _bgIcons = Bind(section, "Icon background", false,
                "Give the damage type icons their own background, split off from the health bar and in the same style.",
                null, null);
            _bgStars = Bind(section, "Star background", false,
                "Give the level stars their own background in the health bar's style.",
                null, null);
            _bgName = Bind(section, "Name background", false,
                "Give the creature name its own background in the health bar's style.",
                null, null);
            _bgAlert = Bind(section, "Alert icon background", false,
                "Give the aware and alerted icons above enemies their own background in the health bar's style.",
                null, null);
        }

        private static Image FrameSource(EnemyUiState state)
        {
            if (state.HealthRect == null) return null;
            Transform t = state.HealthRect.Find("darken");
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static Backdrop CreateBackdrop(Transform parent, int index, Image frameSource)
        {
            GameObject go = new GameObject(BackdropName, typeof(RectTransform));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.SetSiblingIndex(index);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);

            var b = new Backdrop { Root = r };
            GameObject fr = new GameObject(BackdropName + "Frame", typeof(RectTransform), typeof(Image));
            RectTransform frr = (RectTransform)fr.transform;
            frr.SetParent(r, false);
            frr.anchorMin = Vector2.zero; frr.anchorMax = Vector2.one;
            frr.sizeDelta = new Vector2(BackdropFrame * 2f, BackdropFrame * 2f);
            b.Frame = fr.GetComponent<Image>();
            b.Frame.raycastTarget = false;
            if (frameSource != null)
            {
                b.Frame.sprite = frameSource.sprite;
                b.Frame.type = frameSource.type;
                b.Frame.pixelsPerUnitMultiplier = frameSource.pixelsPerUnitMultiplier;
                b.Frame.color = frameSource.color;
            }
            else b.Frame.color = new Color(0f, 0f, 0f, 0.2745f);

            GameObject fi = new GameObject(BackdropName + "Fill", typeof(RectTransform), typeof(Image));
            RectTransform fir = (RectTransform)fi.transform;
            fir.SetParent(r, false);
            fir.anchorMin = Vector2.zero; fir.anchorMax = Vector2.one;
            fir.sizeDelta = Vector2.zero;
            b.Fill = fi.GetComponent<Image>();
            b.Fill.raycastTarget = false;

            go.SetActive(false);
            return b;
        }

        private void ShowBackdrop(Backdrop b, Vector2 center, Vector2 size)
        {
            if (b == null) return;
            Vector2 sz = size + new Vector2(BackdropRoom * 2f, BackdropRoom * 2f);
            if (b.Root.anchoredPosition != center) b.Root.anchoredPosition = center;
            if (b.Root.sizeDelta != sz) b.Root.sizeDelta = sz;
            Color fill = BarBackground(VanillaBarBkg);
            if (b.Fill.color != fill) b.Fill.color = fill;
            if (!b.Root.gameObject.activeSelf) b.Root.gameObject.SetActive(true);
        }

        private static void HideBackdrop(Backdrop b)
        {
            if (b != null && b.Root != null && b.Root.gameObject.activeSelf) b.Root.gameObject.SetActive(false);
        }

        // Runs in the same change-only pass as the damage type icons.
        private void LayoutBackdrops(EnemyUiState state)
        {
            if (state.Kind == HudKind.Player || state.Gui == null) return;
            BackdropSet set = state.Backdrops;
            bool any = _bgStars.Value || _bgName.Value || _bgAlert.Value;
            if (set == null)
            {
                if (!any) return;
                set = state.Backdrops = new BackdropSet();
            }
            Image frame = FrameSource(state);

            // Stars: a child under each star group, behind its stars, so it
            // moves and hides with them.
            LayoutStarBackdrop(ref set.Star2, state.Level2, frame);
            LayoutStarBackdrop(ref set.Star3, state.Level3, frame);

            // Name: a sibling right behind the name text.
            if (_bgName.Value && state.NameRect != null && state.NameText != null)
            {
                if (set.Name == null)
                    set.Name = CreateBackdrop(state.Gui.transform, state.NameRect.GetSiblingIndex(), frame);
                Vector2 pref = state.NameText.GetPreferredValues(state.NameText.text);
                ShowBackdrop(set.Name, state.NameRect.anchoredPosition, new Vector2(pref.x, state.NameText.fontSize));
            }
            else HideBackdrop(set.Name);

            // Alert icon: a sibling behind it. Vanilla turns the icons on and
            // off; a small component on them follows that by event.
            if (state.Kind == HudKind.Enemy && (state.AlertedIcon != null || state.AwareIcon != null))
            {
                if (_bgAlert.Value && set.Alert == null)
                {
                    GameObject first = state.AlertedIcon != null ? state.AlertedIcon : state.AwareIcon;
                    set.Alert = CreateBackdrop(state.Gui.transform, first.transform.GetSiblingIndex(), frame);
                    set.AlertFollower = BackdropFollower.Attach(state.AlertedIcon, state.AwareIcon, set.Alert.Root.gameObject);
                }
                if (set.Alert != null)
                {
                    RectTransform at = (state.AlertedIcon != null ? state.AlertedIcon : state.AwareIcon).transform as RectTransform;
                    ShowBackdrop(set.Alert, at.anchoredPosition, new Vector2(24f, 24f));
                    // ShowBackdrop turns it on; the follower decides.
                    if (set.AlertFollower != null) set.AlertFollower.SetEnabled(_bgAlert.Value);
                }
            }
        }

        private void LayoutStarBackdrop(ref Backdrop b, RectTransform level, Image frame)
        {
            if (level == null) return;
            if (!_bgStars.Value) { HideBackdrop(b); return; }
            bool found = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            for (int i = 0; i < level.childCount; i++)
            {
                RectTransform c = level.GetChild(i) as RectTransform;
                if (c == null || IsBackdrop(c)) continue;
                Vector2 half = c.sizeDelta * 0.5f;
                Vector2 lo = c.anchoredPosition - half, hi = c.anchoredPosition + half;
                if (!found) { min = lo; max = hi; found = true; }
                else { min = Vector2.Min(min, lo); max = Vector2.Max(max, hi); }
            }
            if (!found) { HideBackdrop(b); return; }
            if (b == null) b = CreateBackdrop(level, 0, frame);
            ShowBackdrop(b, (min + max) * 0.5f, max - min);
        }

        // Behind the shown damage icons. Bosses have two groups (either side
        // of the name), enemies one.
        private void LayoutIconBackdrop(EnemyUiState state, WeaknessUi ui, bool split)
        {
            BackdropSet set = state.Backdrops;
            if (!_bgIcons.Value)
            {
                if (set != null) { HideBackdrop(set.IconsA); HideBackdrop(set.IconsB); }
                return;
            }
            if (set == null) set = state.Backdrops = new BackdropSet();
            Image frame = FrameSource(state);
            for (int g = 0; g < (split ? 2 : 1); g++)
            {
                bool found = false;
                Vector2 min = Vector2.zero, max = Vector2.zero;
                for (int i = 0; i < DamageKinds; i++)
                {
                    if (!ui.Show[i]) continue;
                    if (split && (ui.Kind[i] == Weak) != (g == 0)) continue;
                    RectTransform r = ui.Slots[i].rectTransform;
                    Vector2 half = r.sizeDelta * 0.5f;
                    Vector2 lo = r.anchoredPosition - half, hi = r.anchoredPosition + half;
                    if (!found) { min = lo; max = hi; found = true; }
                    else { min = Vector2.Min(min, lo); max = Vector2.Max(max, hi); }
                }
                Backdrop b = g == 0 ? set.IconsA : set.IconsB;
                if (!found) { HideBackdrop(b); continue; }
                if (b == null)
                {
                    b = CreateBackdrop(ui.Root, 0, frame);
                    if (g == 0) set.IconsA = b; else set.IconsB = b;
                }
                ShowBackdrop(b, (min + max) * 0.5f, max - min);
            }
            if (!split) HideBackdrop(set.IconsB);
        }
    }

    // Shows a backdrop while either of two vanilla objects is shown, by
    // their own enable and disable events.
    internal sealed class BackdropFollower : MonoBehaviour
    {
        public GameObject A, B, Target;
        public bool Enabled;
        public BackdropFollower Other;

        internal static BackdropFollower Attach(GameObject a, GameObject b, GameObject target)
        {
            BackdropFollower first = null, second = null;
            if (a != null) first = a.AddComponent<BackdropFollower>();
            if (b != null) second = b.AddComponent<BackdropFollower>();
            if (first == null) { first = second; second = null; }
            if (first == null) return null;
            first.A = a; first.B = b; first.Target = target; first.Other = second;
            if (second != null) { second.A = a; second.B = b; second.Target = target; second.Other = first; }
            return first;
        }

        internal void SetEnabled(bool on)
        {
            Enabled = on;
            if (Other != null) Other.Enabled = on;
            Sync();
        }

        private void OnEnable() { Sync(); }
        private void OnDisable() { Sync(); }

        internal void Sync()
        {
            if (Target == null) return;
            bool on = Enabled && ((A != null && A.activeSelf) || (B != null && B.activeSelf));
            if (Target.activeSelf != on) Target.SetActive(on);
        }
    }
}
