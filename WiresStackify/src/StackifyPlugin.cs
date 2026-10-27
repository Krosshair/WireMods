using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresStackify
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed partial class StackifyPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "kajetan.valheim.stackify";
        public const string PluginName = "Wire's Stackify";
        public const string PluginVersion = "0.1.7";

        internal static StackifyPlugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private sealed class ConfigurationManagerAttributes
        {
            public int Order { get; set; }
        }


        private const string SecControls = "00 - Controls";
        private const string SecStack = "01 - Quick Stacking";
        private const string SecRestock = "02 - Restocking";
        private const string SecNearby = "03 - Nearby Containers";
        private const string OldSecNearby = "03 - Nearby Chests";
        private const string SecFavorites = "04 - Favoriting";
        private const string SecSort = "05 - Sorting";

        internal static ConfigEntry<StackAction> LeftClick, RightClick, MiddleClick;
        internal static ConfigEntry<StackAction> ShiftLeftClick, ShiftRightClick, ShiftMiddleClick;
        internal static ConfigEntry<StackAction> PanelMiddleClick, PanelShiftMiddleClick;
        internal static ConfigEntry<bool> DropOnPouch;
        internal static ConfigEntry<bool> HoverPreview;

        internal static ConfigEntry<float> QuickStackRange;
        internal static ConfigEntry<bool> QuickStackHotbar;
        internal static ConfigEntry<bool> TrophiesTogether;
        internal static ConfigEntry<bool> HoldToStack;

        internal static ConfigEntry<float> RestockRange;
        internal static ConfigEntry<bool> RestockHotbar;
        internal static ConfigEntry<bool> RestockOnlyAmmoAndFood;
        internal static ConfigEntry<bool> RestockOnlyFavorites;
        internal static ConfigEntry<int> RestockLimitAmmo;
        internal static ConfigEntry<int> RestockLimitConsumables;
        internal static ConfigEntry<int> RestockLimitGeneral;

        internal static ConfigEntry<bool> MultiplayerWithoutMuc;
        internal static ConfigEntry<bool> NonPhysicalContainers;
        internal static ConfigEntry<bool> NonPlayerBuilt;
        internal static ConfigEntry<bool> QuietChests;

        internal static ConfigEntry<KeyboardShortcut> FavoriteKey1;
        internal static ConfigEntry<KeyboardShortcut> FavoriteKey2;
        internal static ConfigEntry<Color> ColorItem;
        internal static ConfigEntry<Color> ColorSlot;
        internal static ConfigEntry<bool> FavoriteTooltip;

        internal static ConfigEntry<bool> SortHotbar;
        internal static ConfigEntry<bool> SortMergeStacks;

        private int _order = 1000;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            BindConfig();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(StackifyPlugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T value, string description, AcceptableValueBase range = null)
        {
            ConfigEntry<T> e = Config.Bind(section, key, value,
                new ConfigDescription(description, range, new ConfigurationManagerAttributes { Order = _order }));
            _order -= 10;
            return e;
        }

        // A setting that was renamed keeps the value it had under its old
        // name (BepInEx keeps unknown lines as orphans until saved).
        private void Renamed<T>(ConfigEntry<T> entry, string oldSection, string oldKey)
        {
            try
            {
                Dictionary<ConfigDefinition, string> orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                    ?.GetValue(Config) as Dictionary<ConfigDefinition, string>;
                if (orphans == null) return;
                ConfigDefinition old = new ConfigDefinition(oldSection, oldKey);
                string value;
                if (!orphans.TryGetValue(old, out value)) return;
                entry.SetSerializedValue(value);
                orphans.Remove(old);
                Config.Save();
            }
            catch (Exception e) { Log.LogWarning("Couldn't carry over " + oldKey + ": " + e.Message); }
        }

        private void BindConfig()
        {
            const string actions = " Sort, SortByWeight, QuickStack, Restock, QuickStackAndRestock, Everything (quick stack, restock, then sort your inventory and every container used) or None.";
            LeftClick = Bind(SecControls, "Left click", StackAction.QuickStack,
                "What left clicking a pouch does." + actions);
            RightClick = Bind(SecControls, "Right click", StackAction.None,
                "What right clicking a pouch does." + actions);
            MiddleClick = Bind(SecControls, "Middle click", StackAction.Sort,
                "What middle clicking a pouch does." + actions);
            ShiftLeftClick = Bind(SecControls, "Shift + left click", StackAction.Restock,
                "What shift + left clicking a pouch does." + actions);
            ShiftRightClick = Bind(SecControls, "Shift + right click", StackAction.Everything,
                "What shift + right clicking a pouch does." + actions);
            ShiftMiddleClick = Bind(SecControls, "Shift + middle click", StackAction.SortByWeight,
                "What shift + middle clicking a pouch does." + actions);
            PanelMiddleClick = Bind(SecControls, "Middle click on a panel", StackAction.Sort,
                "What middle clicking your inventory or an open container does, for that one. Counts almost anywhere on screen: with a container open, the top part is your inventory and the bottom part the container; not over other windows." + actions);
            PanelShiftMiddleClick = Bind(SecControls, "Shift + middle click on a panel", StackAction.SortByWeight,
                "The same with shift held." + actions);
            MigrateControls();
            DropOnPouch = Bind(SecControls, "Drop items on the pouch", true,
                "Holding an item, click your pouch to quick stack every stack of it into the open and nearby containers, or a container's pouch to top up your stacks of it from that container.");
            HoverPreview = Bind(SecControls, "Hover preview", true,
                "Hovering a pouch rings the stacks a quick stack (yellow, outer ring) or restock (blue, inner ring) would move, and shows the counts in its tooltip.");

            QuickStackRange = Bind(SecStack, "Range", 10f,
                "How far away, in meters, a container can be. 0: only the container you have open.",
                new AcceptableValueRange<float>(0f, 50f));
            QuickStackHotbar = Bind(SecStack, "Include hotbar", false,
                "Items on your hotbar can be quick stacked too.");
            TrophiesTogether = Bind(SecStack, "Trophies together", false,
                "All trophies go into a container that already holds any trophy, not only the same one.");
            HoldToStack = Bind(SecStack, "Place stacks uses quick stack", true,
                "The game's Place stacks (button and holding the use key on a container) works like quick stacking into that container, so favorites and the hotbar are left alone.");

            RestockRange = Bind(SecRestock, "Range", 10f,
                "How far away, in meters, a container can be. 0: only the container you have open.",
                new AcceptableValueRange<float>(0f, 50f));
            RestockHotbar = Bind(SecRestock, "Include hotbar", false,
                "Stacks on your hotbar are topped up too.");
            RestockOnlyAmmoAndFood = Bind(SecRestock, "Only ammo and consumables", true,
                "Only arrows, bolts and the like, and food, meads and other consumables are topped up.");
            RestockOnlyFavorites = Bind(SecRestock, "Only favorites", false,
                "Only favorited items and items in favorited slots are topped up.");
            RestockLimitAmmo = Bind(SecRestock, "Stack limit for ammo", 0,
                "Ammo is topped up to this many at most. 0: the general limit, or a full stack.",
                new AcceptableValueRange<int>(0, 1000));
            RestockLimitConsumables = Bind(SecRestock, "Stack limit for consumables", 0,
                "Consumables are topped up to this many at most. 0: the general limit, or a full stack.",
                new AcceptableValueRange<int>(0, 1000));
            RestockLimitGeneral = Bind(SecRestock, "Stack limit", 0,
                "Everything else is topped up to this many at most. 0: a full stack.",
                new AcceptableValueRange<int>(0, 1000));

            MultiplayerWithoutMuc = Bind(SecNearby, "Nearby containers in multiplayer", false,
                "Use nearby containers in multiplayer even without Multi User Chest. Without it, two players using the same container at once can lose items. With Multi User Chest installed this is always allowed. Single player always uses nearby containers.");
            Renamed(MultiplayerWithoutMuc, OldSecNearby, "Nearby chests in multiplayer");
            NonPhysicalContainers = Bind(SecNearby, "Carts, ships and other containers", true,
                "Containers that aren't a building piece (carts and ships, for example) count as nearby containers.");
            Renamed(NonPhysicalContainers, OldSecNearby, "Containers without a chest");
            NonPlayerBuilt = Bind(SecNearby, "Containers not built by players", false,
                "Containers you find in the world (dungeons, villages) count as nearby containers.");
            Renamed(NonPlayerBuilt, OldSecNearby, "Chests not built by players");
            QuietChests = Bind(SecNearby, "Quiet containers", true,
                "Nearby containers you quick stack into or restock from don't open and close for other players. You still hear each container you used.");
            Renamed(QuietChests, OldSecNearby, "Quiet chests");

            FavoriteKey1 = Bind(SecFavorites, "Favorite key", new KeyboardShortcut(KeyCode.LeftAlt),
                "Hold and left click an item, in your inventory or a container, to favorite it (every item of that kind); right click a slot in your inventory to favorite the slot. Favorites are never quick stacked.");
            FavoriteKey2 = Bind(SecFavorites, "Second favorite key", new KeyboardShortcut(KeyCode.RightAlt),
                "Works the same as Favorite key.");
            // Wood tones from the game's own inventory panel: its warm grain
            // for items, and the same grain lightened for slots, so they sit
            // in the panel instead of standing out of it.
            ColorItem = Bind(SecFavorites, "Favorite item frame color", new Color(0.729f, 0.494f, 0.306f, 1f),
                "Frame around a favorited item (every item of that kind).");
            ColorSlot = Bind(SecFavorites, "Favorite slot frame color", new Color(0.824f, 0.671f, 0.549f, 1f),
                "Frame around a favorited slot. A favorited item in a favorited slot shows both frames, the slot's outside.");
            // The earlier dark brown default becomes the new light one.
            Color oldSlot = new Color(0.573f, 0.357f, 0.239f, 1f);
            if (Mathf.Abs(ColorSlot.Value.r - oldSlot.r) < 0.01f && Mathf.Abs(ColorSlot.Value.g - oldSlot.g) < 0.01f && Mathf.Abs(ColorSlot.Value.b - oldSlot.b) < 0.01f)
                ColorSlot.Value = (Color)ColorSlot.DefaultValue;
            FavoriteTooltip = Bind(SecFavorites, "Tooltip line", true,
                "Favorited items say so in their tooltip.");

            SortHotbar = Bind(SecSort, "Include hotbar", false,
                "Your hotbar is sorted too.");
            SortMergeStacks = Bind(SecSort, "Merge stacks", true,
                "Part stacks of the same item are put together.");

            ColorItem.SettingChanged += (s, e) => Favorites.MarkDirty();
            ColorSlot.SettingChanged += (s, e) => Favorites.MarkDirty();
        }

        // ---- Shared helpers ----

        private static bool? _hasMuc;
        // A test build had other defaults (left sort, right quick stack,
        // middle restock, shift: both). Still exactly those: the new ones.
        private static void MigrateControls()
        {
            if (LeftClick.Value != StackAction.Sort || RightClick.Value != StackAction.QuickStack ||
                MiddleClick.Value != StackAction.Restock || ShiftLeftClick.Value != StackAction.QuickStackAndRestock ||
                ShiftRightClick.Value != StackAction.QuickStackAndRestock || ShiftMiddleClick.Value != StackAction.QuickStackAndRestock) return;
            foreach (ConfigEntry<StackAction> e in new[] { LeftClick, RightClick, MiddleClick, ShiftLeftClick, ShiftRightClick, ShiftMiddleClick })
                e.Value = (StackAction)e.DefaultValue;
        }

        internal static bool HasMuc
        {
            get
            {
                if (_hasMuc == null) _hasMuc = Chainloader.PluginInfos.ContainsKey("com.maxsch.valheim.MultiUserChest");
                return _hasMuc.Value;
            }
        }

        // Equipment and Quick Slots adds its equipment and quick slot rows
        // under your normal inventory rows. Those are its own: quick stack
        // and sort never touch them.
        private static bool? _hasEaqs;
        internal static int MainRows(Inventory inv, Player player)
        {
            if (_hasEaqs == null) _hasEaqs = Chainloader.PluginInfos.ContainsKey("randyknapp.mods.equipmentandquickslots");
            int h = inv.GetHeight();
            return _hasEaqs.Value && player != null && inv == player.GetInventory() ? Mathf.Min(h, 4) : h;
        }

        internal static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        internal static readonly AccessTools.FieldRef<InventoryGui, GameObject> DragGo =
            AccessTools.FieldRefAccess<InventoryGui, GameObject>("m_dragGo");
        private static readonly Action<InventoryGui, ItemDrop.ItemData, Inventory, int> SetupDragItem =
            AccessTools.MethodDelegate<Action<InventoryGui, ItemDrop.ItemData, Inventory, int>>(
                AccessTools.Method(typeof(InventoryGui), "SetupDragItem"));
        private static readonly Action<Inventory, bool, bool> InventoryChanged =
            AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(
                AccessTools.Method(typeof(Inventory), "Changed"));

        internal static void DropDragItem()
        {
            if (InventoryGui.instance != null) SetupDragItem(InventoryGui.instance, null, null, 0);
        }

        internal static void Changed(Inventory inv)
        {
            InventoryChanged(inv, false, false);
        }


        // Bottom row first, then left to right, like Quick Stack Store.
        internal static int CompareSlotOrder(Vector2i a, Vector2i b)
        {
            int r = -a.y.CompareTo(b.y);
            return r != 0 ? r : a.x.CompareTo(b.x);
        }

        // ---- Keys ----

        internal static bool Held(KeyboardShortcut k)
        {
            return k.MainKey != KeyCode.None && Input.GetKey(k.MainKey) && k.Modifiers.All(Input.GetKey);
        }
    }
}
