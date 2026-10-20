using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // Damage-type icons: one shape per damage type, in the level star's
    // own shading, tinted in game like the stars are.
    //  Enemies: in the star row at the right end of the health bar, three
    //  per row, right to left: weaknesses in color, then (optionally)
    //  faint gray resistances. Immunities are left out.
    //  Bosses: weaknesses left of the name, resistances right of it.
    // Values come from the creature's real damage modifiers.
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _weaknessIcons;
        private ConfigEntry<bool> _resistanceIcons;
        private ConfigEntry<bool> _bossWeaknessIcons;
        private ConfigEntry<bool> _bossResistanceIcons;
        private ConfigEntry<bool> _limitedMode;

        // Limited mode: which damage types the weapon in hand (plus its
        // ammo) can deal. Worked out only when equipment changes; a type you
        // switch away from stays shown for a second, then a single timer
        // drops it.
        private ConfigEntry<float> _limitedDelay;
        private int _limitedNow;
        private int _limitedShown;
        private static readonly HarmonyLib.AccessTools.FieldRef<Humanoid, ItemDrop> UnarmedWeapon =
            HarmonyLib.AccessTools.FieldRefAccess<Humanoid, ItemDrop>("m_unarmedWeapon");

        private static int DealableMask(Player local)
        {
            if (local == null) return 0;
            ItemDrop.ItemData weapon = local.GetCurrentWeapon();
            ItemDrop unarmed = UnarmedWeapon(local);
            if (weapon == null || (unarmed != null && ReferenceEquals(weapon, unarmed.m_itemData))) return 0;

            int mask = MaskOf(weapon.GetDamage());
            string ammoType = weapon.m_shared.m_ammoType;
            ItemDrop.ItemData ammo = local.GetAmmoItem();
            if (!string.IsNullOrEmpty(ammoType) && ammo != null && ammo.m_shared.m_ammoType == ammoType &&
                local.GetInventory() != null && local.GetInventory().ContainsItem(ammo))
                mask |= MaskOf(ammo.GetDamage());
            return mask;
        }

        private static int MaskOf(HitData.DamageTypes d)
        {
            int m = 0;
            if (d.m_blunt > 0f) m |= 1 << (int)DamageKind.Blunt;
            if (d.m_slash > 0f) m |= 1 << (int)DamageKind.Slash;
            if (d.m_pierce > 0f) m |= 1 << (int)DamageKind.Pierce;
            if (d.m_fire > 0f) m |= 1 << (int)DamageKind.Fire;
            if (d.m_frost > 0f) m |= 1 << (int)DamageKind.Frost;
            if (d.m_lightning > 0f) m |= 1 << (int)DamageKind.Lightning;
            if (d.m_poison > 0f) m |= 1 << (int)DamageKind.Poison;
            if (d.m_spirit > 0f) m |= 1 << (int)DamageKind.Spirit;
            if (d.m_pickaxe > 0f) m |= 1 << (int)DamageKind.Pickaxe;
            if (d.m_chop > 0f) m |= 1 << (int)DamageKind.Chop;
            return m;
        }

        // Called from equipment events of the local player.
        internal void OnLocalEquipmentChanged()
        {
            if (_limitedMode == null || !_limitedMode.Value) return;
            int now = DealableMask(Player.m_localPlayer);
            if (now == _limitedNow) return;
            _limitedNow = now;
            float delay = _limitedDelay != null ? _limitedDelay.Value : 0f;
            int shown = delay > 0f ? _limitedShown | now : now;
            CancelInvoke(nameof(ExpireLimited));
            if ((shown & ~now) != 0)
                Invoke(nameof(ExpireLimited), delay);
            if (shown != _limitedShown)
            {
                _limitedShown = shown;
                MarkAllWeaknessDirty();
            }
        }

        private void ExpireLimited()
        {
            int now = DealableMask(Player.m_localPlayer);
            _limitedNow = now;
            if (_limitedShown == now) return;
            _limitedShown = now;
            MarkAllWeaknessDirty();
        }

        private void OnLimitedModeChanged()
        {
            CancelInvoke(nameof(ExpireLimited));
            _limitedNow = _limitedShown = DealableMask(Player.m_localPlayer);
            MarkAllWeaknessDirty();
        }

        private bool KindAllowed(int kind)
        {
            return !_limitedMode.Value || (_limitedShown & (1 << kind)) != 0;
        }

        private void MarkAllWeaknessDirty()
        {
            foreach (KeyValuePair<EnemyUiState, WeaknessUi> pair in _weaknessUi)
                pair.Value.Dirty = true;
        }

        private ConfigEntry<int> _iconsPerRow;
        private ConfigEntry<float> _iconScale;
        private ConfigEntry<float> _bossIconHeight;
        private ConfigEntry<float> _bossIconSpacing;

        private enum DamageKind { Blunt, Slash, Pierce, Fire, Frost, Lightning, Poison, Spirit, Pickaxe, Chop }
        private const int DamageKinds = 10;
        private const int Weak = 1, Resist = -1, Immune = -2;

        private static readonly string[] DamageAsset =
            { "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit", "pickaxe", "chop" };

        // Tints, multiplied over the gray shapes (as the game tints stars).
        private static readonly Color[] DamageTint =
        {
            new Color32(232, 232, 232, 255), // blunt: physical, near white
            new Color32(232, 232, 232, 255), // slash: physical, near white
            new Color32(232, 232, 232, 255), // pierce: physical, near white
            new Color32(255, 140, 40, 255),  // fire: orange
            new Color32(80, 155, 255, 255),  // frost: blue
            new Color32(165, 225, 255, 255), // lightning: pale sky blue (slanted rhombus)
            new Color32(120, 210, 70, 255),  // poison: green
            new Color32(90, 255, 200, 255),  // spirit: aqua green
            new Color32(80, 68, 98, 255),    // pickaxe: very dark violet gray
            new Color32(80, 68, 98, 255)     // chop: same as pickaxe, axe head pointing right
        };
        // Resistances: a darker take on vanilla's resisted-hit gray, at 60%:
        // readable, but quieter than the colored weaknesses.
        private static readonly Color ResistTint = new Color32(124, 124, 124, 153); // gray, 60% opacity
        // Capital height of the boss name font, and icon size relative to it.
        private const float BossCapHeight = 0.7f;
        private const float BossIconToCap = 0.8f;

        // Like the star: a 32 px shape tinted black behind (outline), a
        // 16 px shape tinted in color on top.
        private static readonly Sprite[] DamageBack = new Sprite[DamageKinds];
        private static readonly Sprite[] DamageFill = new Sprite[DamageKinds];
        private static bool _spritesLoaded;

        private sealed class WeaknessUi
        {
            public RectTransform Root;
            public Image[] Slots = new Image[DamageKinds];
            public Image[] Fills = new Image[DamageKinds];
            public int[] Kind = new int[DamageKinds];
            public bool[] Show = new bool[DamageKinds];
            public bool KindsReady;
            public bool Dirty = true;
            public int LastKey = -1;
        }

        private readonly Dictionary<EnemyUiState, WeaknessUi> _weaknessUi = new Dictionary<EnemyUiState, WeaknessUi>();

        // ImageConversion.LoadImage, looked up once (only used at load).
        private static readonly System.Reflection.MethodInfo LoadImage =
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        private static void LoadDamageSprites()
        {
            if (_spritesLoaded) return;
            _spritesLoaded = true;
            for (int i = 0; i < DamageKinds; i++)
            {
                DamageBack[i] = LoadSprite(DamageAsset[i]);
                DamageFill[i] = LoadSprite(DamageAsset[i] + "_16");
            }
        }

        private static Sprite LoadSprite(string name)
        {
            try
            {
                using (Stream s = typeof(EnemyHudReworkPlugin).Assembly.GetManifestResourceStream("EnemyHudRework.Assets." + name + ".png"))
                {
                    if (s == null) return null;
                    byte[] data = new byte[s.Length];
                    int read = 0;
                    while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (LoadImage == null || !(bool)LoadImage.Invoke(null, new object[] { tex, data, true })) return null;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    sp.hideFlags = HideFlags.HideAndDontSave;
                    return sp;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[EnemyHUD] Could not load icon " + name + ": " + e.Message);
                return null;
            }
        }

        // ---- Modifiers ----
        private static int Classify(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.Weak:
                case HitData.DamageModifier.VeryWeak:
                case HitData.DamageModifier.SlightlyWeak: return Weak;
                case HitData.DamageModifier.Resistant:
                case HitData.DamageModifier.VeryResistant:
                case HitData.DamageModifier.SlightlyResistant: return Resist;
                case HitData.DamageModifier.Immune:
                case HitData.DamageModifier.Ignore: return Immune;
                default: return 0;
            }
        }

        private static HitData.DamageModifier Modifier(HitData.DamageModifiers m, DamageKind kind)
        {
            switch (kind)
            {
                case DamageKind.Blunt: return m.m_blunt;
                case DamageKind.Slash: return m.m_slash;
                case DamageKind.Pierce: return m.m_pierce;
                case DamageKind.Fire: return m.m_fire;
                case DamageKind.Frost: return m.m_frost;
                case DamageKind.Lightning: return m.m_lightning;
                case DamageKind.Poison: return m.m_poison;
                case DamageKind.Spirit: return m.m_spirit;
                case DamageKind.Pickaxe: return m.m_pickaxe;
                default: return m.m_chop;
            }
        }

        // ---- UI ----
        private void SetupWeaknessIcons(EnemyUiState state)
        {
            if (state == null || state.Kind == HudKind.Player || state.Gui == null ||
                _weaknessUi.ContainsKey(state) ||
                (state.Kind == HudKind.Enemy ? state.HealthRect == null : state.NameText == null))
                return;
            var ui = new WeaknessUi();
            GameObject root = new GameObject("WireWeaknessIcons", typeof(RectTransform));
            ui.Root = (RectTransform)root.transform;
            ui.Root.SetParent(state.Gui.transform, false);
            ui.Root.anchorMin = ui.Root.anchorMax = new Vector2(0.5f, 0.5f);
            ui.Root.pivot = new Vector2(0.5f, 0.5f);
            ui.Root.sizeDelta = Vector2.zero;
            for (int i = 0; i < DamageKinds; i++)
            {
                GameObject go = new GameObject("Damage_" + DamageAsset[i], typeof(RectTransform), typeof(Image));
                RectTransform r = (RectTransform)go.transform;
                r.SetParent(ui.Root, false);
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                Image img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.color = Color.black;
                GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                RectTransform fr = (RectTransform)fill.transform;
                fr.SetParent(r, false);
                fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0.5f);
                Image fimg = fill.GetComponent<Image>();
                fimg.raycastTarget = false;
                go.SetActive(false);
                ui.Slots[i] = img;
                ui.Fills[i] = fimg;
            }
            root.SetActive(false);
            _weaknessUi[state] = ui;
            state.Weakness = ui;
        }

        private static RectTransform StarRect(EnemyUiState state)
        {
            if (state.Level2 == null) return null;
            Image img = state.Level2.GetComponent<Image>();
            if (img == null || img.sprite == null) img = state.Level2.GetComponentInChildren<Image>(true);
            return img != null ? img.rectTransform : null;
        }

        private static void HideWeaknessIcons(WeaknessUi ui)
        {
            if (ui.Root.gameObject.activeSelf) ui.Root.gameObject.SetActive(false);
        }

        // A creature's modifiers don't change on their own: read once.
        private static void RefreshWeaknessKinds(WeaknessUi ui, Character character)
        {
            if (ui.KindsReady) return;
            ui.KindsReady = true;
            HitData.DamageModifiers mods = character.GetDamageModifiers();
            for (int i = 0; i < DamageKinds; i++)
                ui.Kind[i] = Classify(Modifier(mods, (DamageKind)i));
        }

        // Laid out only when something changes: the icons are children of
        // the HUD, so they follow it on their own. Changes are config edits,
        // limited mode equipment changes, the star row moving, and the
        // enemy turning friendly or hostile.
        private void UpdateWeaknessIcons(EnemyUiState state, bool isEnemy)
        {
            WeaknessUi ui = state != null ? state.Weakness : null;
            if (ui == null || ui.Root == null) return;
            int key = state.Kind == HudKind.Boss
                ? (state.NameText != null && state.NameText.gameObject.activeInHierarchy ? 1 : 0)
                : (isEnemy ? 1 : 0);
            if (!ui.Dirty && key == ui.LastKey) return;
            ui.LastKey = key;
            ui.Dirty = false;
            if (state.Kind == HudKind.Boss) UpdateBossWeaknessIcons(state, ui);
            else UpdateEnemyWeaknessIcons(state, ui, isEnemy);
        }

        private void UpdateEnemyWeaknessIcons(EnemyUiState state, WeaknessUi ui, bool isEnemy)
        {
            RectTransform star = StarRect(state);
            if (!_weaknessIcons.Value || !isEnemy || state.Character == null ||
                state.Character.IsTamed() || star == null)
            {
                HideWeaknessIcons(ui);
                return;
            }
            LoadDamageSprites();
            RefreshWeaknessKinds(ui, state.Character);

            // Exactly the star's construction: outline box, inner fill box
            // and spacing, read from the HUD's own star.
            if (!ui.Root.gameObject.activeSelf) ui.Root.gameObject.SetActive(true);
            float rootScale = Mathf.Max(0.0001f, ui.Root.lossyScale.y);
            float size = star.rect.height * star.lossyScale.y / rootScale * _iconScale.Value;
            float fillRatio = 12f / 15f;
            if (star.childCount > 0)
            {
                RectTransform inner = star.GetChild(0) as RectTransform;
                if (inner != null && star.rect.height > 0.01f) fillRatio = inner.rect.height / star.rect.height;
            }
            float step = size * 16f / 15f;

            // The star row, right end of the health bar.
            RectTransform hr = state.HealthRect;
            Vector3 barRight = hr.TransformPoint(new Vector3(hr.rect.xMax, hr.rect.center.y, 0f));
            Vector3 starCenter = star.TransformPoint(star.rect.center);
            ui.Root.position = new Vector3(barRight.x, starCenter.y, ui.Root.position.z);
            float right = -size * 0.5f;

            // Right to left, three per row, then the next row below:
            // weaknesses (color) first, then resistances (faint gray).
            bool[] show = ui.Show;
            for (int i = 0; i < DamageKinds; i++) show[i] = false;
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int want = pass == 0 ? Weak : Resist;
                if (want != Weak && !_resistanceIcons.Value) break;
                for (int i = 0; i < DamageKinds; i++)
                {
                    if (ui.Kind[i] != want || !KindAllowed(i)) continue;
                    int perRow = Mathf.Max(1, _iconsPerRow.Value);
                    float x = right - (n % perRow) * step;
                    float y = -(n / perRow) * step;
                    Color tint = IconTint(i, want == Weak);
                    show[i] = Place(ui, i, x, y, size, fillRatio, tint, Color.black, false);
                    if (show[i]) n++;
                }
            }
            ApplyShown(ui);
        }

        // Bosses have no star row: one line through the name, weaknesses
        // to its left (outward from the name), resistances
        // to its right, standing on the name's baseline. Icon size follows
    // the name's capital letters.
        private void UpdateBossWeaknessIcons(EnemyUiState state, WeaknessUi ui)
        {
            TMP_Text name = state.NameText;
            bool weak = _bossWeaknessIcons.Value, resist = _bossResistanceIcons.Value;
            if ((!weak && !resist) || name == null || state.Character == null ||
                !name.gameObject.activeInHierarchy)
            {
                HideWeaknessIcons(ui);
                return;
            }
            Bounds b = name.textBounds;
            if (b.size.x <= 0.01f)
            {
                HideWeaknessIcons(ui);
                ui.Dirty = true; // text not laid out yet, try next frame
                return;
            }
            LoadDamageSprites();
            RefreshWeaknessKinds(ui, state.Character);
            if (!ui.Root.gameObject.activeSelf) ui.Root.gameObject.SetActive(true);

            RectTransform nr = name.rectTransform;
            float sx = nr.lossyScale.x / Mathf.Max(0.0001f, ui.Root.lossyScale.x);
            float sy = nr.lossyScale.y / Mathf.Max(0.0001f, ui.Root.lossyScale.y);
            float capHeight = name.fontSize * BossCapHeight;
            // Icons stand on the name's baseline, like the letters do.
            float baseline = name.textInfo != null && name.textInfo.lineCount > 0
                ? name.textInfo.lineInfo[0].baseline : b.min.y;
            Vector3 foot = nr.TransformPoint(new Vector3(b.center.x, baseline, 0f));
            ui.Root.position = new Vector3(foot.x, foot.y, ui.Root.position.z);

            float size = capHeight * BossIconToCap * sy * _iconScale.Value;
            float step = size * 16f / 15f;
            // Spacing pushes both sides outward, mirrored.
            float edge = b.extents.x * sx + size * 0.5f + size * 0.6f + _bossIconSpacing.Value;
            float iconY = size * 0.5f + _bossIconHeight.Value;

            bool[] show = ui.Show;
            for (int i = 0; i < DamageKinds; i++) show[i] = false;
            int left = 0, right = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int want = pass == 0 ? Weak : Resist;
                if (want == Weak ? !weak : !resist) continue;
                for (int i = 0; i < DamageKinds; i++)
                {
                    if (ui.Kind[i] != want || !KindAllowed(i)) continue;
                    float x = want == Weak ? -(edge + left * step) : edge + right * step;
                    Color tint = IconTint(i, want == Weak);
                    show[i] = Place(ui, i, x, iconY, size, 12f / 15f, tint, Color.black, false);
                    if (!show[i]) continue;
                    if (want == Weak) left++; else right++;
                }
            }
            ApplyShown(ui);
        }

        // Toggle only on change, so the canvas isn't rebuilt every frame.
        private static void ApplyShown(WeaknessUi ui)
        {
            for (int i = 0; i < DamageKinds; i++)
            {
                GameObject go = ui.Slots[i].gameObject;
                if (go.activeSelf != ui.Show[i]) go.SetActive(ui.Show[i]);
            }
        }

        // The outline fades with the icon, so faint icons stay faint.
        // A silhouette is the outlined shape in one flat color, no inner fill.
        private bool Place(WeaknessUi ui, int kind, float x, float y, float size, float fillRatio, Color color, Color outline, bool silhouette)
        {
            int shape = _shapeOf[kind];
            Sprite back = DamageBack[shape], fill = DamageFill[shape];
            if (back == null || fill == null) return false;
            Image img = ui.Slots[kind], f = ui.Fills[kind];
            if (img.sprite != back) img.sprite = back;
            if (f.sprite != fill) f.sprite = fill;
            RectTransform r = img.rectTransform;
            Vector2 sz = new Vector2(size, size), pos = new Vector2(x, y);
            if (r.sizeDelta != sz) r.sizeDelta = sz;
            if (r.anchoredPosition != pos) r.anchoredPosition = pos;
            Vector2 fsz = sz * fillRatio;
            if (f.rectTransform.sizeDelta != fsz) f.rectTransform.sizeDelta = fsz;
            if (silhouette)
            {
                if (img.color != color) img.color = color;
                if (f.enabled) f.enabled = false;
                return true;
            }
            if (!f.enabled) f.enabled = true;
            outline.a *= color.a;
            if (img.color != outline) img.color = outline;
            if (f.color != color) f.color = color;
            return true;
        }

        private Color IconTint(int kind, bool weak)
        {
            return weak ? _tintOf[kind] : _resistTint;
        }

        private void DestroyWeaknessIcons(EnemyUiState state)
        {
            WeaknessUi ui;
            if (state == null || !_weaknessUi.TryGetValue(state, out ui)) return;
            if (ui.Root != null) Destroy(ui.Root.gameObject);
            _weaknessUi.Remove(state);
            state.Weakness = null;
        }
    }

    // Equipment events that can change what the local player can deal.
    internal static class LimitedModePatches
    {
        private static void Notify(object who)
        {
            if (EnemyHudReworkPlugin.Instance != null && who != null && ReferenceEquals(who, Player.m_localPlayer))
            {
                EnemyHudReworkPlugin.Instance.OnLocalEquipmentChanged();
                EnemyHudReworkPlugin.Instance.InvalidatePredictions();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Equip { private static void Postfix(Humanoid __instance) { Notify(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        private static class Unequip { private static void Postfix(Humanoid __instance) { Notify(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems))]
        private static class Hide { private static void Postfix(Humanoid __instance) { Notify(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(Humanoid), "ShowHandItems")]
        private static class Show { private static void Postfix(Humanoid __instance) { Notify(__instance); } }

        [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Spawned { private static void Postfix(Player __instance) { Notify(__instance); } }

        // Running out of ammo removes it from the inventory.
        [HarmonyLib.HarmonyPatch(typeof(Inventory), "Changed")]
        private static class InventoryChanged
        {
            private static void Postfix(Inventory __instance)
            {
                Player local = Player.m_localPlayer;
                if (local != null && ReferenceEquals(local.GetInventory(), __instance))
                    Notify(local);
            }
        }
    }
}
