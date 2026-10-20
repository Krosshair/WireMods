using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Bubble bar: the protective bubble (the Staff of Protection's shield,
    // or any shield status effect) shown as a bar in eitr purple, with the
    // gray damage trail of your own stamina bar.
    //  In the world: a bar above the health bar of players and creatures,
    //  built like the stagger bar; the name moves up while it shows.
    //  Your HUD: a copy of your eitr bar running alongside your health bar
    //  on its right, same length, no number; the forsaken power moves over.
    // A bubble's state only exists on the machine that owns the character,
    // so the owner publishes it into the character's network data when it
    // changes (bubble cast, hit, ended). Nothing runs per frame for it.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _bubbleWorld, _bubbleHud;
        private const string SecBubble = "12 - Bubble Bar";

        private static readonly int BubbleMaxKey = "wire.enemyhud.bubble.max".GetStableHashCode();
        private static readonly int BubbleDamageKey = "wire.enemyhud.bubble.dmg".GetStableHashCode();
        // When the bubble runs out by time, in the world's shared clock
        // (milliseconds; 0 = no time limit). A bubble only exists on the
        // machine that owns the character: if that changes hands, or the
        // owner doesn't have this mod, nobody may ever say it ended, so
        // every bar also ends itself at this time.
        private static readonly int BubbleUntilKey = "wire.enemyhud.bubble.until".GetStableHashCode();

        private static long WorldMillis()
        {
            return ZNet.instance != null ? (long)(ZNet.instance.GetTimeSeconds() * 1000.0) : 0L;
        }
        // Your HUD's eitr bar and the gray trail behind it.
        private static readonly Color BubbleColor = new Color(1f, 0.2941177f, 0.9387633f, 1f);
        private static readonly Color BubbleTrail = new Color(0.5f, 0.5f, 0.5f, 1f);
        // A bubble on something hostile to you (a fuling shaman's, which the
        // game draws in blue): the game's UI blue. Decided by who it
        // protects, the same friend-or-foe check the health bar uses, not by
        // where the bubble came from.
        private static readonly Color BubbleColorFoe = new Color(0.3759732f, 0.659875f, 0.8970588f, 1f);
        // How long a broken bubble's empty bar stays, like the stagger bar's
        // one second after it empties.
        private const float BubblePopLinger = 1f;

        private sealed class BubbleUi
        {
            public GameObject Root;
            public RectTransform Rect;
            public GuiBar Fast, Slow;
            public Image Frame;
            public Color FrameOriginal;
        }

        private void BindBubbleConfig()
        {
            _order = 1000;
            _bubbleWorld = Bind(SecBubble, "Bubble bar on nameplates", true,
                "A purple bar above the health bar of players and creatures inside a protective bubble, showing how much it can still take. Other players need Enemy HUD for theirs to show.",
                null, null);
            _bubbleHud = Bind(SecBubble, "Bubble bar on your HUD", true,
                "A purple bar right of your own health bar while you are inside a bubble. The forsaken power moves over to make room.",
                null, null);
            _bubbleWorld.SettingChanged += (s, e) => { foreach (var st in _states.Values) if (st != null) st.BubbleSeen = false; };
            _bubbleHud.SettingChanged += (s, e) => RefreshHudBubble();
        }

        // ---- Owner side: publish on events ----

        private static readonly AccessTools.FieldRef<SE_Shield, float> ShieldTotal =
            AccessTools.FieldRefAccess<SE_Shield, float>("m_totalAbsorbDamage");
        private static readonly AccessTools.FieldRef<SE_Shield, float> ShieldDamage =
            AccessTools.FieldRefAccess<SE_Shield, float>("m_damage");
        private static readonly AccessTools.FieldRef<StatusEffect, Character> SeOwner =
            AccessTools.FieldRefAccess<StatusEffect, Character>("m_character");

        internal void OnShieldChanged(SE_Shield se, bool ended)
        {
            Character c = se != null ? SeOwner(se) : null;
            if (c == null) return;
            // Broken (not run out of time): it stays published as an empty
            // bar for a second, like a stagger bar that emptied, and every
            // bar ends itself at that time.
            bool popped = ended && ShieldTotal(se) > 0f && ShieldDamage(se) > ShieldTotal(se);
            float total = ended && !popped ? 0f : ShieldTotal(se);
            float damage = ended && !popped ? 0f : ShieldDamage(se);
            long until = popped ? WorldMillis() + (long)(BubblePopLinger * 1000f)
                : ended || se.m_ttl <= 0f ? 0L
                : WorldMillis() + (long)(Mathf.Max(0f, se.m_ttl - se.GetDuration()) * 1000f);

            ZNetView view = c.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner())
            {
                ZDO zdo = view.GetZDO();
                if (zdo.GetFloat(BubbleMaxKey) != total) zdo.Set(BubbleMaxKey, total);
                if (zdo.GetFloat(BubbleDamageKey) != damage) zdo.Set(BubbleDamageKey, damage);
                if (zdo.GetLong(BubbleUntilKey) != until) zdo.Set(BubbleUntilKey, until);
            }

            // One line when a bubble starts or ends (not on hits), to trace it.
            if (ended || damage == 0f)
                Logger.LogInfo("Bubble " + (popped ? "popped " : total > 0f ? "on " : "off ") + c.m_name + (view != null && view.IsOwner() ? "" : " (not owner)"));
            InvalidatePredictions();
            if (ReferenceEquals(c, Player.m_localPlayer))
            {
                _hudBubbleTotal = total;
                _hudBubbleDamage = damage;
                if (popped) StartHudBubblePop();
                else StopHudBubblePop();
                RefreshHudBubble();
            }
        }

        // A creature's network data is saved with the world, but its status
        // effects are not: after a reload the bubble is gone while the saved
        // values would still say it's there. When a character starts and
        // this machine owns it, clear them if it has no bubble.
        internal void OnCharacterStart(Character c)
        {
            if (c == null) return;
            ZNetView view = c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            ZDO zdo = view.GetZDO();
            DropOldCriticalCounter(zdo);
            if (zdo.GetFloat(BubbleMaxKey) <= 0f)
            {
                // An ended bubble leaves zeros behind; nothing needs them
                // saved, so they go (an absent value reads as zero too).
                zdo.RemoveFloat(BubbleMaxKey);
                zdo.RemoveFloat(BubbleDamageKey);
                zdo.RemoveLong(BubbleUntilKey);
                return;
            }
            SE_Shield shield = FindShield(c);
            if (shield != null) { OnShieldChanged(shield, false); return; }
            zdo.Set(BubbleMaxKey, 0f);
            zdo.Set(BubbleDamageKey, 0f);
            zdo.Set(BubbleUntilKey, 0L);
        }

        // On login a creature can start before this machine owns it, so the
        // check above skips it; run it again the moment this machine becomes
        // its owner.
        internal void OnOwnerChanged(ZDO zdo)
        {
            if (zdo == null || !zdo.IsOwner() || zdo.GetFloat(BubbleMaxKey) <= 0f || ZNetScene.instance == null) return;
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            Character c = view != null ? view.GetComponent<Character>() : null;
            if (c != null) OnCharacterStart(c);
        }

        private static SE_Shield FindShield(Character c)
        {
            SEMan se = c != null ? c.GetSEMan() : null;
            if (se == null) return null;
            foreach (StatusEffect e in se.GetStatusEffects())
                if (e is SE_Shield s) return s;
            return null;
        }

        // Your HUD after spawning (login, respawn): only what the new
        // character really has.
        internal void OnLocalSpawned(Player p)
        {
            SE_Shield shield = FindShield(p);
            if (shield != null) { OnShieldChanged(shield, false); return; }
            StopHudBubblePop();
            _hudBubbleTotal = 0f;
            _hudBubbleDamage = 0f;
            RefreshHudBubble();
        }

        // Whether a character is inside a bubble right now, as far as its
        // network data says. A bubble swallows a whole hit.
        private static bool HasBubble(ZDO zdo)
        {
            float max = zdo != null ? zdo.GetFloat(BubbleMaxKey) : 0f;
            if (max <= 0f || zdo.GetFloat(BubbleDamageKey) > max) return false;
            long until = zdo.GetLong(BubbleUntilKey);
            return until <= 0L || WorldMillis() <= until;
        }

        // ---- In the world ----

        private Vector2 NameBase(EnemyUiState state)
        {
            return state.NameOriginalPosition + Vector2.up * state.BubbleLift;
        }

        // Per HUD this reads one number from the creature's network data
        // (the same kind of read the game does for its health every frame);
        // the bar is only touched when the bubble actually changed.
        private void UpdateWorldBubble(EnemyUiState state, bool isEnemy)
        {
            if (state == null || state.Kind == HudKind.Boss || state.Character == null || state.HealthRect == null) return;
            // Nothing while the nameplate is hidden: its bars only start
            // once it shows, and they would measure themselves wrong.
            if (state.Gui == null || !state.Gui.activeInHierarchy) return;
            if (state.BubbleView == null) state.BubbleView = state.Character.GetComponent<ZNetView>();
            ZDO zdo = state.BubbleView != null && state.BubbleView.IsValid() ? state.BubbleView.GetZDO() : null;
            if (zdo == null) return;
            // The network data's change counter is one number: the bubble
            // values are only read again when it moved, when friend or foe
            // changed, or or once a timed bubble's end time has passed.
            if (state.BubbleFlashStart >= 0f) UpdateWorldBubbleFlash(state);
            uint revision = zdo.DataRevision;
            if (state.BubbleSeen && revision == state.BubbleRevision && isEnemy == state.BubbleFoe &&
                !(state.BubbleTimed && WorldMillis() > state.BubbleUntil)) return;
            state.BubbleRevision = revision;
            float max = _bubbleWorld.Value ? zdo.GetFloat(BubbleMaxKey) : 0f;
            long until = max > 0f ? zdo.GetLong(BubbleUntilKey) : 0L;
            if (until > 0L && WorldMillis() > until) max = 0f; // ran out of time
            state.BubbleTimed = until > 0L && max > 0f;
            state.BubbleUntil = until;
            float damage = max > 0f ? zdo.GetFloat(BubbleDamageKey) : 0f;
            if (state.BubbleSeen && max == state.BubbleMax && damage == state.BubbleDamage &&
                isEnemy == state.BubbleFoe) return;
            // Popped: it had something left and now has nothing.
            bool popped = state.BubbleSeen && max > 0f && damage > max &&
                state.BubbleMax > 0f && state.BubbleDamage <= state.BubbleMax;
            state.BubbleSeen = true;
            state.BubbleFoe = isEnemy;
            state.BubbleMax = max;
            state.BubbleDamage = damage;

            if (max <= 0f)
            {
                EndWorldBubbleFlash(state);
                if (state.Bubble != null && state.Bubble.Root.activeSelf) state.Bubble.Root.SetActive(false);
                state.BubbleLift = 0f;
                return;
            }
            float value = Mathf.Clamp01((max - damage) / max);
            if (state.Bubble == null) return;

            BubbleUi b = state.Bubble;
            SetBarColor(b.Fast, isEnemy ? BubbleColorFoe : BubbleColor);
            Vector2 pos = state.HealthOriginalPosition + Vector2.up * _staggerVerticalOffset.Value;
            if (b.Rect.anchoredPosition != pos) b.Rect.anchoredPosition = pos;
            if (!b.Root.activeSelf) b.Root.SetActive(true);
            b.Fast.SetValue(value);
            if (b.Slow != null) b.Slow.SetValue(value);
            state.BubbleLift = _staggerVerticalOffset.Value;
            if (popped)
            {
                state.BubbleFlashStart = Time.time;
                UpdateWorldBubbleFlash(state);
            }
        }

        // The pop flash, the same as the stagger bar's: the bar's dark frame
        // lights up. Only runs while the flash lasts.
        private void UpdateWorldBubbleFlash(EnemyUiState state)
        {
            BubbleUi b = state.Bubble;
            float duration = GetPlayerStaggerFlashDuration();
            float t = duration > 0f ? (Time.time - state.BubbleFlashStart) / duration : 1f;
            float alpha = VanillaFlash(t);
            if (b != null) BlendFrame(b.Frame, b.FrameOriginal, alpha, ref state.BubbleFlashShown);
            if (alpha <= 0f) state.BubbleFlashStart = -1f;
        }

        private void EndWorldBubbleFlash(EnemyUiState state)
        {
            if (state.BubbleFlashStart < 0f) return;
            state.BubbleFlashStart = -1f;
            if (state.Bubble != null) BlendFrame(state.Bubble.Frame, state.Bubble.FrameOriginal, 0f, ref state.BubbleFlashShown);
        }

        // A copy of the health bar, like the stagger bar, back at the
        // health bar's original size and with only its two fills.
        private BubbleUi CreateWorldBubble(EnemyUiState state)
        {
            if (state == null || state.HealthRect == null) return null;
            GameObject root = Instantiate(state.HealthRect.gameObject, state.HealthRect.parent, false);
            root.name = "WireBubble";
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true)) DestroyImmediate(t.gameObject);

            var b = new BubbleUi { Root = root, Rect = (RectTransform)root.transform };
            b.Rect.localScale = state.HealthOriginalScale;
            Transform fast = root.transform.Find("health_fast");
            Transform slow = root.transform.Find("health_slow");
            Transform friendly = root.transform.Find("health_fast_friendly");
            if (friendly != null) friendly.gameObject.SetActive(false);
            b.Fast = fast != null ? fast.GetComponent<GuiBar>() : null;
            b.Slow = slow != null ? slow.GetComponent<GuiBar>() : null;
            if (b.Fast == null) { Destroy(root); return null; }
            if (!b.Fast.gameObject.activeSelf) b.Fast.gameObject.SetActive(true);
            b.Fast.SetColor(BubbleColor);
            if (b.Slow != null) b.Slow.SetColor(BubbleTrail);
            b.Frame = Child(b.Rect, "darken");
            Capture(b.Frame, out b.FrameOriginal);
            root.SetActive(false);
            return b;
        }

        private static void SetBarColor(GuiBar bar, Color color)
        {
            Image img = bar != null && bar.m_bar != null ? bar.m_bar.GetComponent<Image>() : null;
            if (img != null) img.color = color;
        }

        private static void DestroyWorldBubble(EnemyUiState state)
        {
            if (state?.Bubble?.Root != null) Destroy(state.Bubble.Root);
            if (state != null) { state.Bubble = null; state.BubbleLift = 0f; state.BubbleFlashStart = -1f; }
        }

        // ---- Your HUD ----

        private RectTransform _hudBubble;
        private CanvasGroup _hudBubbleGroup;
        private GuiBar _hudBubbleFast, _hudBubbleSlow;
        private RectTransform _gpRoot;
        private Vector2 _gpBase;
        private float _gpShift;
        private float _hudBubbleTotal, _hudBubbleDamage;
        private float _hudBubbleAlpha;
        private Coroutine _hudBubbleFade, _hudBubblePop;
        private Image _hudBubbleFrame;
        private Color _hudBubbleFrameOriginal;
        private const float HudBubbleGap = 8f;
        private const float HudBubbleFadeTime = 0.25f;

        // Built when the HUD is created, before Smooth UI looks at the health
        // panel, so Smooth UI's health fading includes it on its own.
        // It is a copy of your eitr bar (its own shading, purple fill and
        // gray trail, made for this thickness), turned and stretched to run
        // alongside your health bar: same length, same direction, so the
        // same share of each bar means the same share of what is left.
        internal void BuildHudBubble(Hud hud)
        {
            if (hud == null || hud.m_healthBarRoot == null || hud.m_healthPanel == null ||
                hud.m_eitrBarFast == null) return;
            RectTransform health = hud.m_healthBarRoot;
            Transform eitr = hud.m_eitrBarFast.transform.parent;
            if (eitr == null) return;

            GameObject root = Instantiate(eitr.gameObject, health.parent, false);
            root.name = "HealthBubble";
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true)) DestroyImmediate(t.gameObject);

            RectTransform rect = (RectTransform)root.transform;
            float length = health.rect.width;
            float healthThick = health.rect.height;
            float thick = ((RectTransform)eitr).rect.height;
            if (thick <= 0f) thick = healthThick * 0.5f;
            rect.anchorMin = health.anchorMin;
            rect.anchorMax = health.anchorMax;
            rect.pivot = health.pivot;
            rect.localRotation = health.localRotation;
            rect.localScale = health.localScale;
            rect.sizeDelta = new Vector2(length, thick);
            // Right of the health bar: across its thickness, whatever way the
            // bar is turned.
            Vector2 across = -(Vector2)(health.localRotation * Vector3.up);
            float shift = healthThick * 0.5f + HudBubbleGap + thick * 0.5f;
            rect.anchoredPosition = health.anchoredPosition + across * shift;

            _bubbleNotches.Clear();
            foreach (GuiBar bar in root.GetComponentsInChildren<GuiBar>(true))
            {
                if (bar.name.ToLowerInvariant().Contains("slow")) _hudBubbleSlow = bar;
                else _hudBubbleFast = bar;
                PrepareBubbleFill(bar);
            }
            _hudBubbleLength = -1f;
            _hudBubble = rect;
            OnHealthBarSize(length);

            _hudBubbleFrame = Child(rect, "darken");
            Capture(_hudBubbleFrame, out _hudBubbleFrameOriginal);

            _hudBubbleGroup = root.AddComponent<CanvasGroup>();
            _hudBubbleGroup.alpha = 0f;
            _hudBubbleGroup.interactable = false;
            _hudBubbleGroup.blocksRaycasts = false;
            _hudBubble = rect;
            _hudBubbleAlpha = 0f;
            root.SetActive(false);

            _gpRoot = hud.m_gpRoot;
            _gpBase = _gpRoot != null ? _gpRoot.anchoredPosition : Vector2.zero;
            _gpShift = HudBubbleGap + thick;
            _hudBubbleTotal = 0f;
            _hudBubbleDamage = 0f;
        }

        // The game sizes your health bar from your max health; the bubble
        // bar follows it exactly. Only does anything when the size changes.
        private float _hudBubbleLength = -1f;

        internal void OnHealthBarSize(float size)
        {
            if (_hudBubble == null || size <= 0f || size == _hudBubbleLength) return;
            _hudBubbleLength = size;
            _hudBubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
            FitBubbleFill(_hudBubbleFast);
            FitBubbleFill(_hudBubbleSlow);
        }

        // The fills follow the bar's length right away: the same share of a
        // longer bar is a longer fill. Set on the fill itself too, so it is
        // right even before the bar has updated itself.
        // The fill's texture is a small tile with a dark one-pixel column on
        // each side, repeated along the bar: on your health bar that makes a
        // notch every 25 health. On the bubble that would mark health, not
        // bubble, so the fill uses the same texture without those columns
        // (seamless), and the notch column is placed by itself at each
        // quarter of the bubble: three notches, none at the very ends.
        private static readonly float[] BubbleQuarters = { 0.25f, 0.5f, 0.75f };
        private readonly System.Collections.Generic.List<RectTransform> _bubbleNotches =
            new System.Collections.Generic.List<RectTransform>();

        private void PrepareBubbleFill(GuiBar bar)
        {
            Image img = bar != null && bar.m_bar != null ? bar.m_bar.GetComponent<Image>() : null;
            if (img == null || img.sprite == null || img.sprite.texture == null) return;
            Sprite s = img.sprite;
            Rect r = s.textureRect;
            if (r.width < 4f) return;
            Sprite plain = Sprite.Create(s.texture, new Rect(r.x + 1f, r.y, r.width - 2f, r.height),
                new Vector2(0.5f, 0.5f), s.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            plain.hideFlags = HideFlags.HideAndDontSave;
            img.sprite = plain;
            // The notch is that dark column, read from the middle of its
            // pixel only: a one pixel wide picture would be smoothed into
            // its bright neighbours and come out faint.
            Texture tex = s.texture;
            Rect notchUv = new Rect((r.x + 0.5f) / tex.width, r.y / tex.height,
                0.0001f, r.height / tex.height);

            // Notches only show on the filled part: the fill clips them.
            // A stencil mask, because the rectangle kind (RectMask2D) clips
            // away everything on a turned bar, and this one is turned.
            if (bar.m_bar.GetComponent<Mask>() == null) bar.m_bar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            // One pixel of the texture, measured on the bar itself (its
            // thickness is the texture's height), the same as on your health
            // bar.
            float texel = bar.m_bar.rect.height > 0f ? bar.m_bar.rect.height / r.height : 1f;
            for (int i = 0; i < BubbleQuarters.Length; i++)
            {
                GameObject go = new GameObject("HealthBubbleNotch", typeof(RectTransform), typeof(RawImage));
                RectTransform nr = (RectTransform)go.transform;
                nr.SetParent(bar.m_bar, false);
                nr.anchorMin = new Vector2(0f, 0f);
                nr.anchorMax = new Vector2(0f, 1f);
                nr.pivot = new Vector2(0.5f, 0.5f);
                nr.sizeDelta = new Vector2(texel * 2f, 0f); // the two columns where tiles meet
                RawImage ni = go.GetComponent<RawImage>();
                ni.texture = tex;
                ni.uvRect = notchUv;
                ni.material = img.material;
                ni.color = img.color;
                ni.raycastTarget = false;
                _bubbleNotches.Add(nr);
            }
        }

        private void FitBubbleFill(GuiBar bar)
        {
            if (bar == null || bar.m_bar == null || _hudBubbleLength <= 0f) return;
            bar.SetWidth(_hudBubbleLength);
            bar.m_bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _hudBubbleLength * bar.GetSmoothValue());
            for (int i = 0; i < _bubbleNotches.Count; i++)
            {
                RectTransform nr = _bubbleNotches[i];
                if (nr == null || nr.parent != bar.m_bar) continue;
                Vector2 pos = new Vector2(_hudBubbleLength * BubbleQuarters[i % BubbleQuarters.Length], 0f);
                if (nr.anchoredPosition != pos) nr.anchoredPosition = pos;
            }
        }

        private void RefreshHudBubble()
        {
            if (_hudBubble == null) return;
            bool show = _bubbleHud.Value && _hudBubbleTotal > 0f;
            if (show)
            {
                float value = Mathf.Clamp01((_hudBubbleTotal - _hudBubbleDamage) / _hudBubbleTotal);
                if (!_hudBubble.gameObject.activeSelf) _hudBubble.gameObject.SetActive(true);
                // A bar that only just started measures itself; make sure it
                // uses the full length.
                FitBubbleFill(_hudBubbleFast);
                FitBubbleFill(_hudBubbleSlow);
                if (_hudBubbleFast != null) _hudBubbleFast.SetValue(value);
                if (_hudBubbleSlow != null) _hudBubbleSlow.SetValue(value);
            }
            float target = show ? 1f : 0f;
            // The forsaken power just moves, no sliding: over when the bar
            // appears, back once it has faded out.
            if (show) SetGpShifted(true);
            if (_hudBubbleAlpha == target) return;
            if (_hudBubbleFade != null) StopCoroutine(_hudBubbleFade);
            _hudBubbleFade = StartCoroutine(FadeHudBubble(target));
        }

        // Runs only while fading in or out.
        private IEnumerator FadeHudBubble(float target)
        {
            while (_hudBubble != null && _hudBubbleAlpha != target)
            {
                _hudBubbleAlpha = Mathf.MoveTowards(_hudBubbleAlpha, target, Time.unscaledDeltaTime / HudBubbleFadeTime);
                ApplyHudBubbleAlpha();
                yield return null;
            }
            if (_hudBubble != null && target <= 0f)
            {
                _hudBubble.gameObject.SetActive(false);
                SetGpShifted(false);
            }
            _hudBubbleFade = null;
        }

        // Your bubble broke: the empty bar flashes like your stagger bar,
        // stays a second, then goes.
        private void StartHudBubblePop()
        {
            StopHudBubblePop();
            if (_hudBubble != null && _bubbleHud.Value) _hudBubblePop = StartCoroutine(HudBubblePop());
            else { _hudBubbleTotal = 0f; _hudBubbleDamage = 0f; }
        }

        private void StopHudBubblePop()
        {
            if (_hudBubblePop != null) StopCoroutine(_hudBubblePop);
            _hudBubblePop = null;
            if (_hudBubbleFrame != null && _hudBubbleFrame.color != _hudBubbleFrameOriginal) _hudBubbleFrame.color = _hudBubbleFrameOriginal;
        }

        // Runs only for that one second.
        private IEnumerator HudBubblePop()
        {
            float start = Time.time;
            float duration = GetPlayerStaggerFlashDuration();
            while (Time.time - start < BubblePopLinger)
            {
                float t = duration > 0f ? (Time.time - start) / duration : 1f;
                if (_hudBubbleFrame != null)
                {
                    Color c = Color.Lerp(_hudBubbleFrameOriginal, Color.white, VanillaFlash(t));
                    if (_hudBubbleFrame.color != c) _hudBubbleFrame.color = c;
                }
                yield return null;
            }
            if (_hudBubbleFrame != null) _hudBubbleFrame.color = _hudBubbleFrameOriginal;
            _hudBubblePop = null;
            _hudBubbleTotal = 0f;
            _hudBubbleDamage = 0f;
            RefreshHudBubble();
        }

        private void ApplyHudBubbleAlpha()
        {
            float eased = _hudBubbleAlpha * _hudBubbleAlpha * (3f - 2f * _hudBubbleAlpha);
            if (_hudBubbleGroup != null) _hudBubbleGroup.alpha = eased;
        }

        private void SetGpShifted(bool shifted)
        {
            if (_gpRoot == null) return;
            Vector2 pos = _gpBase + (shifted ? Vector2.right * _gpShift : Vector2.zero);
            if (_gpRoot.anchoredPosition != pos) _gpRoot.anchoredPosition = pos;
        }
    }

    internal static class BubblePatches
    {
        private static EnemyHudReworkPlugin P => EnemyHudReworkPlugin.Instance;

        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class HudAwake
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Hud __instance)
            {
                if (P != null) P.BuildHudBubble(__instance);
            }
        }

        [HarmonyPatch(typeof(Hud), "SetHealthBarSize")]
        private static class HealthSize
        {
            private static void Postfix(Hud __instance) { if (P != null && __instance.m_healthBarRoot != null) P.OnHealthBarSize(__instance.m_healthBarRoot.rect.width); }
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwnerInternal))]
        private static class OwnerChanged { private static void Postfix(ZDO __instance) { P?.OnOwnerChanged(__instance); } }

        [HarmonyPatch(typeof(Character), "Start")]
        private static class CharacterStart { private static void Postfix(Character __instance) { P?.OnCharacterStart(__instance); } }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Spawned
        {
            private static void Postfix(Player __instance)
            {
                if (P != null && ReferenceEquals(__instance, Player.m_localPlayer)) P.OnLocalSpawned(__instance);
            }
        }

        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.SetLevel))]
        private static class ShieldSet { private static void Postfix(SE_Shield __instance) { P?.OnShieldChanged(__instance, false); } }

        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
        private static class ShieldHit { private static void Postfix(SE_Shield __instance) { P?.OnShieldChanged(__instance, false); } }

        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Stop))]
        private static class ShieldEnd
        {
            private static void Postfix(StatusEffect __instance)
            {
                if (__instance is SE_Shield shield) P?.OnShieldChanged(shield, true);
            }
        }
    }
}
