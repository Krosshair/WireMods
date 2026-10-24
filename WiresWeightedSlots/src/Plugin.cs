using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresWeightedSlots
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    [BepInDependency("wire.valheim.smoothui", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Azumatt.AzuExtendedPlayerInventory", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Guid = "wire.valheim.weightedslots";
        internal const string Name = "Wire's Weighted Slots";
        internal const string Version = "0.1.2";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> MaxDarkness;
        internal static ConfigEntry<float> CurveWeight;
        internal static ConfigEntry<float> BorderInset;

        // Bumped on any inventory change or config change: the slot shades
        // are only worked out again when one of these moved.
        internal static int InventoryVersion;
        internal static int ConfigVersion;
        private Harmony _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Weighted Slot Backgrounds", "Enabled", true,
                "Shade the empty BACKGROUND inside an occupied inventory or hotbar slot according to that item's total weight.");
            MaxDarkness = Config.Bind("Weighted Slot Backgrounds", "Maximum darkness", 0.92f,
                new ConfigDescription("Upper limit of black opacity, reached only asymptotically for extremely heavy stacks. Increase up to 0.95 for stronger contrast.",
                    new AcceptableValueRange<float>(0f, 0.95f)));
            CurveWeight = Config.Bind("Weighted Slot Backgrounds", "Weight curve (kg)", 75f,
                new ConfigDescription("Half-darkness weight in kg. Higher numbers make the shading less intense at the same weight. Even extremely heavy stacks remain visually distinguishable.",
                    new AcceptableValueRange<float>(5f, 500f)));
            BorderInset = Config.Bind("Weighted Slot Backgrounds", "Border inset (pixels)", 3.5f,
                new ConfigDescription("Keep the dark shade away from the original slot border, rarity, and selection indicators.",
                    new AcceptableValueRange<float>(0f, 12f)));

            Config.SettingChanged += (sender, args) => ConfigVersion++;

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
            // Overlay components belong to UI elements and are destroyed with
            // those elements. Restore visible panels if the plugin is unloaded.
            WeightShade.RestoreAll();
        }
    }

    // Never modifies item icons, root slot graphics, button ColorBlocks,
    // quality stars, hover styles, bindings, text, or durability overlays.
    internal sealed class WeightShade : MonoBehaviour
    {
        private static readonly List<WeightShade> All = new List<WeightShade>();
        private Image _shade;
        private float _lastWeight = float.NaN;
        internal int SeenStamp;

        internal void ClearIfNotSeen(int stamp)
        {
            if (SeenStamp != stamp && _shade != null)
                Apply(0f, Plugin.Enabled != null && Plugin.Enabled.Value);
        }
        private float _lastDark = float.NaN;
        private float _lastScale = float.NaN;
        private float _lastInset = float.NaN;
        private bool _lastEnabled;

        internal static void UpdateSlot(GameObject slot, float totalKg, int stamp = 0)
        {
            if (slot == null) return;

            WeightShade component = slot.GetComponent<WeightShade>();
            bool enabled = Plugin.Enabled != null && Plugin.Enabled.Value;

            if (component == null)
            {
                // Avoid allocating any UI components for empty/weightless slots.
                if (!enabled || totalKg < 0.05f) return;
                component = slot.AddComponent<WeightShade>();
            }

            component.SeenStamp = stamp;
            component.Apply(enabled ? Mathf.Max(0f, totalKg) : 0f, enabled);
        }

        private void Awake()
        {
            All.Add(this);
        }

        private void EnsureImage()
        {
            if (_shade != null) return;

            GameObject go = new GameObject(
                "WeightedSlots_BackgroundShade",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            // Parent Image/Button draws first; this child draws second;
            // existing icons, amount/quality text and all other overlays
            // remain above it. The inset protects the border's appearance.
            rect.SetAsFirstSibling();
            _shade = go.GetComponent<Image>();
            _shade.raycastTarget = false;
            _shade.color = new Color(0f, 0f, 0f, 0f);
            _shade.enabled = false;
        }

        private void Apply(float weight, bool enabled)
        {
            float darkness = Plugin.MaxDarkness.Value;
            float scale = Plugin.CurveWeight.Value;
            float inset = Plugin.BorderInset.Value;

            if (_shade != null && weight == _lastWeight &&
                darkness == _lastDark && scale == _lastScale &&
                inset == _lastInset && enabled == _lastEnabled)
            {
                return; // No UI dirtied on unchanged refreshes.
            }

            _lastWeight = weight;
            _lastDark = darkness;
            _lastScale = scale;
            _lastInset = inset;
            _lastEnabled = enabled;

            float alpha = 0f;
            if (enabled && weight >= 0.05f)
                                // Hyperbolic response: 75 kg reaches half of the
                // maximum darkness, 360 kg ~83% of it. Unlike the
                // previous exponential, hundreds of kg remain
                // distinct rather than saturating around 100 kg.
                alpha = darkness * weight / (weight + scale);

            if (alpha <= 0.0005f)
            {
                if (_shade != null && _shade.enabled)
                    _shade.enabled = false;
                return;
            }

            EnsureImage();

            RectTransform rect = (RectTransform)_shade.transform;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            _shade.color = new Color(0f, 0f, 0f, alpha);
            _shade.enabled = true;
        }

        internal static void RestoreAll()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                WeightShade component = All[i];
                if (component != null && component._shade != null)
                    component._shade.enabled = false;
            }
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    [HarmonyAfter("wire.valheim.smoothui",
        "Azumatt.AzuExtendedPlayerInventory", "shudnal.MyLittleUI")]
    internal static class InventoryGridPatch
    {
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>>
            Elements = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private sealed class Seen
        {
            public object Elements;
            public int Count;
            public Inventory Inventory;
            public int InventoryVersion;
            public int ConfigVersion;
        }

        private static readonly Dictionary<InventoryGrid, Seen> Last =
            new Dictionary<InventoryGrid, Seen>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryGrid __instance)
        {
            Inventory inventory = __instance.GetInventory();
            if (inventory == null) return;

            List<InventoryElement> elements = Elements(__instance);
            if (elements == null || elements.Count == 0) return;

            // UpdateGui runs every frame while the inventory is open: only
            // redo the shades when the items, the slots or the settings
            // changed since the last time.
            Seen seen;
            if (!Last.TryGetValue(__instance, out seen))
            {
                if (Last.Count > 16) Last.Clear(); // grids that are gone
                Last[__instance] = seen = new Seen();
            }
            if (ReferenceEquals(seen.Elements, elements) && seen.Count == elements.Count &&
                ReferenceEquals(seen.Inventory, inventory) &&
                seen.InventoryVersion == Plugin.InventoryVersion &&
                seen.ConfigVersion == Plugin.ConfigVersion)
                return;
            seen.Elements = elements;
            seen.Count = elements.Count;
            seen.Inventory = inventory;
            seen.InventoryVersion = Plugin.InventoryVersion;
            seen.ConfigVersion = Plugin.ConfigVersion;

            int width = Mathf.Max(1, inventory.GetWidth());

            // Clear a shade when the item was removed or moved. No objects
            // are created for zero-weight or empty slots.
            for (int i = 0; i < elements.Count; i++)
            {
                InventoryElement element = elements[i];
                if (element != null && !element.m_used)
                    WeightShade.UpdateSlot(element.gameObject, 0f);
            }

            // Valheim already provides this list in UpdateGui. No LINQ,
            // new arrays, or extra physics; count changes are handled on
            // the existing GUI update event.
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null) continue;
                int index = item.m_gridPos.y * width + item.m_gridPos.x;
                if (index < 0 || index >= elements.Count) continue;
                InventoryElement element = elements[index];
                if (element == null) continue;
                WeightShade.UpdateSlot(element.gameObject, item.GetWeight());
            }
        }
    }

    // For vanilla hotbar and the actual Azu QuickAccessBar implementation.
    // Azu replaces HotkeyBar.UpdateIcons in a Harmony prefix; our postfix
    // still runs after it and uses Azu's exact logical quickslot indices.
    [HarmonyPatch(typeof(HotkeyBar), "UpdateIcons")]
    [HarmonyAfter("Azumatt.AzuExtendedPlayerInventory",
        "wire.valheim.smoothui")]
    internal static class HotbarPatch
    {
        private static readonly AccessTools.FieldRef<HotkeyBar, List<ItemDrop.ItemData>>
            Items = AccessTools.FieldRefAccess<HotkeyBar, List<ItemDrop.ItemData>>("m_items");

        private static readonly FieldInfo ElementsField =
            AccessTools.Field(typeof(HotkeyBar), "m_elements");

        private static readonly FieldInfo ElementGoField =
            AccessTools.Field(
                typeof(HotkeyBar).GetNestedType(
                    "ElementData", BindingFlags.NonPublic), "m_go");

        private static int _drawStamp;
        private static bool _azuInitialized;
        private static FieldInfo _azuSlots;
        private static FieldInfo _azuQuickCount;

        private static void InitAzu()
        {
            if (_azuInitialized) return;
            _azuInitialized = true;

            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(
                "Azumatt.AzuExtendedPlayerInventory", out info))
                return;

            Assembly assembly = info.Instance.GetType().Assembly;
            Type slotsType = assembly.GetType(
                "AzuEPI.EPI.ExtendedPlayerInventory", false);
            Type mainType = assembly.GetType(
                "AzuEPI.AzuExtendedPlayerInventoryPlugin", false);

            if (slotsType != null && mainType != null)
            {
                _azuSlots = AccessTools.Field(slotsType, "slots");
                _azuQuickCount = AccessTools.Field(mainType, "QuickSlotsAmount");
            }
        }

        private static bool TryGetAzuStart(Player player, out int start)
        {
            start = 0;
            InitAzu();

            if (_azuSlots == null || _azuQuickCount == null) return false;

            ICollection slotList = _azuSlots.GetValue(null) as ICollection;
            ConfigEntry<int> countConfig =
                _azuQuickCount.GetValue(null) as ConfigEntry<int>;
            if (slotList == null || countConfig == null) return false;

            Inventory inventory = player.GetInventory();
            int width = inventory.GetWidth();
            if (width <= 0) return false;

            // Mirrors AzuEPI.Game.Slots.QAB.QuickAccessBar.Prefix:
            // start = (height - API.GetAddedRows(width))*width +
            //         ExtendedPlayerInventory.slots.Count - amount.
            int extra = (slotList.Count + width - 1) / width;
            start = (inventory.GetHeight() - extra) * width +
                slotList.Count - countConfig.Value;
            return start >= 0;
        }

        private static object _lastElements;
        private static int _lastCount = -1, _lastInventoryVersion = -1, _lastConfigVersion = -1, _lastPlayer;
        private static HotkeyBar _lastBar;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HotkeyBar __instance, Player player)
        {
            if (player == null || player.IsDead()) return;

            IList elements = ElementsField.GetValue(__instance) as IList;
            if (elements == null || elements.Count == 0) return;

            // UpdateIcons runs every frame: only redo the shades when the
            // items, the slots or the settings changed since last time.
            // (One bar is remembered; with two bars, as with Azu's, each
            // redoes its shades when the other one ran last.)
            int playerId = player.GetInstanceID();
            if (ReferenceEquals(_lastBar, __instance) && ReferenceEquals(_lastElements, elements) &&
                _lastCount == elements.Count && _lastPlayer == playerId &&
                _lastInventoryVersion == Plugin.InventoryVersion &&
                _lastConfigVersion == Plugin.ConfigVersion)
                return;
            _lastBar = __instance;
            _lastElements = elements;
            _lastCount = elements.Count;
            _lastPlayer = playerId;
            _lastInventoryVersion = Plugin.InventoryVersion;
            _lastConfigVersion = Plugin.ConfigVersion;

            // Stamp occupied slots first, then clear only those that became
            // empty. Existing weights do not change or dirty the UI each frame.
            int stamp = ++_drawStamp;
            List<ItemDrop.ItemData> items = Items(__instance);

            bool isAzu = __instance.name == "AzuEPI_QuickAccessBar";
            int start = 0;
            int width = player.GetInventory().GetWidth();
            bool canMapItems =
                !isAzu || TryGetAzuStart(player, out start);

            if (canMapItems && items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ItemDrop.ItemData item = items[i];
                    if (item == null || item.m_shared == null) continue;
                    int index = isAzu
                        ? item.m_gridPos.y * width + item.m_gridPos.x - start
                        : item.m_gridPos.x;

                    if (index < 0 || index >= elements.Count) continue;
                    GameObject slot = ElementGoField.GetValue(elements[index]) as GameObject;
                    if (slot == null) continue;
                    WeightShade.UpdateSlot(slot, item.GetWeight(), stamp);
                }
            }

            for (int i = 0; i < elements.Count; i++)
            {
                GameObject root = ElementGoField.GetValue(elements[i]) as GameObject;
                if (root == null) continue;
                WeightShade existing = root.GetComponent<WeightShade>();
                if (existing != null) existing.ClearIfNotSeen(stamp);
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), "Changed")]
    internal static class InventoryChangedPatch
    {
        private static void Postfix()
        {
            Plugin.InventoryVersion++;
        }
    }
}
