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
        public const string PluginVersion = "0.1.0";

        internal static StackifyPlugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private sealed class ConfigurationManagerAttributes
        {
            public int Order { get; set; }
        }

        internal enum ChestOpen { OnlyThatChest, AlsoNearbyChests }

        private const string SecStack = "01 - Quick Stacking";
        private const string SecRestock = "02 - Restocking";
        private const string SecNearby = "03 - Nearby Chests";
        private const string SecFavorites = "04 - Favoriting";

        internal static ConfigEntry<KeyboardShortcut> QuickStackKey;
        internal static ConfigEntry<float> QuickStackRange;
        internal static ConfigEntry<bool> QuickStackHotbar;
        internal static ConfigEntry<ChestOpen> QuickStackChestOpen;
        internal static ConfigEntry<bool> TrophiesTogether;
        internal static ConfigEntry<bool> HoldToStack;
        internal static ConfigEntry<bool> QuickStackMessage;

        internal static ConfigEntry<KeyboardShortcut> RestockKey;
        internal static ConfigEntry<float> RestockRange;
        internal static ConfigEntry<bool> RestockHotbar;
        internal static ConfigEntry<ChestOpen> RestockChestOpen;
        internal static ConfigEntry<bool> RestockOnlyAmmoAndFood;
        internal static ConfigEntry<bool> RestockOnlyFavorites;
        internal static ConfigEntry<int> RestockLimitAmmo;
        internal static ConfigEntry<int> RestockLimitConsumables;
        internal static ConfigEntry<int> RestockLimitGeneral;
        internal static ConfigEntry<bool> RestockMessage;

        internal static ConfigEntry<bool> MultiplayerWithoutMuc;
        internal static ConfigEntry<bool> NonPhysicalContainers;
        internal static ConfigEntry<bool> NonPlayerBuilt;
        internal static ConfigEntry<bool> QuietChests;

        internal static ConfigEntry<KeyboardShortcut> FavoriteKey1;
        internal static ConfigEntry<KeyboardShortcut> FavoriteKey2;
        internal static ConfigEntry<Color> ColorItem;
        internal static ConfigEntry<Color> ColorSlot;
        internal static ConfigEntry<Color> ColorBoth;
        internal static ConfigEntry<bool> FavoriteTooltip;

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

        private void BindConfig()
        {
            QuickStackKey = Bind(SecStack, "Quick stack key", new KeyboardShortcut(KeyCode.P),
                "Quick stacks your items into nearby chests that already hold the same item. Same as left clicking the chest button.");
            QuickStackRange = Bind(SecStack, "Range", 10f,
                "How far away, in meters, a chest can be. 0: only the chest you have open.",
                new AcceptableValueRange<float>(0f, 50f));
            QuickStackHotbar = Bind(SecStack, "Include hotbar", false,
                "Items on your hotbar can be quick stacked too.");
            QuickStackChestOpen = Bind(SecStack, "With a chest open", ChestOpen.OnlyThatChest,
                "Only that chest: the key and the inventory button only stack into the chest you have open. Also nearby chests: into it first, then the chests around you.");
            TrophiesTogether = Bind(SecStack, "Trophies together", false,
                "All trophies go into a chest that already holds any trophy, not only the same one.");
            HoldToStack = Bind(SecStack, "Place stacks uses quick stack", true,
                "The game's Place stacks (button and holding the use key on a chest) works like quick stacking into that chest, so favorites and the hotbar are left alone.");
            QuickStackMessage = Bind(SecStack, "Result message", true,
                "A message in the middle of the screen saying how many stacks were moved.");

            RestockKey = Bind(SecRestock, "Restock key", new KeyboardShortcut(KeyCode.L),
                "Tops up stacks in your inventory from nearby chests. Same as right clicking the chest button.");
            RestockRange = Bind(SecRestock, "Range", 10f,
                "How far away, in meters, a chest can be. 0: only the chest you have open.",
                new AcceptableValueRange<float>(0f, 50f));
            RestockHotbar = Bind(SecRestock, "Include hotbar", false,
                "Stacks on your hotbar are topped up too.");
            RestockChestOpen = Bind(SecRestock, "With a chest open", ChestOpen.OnlyThatChest,
                "Only that chest: the key and the inventory button only take from the chest you have open. Also nearby chests: from it first, then the chests around you.");
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
            RestockMessage = Bind(SecRestock, "Result message", true,
                "A message in the middle of the screen saying how many stacks were topped up.");

            MultiplayerWithoutMuc = Bind(SecNearby, "Nearby chests in multiplayer", false,
                "Use nearby chests in multiplayer even without Multi User Chest. Without it, two players using the same chest at once can lose items. With Multi User Chest installed this is always allowed. Single player always uses nearby chests.");
            NonPhysicalContainers = Bind(SecNearby, "Containers without a chest", true,
                "Containers that aren't a placed chest (carts, for example) count as nearby chests.");
            NonPlayerBuilt = Bind(SecNearby, "Chests not built by players", false,
                "Chests you find in the world (dungeons, villages) count as nearby chests.");
            QuietChests = Bind(SecNearby, "Quiet chests", true,
                "Nearby chests don't open, close or play their sound when you quick stack or restock.");

            FavoriteKey1 = Bind(SecFavorites, "Favorite key", new KeyboardShortcut(KeyCode.LeftAlt),
                "Hold and left click an item to favorite it (every item of that kind), right click to favorite the slot. Favorites are never quick stacked.");
            FavoriteKey2 = Bind(SecFavorites, "Second favorite key", new KeyboardShortcut(KeyCode.RightAlt),
                "Works the same as Favorite key.");
            ColorItem = Bind(SecFavorites, "Favorited item color", new Color(1f, 0.847f, 0f, 1f),
                "Border of a favorited item.");
            ColorSlot = Bind(SecFavorites, "Favorited slot color", new Color(0f, 0.502f, 1f, 1f),
                "Border of a favorited slot.");
            ColorBoth = Bind(SecFavorites, "Favorited item in favorited slot color", new Color(0.502f, 0.675f, 0.502f, 1f),
                "Border of a favorited item in a favorited slot.");
            FavoriteTooltip = Bind(SecFavorites, "Tooltip line", true,
                "Favorited items say so in their tooltip.");

            ColorItem.SettingChanged += (s, e) => Favorites.MarkDirty();
            ColorSlot.SettingChanged += (s, e) => Favorites.MarkDirty();
            ColorBoth.SettingChanged += (s, e) => Favorites.MarkDirty();
        }

        // ---- Shared helpers ----

        private static bool? _hasMuc;
        internal static bool HasMuc
        {
            get
            {
                if (_hasMuc == null) _hasMuc = Chainloader.PluginInfos.ContainsKey("com.maxsch.valheim.MultiUserChest");
                return _hasMuc.Value;
            }
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

        // The chest's own opening sound, once per quick stack or restock: at
        // the chest you have open, otherwise the closest chest it used.
        internal static void ChestSound(Container c)
        {
            if (c == null || c.m_openEffects == null) return;
            c.m_openEffects.Create(c.transform.position, c.transform.rotation);
        }

        internal static Container Closest(Container best, Container c, Vector3 from)
        {
            if (best == null) return c;
            return (c.transform.position - from).sqrMagnitude < (best.transform.position - from).sqrMagnitude ? c : best;
        }

        internal static void Message(Player player, string text)
        {
            player.Message(MessageHud.MessageType.Center, text);
        }

        // Bottom row first, then left to right, like Quick Stack Store.
        internal static int CompareSlotOrder(Vector2i a, Vector2i b)
        {
            int r = -a.y.CompareTo(b.y);
            return r != 0 ? r : a.x.CompareTo(b.x);
        }

        // ---- Keys ----

        [HarmonyPatch(typeof(Player), "Update")]
        private static class KeyPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || IgnoreKeys(__instance)) return;
                if (Down(QuickStackKey.Value)) QuickStack.Run(__instance, false);
                else if (Down(RestockKey.Value)) Restock.Run(__instance, false);
            }
        }

        internal static bool Down(KeyboardShortcut k)
        {
            return k.MainKey != KeyCode.None && Input.GetKeyDown(k.MainKey) && k.Modifiers.All(Input.GetKey);
        }

        internal static bool Held(KeyboardShortcut k)
        {
            return k.MainKey != KeyCode.None && Input.GetKey(k.MainKey) && k.Modifiers.All(Input.GetKey);
        }

        private static bool IgnoreKeys(Player p)
        {
            return p.InCutscene() || p.IsTeleporting() || p.IsDead() || p.InPlaceMode() ||
                ZNetScene.instance == null || Minimap.IsOpen() || Menu.IsVisible() || global::Console.IsVisible() ||
                StoreGui.IsVisible() || TextInput.IsVisible() ||
                (Chat.instance != null && Chat.instance.HasFocus()) ||
                (ZNet.instance != null && ZNet.instance.InPasswordDialog()) ||
                (TextViewer.instance != null && TextViewer.instance.IsVisible());
        }
    }
}
