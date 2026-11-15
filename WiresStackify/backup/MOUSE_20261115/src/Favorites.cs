using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresStackify
{
    // Favorited items (every item of that kind) and favorited slots, per
    // character, saved next to the config. Quick stacking leaves them alone.
    internal sealed class Favorites
    {
        private static readonly Dictionary<long, Favorites> ByPlayer = new Dictionary<long, Favorites>();


        private readonly string _path;
        private readonly HashSet<Vector2i> _slots = new HashSet<Vector2i>();
        private readonly HashSet<string> _items = new HashSet<string>();

        internal static Favorites For(Player p)
        {
            long id = p.GetPlayerID();
            Favorites f;
            if (!ByPlayer.TryGetValue(id, out f)) ByPlayer[id] = f = new Favorites(id);
            return f;
        }

        internal static void MarkDirty()
        {
            // Loading/importing a character's saved favorites happens
            // inside For()'s constructor, before it enters the cache.
            // Do not recursively create Favorites while loading.
            Player player = Player.m_localPlayer;
            if (player != null && ByPlayer.ContainsKey(player.GetPlayerID()))
                RefreshVisible();
            StackifyButtons.RefreshHoverSoon();
        }

        private Favorites(long id)
        {
            _path = Path.Combine(Paths.ConfigPath, "WiresStackify_favorites_" + id + ".txt");
            if (File.Exists(_path)) Load();
            else if (ImportQuickStackStore(id)) Save();
        }

        internal bool IsFavorited(ItemDrop.ItemData item)
        {
            return _items.Contains(item.m_shared.m_name) || _slots.Contains(item.m_gridPos);
        }

        internal bool IsItemFavorited(ItemDrop.ItemData.SharedData s) { return _items.Contains(s.m_name); }
        internal bool IsSlotFavorited(Vector2i pos) { return _slots.Contains(pos); }

        internal void ToggleSlot(Vector2i pos)
        {
            if (!_slots.Remove(pos)) _slots.Add(pos);
            Save();
        }

        internal void ToggleItem(ItemDrop.ItemData.SharedData s)
        {
            if (!_items.Remove(s.m_name)) _items.Add(s.m_name);
            Save();
        }

        private void Load()
        {
            try
            {
                foreach (string line in File.ReadAllLines(_path))
                {
                    if (line.StartsWith("slot "))
                    {
                        string[] p = line.Split(' ');
                        int x, y;
                        if (p.Length == 3 && int.TryParse(p[1], out x) && int.TryParse(p[2], out y)) _slots.Add(new Vector2i(x, y));
                    }
                    else if (line.StartsWith("item ")) _items.Add(line.Substring(5));
                }
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Couldn't read favorites: " + e.Message); }
            MarkDirty();
        }

        private void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Vector2i s in _slots) sb.Append("slot ").Append(s.x).Append(' ').Append(s.y).Append('\n');
                foreach (string i in _items) sb.Append("item ").Append(i).Append('\n');
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception e) { StackifyPlugin.Log.LogWarning("Couldn't save favorites: " + e.Message); }
            MarkDirty();
        }

        // First time for this character: take over Quick Stack Store's
        // favorites if it has some (its file is only read, never changed).
        private bool ImportQuickStackStore(long id)
        {
            string qss = Path.Combine(Paths.ConfigPath, "QuickStackStore_player_" + id + ".dat");
            if (!File.Exists(qss)) return false;
            try
            {
                using (Stream s = File.OpenRead(qss))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    if (bf.Deserialize(s) is List<Tuple<int, int>> slots)
                        foreach (Tuple<int, int> t in slots) _slots.Add(new Vector2i(t.Item1, t.Item2));
                    if (bf.Deserialize(s) is List<string> items)
                        foreach (string i in items) _items.Add(i);
                }
                StackifyPlugin.Log.LogInfo("Took over Quick Stack Store favorites: " + _slots.Count + " slots, " + _items.Count + " items.");
                return _slots.Count > 0 || _items.Count > 0;
            }
            catch (Exception e)
            {
                StackifyPlugin.Log.LogWarning("Couldn't read Quick Stack Store favorites: " + e.Message);
                return false;
            }
        }

        // ---- Clicks: hold the favorite key, left click an item, right click a slot ----

        private static readonly Func<InventoryGrid, GameObject, Vector2i> ButtonPos =
            AccessTools.MethodDelegate<Func<InventoryGrid, GameObject, Vector2i>>(
                AccessTools.Method(typeof(InventoryGrid), "GetButtonPos"));
        internal static readonly AccessTools.FieldRef<InventoryGrid, Inventory> GridInventory =
            AccessTools.FieldRefAccess<InventoryGrid, Inventory>("m_inventory");
        internal static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> GridElements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        // Your inventory and the open container. Item favorites work in
        // both; favorited slots are only in your inventory.
        private static bool Favoriting(InventoryGrid grid)
        {
            InventoryGui gui = InventoryGui.instance;
            return gui != null && grid != null &&
                (grid == gui.m_playerGrid || (grid == gui.ContainerGrid && StackifyPlugin.CurrentContainer(gui) != null)) &&
                Player.m_localPlayer != null && !Player.m_localPlayer.IsTeleporting() &&
                StackifyPlugin.DragGo(InventoryGui.instance) == null &&
                (StackifyPlugin.Held(StackifyPlugin.FavoriteKey1.Value) || StackifyPlugin.Held(StackifyPlugin.FavoriteKey2.Value));
        }

        private static bool Click(InventoryGrid grid, UIInputHandler handler, bool left)
        {
            if (handler == null || !Favoriting(grid)) return true;
            Vector2i pos = ButtonPos(grid, handler.gameObject);
            if (pos == new Vector2i(-1, -1)) return true;
            if (!left && grid != InventoryGui.instance.m_playerGrid) return true;
            Favorites fav = For(Player.m_localPlayer);
            bool on;
            if (!left) { fav.ToggleSlot(pos); on = fav.IsSlotFavorited(pos); }
            else
            {
                ItemDrop.ItemData item = GridInventory(grid).GetItemAt(pos.x, pos.y);
                if (item == null) return true;
                fav.ToggleItem(item.m_shared);
                on = fav.IsItemFavorited(item.m_shared);
            }
            // Favoriting: a low click and the new frame pops into place.
            // Unfavoriting: the same click, higher, and the frame goes.
            ToggleSound(on);
            if (on)
            {
                List<InventoryElement> elements = GridElements(grid);
                int i = pos.y * GridInventory(grid).GetWidth() + pos.x;
                if (elements != null && i >= 0 && i < elements.Count)
                    StackifyEffects.Runner.Get().StartCoroutine(PopFrame(elements[i], left ? ItemFrame : SlotFrame, left ? 3f : 0f));
            }
            return false;
        }

        [HarmonyPatch(typeof(InventoryGrid), "OnLeftDown")]
        private static class LeftPatch
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler __0) { return Click(__instance, __0, true); }
        }

        [HarmonyPatch(typeof(InventoryGrid), "OnRightDown")]
        private static class RightPatch
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler __0) { return Click(__instance, __0, false); }
        }

        // The game's button click: lower for on, higher for off.
        private static GameObject _click;
        private static void ToggleSound(bool on)
        {
            InventoryGui gui = InventoryGui.instance;
            if (_click == null && gui != null && gui.m_takeAllButton != null)
            {
                ButtonSfx sfx = gui.m_takeAllButton.GetComponent<ButtonSfx>();
                if (sfx != null) _click = sfx.m_sfxPrefab;
            }
            if (_click != null) StackifyEffects.PlayUi(_click, on ? 0.82f : 1.18f);
        }

        // The frame drops in from a little bigger and fades in, overshoots
        // slightly smaller and settles, like it's being pressed onto the slot.
        private static System.Collections.IEnumerator PopFrame(InventoryElement e, string name, float inset)
        {
            yield return null; // the frame is switched on when the grid redraws
            Image img = Frame(e, name, inset);
            if (img == null) yield break;
            RectTransform r = img.rectTransform;
            Color full = img.color;
            float x = 0.35f, v = 0f; // scale offset on a spring: starts 35% bigger
            for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
            {
                if (r == null || img == null) yield break;
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                v += (-x * 420f - v * 20f) * dt;
                x += v * dt;
                float s = 1f + x;
                r.localScale = new Vector3(s, s, 1f);
                Color c = img.color;
                c.a = full.a * Mathf.Clamp01(t / 0.08f);
                img.color = c;
                if (t > 0.12f && Mathf.Abs(x) < 0.002f && Mathf.Abs(v) < 0.02f) break;
                yield return null;
            }
            if (r != null) r.localScale = Vector3.one;
            if (img != null) { Color c = img.color; c.a = full.a; img.color = c; }
        }

        // ---- The item you're holding keeps its frame ----
        // Set up once when you pick it up; it moves with the held icon.
        [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
        private static class HeldFramePatch
        {
            private static void Postfix(InventoryGui __instance, ItemDrop.ItemData item)
            {
                Player p = Player.m_localPlayer;
                GameObject drag = StackifyPlugin.DragGo(__instance);
                if (drag == null || item == null || p == null || !For(p).IsItemFavorited(item.m_shared)) return;
                try { HeldFrame(__instance, drag); }
                catch (Exception e) { StackifyPlugin.Log.LogWarning("Stackify held frame: " + e.Message); }
            }
        }

        private static void HeldFrame(InventoryGui gui, GameObject drag)
        {
            List<InventoryElement> elements = gui.m_playerGrid != null ? GridElements(gui.m_playerGrid) : null;
            InventoryElement sample = null;
            if (elements != null)
                foreach (InventoryElement e in elements)
                    if (e != null) { sample = e; break; }
            if (sample == null || drag.transform.parent == null) return;

            Transform icon = drag.transform.Find("icon");
            GameObject go = new GameObject(ItemFrame, typeof(RectTransform), typeof(Image));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(drag.transform, false);
            if (icon != null) r.SetSiblingIndex(icon.GetSiblingIndex() + 1);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);

            // The same size on screen as the frame in a slot, centred on the icon.
            RectTransform slot = (RectTransform)sample.transform;
            float scale = slot.lossyScale.x / Mathf.Max(0.0001f, drag.transform.parent.lossyScale.x);
            r.sizeDelta = (slot.rect.size - new Vector2(6f, 6f)) * scale;
            RectTransform iconRect = icon as RectTransform;
            r.position = iconRect != null ? iconRect.TransformPoint(iconRect.rect.center) : drag.transform.position;

            Image img = go.GetComponent<Image>();
            Image vanilla = sample.m_selected != null ? sample.m_selected.GetComponentInChildren<Image>(true) : null;
            if (vanilla != null && vanilla.sprite != null)
            {
                img.sprite = vanilla.sprite;
                img.type = vanilla.type;
                img.pixelsPerUnitMultiplier = vanilla.pixelsPerUnitMultiplier;
                img.fillCenter = false;
            }
            else img.sprite = BorderSprite();
            img.raycastTarget = false;
            img.color = StackifyPlugin.ColorItem.Value;
        }

        // ---- Borders: redone only when favorites or the inventory change ----

                private static Sprite _border;

        private static void RefreshVisible()
        {
            Player player = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            if (player == null || gui == null) return;

            Favorites fav = For(player);
            DrawGrid(gui.m_playerGrid, player.GetInventory(), fav, true);
            Container container = StackifyPlugin.CurrentContainer(gui);
            if (container != null)
                DrawGrid(gui.ContainerGrid, container.GetInventory(), fav, false);
        }

        private static void DrawGrid(InventoryGrid grid, Inventory inventory,
            Favorites fav, bool personal)
        {
            if (grid == null || inventory == null) return;
            List<InventoryElement> elements = GridElements(grid);
            if (elements == null) return;
            foreach (InventoryElement element in elements)
                DrawSlot(element, inventory, fav, personal);
        }

        private static void DrawSlot(InventoryElement element, Inventory inventory,
            Favorites fav, bool personal)
        {
            if (element == null || inventory == null) return;
            Vector2i position = element.Position;
            if (position.x < 0 || position.y < 0 ||
                position.x >= inventory.GetWidth() ||
                position.y >= inventory.GetHeight()) return;

            ItemDrop.ItemData item = inventory.GetItemAt(position.x, position.y);
            // Only the player's own inventory can have protected slots.
            if (personal)
                Show(Frame(element, SlotFrame, 0f),
                    fav.IsSlotFavorited(position), StackifyPlugin.ColorSlot.Value);
            // Item-type favorites are shared across inventory and containers.
            Show(Frame(element, ItemFrame, 3f),
                item != null && fav.IsItemFavorited(item.m_shared),
                StackifyPlugin.ColorItem.Value);
        }

        [HarmonyPatch(typeof(Inventory), "Changed")]
        private static class InventoryChangedPatch
        {
            private static void Postfix(Inventory __instance)
            {
                Player player = Player.m_localPlayer;
                if (player == null) return;
                InventoryGui gui = InventoryGui.instance;
                Container container = gui != null
                    ? StackifyPlugin.CurrentContainer(gui) : null;
                if (__instance == player.GetInventory() ||
                    (container != null && __instance == container.GetInventory()))
                    RefreshVisible();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class InventoryOpenedPatch
        {
            private static void Postfix()
            {
                RefreshVisible();
            }
        }

        // One-time slot construction callback, not a per-frame HUD hook.
        // Handles initial grids and resized grids before Inventory.Changed.
        [HarmonyPatch(typeof(InventoryElement), nameof(InventoryElement.Initialize))]
        private static class SlotCreatedPatch
        {
            private static void Postfix(InventoryElement __instance)
            {
                Player player = Player.m_localPlayer;
                InventoryGui gui = InventoryGui.instance;
                if (player == null || gui == null || __instance == null)
                    return;

                InventoryGrid grid = __instance.GetComponentInParent<InventoryGrid>();
                if (grid == null) return;
                bool personal = grid == gui.m_playerGrid;
                if (!personal && grid != gui.ContainerGrid) return;

                Inventory inventory;
                if (personal) inventory = player.GetInventory();
                else
                {
                    Container container = StackifyPlugin.CurrentContainer(gui);
                    if (container == null) return;
                    inventory = container.GetInventory();
                }

                // A slot at (0,0) begins each grid rebuild. Clear stale
                // lookup entries; attached border objects remain intact.
                if (__instance.Position.x == 0 && __instance.Position.y == 0)
                    Frames.Clear();
                DrawSlot(__instance, inventory, For(player), personal);
            }
        }

        private static void Show(Image img, bool on, Color color)
        {
            if (img == null) return;
            if (img.color != color) img.color = color;
            if (img.enabled != on) img.enabled = on;
        }

        private const string SlotFrame = "WireStackifySlotFrame";
        private const string ItemFrame = "WireStackifyItemFrame";

        // The game's own slot frame (the one it draws around a selected
        // slot), so thickness and corners match the inventory. The item
        // frame sits just inside the slot frame, so a favorited item in a
        // favorited slot shows both.
        // Found or made once per slot, then kept.
        private static readonly Dictionary<InventoryElement, Image[]> Frames = new Dictionary<InventoryElement, Image[]>();

        private static Image Frame(InventoryElement e, string name, float inset)
        {
            if (e == null || e.m_queued == null) return null;
            int k = name == SlotFrame ? 0 : name == ItemFrame ? 1 : name == StackRing ? 2 : 3;
            Image[] pair;
            if (!Frames.TryGetValue(e, out pair)) Frames[e] = pair = new Image[4];
            if (pair[k] != null) return pair[k];
            Transform t = e.m_queued.transform.Find(name);
            if (t != null) return pair[k] = t.GetComponent<Image>();
            pair[k] = MakeFrame(e, name, inset);
            return pair[k];
        }

        private static Image MakeFrame(InventoryElement e, string name, float inset)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform r = (RectTransform)go.transform;
            r.SetParent(e.m_queued.transform, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            Image img = go.GetComponent<Image>();
            Image vanilla = e.m_selected != null ? e.m_selected.GetComponentInChildren<Image>(true) : null;
            if (vanilla != null && vanilla.sprite != null)
            {
                img.sprite = vanilla.sprite;
                img.type = vanilla.type;
                img.pixelsPerUnitMultiplier = vanilla.pixelsPerUnitMultiplier;
                img.fillCenter = false;
            }
            else img.sprite = BorderSprite();
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        // A rounded frame, drawn once: the same proportions as Quick Stack
        // Store's border (thickness 2.3% of the slot, corners 6%).
        private static Sprite BorderSprite()
        {
            if (_border != null) return _border;
            const int n = 128;
            float thick = n * 24f / 1024f, radius = n * 64f / 1024f;
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = x + 0.5f - n * 0.5f, cy = y + 0.5f - n * 0.5f;
                    float half = n * 0.5f;
                    float dx = Mathf.Abs(cx) - half + radius, dy = Mathf.Abs(cy) - half + radius;
                    float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    // inside the outer edge and within the frame's thickness
                    float a = Mathf.Clamp01(0.5f - outside) * Mathf.Clamp01(outside + thick + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _border = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _border;
        }

        // ---- Hover preview ----
        // While a pouch is hovered: the outer ring in the game's yellow (its
        // stamina bar) on stacks a quick stack would take, the inner ring
        // in the game's blue (its equipped item) on stacks a restock would
        // top up. Drawn above the favorite frames, gone when you move off.
        private const string StackRing = "WireStackifyStackRing";
        private const string RestockRing = "WireStackifyRestockRing";
        private static readonly List<Image> Previewing = new List<Image>();
        private static readonly AccessTools.FieldRef<GuiBar, Color> BarColor =
            AccessTools.FieldRefAccess<GuiBar, Color>("m_originalColor");

        // The stamina bar's own yellow, read from the HUD.
        private static Color Yellow()
        {
            try
            {
                if (Hud.instance != null && Hud.instance.m_staminaBar2Fast != null)
                {
                    Color c = BarColor(Hud.instance.m_staminaBar2Fast);
                    if (c.a > 0f) { c.a = 1f; return c; }
                }
            }
            catch (Exception) { }
            return new Color(1f, 0.848f, 0f, 1f);
        }
        private static readonly Color Blue = new Color(0.376f, 0.66f, 0.897f, 1f);

        internal static string BlueHex() { return ColorUtility.ToHtmlStringRGB(Blue); }
        internal static string YellowHex() { return ColorUtility.ToHtmlStringRGB(Yellow()); }

        internal static void ShowPreview(List<ItemDrop.ItemData> stack, List<ItemDrop.ItemData> restock)
        {
            ClearPreview();
            InventoryGui gui = InventoryGui.instance;
            Player p = Player.m_localPlayer;
            if (gui == null || gui.m_playerGrid == null || p == null) return;
            Inventory inv = p.GetInventory();
            if (GridInventory(gui.m_playerGrid) != inv) return;
            List<InventoryElement> elements = GridElements(gui.m_playerGrid);
            if (elements == null) return;
            int w = inv.GetWidth();
            if (stack != null) Mark(stack, elements, w, StackRing, 0f, Yellow());
            if (restock != null) Mark(restock, elements, w, RestockRing, 3f, Blue);
        }

        private static void Mark(List<ItemDrop.ItemData> items, List<InventoryElement> elements, int w, string ring, float inset, Color c)
        {
            foreach (ItemDrop.ItemData it in items)
            {
                int i = it.m_gridPos.y * w + it.m_gridPos.x;
                if (i < 0 || i >= elements.Count) continue;
                Image img = Frame(elements[i], ring, inset);
                if (img == null) continue;
                if (img.color != c) img.color = c;
                img.transform.SetAsLastSibling();
                img.enabled = true;
                Previewing.Add(img);
            }
        }

        internal static void ClearPreview()
        {
            foreach (Image img in Previewing) if (img != null) img.enabled = false;
            Previewing.Clear();
        }

        // ---- Tooltip line ----

        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        private static class TooltipPatch
        {
            private static void Postfix(ItemDrop.ItemData item, bool crafting, ref string __result)
            {
                if (crafting || !StackifyPlugin.FavoriteTooltip.Value || Player.m_localPlayer == null || item == null) return;
                if (!For(Player.m_localPlayer).IsItemFavorited(item.m_shared)) return;
                __result += "\n<color=#" + ColorUtility.ToHtmlStringRGB(StackifyPlugin.ColorItem.Value) + ">Favorited: won't be quick stacked</color>";
            }
        }
    }
}
