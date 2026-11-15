using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
    // What each click does is set in the config (Controls). Holding an item
    // and clicking a pouch quick stacks every stack of that item; the pouch
    // opens while you hold one.
    internal static class StackifyButtons
    {
        private const string Name = "WireStackify";
        private const string Topic = "Stackify", ChestTopic = "Stackify (Container)";

        private sealed class PendingIcon { public Image Image; public Color Color; public bool Chest; }
        private static readonly List<PendingIcon> Pending = new List<PendingIcon>();
        private static readonly List<KeyValuePair<Image, bool>> Icons = new List<KeyValuePair<Image, bool>>();
        private static bool _openInv, _openChest;
        private static Sprite _pouch, _open, _closed;
        private static bool _openTried, _closedTried;
        private static bool _flashInv, _flashChest;
        private static int _flashToken;
        private static readonly List<KeyValuePair<UITooltip, bool>> Tips = new List<KeyValuePair<UITooltip, bool>>();
        private static RectTransform _tooltipAnchor;
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        internal static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem =
            AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragInventory =
            AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");

        // Acts the moment the button goes down, not when it's let go.
        internal sealed class ClickHandler : MonoBehaviour, IPointerDownHandler
        {
            public bool Chest;

            public void OnPointerDown(PointerEventData e)
            {
                InventoryGui gui = InventoryGui.instance;
                if (Player.m_localPlayer == null || gui == null) return;
                ItemDrop.ItemData held = Holding(gui);
                if (held != null)
                {
                    Actions.DropOn(Chest, held, DragInventory(gui));
                    return;
                }
                int button = e.button == PointerEventData.InputButton.Left ? 0
                    : e.button == PointerEventData.InputButton.Right ? 1
                    : e.button == PointerEventData.InputButton.Middle ? 2 : -1;
                Actions.Do(Actions.ForButton(button, Actions.Shift()), Chest);
            }
        }

        // The item in your hand, when dropping items on the pouch is on.
        private static ItemDrop.ItemData Holding(InventoryGui gui)
        {
            if (gui == null || !StackifyPlugin.DropOnPouch.Value || StackifyPlugin.DragGo(gui) == null) return null;
            return DragItem(gui);
        }

        // After a quick stack or restock that moved something: the button
        // that did it answers (the chest one also for the game's Place
        // stacks).
        internal static void Feedback(StackifyEffects.Kind kind, int count, bool chest)
        {
            if (count <= 0) return;
            ButtonFeel.Result(kind, count, chest);
            Flash(chest);
        }

        // ---- Hover: the
        // stacks a quick stack would take get a faint frame ----
        private static bool _hovering, _hoverChest;
        private static string _previewChest = "", _previewInv = "";

        internal static void Enter(bool chest)
        {
            _hovering = true;
            _hoverChest = chest;
            StartWatch();
            ButtonFeel.Hover(chest, true);
            RefreshHover();
        }

        internal static void Exit(bool chest)
        {
            if (!_hovering || _hoverChest != chest) return;
            _hovering = false;

            ButtonFeel.Hover(chest, false);
            Favorites.ClearPreview();
        }

        // After anything changes while a pouch is hovered: what it would do
        // now. Only on hover and after a click, never every frame.
        internal static void RefreshHover()
        {
            if (!_hovering) return;
            Player p = Player.m_localPlayer;
            string text = "";
            if (p != null && StackifyPlugin.HoverPreview.Value)
            {
                try
                {
                    ItemDrop.ItemData held = Holding(InventoryGui.instance);
                    int chests = 0;
                    bool chestDrop = held != null && _hoverChest;
                    List<ItemDrop.ItemData> items = chestDrop ? null : QuickStack.Preview(p, _hoverChest, out chests, held);
                    int sources = 0;
                    float gain = 0f;
                    List<ItemDrop.ItemData> restock = held == null ? Restock.Preview(p, _hoverChest, out sources, out gain)
                        : chestDrop ? Restock.PreviewOnly(p, StackifyPlugin.CurrentContainer(InventoryGui.instance), held, out gain) : null;
                    float loss = 0f;
                    if (items != null) foreach (ItemDrop.ItemData it in items) loss += it.GetWeight();
                    // Rings only redrawn when the slots they'd go on change.
                    if (!SameSlots(items, _shownStack) || !SameSlots(restock, _shownRestock))
                    {
                        Favorites.ShowPreview(items, restock);
                        _shownStack = Slots(items);
                        _shownRestock = Slots(restock);
                    }
                    if (chestDrop && restock.Count > 0) sources = 1;
                    string yellow = Favorites.YellowHex(), blue = Favorites.BlueHex();
                    text = "\n";
                    if (items != null)
                        text += "\n" + (items.Count == 0 ? "Quick stack: nothing"
                            : "Quick stack: <color=#" + yellow + ">" + Stacks(items.Count) + "</color> into " + Containers(chests) + Weight(-loss, yellow));
                    if (restock != null)
                        text += "\n" + (restock.Count == 0 ? "Restock: nothing"
                            : "Restock: <color=#" + blue + ">" + Stacks(restock.Count) + "</color> from " + Containers(sources) + Weight(gain, blue));
                }
                catch (Exception e) { StackifyPlugin.Log.LogWarning("Stackify preview failed: " + e); }
            }
            else Favorites.ClearPreview();
            if (_hoverChest) _previewChest = text; else _previewInv = text;
            UpdateTips(_hoverChest ? 1 : 0);
        }

        private static string Stacks(int n) { return n == 1 ? "1 stack" : n + " stacks"; }
        // On its own line: "-86.5 weight", the number in the action's
        // colour, one decimal at most like the game's item weights.
        private static string Weight(float w, string hex)
        {
            if (Mathf.Abs(w) < 0.05f) return "";
            return "\n<color=#" + hex + ">" + (w > 0f ? "+" : "-") +
                Mathf.Abs(w).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "</color> weight";
        }

        private static string Containers(int n) { return "<color=orange>" + n + "</color>" + (n == 1 ? " container" : " containers"); }

        // ---- The open pouch while you hold an item ----
        // Only the pouch that would do something with it opens: the
        // inventory one when the open chest or a nearby one would take it,
        // the chest one when the open chest can top up your stacks of it.
        // Worked out once, when you pick it up.
        [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
        private static class DragPatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                UpdateOpen(__instance);
                RefreshHover();
            }
        }

        private static void UpdateOpen(InventoryGui gui)
        {
            bool inv = false, chest = false;
            Player p = Player.m_localPlayer;
            ItemDrop.ItemData held = Holding(gui);
            if (held != null && p != null)
            {
                try
                {
                    int n;
                    inv = QuickStack.Preview(p, false, out n, held).Count > 0;
                    float g;
                    chest = Restock.PreviewOnly(p, StackifyPlugin.CurrentContainer(gui), held, out g).Count > 0;
                }
                catch (Exception e) { StackifyPlugin.Log.LogWarning("Stackify open pouch: " + e.Message); }
            }
            if (inv != _openInv) { _openInv = inv; Refresh(false); }
            if (chest != _openChest) { _openChest = chest; Refresh(true); }
        }

        // Open while you hold something it would take, and for a moment
        // after it quick stacks or restocks something.
        internal static void Flash(bool chest)
        {
            if (chest) _flashChest = true; else _flashInv = true;
            int token = ++_flashToken;
            Refresh(chest);
            StackifyEffects.Runner.Get().StartCoroutine(Actions.After(FlashTime, () =>
            {
                if (token != _flashToken) return;
                _flashInv = _flashChest = false;
                Refresh(false);
                Refresh(true);
            }));
        }

        private const float FlashTime = 0.7f;

        private static void Refresh(bool chest)
        {
            if (_pouch == null) return;
            bool open = chest ? (_openChest || _flashChest) : (_openInv || _flashInv);
            Sprite sprite = open ? OpenPouch() : Closed();
            if (sprite == null) sprite = Closed();
            for (int i = Icons.Count - 1; i >= 0; i--)
            {
                Image img = Icons[i].Key;
                if (img == null) { Icons.RemoveAt(i); continue; }
                if (Icons[i].Value != chest || img.sprite == sprite) continue;
                img.sprite = sprite;
                ButtonFeel.Pop(chest, open);
            }
        }

        // The pouch without the map icon's drop shadow (the armor and weight
        // icons next to it have none), and its open version. Both drawn at
        // the map icon's own size, so they sit exactly where it does.
        private static Sprite Closed()
        {
            if (_closed != null || _closedTried || _pouch == null) return _closed != null ? _closed : _pouch;
            _closedTried = true;
            _closed = LoadSprite("pouch_closed");
            return _closed != null ? _closed : _pouch;
        }

        private static Sprite OpenPouch()
        {
            if (_open != null || _openTried || _pouch == null) return _open;
            _openTried = true;
            _open = LoadSprite("pouch_open");
            return _open;
        }

        private static Sprite LoadSprite(string name)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WiresStackify.Assets." + name + ".png"))
                {
                    if (s == null) return null;
                    byte[] data = new byte[s.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = s.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    // By reflection: the overloads taking spans don't resolve here.
                    MethodInfo load = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    if (load == null || !(bool)load.Invoke(null, new object[] { tex, data })) return null;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    if (_pouch.texture != null) tex.filterMode = _pouch.texture.filterMode;
                    Rect r = _pouch.rect;
                    Vector2 pivot = new Vector2(_pouch.pivot.x / r.width, _pouch.pivot.y / r.height);
                    Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot,
                        _pouch.pixelsPerUnit * tex.width / r.width);
                    sp.name = "WireStackify_" + name;
                    return sp;
                }
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Couldn't load " + name + ": " + e.Message); return null; }
        }

        // Wire's Native Cursor Support, when it's there: the hand over the
        // pouches, like over an item.
        private static void CursorHand(GameObject go)
        {
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "WiresNativeCursorSupport") continue;
                    Type t = a.GetType("WiresNativeCursorSupport.NativeCursorApi");
                    MethodInfo m = t != null ? t.GetMethod("SetHand", BindingFlags.Public | BindingFlags.Static) : null;
                    if (m != null) m.Invoke(null, new object[] { go });
                    return;
                }
            }
            catch (Exception e) { StackifyPlugin.Log.LogDebug("Native Cursor Support hand: " + e.Message); }
        }

        // The minimap loads separately: set the icon the first time the
        // inventory opens after it's there.
        [HarmonyPatch(typeof(InventoryGui), "Show")]
        private static class ShowPatch
        {
            private static void Postfix(InventoryGui __instance) { SetIcons(); UpdateCounts(); UpdateOpen(__instance); }
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
                // While a pouch is hovered, any inventory change (yours, the
                // open container's, a nearby one's) can change what it would
                // do: refresh its rings and tooltip once, after the changes.
                if (_hovering) RefreshHoverSoon();
            }
        }

        private static bool _refreshQueued;

        // While a pouch is hovered, what a click would do is worked out
        // again every 0.1 s (your choice: not every frame, but often enough
        // to follow you walking in and out of range). Rings and tooltip
        // only change when the result does. Stops when you stop hovering.
        private const float WatchSeconds = 0.1f;
        private static bool _watching;
        private static List<Vector2i> _shownStack, _shownRestock;

        private static void StartWatch()
        {
            _shownStack = _shownRestock = null;
            if (_watching) return;
            _watching = true;
            StackifyEffects.Runner.Get().StartCoroutine(Watch());
        }

        private static System.Collections.IEnumerator Watch()
        {
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(WatchSeconds);
            while (_hovering)
            {
                yield return wait;
                if (_hovering) RefreshHover();
            }
            _watching = false;
        }

        private static List<Vector2i> Slots(List<ItemDrop.ItemData> items)
        {
            if (items == null) return null;
            List<Vector2i> slots = new List<Vector2i>(items.Count);
            foreach (ItemDrop.ItemData it in items) slots.Add(it.m_gridPos);
            return slots;
        }

        private static bool SameSlots(List<ItemDrop.ItemData> items, List<Vector2i> shown)
        {
            if (items == null || shown == null) return items == null && shown == null;
            if (items.Count != shown.Count) return false;
            for (int i = 0; i < items.Count; i++)
                if (items[i].m_gridPos != shown[i]) return false;
            return true;
        }

        internal static void RefreshHoverSoon()
        {
            if (_refreshQueued || !_hovering) return;
            _refreshQueued = true;
            StackifyEffects.Runner.Get().StartCoroutine(RefreshAtEndOfFrame());
        }

        private static System.Collections.IEnumerator RefreshAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            _refreshQueued = false;
            RefreshHover();
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
                Sprite sprite = Closed();
                if (p.Image == null) { Pending.RemoveAt(i); continue; }
                if (sprite == null) continue;
                p.Image.sprite = sprite;
                Icons.Add(new KeyValuePair<Image, bool>(p.Image, p.Chest));
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

            // Position once. Averaging the anchors makes the pouch stay
            // midway between armor and weight as the inventory panel grows.
            RectTransform inv = Clone(armor, gui.m_armor, false, template);
            if (armor.parent == weight.parent)
            {
                inv.anchorMin = (armor.anchorMin + weight.anchorMin) * 0.5f;
                inv.anchorMax = (armor.anchorMax + weight.anchorMax) * 0.5f;
                inv.anchoredPosition = (armor.anchoredPosition + weight.anchoredPosition) * 0.5f;
            }
            else inv.position = (armor.position + weight.position) * 0.5f;

            // Chest: the same distance above the chest's weight as the
            // inventory button sits above yours.
            if (chestWeight != null)
            {
                RectTransform chest = Clone(chestWeight, gui.m_containerWeight, true, template);
                chest.localPosition = chestWeight.localPosition + (inv.localPosition - weight.localPosition);
            }
            UpdateTips();
            StackifyPlugin.Instance.Config.SettingChanged += (s, e) => UpdateTips();
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
            // Runs before the tooltip on hover, so its text is fresh.
            go.AddComponent<TipRefresh>().Chest = chest;
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
            CursorHand(go);
            return r;
        }

        // Hover start and end, before the tooltip sees them.
        internal sealed class TipRefresh : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public bool Chest;
            public void OnPointerEnter(PointerEventData e) { Enter(Chest); }
            public void OnPointerExit(PointerEventData e) { Exit(Chest); }
            private void OnDisable() { Exit(Chest); }
        }

        private static string NearbyLine()
        {
            Player p = Player.m_localPlayer;
            if (p == null) return "";
            if (!NearbyChests.AreaAllowed()) return "\nNearby containers: only with Multi User Chest in multiplayer";
            float range = Mathf.Max(StackifyPlugin.QuickStackRange.Value, StackifyPlugin.RestockRange.Value);
            if (range <= 0f) return "";
            bool single = NearbyChests.IsTrueSingleplayer();
            long id = p.GetPlayerID();
            int n = 0;
            foreach (Container c in NearbyChests.InRange(p.transform.position, range))
                if (NearbyChests.CanUse(c, id, single)) n++;
            return "\nContainers in range: <color=orange>" + n + "</color>";
        }

        // which: -1 both, 0 the inventory's, 1 the chest's.
        // The tooltip: one line per action, in this order, each with the
        // click(s) it's on. Actions nothing is bound to are left out.
        private static readonly StackAction[] TipOrder =
        {
            StackAction.QuickStack, StackAction.Restock, StackAction.QuickStackAndRestock,
            StackAction.Sort, StackAction.SortByWeight, StackAction.Everything
        };

        private static void UpdateTips(int which = -1)
        {
            List<KeyValuePair<string, StackAction>> binds = Bindings();
            foreach (KeyValuePair<UITooltip, bool> t in Tips)
            {
                if (t.Key == null || (which >= 0 && t.Value != (which == 1))) continue;
                bool chest = t.Value;
                StringBuilder text = new StringBuilder();
                foreach (StackAction a in TipOrder)
                {
                    string clicks = null;
                    foreach (KeyValuePair<string, StackAction> b in binds)
                        if (b.Value == a) clicks = clicks == null ? b.Key : clicks + " or " + b.Key;
                    if (clicks != null)
                        text.Append("\n<color=orange>").Append(clicks).Append("</color>: ").Append(Actions.Describe(a));
                }
                if (StackifyPlugin.DropOnPouch.Value)
                    text.Append(chest ? "\n<color=orange>Click while holding an item</color>: restock held item type"
                        : "\n<color=orange>Click while holding an item</color>: quick stack held item type");
                text.Append("\n").Append(chest ? "\nOnly works in this container." : NearbyLine());
                text.Append(chest ? _previewChest : _previewInv);
                t.Key.Set(chest ? ChestTopic : Topic, text.ToString().TrimStart('\n'), _tooltipAnchor);
            }
        }

        // Each click and what it's set to; the shift clicks as one when
        // they're all the same.
        private static List<KeyValuePair<string, StackAction>> Bindings()
        {
            List<KeyValuePair<string, StackAction>> b = new List<KeyValuePair<string, StackAction>>
            {
                new KeyValuePair<string, StackAction>("Left click", StackifyPlugin.LeftClick.Value),
                new KeyValuePair<string, StackAction>("Right click", StackifyPlugin.RightClick.Value),
                new KeyValuePair<string, StackAction>("Middle click", StackifyPlugin.MiddleClick.Value),
            };
            StackAction sl = StackifyPlugin.ShiftLeftClick.Value, sr = StackifyPlugin.ShiftRightClick.Value, sm = StackifyPlugin.ShiftMiddleClick.Value;
            if (sl == sr && sr == sm) b.Add(new KeyValuePair<string, StackAction>("Shift + click", sl));
            else
            {
                b.Add(new KeyValuePair<string, StackAction>("Shift + left click", sl));
                b.Add(new KeyValuePair<string, StackAction>("Shift + right click", sr));
                b.Add(new KeyValuePair<string, StackAction>("Shift + middle click", sm));
            }
            return b;
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

        internal static bool AnyHovered { get; private set; }

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

        // Only remembered (a middle click over the pouch isn't also a panel
        // click); the tab itself doesn't change on hover.
        internal static void Hover(bool chest, bool on) { AnyHovered = on; }

        // The pouch opening (a little puff up) or closing (settling back).
        internal static void Pop(bool chest, bool open)
        {
            ButtonFeel f = For(chest);
            if (f == null) return;
            f.Wake();
            f._grow.V += open ? 1.3f : -0.7f;
            if (open) f._hop.V += 3f;
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
            {
                Color white = new Color(1f, 1f, 1f, _iconColor.a);
                _iconImage.color = Color.Lerp(_iconColor, white, _flash);
            }
            if (_labelText != null) _labelText.color = Color.Lerp(_labelColor, Color.white, _flash);
        }

        internal void ResetNow()
        {
            _grow = default(Spring); _hop = default(Spring); _tilt = default(Spring);
            _bounceAge = -1f; _flash = 0f; _wiggleAge = -1f; _squeezeAge = -1f; _wiggle = 0f; _squeeze = 1f;
            AnyHovered = false;
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
