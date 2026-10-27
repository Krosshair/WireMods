using HarmonyLib;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Damage type status effects (burning, frozen, shocked, poisoned,
    // spirit), left of the health bar: your damage type icons for them, or
    // the game's status icons with Icon style on Game icons. Nothing else. A status effect only exists on the machine that owns
    // the creature, so the owner keeps one number in the creature's network
    // data with a bit per effect, changed only when an effect starts or
    // ends. Nameplates read it again only when that data changed.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _statusEffects;
        private static readonly int StatusKey = "wire.enemyhud.status".GetStableHashCode();
        private static readonly int[] StatusHashes =
        {
            SEMan.s_statusEffectBurning, SEMan.s_statusEffectFrost, SEMan.s_statusEffectLightning,
            SEMan.s_statusEffectPoison, SEMan.s_statusEffectSpirit
        };
        private static readonly Sprite[] StatusIcon = new Sprite[5];
        // Our own damage type icons for them.
        private static readonly int[] StatusKind =
        {
            (int)DamageKind.Fire, (int)DamageKind.Frost, (int)DamageKind.Lightning,
            (int)DamageKind.Poison, (int)DamageKind.Spirit
        };

        private static int StatusBit(int nameHash)
        {
            for (int i = 0; i < StatusHashes.Length; i++)
                if (StatusHashes[i] == nameHash) return 1 << i;
            return 0;
        }

        private sealed class StatusUi
        {
            public RectTransform Root;
            public readonly Image[] Icons = new Image[5];
            public readonly Image[] Fills = new Image[5];
            public uint Revision = uint.MaxValue; public int Mask = -1; public int Layout = -1;
        }

        // ---- Owner side ----

        internal void OnStatusChanged(StatusEffect se, bool on)
        {
            Prof.Begin(Prof.StatusChanged);
            try { OnStatusChangedTimed(se, on); }
            finally { Prof.End(Prof.StatusChanged); }
        }

        private void OnStatusChangedTimed(StatusEffect se, bool on)
        {
            if (se == null) return;
            // Your own effects starting or running out (meads, powers,
            // rested...) change what your next hit would do.
            if (ReferenceEquals(SeOwner(se), Player.m_localPlayer)) InvalidatePredictions();
            OnResistancesMayChange(SeOwner(se));
            int bit = StatusBit(se.NameHash());
            Character c = SeOwner(se);
            if (c == null) return;
            bool other = bit == 0 && CountsAsOther(se, c);
            if (bit == 0 && !other) return;
            ZNetView view = CharacterView(c);
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            ZDO zdo = view.GetZDO();
            if (other) { ShareOtherEffects(c, zdo, on ? null : se); return; }
            int mask = GetSharedInt(zdo, StatusKey);
            int next = on ? mask | bit : mask & ~bit;
            if (next == mask) return;
            SetSharedInt(zdo, StatusKey, next);
        }

        // Saved creatures lose their status effects on reload, or when
        // another machine takes them over: start from what they really have.
        internal void ResyncStatus(Character c, ZDO zdo)
        {
            if (c == null || zdo == null) return;
            if (zdo.GetInt(OtherKeys[0]) != 0) ShareOtherEffects(c, zdo, null);
            if (zdo.GetInt(StatusKey) == 0) return;
            int mask = 0;
            SEMan se = c.GetSEMan();
            if (se != null)
                foreach (StatusEffect e in se.GetStatusEffects())
                    if (e != null) mask |= StatusBit(e.NameHash());
            if (mask != zdo.GetInt(StatusKey)) zdo.Set(StatusKey, mask);
            if (mask == 0) zdo.RemoveInt(StatusKey);
        }

        // ---- Nameplates ----

        private void UpdateStatusIcons(EnemyUiState state)
        {
            // Creatures, bosses and other players (whose own game shares
            // their effects, when they have the mod too).
            if (state.HealthRect == null || state.Character == null)
                return;
            StatusUi ui = state.Status;
            if (state.BubbleView == null) state.BubbleView = CharacterView(state.Character);
            ZDO zdo = state.BubbleView != null && state.BubbleView.IsValid() ? state.BubbleView.GetZDO() : null;
            if (zdo == null) return;
            uint revision = SharedRevision(zdo);
            UpdateOtherEffects(state, zdo, revision);
            if (!_statusEffects.Value)
            {
                if (ui != null && ui.Root != null && ui.Root.gameObject.activeSelf)
                    ui.Root.gameObject.SetActive(false);
                if (ui != null) ui.Mask = -1;
                return;
            }
            if (ui != null && ui.Revision == revision && ui.Layout == _layoutVersion) return;
            int mask = GetSharedInt(zdo, StatusKey);
            if (ui == null)
            {
                if (mask == 0) return; // nothing to show, nothing built
                ui = state.Status = new StatusUi();
                GameObject go = new GameObject("WireStatusEffects", typeof(RectTransform));
                ui.Root = (RectTransform)go.transform;
                ui.Root.SetParent(state.Gui.transform, false);
                ui.Root.anchorMin = ui.Root.anchorMax = ui.Root.pivot = new Vector2(0.5f, 0.5f);
                ui.Root.sizeDelta = Vector2.zero;
            }
            ui.Revision = revision;
            if (mask == ui.Mask && ui.Layout == _layoutVersion) return;
            ui.Mask = mask; ui.Layout = _layoutVersion;

            bool any = mask != 0;
            if (ui.Root.gameObject.activeSelf != any) ui.Root.gameObject.SetActive(any);
            if (!any) return;

            // Left of the bar, from its left end outward, star-sized.
            RectTransform hr = state.HealthRect;
            Vector3 barLeft = hr.TransformPoint(new Vector3(hr.rect.xMin, hr.rect.center.y, 0f));
            ui.Root.position = barLeft;
            RectTransform star = StarRect(state);
            float size = star != null ? star.rect.height : 15f;
            if (state.Kind == HudKind.Boss) size = Mathf.Max(size, hr.rect.height * 1.6f);
            float step = size + 2f;
            int n = 0;
            // Your damage type icons (shape and color from Damage Icons),
            // built like the weakness icons; the game's status icons only
            // with Icon style set to Game icons.
            bool game = _iconStyle.Value == IconStyle.GameIcons;
            if (!game) LoadDamageSprites();
            for (int i = 0; i < StatusHashes.Length; i++)
            {
                bool on = (mask & (1 << i)) != 0;
                Sprite back = null, fill = null;
                if (on && game)
                {
                    if (StatusIcon[i] == null && ObjectDB.instance != null)
                    {
                        StatusEffect def = ObjectDB.instance.GetStatusEffect(StatusHashes[i]);
                        StatusIcon[i] = def != null ? def.m_icon : null;
                    }
                    back = StatusIcon[i];
                }
                else if (on && StatusKind[i] >= 0)
                {
                    int shape = _shapeOf[StatusKind[i]];
                    back = DamageBack[shape];
                    fill = DamageFill[shape];
                }
                on = on && back != null && (game || fill != null);

                Image img = ui.Icons[i];
                if (on && img == null)
                {
                    GameObject g = new GameObject("Status", typeof(RectTransform), typeof(Image));
                    g.transform.SetParent(ui.Root, false);
                    img = ui.Icons[i] = g.GetComponent<Image>();
                    img.raycastTarget = false;
                    RectTransform ir = img.rectTransform;
                    ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0.5f, 0.5f);
                    GameObject fg = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                    fg.transform.SetParent(g.transform, false);
                    Image f = ui.Fills[i] = fg.GetComponent<Image>();
                    f.raycastTarget = false;
                    RectTransform fr = f.rectTransform;
                    fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0.5f);
                }
                if (img == null) continue;
                if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
                if (!on) continue;
                Image fi = ui.Fills[i];
                img.sprite = back;
                img.preserveAspect = game;
                img.color = game ? Color.white : Color.black;
                fi.enabled = !game;
                if (!game)
                {
                    fi.sprite = fill;
                    fi.color = _tintOf[StatusKind[i]];
                    fi.rectTransform.sizeDelta = new Vector2(size, size) * (12f / 15f);
                }
                RectTransform r = img.rectTransform;
                r.sizeDelta = new Vector2(size, size);
                r.anchoredPosition = new Vector2(-(3f + size * 0.5f + n * step), 0f);
                n++;
            }
        }
    }

    internal static class StatusPatches
    {
        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Setup))]
        private static class Started
        {
            private static void Postfix(StatusEffect __instance) { EnemyHudReworkPlugin.Instance?.OnStatusChanged(__instance, true); }
        }

        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Stop))]
        private static class Ended
        {
            private static void Postfix(StatusEffect __instance) { EnemyHudReworkPlugin.Instance?.OnStatusChanged(__instance, false); }
        }
    }
}
