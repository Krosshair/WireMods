using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Status effects that change the creature or player itself, other than
    // the damage-over-time ones on the left: right of the health bar, with
    // the game's own icons. Read from the effect's data: it counts if it
    // changes resistances, movement speed, the damage they deal, stagger or
    // armor, or pulls them (harpoon). Wet and tar do; cold, rested and
    // the like (regeneration, stamina) don't. Shared like the others: the
    // machine running the creature works out the list when an effect
    // starts or ends, up to four.
    public sealed partial class EnemyHudReworkPlugin
    {
        private BepInEx.Configuration.ConfigEntry<bool> _otherEffects;
        private BepInEx.Configuration.ConfigEntry<bool> _playerEffects;
        private const int MaxOtherEffects = 4;
        private static readonly int[] OtherKeys =
        {
            "wire.enemyhud.se0".GetStableHashCode(), "wire.enemyhud.se1".GetStableHashCode(),
            "wire.enemyhud.se2".GetStableHashCode(), "wire.enemyhud.se3".GetStableHashCode()
        };
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> EffectIcons =
            new System.Collections.Generic.Dictionary<int, Sprite>();
        private readonly int[] _otherWork = new int[MaxOtherEffects];
        private readonly int[] _otherPrio = new int[MaxOtherEffects];

        internal sealed class EffectUi
        {
            public RectTransform Root;
            public readonly Image[] Icons = new Image[MaxOtherEffects];
            public readonly int[] Hashes = new int[MaxOtherEffects];
            public uint Revision = uint.MaxValue; public int Layout = -1;
        }

        private static bool AffectsTarget(StatusEffect se)
        {
            if (se == null || se.m_icon == null || StatusBit(se.NameHash()) != 0 || se is SE_Shield) return false;
            if (se is SE_Harpooned) return true;
            SE_Stats s = se as SE_Stats;
            if (s == null) return false;
            HitData.DamageTypes p = s.m_percentigeDamageModifiers;
            return s.m_mods.Count > 0 || s.m_speedModifier != 0f || s.m_damageModifier != 1f ||
                s.m_staggerModifier != 0f || s.m_addArmor != 0f || s.m_armorMultiplier != 0f ||
                p.m_damage != 0f || p.m_blunt != 0f || p.m_slash != 0f || p.m_pierce != 0f || p.m_chop != 0f ||
                p.m_pickaxe != 0f || p.m_fire != 0f || p.m_frost != 0f || p.m_lightning != 0f ||
                p.m_poison != 0f || p.m_spirit != 0f;
        }

        // Creatures use their real resistance/combat-affecting statuses.
        // Players use the broader lifecycle-cached selection in
        // EnemyHudPlayerEffects.cs (six strict player effect groups).
        private bool CountsAsOther(StatusEffect se, Character c)
        {
            if (se == null || se.m_icon == null || StatusBit(se.NameHash()) != 0 ||
                se is SE_Shield) return false;
            if (c is Player) return PlayerEffectGroup(se) >= 0;
            return se.NameHash() == SEMan.s_statusEffectWet || AffectsTarget(se);
        }

        private static int OtherPriority(StatusEffect se, Character c) => 0;

        // ---- Owner side: when an effect starts or ends ----

        private void ShareOtherEffects(Character c, ZDO zdo,
            StatusEffect leaving, StatusEffect starting = null)
        {
            if (c is Player player)
            {
                RebuildPlayerEffects(player, zdo, leaving, starting);
                return;
            }
            // Creatures keep their existing prioritized data-driven rule.
            int n = 0;
            SEMan seman = c.GetSEMan();
            if (seman != null)
                foreach (StatusEffect e in seman.GetStatusEffects())
                {
                    if (e == null || ReferenceEquals(e, leaving) || !CountsAsOther(e, c)) continue;
                    int h = e.NameHash();
                    bool dup = false;
                    for (int i = 0; i < n; i++) if (_otherWork[i] == h) dup = true;
                    if (dup) continue;
                    int pr = OtherPriority(e, c);
                    int at = n;
                    while (at > 0 && _otherPrio[at - 1] > pr) at--;
                    if (at >= MaxOtherEffects) continue;
                    int last = Mathf.Min(n, MaxOtherEffects - 1);
                    for (int i = last; i > at; i--) { _otherWork[i] = _otherWork[i - 1]; _otherPrio[i] = _otherPrio[i - 1]; }
                    _otherWork[at] = h; _otherPrio[at] = pr;
                    if (n < MaxOtherEffects) n++;
                }
            // StatusEffect.Setup may run before SEMan inserts the effect.
            // Creatures need the same start-event handling as players.
            if (starting != null && !ReferenceEquals(starting, leaving) &&
                CountsAsOther(starting, c) && n < MaxOtherEffects)
            {
                int added = starting.NameHash();
                bool alreadyThere = false;
                for (int i = 0; i < n; i++)
                    if (_otherWork[i] == added) { alreadyThere = true; break; }
                if (!alreadyThere) _otherWork[n++] = added;
            }
            for (int i = 0; i < MaxOtherEffects; i++)
                SetSharedInt(zdo, OtherKeys[i], i < n ? _otherWork[i] : 0);
        }

        // ---- Nameplates ----

        private void UpdateOtherEffects(EnemyUiState state, ZDO zdo, uint revision)
        {
            EffectUi ui = state.Effects;
            if (!_otherEffects.Value || (state.Kind == HudKind.Player && !_playerEffects.Value))
            {
                if (ui != null && ui.Root != null && ui.Root.gameObject.activeSelf) ui.Root.gameObject.SetActive(false);
                if (ui != null) { ui.Revision = uint.MaxValue; ui.Layout = -1; }
                return;
            }
            // Only when the shared data or the layout changed, also while
            // there's nothing to show (no GameObject until there is).
            if (ui == null) ui = state.Effects = new EffectUi();
            if (ui.Revision == revision && ui.Layout == _layoutVersion) return;
            ui.Revision = revision;
            // The creature's network data changes all the time in a fight
            // (health, every damage tick of an effect): lay the icons out
            // again only when the effects themselves or the layout changed.
            bool same = ui.Layout == _layoutVersion;
            bool any = false;
            for (int i = 0; i < MaxOtherEffects; i++)
            {
                int h = GetSharedInt(zdo, OtherKeys[i]);
                if (h != 0) any = true;
                if (ui.Hashes[i] != h) { ui.Hashes[i] = h; same = false; }
            }
            if (same && (ui.Root != null || !any)) return;
            ui.Layout = _layoutVersion;
            if (ui.Root == null)
            {
                if (!any) return; // nothing to show, nothing built
                GameObject go = new GameObject("WireOtherEffects", typeof(RectTransform));
                ui.Root = (RectTransform)go.transform;
                ui.Root.SetParent(state.Gui.transform, false);
                ui.Root.anchorMin = ui.Root.anchorMax = ui.Root.pivot = new Vector2(0.5f, 0.5f);
                ui.Root.sizeDelta = Vector2.zero;
            }
            if (ui.Root.gameObject.activeSelf != any) ui.Root.gameObject.SetActive(any);
            if (!any) return;

            // Right of the bar, from its right end outward, the same size
            // and spacing as the effects on the left.
            RectTransform hr = state.HealthRect;
            ui.Root.position = hr.TransformPoint(new Vector3(hr.rect.xMax, hr.rect.center.y, 0f));
            RectTransform star = StarRect(state);
            float size = star != null ? star.rect.height : 15f;
            if (state.Kind == HudKind.Boss) size = Mathf.Max(size, hr.rect.height * 1.6f);
            float step = size + 2f;
            int n = 0;
            for (int i = 0; i < MaxOtherEffects; i++)
            {
                int h = GetSharedInt(zdo, OtherKeys[i]);
                Sprite icon = h != 0 ? EffectIcon(h) : null;
                Image img = ui.Icons[i];
                if (icon != null && img == null)
                {
                    GameObject g = new GameObject("Effect", typeof(RectTransform), typeof(Image));
                    g.transform.SetParent(ui.Root, false);
                    img = ui.Icons[i] = g.GetComponent<Image>();
                    img.raycastTarget = false;
                    img.preserveAspect = true;
                    RectTransform r = img.rectTransform;
                    r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
                }
                if (img == null) continue;
                bool on = icon != null;
                if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
                if (!on) continue;
                img.sprite = icon;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                img.rectTransform.anchoredPosition = new Vector2(3f + size * 0.5f + n * step, 0f);
                n++;
            }
        }

        private static Sprite EffectIcon(int hash)
        {
            Sprite s;
            if (EffectIcons.TryGetValue(hash, out s)) return s;
            StatusEffect def = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(hash) : null;
            s = def != null ? def.m_icon : null;
            if (def != null) EffectIcons[hash] = s; // unknown ones are tried again later
            return s;
        }
    }
}
