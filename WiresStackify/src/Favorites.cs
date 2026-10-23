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
        private static int _version;

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

        internal static void MarkDirty() { _version++; }

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

        private static bool Favoriting(InventoryGrid grid)
        {
            return InventoryGui.instance != null && grid == InventoryGui.instance.m_playerGrid &&
                Player.m_localPlayer != null && !Player.m_localPlayer.IsTeleporting() &&
                StackifyPlugin.DragGo(InventoryGui.instance) == null &&
                (StackifyPlugin.Held(StackifyPlugin.FavoriteKey1.Value) || StackifyPlugin.Held(StackifyPlugin.FavoriteKey2.Value));
        }

        private static bool Click(InventoryGrid grid, UIInputHandler handler, bool left)
        {
            if (handler == null || !Favoriting(grid)) return true;
            Vector2i pos = ButtonPos(grid, handler.gameObject);
            if (pos == new Vector2i(-1, -1)) return true;
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
            if (_click != null) StackifyEffects.PlayNow(_click, StackifyEffects.Ear(), Quaternion.identity, null, on ? 0.82f : 1.18f);
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

        // ---- Borders: redone only when favorites or the inventory change ----

                private static Sprite _border;
        private static int _drawnVersion = -1;
        private static List<InventoryElement> _drawnElements;
        private static int _drawnCount;

        [HarmonyPatch(typeof(Inventory), "Changed")]
        private static class InventoryChangedPatch
        {
            private static void Postfix(Inventory __instance)
            {
                if (Player.m_localPlayer != null && __instance == Player.m_localPlayer.GetInventory()) _version++;
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class BorderPatch
        {
            private static void Postfix(InventoryGrid __instance, Player player)
            {
                if (player == null || InventoryGui.instance == null || __instance != InventoryGui.instance.m_playerGrid) return;
                Inventory inv = GridInventory(__instance);
                if (inv != player.GetInventory()) return;
                List<InventoryElement> elements = GridElements(__instance);
                if (_drawnVersion == _version && ReferenceEquals(_drawnElements, elements) && _drawnCount == elements.Count) return;
                if (!ReferenceEquals(_drawnElements, elements)) Frames.Clear(); // the grid was rebuilt
                _drawnVersion = _version;
                _drawnElements = elements;
                _drawnCount = elements.Count;
                Draw(inv, elements, For(player));
            }
        }

        private static void Draw(Inventory inv, List<InventoryElement> elements, Favorites fav)
        {
            int w = inv.GetWidth(), h = inv.GetHeight();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (i >= elements.Count) break;
                    Show(Frame(elements[i], SlotFrame, 0f), fav.IsSlotFavorited(new Vector2i(x, y)), StackifyPlugin.ColorSlot.Value);
                    Show(Frame(elements[i], ItemFrame, 3f), false, StackifyPlugin.ColorItem.Value);
                }
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                if (!fav.IsItemFavorited(it.m_shared)) continue;
                int i = it.m_gridPos.y * w + it.m_gridPos.x;
                if (i < 0 || i >= elements.Count) continue;
                Show(Frame(elements[i], ItemFrame, 3f), true, StackifyPlugin.ColorItem.Value);
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
            int k = name == SlotFrame ? 0 : 1;
            Image[] pair;
            if (!Frames.TryGetValue(e, out pair)) Frames[e] = pair = new Image[2];
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
