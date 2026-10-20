using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Hit predictions: what one hit with the weapon in hand would at least do,
    // worked out with the game's own damage steps (weapon and ammo damage,
    // the attack's multiplier, the lowest skill roll, your status
    // effects, then the target's resistances, world level armor, double
    // damage while staggered and the difficulty scale).
    //  Health bar: the part one hit would take off is drawn over the fill.
    //  Stagger bar: a bar shows where one hit would bring it; the stagger
    //  bar shows while there is a prediction. Only on the creature you
    //  are looking at, popping in and out. Off by default.
    //  Bare hands make no prediction.
    // Your side is recomputed on events (equipment, status effects, skill
    // gains, starting and ending attacks); each bar only when its creature's
    // health or stagger actually changes.
    public sealed partial class EnemyHudReworkPlugin
    {
        private const string PredictPrimary = "Primary, secondary while using it";
        private const string PredictSecondary = "Secondary, primary while using it";

        private ConfigEntry<bool> _hitPredictions;
        private ConfigEntry<string> _predictionAttack;

        // All colors come from the game's own HUD:
        //  health, default: the health bar's own fill at half brightness;
        //  stagger, default: the gray trail of your own stamina bar;
        //  high contrast: the health bar's damage trail (read from the bar
        //  itself, sprite included) and the blue of the game's selection
        //  highlight.
        private static readonly Color StaggerQuiet = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color StaggerLoud = new Color(0.3759732f, 0.659875f, 0.8970588f, 1f);

        private int _predictionVersion = 1;
        // 0 not attacking, 1 primary, 2 secondary.
        private int _attackKind;
        private bool _baseValid;
        private HitData.DamageTypes _baseDamage;
        private float _baseStaggerMultiplier = 1f;
        private float _baseBackstab = 1f;
        private readonly HitData _scratchHit = new HitData();

        private sealed class HitPredictionUi
        {
            public Image HealthOverlay;
            public RectTransform HealthBar;
            public Image StaggerGhost;
            public Image BubbleOverlay;
            public RectTransform BubbleBar;
            public bool BubbleShown;
            public float BubbleMax, BubbleTaken;
            public RectTransform StaggerBar;
            public int Version = -1;
            public float Stagger = -2f, Health = -1f, MaxHealth = -1f;
            public bool Alerted, Bubbled;
            public uint Revision;
            public ZNetView View;
            public bool Staggering, Enemy, HealthShown, StaggerShown;
            public float Fade;
        }

        private ConfigEntry<bool> _predictionHighContrast;
        private ConfigEntry<string> _predictionRoll;
        private const string RollTypical = "Typical hit";
        private const string RollMinimum = "Weakest possible hit";

        private void BindHitPredictionConfig(string section)
        {
            _order = 1000;
            _hitPredictions = Bind(section, "Hit predictions", false,
                "On the enemy you are looking at, show the part of its health bar that one hit with your weapon would take off, and how far it would push its stagger bar. Uses the typical hit by default (see Damage roll). Bare hands show nothing.",
                null, null, SecEnemy, "Hit predictions");
            _predictionAttack = Bind(section, "Attack", PredictPrimary,
                "Which attack the predictions show when you are not attacking. While you do an attack, it always shows that one.",
                new AcceptableValueList<string>(PredictPrimary, PredictSecondary), null, SecEnemy, "Hit prediction attack");
            _predictionRoll = Bind(section, "Damage roll", RollTypical,
                "Every hit rolls its damage between a low and a high value, all equally likely. Typical hit shows the middle of that range; Weakest possible hit shows the bottom, so it never promises more than you deal.",
                new AcceptableValueList<string>(RollTypical, RollMinimum), null);
            _predictionRoll.SettingChanged += (s, e) => InvalidatePredictions();
            _predictionHighContrast = Bind(section, "High contrast", false,
                "Show the health prediction in the yellow of the bar's damage trail and the stagger prediction in the game's UI blue, instead of the quieter defaults (the health bar's own red at half brightness, and the gray trail of your own stamina bar).",
                null, null);
            _hitPredictions.SettingChanged += (s, e) => InvalidatePredictions();
            _predictionAttack.SettingChanged += (s, e) => InvalidatePredictions();
            _predictionHighContrast.SettingChanged += (s, e) => InvalidatePredictions();
        }

        internal void InvalidatePredictions()
        {
            _baseValid = false;
            _predictionVersion++;
        }

        // The last hit of a combo deals the attack's chain bonus (usually
        // double). Known when the attack starts, from the attack itself.
        private float _chainBonus = 1f;
        private static readonly HarmonyLib.AccessTools.FieldRef<Humanoid, Attack> CurrentAttack =
            HarmonyLib.AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static readonly HarmonyLib.AccessTools.FieldRef<Attack, int> ChainLevel =
            HarmonyLib.AccessTools.FieldRefAccess<Attack, int>("m_currentAttackCainLevel");

        internal void OnLocalAttackStarted(Humanoid who, bool secondary)
        {
            _attackKind = secondary ? 2 : 1;
            _chainBonus = 1f;
            Attack a = who != null ? CurrentAttack(who) : null;
            if (a != null && a.m_attackChainLevels > 1 && ChainLevel(a) == a.m_attackChainLevels - 1 && a.m_lastChainDamageMultiplier > 1f)
                _chainBonus = a.m_lastChainDamageMultiplier;
            InvalidatePredictions();
        }

        internal void OnLocalAttackStopped()
        {
            if (_attackKind == 0) return;
            _attackKind = 0;
            _chainBonus = 1f;
            InvalidatePredictions();
        }


        // Your side of the hit, recomputed only after an event.
        private bool EnsureBaseHit(Player local)
        {
            if (_baseValid) return _baseDamage.GetTotalDamage() > 0f;
            _baseValid = true;
            _baseDamage = new HitData.DamageTypes();
            _baseStaggerMultiplier = 1f;
            _baseBackstab = 1f;
            if (local == null || local.IsDead()) return false;

            ItemDrop.ItemData weapon = local.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared == null) return false;
            // Bare hands: no prediction.
            ItemDrop unarmed = UnarmedWeapon(local);
            if (unarmed != null && ReferenceEquals(weapon, unarmed.m_itemData)) return false;
            // The attack you are doing right now, otherwise the default one.
            bool secondary = _attackKind != 0
                ? _attackKind == 2
                : _predictionAttack.Value == PredictSecondary;
            Attack attack = secondary && weapon.HaveSecondaryAttack()
                ? weapon.m_shared.m_secondaryAttack
                : weapon.m_shared.m_attack;
            if (attack == null) return false;

            HitData hit = _scratchHit;
            hit.m_damage = weapon.GetDamage();
            if (!string.IsNullOrEmpty(weapon.m_shared.m_ammoType))
            {
                ItemDrop.ItemData ammo = local.GetAmmoItem();
                Inventory inv = local.GetInventory();
                if (ammo == null || inv == null || !inv.ContainsItem(ammo) || ammo.m_shared.m_ammoType != weapon.m_shared.m_ammoType)
                    ammo = inv != null ? inv.GetAmmoItem(weapon.m_shared.m_ammoType) : null;
                // Needs ammo and has none: it can't attack at all.
                if (ammo == null) return false;
                hit.m_damage.Add(ammo.GetDamage());
            }

            // The skill roll: the game picks a factor evenly anywhere between
            // two bounds, so every value is equally likely and the middle is
            // the typical hit. Optionally the lowest, which never promises
            // more than the hit can deliver.
            float skill = local.GetSkillFactor(weapon.m_shared.m_skillType);
            float center = Mathf.Lerp(0.4f, 1f, skill);
            float low = Mathf.Clamp01(center - 0.15f), high = Mathf.Clamp01(center + 0.15f);
            float roll = _predictionRoll.Value == RollMinimum ? low : (low + high) * 0.5f;
            // Bows: a full draw.
            hit.m_damage.Modify(attack.m_damageMultiplier);
            hit.m_damage.Modify(roll);
            hit.m_damage.Modify(1f + Mathf.Max(0, local.GetLevel() - 1) * 0.5f);
            if (attack.m_damageMultiplierPerMissingHP > 0f)
                hit.m_damage.Modify(1f + (local.GetMaxHealth() - local.GetHealth()) * attack.m_damageMultiplierPerMissingHP);
            if (attack.m_damageMultiplierByTotalHealthMissing > 0f)
                hit.m_damage.Modify(1f + (1f - local.GetHealthPercentage()) * attack.m_damageMultiplierByTotalHealthMissing);
            if (_attackKind != 0 && _chainBonus > 1f) hit.m_damage.Modify(_chainBonus);
            hit.m_staggerMultiplier = attack.m_staggerMultiplier;
            hit.m_skill = weapon.m_shared.m_skillType;
            HitData h = hit;
            local.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref h);
            _baseDamage = h.m_damage;
            _baseStaggerMultiplier = h.m_staggerMultiplier;
            _baseBackstab = weapon.m_shared.m_backstabBonus;
            return _baseDamage.GetTotalDamage() > 0f;
        }

        // Sneak attack: an unalerted creature takes the weapon's backstab bonus.
        private bool WouldBackstab(Character target)
        {
            if (_baseBackstab <= 1f) return false;
            BaseAI ai = target.GetBaseAI();
            return ai != null && !ai.IsAlerted();
        }

        // The target's side, for one creature, in the game's order: sneak
        // attack bonus, double damage while staggered (the game's critical
        // hit), then resistances and weaknesses, armor and difficulty.
        private bool PredictHit(Character target, bool staggering, out float damage, out float stagger)
        {
            damage = 0f; stagger = 0f;
            HitData hit = _scratchHit;
            hit.m_damage = _baseDamage;
            hit.m_staggerMultiplier = _baseStaggerMultiplier;
            if (WouldBackstab(target)) hit.ApplyModifier(_baseBackstab);
            if (staggering && !target.IsPlayer()) hit.ApplyModifier(2f);
            hit.ApplyResistance(target.GetDamageModifiers(), out _);
            if (Game.m_worldLevel > 0 && Game.instance != null)
                hit.ApplyArmor(Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC);
            if (Game.instance != null)
                hit.ApplyModifier(Game.instance.GetDifficultyDamageScaleEnemy(target.transform.position));
            hit.ApplyModifier(Game.m_playerDamageRate);
            damage = hit.m_damage.GetTotalDamage();
            float threshold = target.GetMaxHealth() * target.m_staggerDamageFactor;
            if (target.m_staggerDamageFactor > 0f && threshold > 0f)
                stagger = hit.m_damage.GetTotalStaggerDamage() * hit.m_staggerMultiplier / threshold;
            return damage > 0f;
        }

        // Called from the per-frame HUD update, but it only compares a few
        // numbers unless something changed.
        // Predictions only show on the creature you are looking at, popping
        // in and out.

        private void UpdateHitPrediction(EnemyUiState state, bool isEnemy, Player local, Character looked, float dt)
        {
            if (state == null || state.Character == null || state.Kind == HudKind.Player || state.HealthRect == null) return;
            HitPredictionUi ui = state.Prediction;
            Character c = state.Character;
            // Only hostile creatures: friendly and tamed ones (green bar) and
            // players never get a prediction, nor any work for it. Bosses
            // always do while their bar shows: it sits at the top of the
            // screen, and a boss's hitboxes don't always count as looking
            // at it.
            bool focused = _hitPredictions.Value && isEnemy && !c.IsPlayer() && !c.IsTamed() &&
                (state.Kind == HudKind.Boss || ReferenceEquals(c, looked));
            if (ui == null)
            {
                if (!focused) return;
                ui = state.Prediction = new HitPredictionUi();
            }

            // Starting to look at it: work it out fresh (catches anything that
            // changed while it was not shown, like how many players are near).
            if (focused && ui.Fade <= 0f) ui.Version = -1;
            float target = focused ? 1f : 0f;
            bool fadeChanged = ui.Fade != target;
            if (fadeChanged) ui.Fade = target; // pops in and out, no fade
            if (ui.Fade <= 0f && !fadeChanged && !focused) return; // fully faded out: nothing to do

            // Nothing new is asked of the game here. The stagger values are
            // the ones the stagger bar already read this frame. Health, alert
            // state and bubble live in the creature's network data, whose
            // change counter is a single number: only when it moved are they
            // read again, and only when one of them really changed is the
            // hit worked out again.
            bool barRan = state.StaggerRoot != null && _staggerBarEnabled.Value;
            float stagger = barRan ? state.LastStaggerPercentage : -1f;
            bool staggering = barRan && state.WasStaggering;
            if (ui.View == null) ui.View = c.GetComponent<ZNetView>();
            ZDO pzdo = ui.View != null && ui.View.IsValid() ? ui.View.GetZDO() : null;
            uint revision = pzdo != null ? pzdo.DataRevision : 0u;
            if (ui.Version == _predictionVersion && pzdo != null && revision == ui.Revision &&
                ui.Stagger == stagger && ui.Staggering == staggering && ui.Enemy == isEnemy)
            {
                if (fadeChanged) ApplyPredictionFade(state, ui);
                return;
            }
            ui.Revision = revision;
            float health = c.GetHealth(), max = c.GetMaxHealth();
            BaseAI ai = c.GetBaseAI();
            bool alerted = ai != null && ai.IsAlerted();
            bool bubbled = HasBubble(pzdo);
            float bubbleMax = bubbled ? pzdo.GetFloat(BubbleMaxKey) : 0f;
            float bubbleTaken = bubbled ? pzdo.GetFloat(BubbleDamageKey) : 0f;
            bool same = ui.Version == _predictionVersion && ui.Health == health && ui.MaxHealth == max &&
                ui.Alerted == alerted && ui.Stagger == stagger && ui.Staggering == staggering && ui.Enemy == isEnemy &&
                ui.Bubbled == bubbled && ui.BubbleMax == bubbleMax && ui.BubbleTaken == bubbleTaken;
            if (same)
            {
                if (fadeChanged) ApplyPredictionFade(state, ui);
                return;
            }
            ui.Version = _predictionVersion; ui.Health = health; ui.MaxHealth = max; ui.Alerted = alerted;
            ui.Stagger = stagger; ui.Staggering = staggering; ui.Enemy = isEnemy; ui.Bubbled = bubbled;
            ui.BubbleMax = bubbleMax; ui.BubbleTaken = bubbleTaken;

            float damage = 0f, staggerAdd = 0f;
            bool have = _hitPredictions.Value && isEnemy && !bubbled && max > 0f && health > 0f &&
                EnsureBaseHit(local) && PredictHit(c, staggering, out damage, out staggerAdd);

            // Health: from where it would end up to where it is now.
            float now = Mathf.Clamp01(health / Mathf.Max(0.0001f, max));
            float after = Mathf.Clamp01((health - damage) / Mathf.Max(0.0001f, max));
            Transform fast = state.HealthRect.Find("health_fast");
            ui.HealthShown = have && fast != null && now > after;
            if (ui.HealthShown)
            {
                Image look = null;
                Color color;
                if (_predictionHighContrast.Value)
                {
                    // The damage trail: the bar's own slow fill.
                    GuiBar slow = state.HealthRect.Find("health_slow")?.GetComponent<GuiBar>();
                    look = slow != null && slow.m_bar != null ? slow.m_bar.GetComponent<Image>() : null;
                    color = look != null ? look.color : new Color(1f, 0.8482759f, 0f, 1f);
                }
                else
                {
                    GuiBar gb = fast.GetComponent<GuiBar>();
                    Image fill = gb != null && gb.m_bar != null ? gb.m_bar.GetComponent<Image>() : null;
                    Color b = fill != null ? fill.color : new Color(1f, 0.333f, 0.333f, 1f);
                    color = new Color(b.r * 0.5f, b.g * 0.5f, b.b * 0.5f, 1f);
                }
                PlacePrediction(ref ui.HealthOverlay, ref ui.HealthBar, fast, after, now, true, color, look);
            }

            // Stagger: from the current fill to where it would go.
            ui.StaggerShown = have && stagger >= 0f && !staggering && staggerAdd > 0f && state.StaggerFast != null;
            if (ui.StaggerShown)
                PlacePrediction(ref ui.StaggerGhost, ref ui.StaggerBar, state.StaggerFast.transform,
                    stagger, Mathf.Clamp01(stagger + staggerAdd), false,
                    _predictionHighContrast.Value ? StaggerLoud : StaggerQuiet, null);
            // Bubble: the game lets a bubble take the whole hit as it comes,
            // before the creature's own resistances, armor or bonuses.
            ui.BubbleShown = false;
            BubbleUi bub = state.Bubble;
            if (_hitPredictions.Value && isEnemy && bubbled && bubbleMax > 0f && bub != null && bub.Root.activeSelf &&
                bub.Fast != null && EnsureBaseHit(local))
            {
                float hit = _baseDamage.GetTotalDamage();
                float bNow = Mathf.Clamp01((bubbleMax - bubbleTaken) / bubbleMax);
                float bAfter = Mathf.Clamp01(bNow - hit / bubbleMax);
                if (bNow > bAfter)
                {
                    Color color;
                    Image look = null;
                    if (_predictionHighContrast.Value)
                    {
                        GuiBar slowBar = state.HealthRect.Find("health_slow")?.GetComponent<GuiBar>();
                        look = slowBar != null && slowBar.m_bar != null ? slowBar.m_bar.GetComponent<Image>() : null;
                        color = look != null ? look.color : new Color(1f, 0.8482759f, 0f, 1f);
                    }
                    else
                    {
                        Image fill = bub.Fast.m_bar != null ? bub.Fast.m_bar.GetComponent<Image>() : null;
                        Color b = fill != null ? fill.color : BubbleColorFoe;
                        color = new Color(b.r * 0.5f, b.g * 0.5f, b.b * 0.5f, 1f);
                    }
                    PlacePrediction(ref ui.BubbleOverlay, ref ui.BubbleBar, bub.Fast.transform, bAfter, bNow, true, color, look);
                    ui.BubbleShown = true;
                }
            }
            ApplyPredictionFade(state, ui);
        }

        private static void ApplyPredictionFade(EnemyUiState state, HitPredictionUi ui)
        {
            SetPredictionAlpha(ui.HealthOverlay, ui.HealthShown ? ui.Fade : 0f);
            SetPredictionAlpha(ui.StaggerGhost, ui.StaggerShown ? ui.Fade : 0f);
            SetPredictionAlpha(ui.BubbleOverlay, ui.BubbleShown ? ui.Fade : 0f);
            // The stagger bar shows while its prediction is visible.
            state.PredictShowStagger = ui.StaggerShown && ui.Fade > 0f;
        }

        private static void SetPredictionAlpha(Image img, float alpha)
        {
            if (img == null) return;
            if (alpha <= 0f) { Hide(img); return; }
            Color c = img.color;
            if (c.a != alpha) { c.a = alpha; img.color = c; }
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }

        private static void Hide(Image img)
        {
            if (img != null && img.gameObject.activeSelf) img.gameObject.SetActive(false);
        }

        // A prediction segment between two fractions of the bar.
        // onTop: drawn over the fill (health); otherwise behind it (stagger).
        private static void PlacePrediction(ref Image img, ref RectTransform bar, Transform track,
            float from, float to, bool onTop, Color color, Image look)
        {
            if (bar == null)
            {
                GuiBar gb = track.GetComponent<GuiBar>();
                bar = gb != null ? gb.m_bar : null;
                if (bar == null && track.childCount > 0) bar = track.GetChild(0) as RectTransform;
                if (bar == null) return;
            }
            Image barImage = look != null ? look : bar.GetComponent<Image>();
            if (img == null)
            {
                GameObject go = new GameObject(onTop ? "WireHitPrediction" : "WireStaggerPrediction",
                    typeof(RectTransform), typeof(Image));
                RectTransform r = (RectTransform)go.transform;
                r.SetParent(track, false);
                if (onTop) r.SetAsLastSibling(); else r.SetSiblingIndex(bar.GetSiblingIndex());
                r.pivot = new Vector2(0f, 0.5f);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
            }
            // Same sprite, slicing and material as the bar it copies.
            if (barImage != null)
            {
                if (img.sprite != barImage.sprite) img.sprite = barImage.sprite;
                if (img.type != barImage.type) img.type = barImage.type;
                if (img.material != barImage.material) img.material = barImage.material;
                img.pixelsPerUnitMultiplier = barImage.pixelsPerUnitMultiplier;
            }
            RectTransform rt = img.rectTransform;
            Vector2 aMin = new Vector2(from, 0.5f), aMax = new Vector2(to, 0.5f);
            if (rt.anchorMin != aMin) rt.anchorMin = aMin;
            if (rt.anchorMax != aMax) rt.anchorMax = aMax;
            Vector2 size = new Vector2(0f, bar.sizeDelta.y);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            if (rt.anchoredPosition != Vector2.zero) rt.anchoredPosition = Vector2.zero;
            // Over the health fill, behind the stagger fill. The fade sets
            // the opacity afterwards.
            Color c = new Color(color.r, color.g, color.b, img.color.a);
            if (img.color != c) img.color = c;
        }
    }

    // Events of the local player that change what one hit would do.
    internal static class HitPredictionPatches
    {
        private static EnemyHudReworkPlugin P => EnemyHudReworkPlugin.Instance;

        [HarmonyLib.HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class StartAttack
        {
            private static void Postfix(Humanoid __instance, bool secondaryAttack, bool __result)
            {
                if (__result && P != null && ReferenceEquals(__instance, Player.m_localPlayer))
                    P.OnLocalAttackStarted(__instance, secondaryAttack);
            }
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Attack, Humanoid> AttackCharacter =
            HarmonyLib.AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

        [HarmonyLib.HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
        private static class StopAttack
        {
            private static void Postfix(Attack __instance)
            {
                if (P != null && ReferenceEquals(AttackCharacter(__instance), Player.m_localPlayer))
                    P.OnLocalAttackStopped();
            }
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<SEMan, Character> SeCharacter =
            HarmonyLib.AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        private static void Se(SEMan se)
        {
            if (P != null && se != null && ReferenceEquals(SeCharacter(se), Player.m_localPlayer))
                P.InvalidatePredictions();
        }

        [HarmonyLib.HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short) })]
        private static class AddSeHash { private static void Postfix(SEMan __instance) { Se(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) })]
        private static class AddSe { private static void Postfix(SEMan __instance) { Se(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(int), typeof(bool) })]
        private static class RemoveSeHash { private static void Postfix(SEMan __instance) { Se(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(StatusEffect), typeof(bool) })]
        private static class RemoveSe { private static void Postfix(SEMan __instance) { Se(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveAllStatusEffects))]
        private static class RemoveAllSe { private static void Postfix(SEMan __instance) { Se(__instance); } }

        // Players coming into or leaving the area change the game's
        // difficulty damage scale.
        [HarmonyLib.HarmonyPatch(typeof(Player), "Start")]
        private static class PlayerStart { private static void Postfix() { if (P != null) P.InvalidatePredictions(); } }

        [HarmonyLib.HarmonyPatch(typeof(Player), "OnDestroy")]
        private static class PlayerGone { private static void Postfix() { if (P != null) P.InvalidatePredictions(); } }

        [HarmonyLib.HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class Raise { private static void Postfix() { if (P != null) P.InvalidatePredictions(); } }
    }
}
