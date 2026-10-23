using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresStackify
{
    // One button on the inventory, between armor and weight, and one on an
    // open chest, above its weight: the game's own armor and weight blocks
    // with the game's coin pouch (the trader's map icon, as My Little UI
    // uses it) and how many stacks are in that inventory or chest. The
    // inventory one works with nearby chests, the chest one with that chest.
    // Left click quick stacks, right click restocks, middle click sorts by
    // type, shift + middle click sorts by weight.
    internal static class StackifyButtons
    {
        private const string Name = "WireStackify";
        private const string Topic = "Quick stack & restock";

        private sealed class PendingIcon { public Image Image; public Color Color; public bool Chest; }
        private static readonly List<PendingIcon> Pending = new List<PendingIcon>();
        private static Sprite _pouch;
        private static readonly List<KeyValuePair<UITooltip, bool>> Tips = new List<KeyValuePair<UITooltip, bool>>();
        private static RectTransform _tooltipAnchor;
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");

        // Acts the moment the button goes down, not when it's let go.
        internal sealed class ClickHandler : MonoBehaviour, IPointerDownHandler
        {
            public bool Chest;

            public void OnPointerDown(PointerEventData e)
            {
                Player p = Player.m_localPlayer;
                if (p == null) return;
                if (e.button == PointerEventData.InputButton.Middle)
                {
                    if (!StackifyPlugin.MiddleClickSort.Value) return;
                    // Shift: by weight, and the weight icon next to it shakes.
                    // The pouch wiggles round one way for type, the other for
                    // weight. Nothing moved: the empty bag.
                    bool weight = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (Sorting.Run(Chest, weight ? Sorting.Mode.Weight : Sorting.Mode.Type))
                    {
                        ButtonFeel.Wiggle(Chest, weight ? -1f : 1f);
                        if (weight) WeightShake.Shake(Chest);
                    }
                    else Nothing(Chest);
                    return;
                }
                bool left = e.button == PointerEventData.InputButton.Left;
                if (!left && e.button != PointerEventData.InputButton.Right) return;
                // Shift + either click: quick stack, then restock, one trip.
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    int stacked = QuickStack.Run(p, Chest, null, true);
                    int restocked = Restock.Run(p, Chest, true);
                    if (stacked > 0 || restocked > 0)
                        StackifyEffects.SpawnLocal(stacked > 0 ? p.m_pickupEffects : p.m_dropEffects, StackifyEffects.Ear(), Quaternion.identity);
                    if (stacked == 0 && restocked == 0) Nothing(Chest);
                    if (StackifyPlugin.QuickStackMessage.Value || StackifyPlugin.RestockMessage.Value)
                        StackifyPlugin.Message(p, stacked == 0 && restocked == 0 ? "Nothing to quickstack or restock"
                            : stacked == 0 ? "Restocked " + restocked
                            : restocked == 0 ? "Quickstacked " + stacked
                            : "Quickstacked " + stacked + ", restocked " + restocked);
                    return;
                }
                int done = left ? QuickStack.Run(p, Chest) : Restock.Run(p, Chest);
                // Only when something happened. Quick stack: the sound of
                // picking an item up. Restock: the sound of dropping one.
                if (done > 0) StackifyEffects.SpawnLocal(left ? p.m_pickupEffects : p.m_dropEffects, StackifyEffects.Ear(), Quaternion.identity);
            }
        }

        // After a quick stack or restock: only the button that did it
        // answers (the chest one also for the game's Place stacks).
        internal static void Feedback(StackifyEffects.Kind kind, int count, bool chest)
        {
            if (count > 0) ButtonFeel.Result(kind, count, chest);
            else Nothing(chest);
        }

        // Nothing to do: the sound of shaking an empty bag (the game's
        // inventory closing rustle, light and quick), and the pouch gives a
        // quick squeeze. Only while the button can be seen.
        internal static void Nothing(bool chest)
        {
            if (!ButtonFeel.Squeeze(chest)) return;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_closeInventoryEffects == null) return;
            foreach (EffectList.EffectData d in gui.m_closeInventoryEffects.m_effectPrefabs)
                if (d != null && d.m_enabled && d.m_prefab != null && d.m_prefab.GetComponentInChildren<ZSFX>(true) != null)
                {
                    StackifyEffects.PlayUi(d.m_prefab, 1.25f);
                    break;
                }
        }


        // The minimap loads separately: set the icon the first time the
        // inventory opens after it's there.
        [HarmonyPatch(typeof(InventoryGui), "Show")]
        private static class ShowPatch
        {
            private static void Postfix() { SetIcons(); UpdateCounts(); }
        }

        // ---- The count: stacks in your inventory, or in the open chest ----
        // Updated only when one of them changes.
        private static TMP_Text _invCount, _chestCount;

        [HarmonyPatch(typeof(Inventory), "Changed")]
        private static class CountPatch
        {
            private static void Postfix(Inventory __instance)
            {
                Player p = Player.m_localPlayer;
                if (p == null || InventoryGui.instance == null) return;
                Container c = StackifyPlugin.CurrentContainer(InventoryGui.instance);
                if (__instance == p.GetInventory() || (c != null && __instance == c.GetInventory())) UpdateCounts();
            }
        }

        internal static void UpdateCounts()
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            if (_invCount != null)
            {
                Inventory inv = p.GetInventory();
                int rows = StackifyPlugin.MainRows(inv, p), n = 0;
                foreach (ItemDrop.ItemData it in inv.GetAllItems()) if (it.m_gridPos.y < rows) n++;
                SetCount(_invCount, n);
            }
            if (_chestCount != null && InventoryGui.instance != null)
            {
                Container c = StackifyPlugin.CurrentContainer(InventoryGui.instance);
                if (c != null) SetCount(_chestCount, c.GetInventory().GetAllItems().Count);
            }
        }

        private static void SetCount(TMP_Text t, int n)
        {
            string s = n.ToString();
            if (t.text != s) t.text = s;
        }

        private static void SetIcons()
        {
            if (Pending.Count == 0) return;
            if (_pouch == null && Minimap.instance != null && Minimap.instance.m_locationIcons != null)
                foreach (Minimap.LocationSpriteData loc in Minimap.instance.m_locationIcons)
                    if (loc.m_icon != null && loc.m_icon.name == "mapicon_trader") { _pouch = loc.m_icon; break; }
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingIcon p = Pending[i];
                Sprite sprite = _pouch;
                if (p.Image == null) { Pending.RemoveAt(i); continue; }
                if (sprite == null) continue;
                p.Image.sprite = sprite;
                p.Image.color = p.Color; // the weight or armor icon's own color
                // The map icon has some empty space around it: drawn a bit
                // bigger, like My Little UI does (38 instead of 32).
                p.Image.rectTransform.sizeDelta *= 38f / 32f;
                ButtonFeel feel = p.Image.GetComponentInParent<ButtonFeel>();
                if (feel != null) feel.IconReady();
                Pending.RemoveAt(i);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static class AwakePatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                try { Build(__instance); }
                catch (Exception e) { StackifyPlugin.Log.LogWarning("Couldn't add the Stackify buttons: " + e); }
            }
        }

        private static void Build(InventoryGui gui)
        {
            RectTransform armor = gui.m_armor != null ? gui.m_armor.transform.parent as RectTransform : null;
            RectTransform weight = gui.m_weight != null ? gui.m_weight.transform.parent as RectTransform : null;
            RectTransform chestWeight = gui.m_containerWeight != null ? gui.m_containerWeight.transform.parent as RectTransform : null;
            if (armor == null || weight == null) return;
            InventoryGrid grid = ContainerGrid(gui);
            _tooltipAnchor = grid != null ? grid.m_tooltipAnchor : null;
            UITooltip template = grid != null && grid.m_elementPrefab != null
                ? grid.m_elementPrefab.GetComponent<UITooltip>() : null;

            // The weight icons shake when sorting by weight.
            WeightShake.Add(weight, false);
            if (chestWeight != null) WeightShake.Add(chestWeight, true);

            // Inventory: halfway between armor and weight.
            RectTransform inv = Clone(armor, gui.m_armor, false, template);
            inv.localPosition = (armor.localPosition + weight.localPosition) * 0.5f;

            // Chest: the same distance above the chest's weight as the
            // inventory button sits above yours.
            if (chestWeight != null)
            {
                RectTransform chest = Clone(chestWeight, gui.m_containerWeight, true, template);
                chest.localPosition = chestWeight.localPosition + (inv.localPosition - weight.localPosition);
            }
            UpdateTips();
            StackifyPlugin.MiddleClickSort.SettingChanged += (s, e) => UpdateTips();
            SetIcons();
        }

        private static RectTransform Clone(RectTransform block, TMP_Text valueText, bool chest, UITooltip template)
        {
            GameObject go = UnityEngine.Object.Instantiate(block.gameObject, block.parent, false);
            go.name = Name + (chest ? "Chest" : "Inventory");
            RectTransform r = (RectTransform)go.transform;
            // Right after the block it copies, so it sits behind the panel
            // the same way the armor and weight blocks do.
            r.SetSiblingIndex(block.GetSiblingIndex() + 1);

            // The number becomes an S, the icon becomes the pouch.
            string textPath = RelativePath(valueText.transform, block);
            TMP_Text label = null;
            foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (RelativePath(t.transform, r) == textPath)
                {
                    t.text = "";
                    label = t;
                    if (chest) _chestCount = t; else _invCount = t;
                }
                t.raycastTarget = false;
            }
            Image icon = null;
            foreach (Image img in go.GetComponentsInChildren<Image>(true))
            {
                if (img.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    img.sprite = null;
                    Pending.Add(new PendingIcon { Image = img, Color = img.color, Chest = chest });
                    img.color = new Color(1f, 1f, 1f, 0f); // hidden until the icon is found
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    icon = img;
                }
                else img.raycastTarget = true; // the background takes the clicks
            }
            // One invisible area over the whole tab, on top of its pieces:
            // anywhere on it counts as on the button.
            GameObject hitGo = new GameObject("HitArea", typeof(RectTransform), typeof(Image));
            RectTransform hitRect = (RectTransform)hitGo.transform;
            hitRect.SetParent(r, false);
            hitRect.SetAsLastSibling();
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = hitRect.offsetMax = Vector2.zero;
            Image hit = hitGo.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            // The tooltip: built like the armor and weight tooltips (the item
            // slot's tooltip, shown where item tooltips show).
            foreach (UITooltip old in go.GetComponentsInChildren<UITooltip>(true))
                UnityEngine.Object.DestroyImmediate(old);
            // Runs before the tooltip on hover, so the chest count is fresh.
            if (!chest) go.AddComponent<TipRefresh>();
            UITooltip tip = go.AddComponent<UITooltip>();
            if (template != null)
                foreach (FieldInfo f in typeof(UITooltip).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!f.IsInitOnly) f.SetValue(tip, f.GetValue(template));
            tip.m_gamepadFocusObject = null;
            Tips.Add(new KeyValuePair<UITooltip, bool>(tip, chest));

            ButtonFeel feel = go.AddComponent<ButtonFeel>();
            go.AddComponent<FeelReset>().Feel = feel;
            feel.Setup(icon != null ? icon.rectTransform : null, label != null ? label.rectTransform : null, icon, chest);
            go.AddComponent<ClickHandler>().Chest = chest;
            return r;
        }

        // Hovering the inventory button: how many chests it would use right
        // now, counted once as the tooltip opens.
        internal sealed class TipRefresh : MonoBehaviour, IPointerEnterHandler
        {
            public void OnPointerEnter(PointerEventData e) { UpdateTips(); }
        }

        private static string NearbyLine()
        {
            Player p = Player.m_localPlayer;
            if (p == null) return "";
            if (!NearbyChests.AreaAllowed()) return "\nNearby chests: only with Multi User Chest in multiplayer";
            float range = Mathf.Max(StackifyPlugin.QuickStackRange.Value, StackifyPlugin.RestockRange.Value);
            if (range <= 0f) return "";
            bool single = NearbyChests.IsTrueSingleplayer();
            long id = p.GetPlayerID();
            int n = 0;
            foreach (Container c in NearbyChests.InRange(p.transform.position, range))
                if (NearbyChests.CanUse(c, id, single)) n++;
            return "\nChests in range: <color=orange>" + n + "</color>";
        }

        private static void UpdateTips()
        {
            foreach (KeyValuePair<UITooltip, bool> t in Tips)
            {
                if (t.Key == null) continue;
                string where = t.Value ? "this chest" : "nearby chests";
                string text =
                    "<color=orange>Left click</color>: quick stack into " + where + "\n" +
                    "<color=orange>Right click</color>: restock from " + where + "\n" +
                    "<color=orange>Shift + click</color>: both, quick stack then restock";
                if (StackifyPlugin.MiddleClickSort.Value)
                    text += "\n<color=orange>Middle click</color>: sort by type" +
                            "\n<color=orange>Shift + middle click</color>: sort by weight";
                if (!t.Value) text += "\n" + NearbyLine();
                t.Key.Set(Topic, text, _tooltipAnchor);
            }
        }

        private static string RelativePath(Transform t, Transform root)
        {
            string path = "";
            while (t != null && t != root)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }
    }

    // How a button answers after it's used, on springs so it settles with a
    // little give. Only the button that did it moves.
    //  sort:        the icon wiggles round (one way for type, the other for
    //               weight)
    //  nothing:     a quick squeeze, smaller and back
    //  quick stack: the icon hops and wobbles and flashes
    //  restock:     the icon bounces up and down and flashes
    //  nothing done:  no movement at all
    // Runs only while something is moving, then switches itself off.
    internal sealed class ButtonFeel : MonoBehaviour
    {
        private static readonly List<ButtonFeel> All = new List<ButtonFeel>();

        private struct Spring
        {
            public float X, V;
            public void Step(float dt, float stiffness, float damping)
            {
                V += (-X * stiffness - V * damping) * dt;
                X += V * dt;
            }
            public bool Settled => Mathf.Abs(X) < 0.002f && Mathf.Abs(V) < 0.02f;
        }

        private RectTransform _icon, _label;
        private Image _iconImage;
        private TMP_Text _labelText;
        private bool _chest;
        private Vector3 _iconPos, _iconScale;
        private Color _iconColor, _labelColor;
        private bool _hasBase, _iconColorKnown;

        private Spring _grow, _hop, _tilt;
        private float _bounceAge = -1f, _flash, _wiggleAge = -1f, _wiggleDir = 1f, _squeezeAge = -1f;

        internal void Setup(RectTransform icon, RectTransform label, Image iconImage, bool chest)
        {
            _icon = icon; _label = label; _iconImage = iconImage; _chest = chest;
            _labelText = label != null ? label.GetComponent<TMP_Text>() : null;
            All.Add(this);
            enabled = false;
        }

        private void OnDestroy() { All.Remove(this); }

        internal void IconReady()
        {
            if (_iconImage == null) return;
            _iconColor = _iconImage.color;
            _iconColorKnown = true;
        }

        private void Wake()
        {
            if (!_hasBase)
            {
                if (_icon != null) { _iconPos = _icon.localPosition; _iconScale = _icon.localScale; }
                if (_labelText != null) _labelColor = _labelText.color;
                _hasBase = true;
            }
            enabled = true;
        }

        private static ButtonFeel For(bool chest)
        {
            foreach (ButtonFeel f in All)
                if (f != null && f._chest == chest && f.gameObject.activeInHierarchy) return f;
            return null;
        }

        internal static void Result(StackifyEffects.Kind kind, int count, bool chest)
        {
            ButtonFeel f = For(chest);
            if (f != null) f.Play(kind, count);
        }

        internal static void Wiggle(bool chest, float dir)
        {
            ButtonFeel f = For(chest);
            if (f == null) return;
            f.Wake();
            f._wiggleAge = 0f;
            f._wiggleDir = dir;
        }

        // Returns whether the button is there to be seen.
        internal static bool Squeeze(bool chest)
        {
            ButtonFeel f = For(chest);
            if (f == null) return false;
            f.Wake();
            f._squeezeAge = 0f;
            return true;
        }

        private void Play(StackifyEffects.Kind kind, int count)
        {
            if (count <= 0) return;
            Wake();
            _flash = 1f;
            _grow.V += 1.6f;
            if (kind == StackifyEffects.Kind.Stack)
            {
                _hop.V += 9f;
                _tilt.V += (UnityEngine.Random.value < 0.5f ? -1f : 1f) * 120f;
            }
            else _bounceAge = 0f;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _grow.Step(dt, 420f, 24f);
            _hop.Step(dt, 260f, 15f);
            _tilt.Step(dt, 300f, 14f);
            _flash = Mathf.Max(0f, _flash - dt * 3.5f);

            float bounce = 0f;
            if (_bounceAge >= 0f)
            {
                _bounceAge += dt;
                float t = _bounceAge / 0.4f;
                if (t >= 1f) _bounceAge = -1f;
                else bounce = -Mathf.Sin(t * Mathf.PI * 5f) * 4f * (1f - t);
            }
            _wiggle = 0f;
            if (_wiggleAge >= 0f)
            {
                _wiggleAge += dt;
                float t = _wiggleAge / 0.42f;
                if (t >= 1f) _wiggleAge = -1f;
                else _wiggle = Mathf.Sin(t * Mathf.PI * 4f) * 11f * (1f - t) * _wiggleDir;
            }
            _squeeze = 1f;
            if (_squeezeAge >= 0f)
            {
                _squeezeAge += dt;
                float t = _squeezeAge / 0.16f;
                if (t >= 1f) _squeezeAge = -1f;
                else _squeeze = 1f - 0.14f * Mathf.Sin(t * Mathf.PI);
            }
            Apply(bounce);

            if (_grow.Settled && _hop.Settled && _tilt.Settled &&
                _bounceAge < 0f && _flash <= 0f && _wiggleAge < 0f && _squeezeAge < 0f)
            {
                _grow = default(Spring); _hop = default(Spring); _tilt = default(Spring);
                _wiggle = 0f; _squeeze = 1f;
                Apply(0f);
                enabled = false;
            }
        }

        private float _wiggle, _squeeze = 1f;

        private void Apply(float bounce)
        {
            if (!_hasBase) return;
            if (_icon != null)
            {
                _icon.localScale = _iconScale * ((1f + _grow.X) * _squeeze);
                _icon.localPosition = _iconPos + new Vector3(0f, _hop.X * 7f + bounce, 0f);
                _icon.localRotation = Quaternion.Euler(0f, 0f, _tilt.X + _wiggle);
            }
            if (_iconImage != null && _iconColorKnown)
                _iconImage.color = Color.Lerp(_iconColor, new Color(1f, 1f, 1f, _iconColor.a), _flash);
            if (_labelText != null) _labelText.color = Color.Lerp(_labelColor, Color.white, _flash);
        }

        internal void ResetNow()
        {
            _grow = default(Spring); _hop = default(Spring); _tilt = default(Spring);
            _bounceAge = -1f; _flash = 0f; _wiggleAge = -1f; _squeezeAge = -1f; _wiggle = 0f; _squeeze = 1f;
            Apply(0f);
            enabled = false;
        }
    }

    // The weight icon next to a button gives a little shake when that
    // inventory is sorted by weight. Only runs while shaking.
    internal sealed class WeightShake : MonoBehaviour
    {
        private static readonly List<WeightShake> All = new List<WeightShake>();
        public bool Chest;
        private RectTransform _icon;
        private Vector3 _base;
        private float _age = -1f;

        internal static void Add(RectTransform block, bool chest)
        {
            if (block == null || block.GetComponent<WeightShake>() != null) return;
            WeightShake w = block.gameObject.AddComponent<WeightShake>();
            w.Chest = chest;
            foreach (Image img in block.GetComponentsInChildren<Image>(true))
                if (img.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) { w._icon = img.rectTransform; break; }
            All.Add(w);
            w.enabled = false;
        }

        private void OnDestroy() { All.Remove(this); }

        internal static void Shake(bool chest)
        {
            foreach (WeightShake w in All)
                if (w != null && w.Chest == chest && w._icon != null && w.gameObject.activeInHierarchy)
                {
                    if (w._age < 0f) w._base = w._icon.localPosition;
                    w._age = 0f;
                    w.enabled = true;
                }
        }

        private void Update()
        {
            _age += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float t = _age / 0.3f;
            if (t >= 1f || _icon == null)
            {
                if (_icon != null) _icon.localPosition = _base;
                _age = -1f;
                enabled = false;
                return;
            }
            _icon.localPosition = _base + new Vector3(Mathf.Sin(t * Mathf.PI * 6f) * 3f * (1f - t), 0f, 0f);
        }

        private void OnDisable()
        {
            if (_age >= 0f && _icon != null) _icon.localPosition = _base;
            _age = -1f;
        }
    }

    // Closing the inventory or the chest puts the button back at rest.
    // Nothing runs while it's open.
    internal sealed class FeelReset : MonoBehaviour
    {
        public ButtonFeel Feel;
        private void OnDisable() { if (Feel != null) Feel.ResetNow(); }
    }
}
