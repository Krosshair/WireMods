using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Weakness ring: an outline on a health bar when the weapon you have
    // equipped would hit this creature's weakness or resistance. Judged
    // exactly like vanilla's damage numbers (HitData.ApplyResistance), in
    // vanilla's own colors: yellow like a weak hit, gray like a resisted or
    // immune one. Enemies: inside the bar's edge. Bosses: a margin around it.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _weaknessRing;

        // DamageText.ShowText colors for TextType.Weak and TextType.Resistant.
        private static readonly Color WeakRingColor = new Color(1f, 1f, 0f, 1f);
        private static readonly Color ResistRingColor = new Color(.6f, .6f, .6f, 1f);
        private const float RingFadeSeconds = .1f;
        private const float RingRecheckSeconds = .5f;

        private sealed class WeaknessRing
        {
            public Image Image;
            public int Shown;          // 1 weak, -1 resistant/immune, 0 none
            public int Wanted;
            public float Alpha;
            public float NextCheck;
            public ItemDrop.ItemData Weapon;
            public ItemDrop.ItemData Ammo;
        }

        private readonly Dictionary<EnemyUiState, WeaknessRing> _weaknessRings =
            new Dictionary<EnemyUiState, WeaknessRing>();
        private static readonly HitData RingProbe = new HitData();

        private void BindWeaknessConfig()
        {
            _weaknessRing = Config.Bind(
                "Enemy Health Bar", "Weakness ring", true,
                new ConfigDescription(
                    "Outline a health bar when your equipped weapon hits this creature's weakness (yellow, like vanilla's weak-hit damage numbers) or its resistance (gray, like resisted hits). Also shown around boss health bars.",
                    null,
                    new ConfigurationManagerAttributes { Order = 65 }));
        }

        private void SetupWeaknessRing(EnemyUiState state)
        {
            if (state == null || state.HealthRect == null ||
                (state.Kind != HudKind.Enemy && state.Kind != HudKind.Boss) ||
                _weaknessRings.ContainsKey(state))
                return;
            GameObject go = new GameObject("WireWeaknessRing",
                typeof(RectTransform), typeof(Image));
            go.transform.SetParent(state.HealthRect, false);
            go.transform.SetAsLastSibling(); // above the fills; text is outside this bar
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            go.SetActive(false);
            _weaknessRings[state] = new WeaknessRing { Image = image };
        }

        // Which vanilla damage-number color your current weapon would get.
        private static int JudgeWeapon(Player player, Character target,
            out ItemDrop.ItemData weapon, out ItemDrop.ItemData ammo)
        {
            ammo = null;
            weapon = player != null ? player.GetCurrentWeapon() : null;
            if (weapon == null || target == null) return 0;
            HitData.DamageTypes damage = weapon.GetDamage();
            if (!string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType))
            {
                // Same ammo choice as vanilla Attack.
                Inventory inventory = player.GetInventory();
                ammo = player.GetAmmoItem();
                if (ammo != null && (inventory == null || !inventory.ContainsItem(ammo) ||
                    ammo.m_shared.m_ammoType != weapon.m_shared.m_ammoType))
                    ammo = null;
                if (ammo == null && inventory != null)
                    ammo = inventory.GetAmmoItem(weapon.m_shared.m_ammoType);
                if (ammo != null) damage.Add(ammo.GetDamage());
            }
            RingProbe.m_damage = damage;
            if (RingProbe.GetTotalDamage() <= 0f) return 0;
            HitData.DamageModifier significant;
            RingProbe.ApplyResistance(target.GetDamageModifiers(), out significant);
            switch (significant)
            {
                case HitData.DamageModifier.Weak:
                case HitData.DamageModifier.VeryWeak:
                case HitData.DamageModifier.SlightlyWeak:
                    return 1;
                case HitData.DamageModifier.Resistant:
                case HitData.DamageModifier.VeryResistant:
                case HitData.DamageModifier.SlightlyResistant:
                case HitData.DamageModifier.Immune:
                    return -1;
                default:
                    return 0;
            }
        }

        private void UpdateWeaknessRing(EnemyUiState state, Player player, bool isEnemy)
        {
            WeaknessRing ring;
            if (state == null || !_weaknessRings.TryGetValue(state, out ring) ||
                ring.Image == null)
                return;

            float now = Time.unscaledTime;
            bool enabled = _weaknessRing.Value && player != null &&
                state.Character != null && (isEnemy || state.Kind == HudKind.Boss);
            if (!enabled) ring.Wanted = 0;
            else
            {
                // Recheck at once on a weapon or ammo change, otherwise
                // twice a second (armor and status effects can change it).
                ItemDrop.ItemData weapon = player.GetCurrentWeapon();
                if (weapon != ring.Weapon || now >= ring.NextCheck ||
                    (ring.Ammo != null && ring.Ammo.m_stack <= 0))
                {
                    ring.Wanted = JudgeWeapon(player, state.Character,
                        out ring.Weapon, out ring.Ammo);
                    ring.NextCheck = now + RingRecheckSeconds;
                }
            }

            // Fade out before changing color, then fade in.
            float step = Time.unscaledDeltaTime / RingFadeSeconds;
            if (ring.Wanted != ring.Shown)
            {
                ring.Alpha = Mathf.Max(0f, ring.Alpha - step);
                if (ring.Alpha <= 0f) ring.Shown = ring.Wanted;
            }
            else if (ring.Shown != 0)
                ring.Alpha = Mathf.Min(1f, ring.Alpha + step);

            bool visible = ring.Shown != 0 && ring.Alpha > 0f;
            GameObject go = ring.Image.gameObject;
            if (go.activeSelf != visible) go.SetActive(visible);
            if (!visible) return;

            NativeFrameFlash.FitRing(state.HealthRect, ring.Image,
                state.Kind == HudKind.Boss);
            Color color = ring.Shown > 0 ? WeakRingColor : ResistRingColor;
            color.a = ring.Alpha;
            if (ring.Image.color != color) ring.Image.color = color;
        }

        private void DestroyWeaknessRing(EnemyUiState state)
        {
            WeaknessRing ring;
            if (state == null || !_weaknessRings.TryGetValue(state, out ring))
                return;
            if (ring.Image != null) Destroy(ring.Image.gameObject);
            _weaknessRings.Remove(state);
        }
    }
}
