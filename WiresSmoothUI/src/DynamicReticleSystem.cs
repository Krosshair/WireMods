using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal sealed class DynamicReticleSystem : MonoBehaviour
    {

        private const float VanillaStartScale = 1f;
        private const float VanillaEndScale = 0.15f;
        private const float VanillaCrosshairIdleAlpha = 0.5f;

        internal static DynamicReticleSystem Instance;
        private Image _customRing;
        private float _ringShownAt;
        private Hud _customRingHud;

        private float _crosshairVisibility;
        private bool _bowReleaseWaitingForZero;
        private GameObject _lastHoverObject;
        private bool _lastHoverInteractable;

        private Attack _activeAttack;
        private ItemDrop.ItemData _activeWeapon;
        private AttackGroup _activeGroup;
        private bool _activeReleased;

        private readonly Dictionary<string, float> _learnedReleaseTimes =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private enum AttackGroup
        {
            None,
            Spear,
            Magic,
            Other
        }

        public enum EasingMode
        {
            Linear,
            SineInOut,
            ExpoInOut
        }

        private static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> ActionQueueRef =
            AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");
        private static readonly AccessTools.FieldRef<Attack, Humanoid> AttackCharacterRef =
            AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<Attack, float> AttackTimeRef =
            AccessTools.FieldRefAccess<Attack, float>("m_time");

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        internal static void Shutdown()
        {
            DynamicReticleSystem instance = Instance;
            if (instance == null)
                return;

            if (instance._customRing != null)
                UnityEngine.Object.Destroy(instance._customRing.gameObject);

            instance._customRing = null;
            instance._customRingHud = null;
            instance._activeAttack = null;
            instance._activeWeapon = null;
            instance._activeGroup = AttackGroup.None;
            instance._activeReleased = false;
            instance._bowReleaseWaitingForZero = false;
            instance._crosshairVisibility = 0f;
            instance._lastHoverObject = null;
            instance._lastHoverInteractable = false;
        }

        internal void UpdateHud(Hud hud, Player player, float bowDrawPercentage)
        {
            if (hud == null || player == null)
                return;

            bool customCrosshair = WiresSmoothUIPlugin.HudEnabled.Value &&
                WiresSmoothUIPlugin.CrosshairAnimations.Value;
            bool customProjectile = WiresSmoothUIPlugin.Enabled.Value &&
                WiresSmoothUIPlugin.ProjectileReticle.Value;

            if (!customCrosshair && !customProjectile)
            {
                HideRing(hud);
                _crosshairVisibility = 0f;
                return;
            }

            if (customProjectile)
            {
                EnsureCustomRing(hud);
                if (hud.m_crosshairBow != null)
                    hud.m_crosshairBow.gameObject.SetActive(false);
            }
            else
                HideRing(hud);

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            ItemDrop.ItemData rightItem = player.RightItem;
            ItemDrop.ItemData leftItem = player.LeftItem;
            if (customCrosshair)
                UpdateNormalCrosshair(hud, player, rightItem, leftItem);
            else
                _crosshairVisibility = 0f;

            if (!customProjectile)
                return;

            if (WiresSmoothUIPlugin.ProjectileReticle.Value && bowDrawPercentage > 0f)
            {
                // Valheim can report the previous bow draw for a frame after firing.
                // Keep our independent ring hidden until the draw value has actually reset to zero.
                if (_bowReleaseWaitingForZero)
                {
                    HideRing(hud);
                    return;
                }

                RenderProgress(hud, bowDrawPercentage, true);
                return;
            }

            if (bowDrawPercentage <= 0f && _bowReleaseWaitingForZero)
                _bowReleaseWaitingForZero = false;

            if (!WiresSmoothUIPlugin.ProjectileReticle.Value)
            {
                HideRing(hud);
                return;
            }

            if (weapon == null || weapon.m_shared == null)
            {
                HideRing(hud);
                return;
            }

            if (weapon.m_shared.m_skillType == Skills.SkillType.Crossbows)
            {
                RenderCrossbow(hud, player, weapon);
                return;
            }

            if (IsDundr(weapon))
            {
                RenderDundr(hud, player, weapon);
                return;
            }

            if (_activeAttack != null && _activeWeapon == weapon)
            {
                RenderActiveAttack(hud);
                return;
            }

            HideRing(hud);
        }

        private void UpdateNormalCrosshair(
            Hud hud,
            Player player,
            ItemDrop.ItemData rightItem,
            ItemDrop.ItemData leftItem)
        {
            Image image = hud.m_crosshair;
            if (image == null)
                return;

            if (player.IsAttached() && player.GetAttachCameraPoint() != null)
            {
                _crosshairVisibility = 0f;
                return;
            }

            bool hoverTarget = false;
            if (WiresSmoothUIPlugin.ShowHoverCrosshair.Value)
            {
                // Vanilla has already resolved the real in-range hover text this frame.
                hoverTarget = hud.m_hoverName != null &&
                    !string.IsNullOrEmpty(hud.m_hoverName.text);

                if (!hoverTarget)
                {
                    GameObject hover = player.GetHoverObject();
                    if (hover != _lastHoverObject)
                    {
                        _lastHoverObject = hover;
                        _lastHoverInteractable = false;

                        if (hover != null)
                        {
                            try
                            {
                                _lastHoverInteractable =
                                    hover.GetComponentInParent<Interactable>() != null;
                            }
                            catch
                            {
                                _lastHoverInteractable = false;
                            }
                        }
                    }

                    hoverTarget = _lastHoverInteractable;
                }
            }

            bool heldTarget =
                ShouldShowForHeldItem(rightItem) ||
                ShouldShowForHeldItem(leftItem);

            bool activator =
                WiresSmoothUIPlugin
                    .CrosshairActivator.Value &&
                MapStatusVisibilitySystem
                    .ActivatorHeld;

            bool show =
                activator ||
                heldTarget ||
                hoverTarget;

            float seconds = WiresSmoothUIPlugin.CrosshairFade.Value;

            _crosshairVisibility = seconds <= 0f
                ? (show ? 1f : 0f)
                : Mathf.MoveTowards(
                    _crosshairVisibility,
                    show ? 1f : 0f,
                    Time.unscaledDeltaTime / seconds);

            Color c;

            if (hoverTarget &&
                WiresSmoothUIPlugin
                    .CrosshairYellowHover.Value)
            {
                // Vanilla interactable hover: yellow and fully opaque.
                c = Color.yellow;
                c.a = _crosshairVisibility;
            }
            else
            {
                // Without the hover highlight, hover only reveals the
                // ordinary half-opacity white crosshair.
                c = Color.white;
                c.a =
                    VanillaCrosshairIdleAlpha *
                    _crosshairVisibility;
            }

            image.color = c;
        }

        private bool ShouldShowForHeldItem(ItemDrop.ItemData item)
        {
            if (item == null ||
                item.m_shared == null)
                return false;

            if (WiresSmoothUIPlugin
                    .CrosshairWithRanged.Value &&
                IsRangedWeaponForCrosshair(item))
            {
                return true;
            }

            ItemDrop.ItemData.ItemType type =
                item.m_shared.m_itemType;

            bool tool =
                IsHammer(item) ||
                type == ItemDrop.ItemData.ItemType.Tool ||
                type == ItemDrop.ItemData.ItemType.Torch;

            if (tool)
            {
                return WiresSmoothUIPlugin
                    .CrosshairWithTools.Value;
            }

            bool melee =
                item.IsWeapon() &&
                !IsRangedWeaponForCrosshair(item);

            return
                melee &&
                WiresSmoothUIPlugin
                    .CrosshairWithMelee.Value;
        }

        private static bool IsActualHeldWeaponOrTool(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
                return false;

            // Valheim's GetCurrentWeapon() falls back to an unarmed pseudo-weapon,
            // so visibility must be based on the actual equipped hand items.
            if (item.IsWeapon())
                return true;

            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.Tool ||
                   type == ItemDrop.ItemData.ItemType.Torch;
        }

        private static bool IsHammer(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared != null &&
                   string.Equals(
                       item.m_shared.m_name,
                       "$item_hammer",
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RenderCrossbow(Hud hud, Player player, ItemDrop.ItemData weapon)
        {
            float reloadProgress;
            if (TryGetReloadProgress(player, weapon, out reloadProgress))
            {
                RenderProgress(hud, reloadProgress);
                return;
            }

            if (player.IsWeaponLoaded())
            {
                RenderProgress(hud, 1f);
                return;
            }

            HideRing(hud);
        }

        private void RenderDundr(Hud hud, Player player, ItemDrop.ItemData weapon)
        {
            // Dundr is handled like a crossbow whenever the game exposes a real reload/minor-action state.
            // That gives it the same load -> hold at 0.15 -> fire behavior.
            float reloadProgress;
            if (TryGetReloadProgress(player, weapon, out reloadProgress))
            {
                RenderProgress(hud, reloadProgress);
                return;
            }

            if (player.IsWeaponLoaded())
            {
                RenderProgress(hud, 1f);
                return;
            }

            // Fallback for versions/items where Dundr's "loading" is represented only by its attack animation.
            if (_activeAttack != null && _activeWeapon == weapon && !_activeReleased)
            {
                RenderActiveAttack(hud);
                return;
            }

            HideRing(hud);
        }

        private bool TryGetReloadProgress(Player player, ItemDrop.ItemData weapon, out float progress)
        {
            progress = 0f;

            List<Player.MinorActionData> actions = ActionQueueRef(player);
            if (actions == null)
                return false;

            for (int i = 0; i < actions.Count; i++)
            {
                Player.MinorActionData action = actions[i];
                if (action == null ||
                    action.m_type != Player.MinorActionData.ActionType.Reload)
                    continue;

                if (action.m_item != null && action.m_item != weapon)
                    continue;

                progress = action.m_duration <= 0f
                    ? 1f
                    : Mathf.Clamp01(action.m_time / action.m_duration);

                return true;
            }

            return false;
        }

        private void RenderActiveAttack(Hud hud)
        {
            if (_activeAttack == null)
            {
                HideRing(hud);
                return;
            }

            if (_activeReleased)
            {
                if (IsSustainedBurstAttack(_activeAttack))
                {
                    RenderProgress(hud, 1f);
                    return;
                }

                HideRing(hud);
                return;
            }

            float elapsed = GetAttackElapsed(_activeAttack);
            float expected = GetExpectedReleaseSeconds(
                _activeAttack,
                _activeWeapon,
                _activeGroup);

            float rawProgress = expected <= 0.001f
                ? 0f
                : Mathf.Clamp01(elapsed / expected);

            // If our first-use estimate is shorter than the animation, reaching 1.0 simply means
            // "loaded": stay at 0.15 until the actual OnAttackTrigger event fires the projectile.
            RenderProgress(hud, rawProgress);
        }

        private void RenderProgress(Hud hud, float rawProgress)
        {
            RenderProgress(hud, rawProgress, true);
        }

        private void RenderProgress(Hud hud, float rawProgress, bool useConfiguredEasing)
        {
            EnsureCustomRing(hud);
            if (_customRing == null)
                return;

            rawProgress = Mathf.Clamp01(rawProgress);

            if (rawProgress <= 0f)
            {
                _customRing.gameObject.SetActive(false);
                return;
            }

            float p = useConfiguredEasing ? Ease(rawProgress) : rawProgress;
            float scale = Mathf.Lerp(GetStartScale(), GetEndScale(), p);

            if (!_customRing.gameObject.activeSelf)
                _ringShownAt = Time.unscaledTime;
            _customRing.gameObject.SetActive(true);
            _customRing.transform.localScale =
                new Vector3(scale, scale, scale);

            // Same bow blueprint: opacity + white->yellow + scale all use one progress value.
            Color ringColor =
                Color.Lerp(new Color(1f, 1f, 1f, 0f), Color.yellow, p);
            float fade = WiresSmoothUIPlugin.ReticleFadeIn.Value;
            if (fade > 0f)
                ringColor.a *= Mathf.Clamp01((Time.unscaledTime - _ringShownAt) / fade);
            _customRing.color = ringColor;
        }

        internal void OnAttackStarted(
            Attack attack,
            Humanoid character,
            ItemDrop.ItemData weapon,
            bool started)
        {
            if (!started || !WiresSmoothUIPlugin.Enabled.Value || !WiresSmoothUIPlugin.ProjectileReticle.Value || attack == null ||
                character != Player.m_localPlayer ||
                weapon == null || weapon.m_shared == null)
                return;

            AttackGroup group = ClassifyAttack(weapon, attack);
            if (group == AttackGroup.None)
                return;

            _activeAttack = attack;
            _activeWeapon = weapon;
            _activeGroup = group;
            _activeReleased = false;
        }

        internal void OnAttackTriggered(Attack attack)
        {
            if (!WiresSmoothUIPlugin.Enabled.Value || !WiresSmoothUIPlugin.ProjectileReticle.Value || attack == null)
                return;

            Humanoid character = GetAttackCharacter(attack);
            if (character != Player.m_localPlayer)
                return;

            ItemDrop.ItemData weapon = character.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared == null)
                return;

            Skills.SkillType skill = weapon.m_shared.m_skillType;

            if (skill == Skills.SkillType.Bows)
            {
                // Hide exactly when Valheim fires the arrow, and suppress the stale draw value
                // until Valheim reports bowDrawPercentage == 0.
                HideRing(Hud.instance);
                _bowReleaseWaitingForZero = true;
                return;
            }

            if (skill == Skills.SkillType.Crossbows)
            {
                HideRing(Hud.instance);
                return;
            }

            if (attack != _activeAttack)
                return;

            float elapsed = GetAttackElapsed(attack);
            if (elapsed > 0.03f && elapsed < 10f)
                _learnedReleaseTimes[GetAttackKey(attack, _activeWeapon)] = elapsed;

            _activeReleased = true;

            if (IsSustainedBurstAttack(attack))
            {
                // Sustained attacks remain at 0.15 until the player stops attacking.
                return;
            }

            // Single-shot projectile weapons disappear on the exact projectile-release event.
            HideRing(Hud.instance);
        }
        internal void OnAttackStopped(Attack attack)
        {
            if (attack == null || attack != _activeAttack)
                return;

            if (_activeReleased &&
                IsSustainedBurstAttack(attack))
            {
                // Sustained attacks disappear immediately when the player stops attacking.
                HideRing(Hud.instance);
            }

            _activeAttack = null;
            _activeWeapon = null;
            _activeGroup = AttackGroup.None;
            _activeReleased = false;
        }

        private AttackGroup ClassifyAttack(
            ItemDrop.ItemData weapon,
            Attack attack)
        {
            if (weapon == null || weapon.m_shared == null ||
                !IsProjectileAttack(attack))
                return AttackGroup.None;

            Skills.SkillType skill = weapon.m_shared.m_skillType;

            if (skill == Skills.SkillType.Bows ||
                skill == Skills.SkillType.Crossbows)
                return AttackGroup.None;

            if (skill == Skills.SkillType.Spears)
                return AttackGroup.Spear;

            if (IsMagicSkill(skill))
            {
                if (IsUtilityMagicStaff(weapon))
                    return AttackGroup.None;
                return AttackGroup.Magic;
            }

            return AttackGroup.Other;
        }

        private bool IsRangedWeaponForCrosshair(ItemDrop.ItemData weapon)
        {
            if (weapon == null || weapon.m_shared == null)
                return false;

            Skills.SkillType skill = weapon.m_shared.m_skillType;

            if (skill == Skills.SkillType.Bows ||
                skill == Skills.SkillType.Crossbows ||
                skill == Skills.SkillType.Spears)
                return true;

            if (IsMagicSkill(skill) && IsUtilityMagicStaff(weapon))
                return false;

            return IsProjectileAttack(weapon.m_shared.m_attack) ||
                   IsProjectileAttack(weapon.m_shared.m_secondaryAttack);
        }

        private float GetExpectedReleaseSeconds(
            Attack attack,
            ItemDrop.ItemData weapon,
            AttackGroup group)
        {
            string key = GetAttackKey(attack, weapon);
            float learned;

            if (_learnedReleaseTimes.TryGetValue(key, out learned) &&
                learned > 0.03f)
                return learned;

            switch (group)
            {
                case AttackGroup.Spear:
                    return 0.55f;
                case AttackGroup.Magic:
                    return 0.42f;
                default:
                    return 0.40f;
            }
        }

        private float GetScaleForRawProgress(float rawProgress)
        {
            float p = Ease(Mathf.Clamp01(rawProgress));
            return Mathf.Lerp(GetStartScale(), GetEndScale(), p);
        }

        private float GetStartScale()
        {
            return VanillaStartScale;
        }

        private float GetEndScale()
        {
            return VanillaEndScale;
        }

        private float Ease(float t)
        {
            t = Mathf.Clamp01(t);

            switch (WiresSmoothUIPlugin.ReticleEasing.Value)
            {
                case EasingMode.Linear:
                    return t;

                case EasingMode.ExpoInOut:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? Mathf.Pow(2f, 20f * t - 10f) / 2f
                        : (2f - Mathf.Pow(2f, -20f * t + 10f)) / 2f;

                case EasingMode.SineInOut:
                default:
                    return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
            }
        }

        private static bool IsSustainedBurstAttack(Attack attack)
        {
            return attack != null && attack.m_projectileBursts > 1;
        }

        private static string GetAttackKey(
            Attack attack,
            ItemDrop.ItemData weapon)
        {
            string itemName =
                weapon != null && weapon.m_shared != null
                    ? (weapon.m_shared.m_name ?? string.Empty)
                    : string.Empty;

            string animation =
                attack != null
                    ? (attack.m_attackAnimation ?? string.Empty)
                    : string.Empty;

            return itemName + "|" + animation + "|" +
                   (attack != null
                       ? attack.m_attackType.ToString()
                       : string.Empty);
        }

        private static float GetAttackElapsed(Attack attack)
        {
            return attack == null ? 0f : AttackTimeRef(attack);
        }

        private static Humanoid GetAttackCharacter(Attack attack)
        {
            return attack == null ? null : AttackCharacterRef(attack);
        }

        private static bool IsProjectileAttack(Attack attack)
        {
            return attack != null &&
                   (attack.m_attackType == Attack.AttackType.Projectile ||
                    attack.m_attackType == Attack.AttackType.TriggerProjectile);
        }

        private static bool IsMagicSkill(Skills.SkillType skill)
        {
            return skill == Skills.SkillType.ElementalMagic ||
                   skill == Skills.SkillType.BloodMagic;
        }

        private static bool IsDundr(ItemDrop.ItemData weapon)
        {
            return weapon != null && weapon.m_shared != null &&
                   string.Equals(
                       weapon.m_shared.m_name,
                       "$item_staff_lightning",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUtilityMagicStaff(ItemDrop.ItemData weapon)
        {
            if (weapon == null || weapon.m_shared == null)
                return false;

            string name = weapon.m_shared.m_name ?? string.Empty;

            switch (name.ToLowerInvariant())
            {
                case "$item_staffshield":
                case "$item_staffskeleton":
                case "$item_staffredtroll":
                case "$item_staff_frostorbs":
                case "$item_staff_spiritcaller":
                    return true;
                default:
                    return false;
            }
        }

        internal static void DisableStealthHud(Hud hud)
        {
            if (Instance == null ||
                !WiresSmoothUIPlugin.HudEnabled.Value ||
                !WiresSmoothUIPlugin.CrosshairAnimations.Value ||
                !WiresSmoothUIPlugin.DisableStealthIndicator.Value ||
                hud == null)
                return;

            if (hud.m_targetedAlert != null)
                hud.m_targetedAlert.SetActive(false);

            if (hud.m_targeted != null)
                hud.m_targeted.SetActive(false);

            if (hud.m_hidden != null)
                hud.m_hidden.SetActive(false);

            if (hud.m_stealthBar != null)
                hud.m_stealthBar.gameObject.SetActive(false);
        }

        private void EnsureCustomRing(Hud hud)
        {
            if (hud == null || hud.m_crosshairBow == null)
                return;

            if (_customRing != null && _customRingHud == hud)
                return;

            if (_customRing != null)
                Destroy(_customRing.gameObject);

            _customRingHud = hud;
            _customRing = Instantiate(
                hud.m_crosshairBow,
                hud.m_crosshairBow.transform.parent,
                false);

            _customRing.name = "DynamicReticleRing";
            _customRing.raycastTarget = false;
            _customRing.transform.SetSiblingIndex(
                hud.m_crosshairBow.transform.GetSiblingIndex() + 1);
            _customRing.gameObject.SetActive(false);
        }

        private void HideRing(Hud hud)
        {
            if (_customRing != null)
                _customRing.gameObject.SetActive(false);
        }

        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class HudAwakePatch
        {
            [HarmonyPostfix]
            private static void Postfix(Hud __instance)
            {
                if (__instance != null &&
                    __instance.GetComponent<DynamicReticleSystem>() == null)
                    __instance.gameObject.AddComponent<DynamicReticleSystem>();
            }
        }

        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        private static class HudUpdateCrosshairPatch
        {
            private static void Postfix(
                Hud __instance,
                Player player,
                float bowDrawPercentage)
            {
                if (Instance != null)
                    Instance.UpdateHud(
                        __instance,
                        player,
                        bowDrawPercentage);
            }
        }

        [HarmonyPatch(typeof(Hud), "UpdateStealth")]
        private static class HudUpdateStealthPatch
        {
            private static void Postfix(Hud __instance)
            {
                DisableStealthHud(__instance);
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        private static class AttackStartPatch
        {
            private static void Postfix(
                Attack __instance,
                Humanoid character,
                ItemDrop.ItemData weapon,
                ref bool __result)
            {
                if (Instance != null)
                    Instance.OnAttackStarted(
                        __instance,
                        character,
                        weapon,
                        __result);
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        private static class AttackTriggerPatch
        {
            private static void Postfix(Attack __instance)
            {
                if (Instance != null)
                    Instance.OnAttackTriggered(__instance);
            }
        }
        [HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
        private static class AttackStopPatch
        {
            private static void Postfix(Attack __instance)
            {
                if (Instance != null)
                    Instance.OnAttackStopped(__instance);
            }
        }

    }
}
