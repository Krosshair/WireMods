using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WiresRecycler
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.recycler";
        public const string Name = "Wire's Recycler";
        public const string Version = "1.0.2";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            RecyclerUi.Shutdown();
            CraftXpDebt.Reset();

            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }

    internal sealed class RecycleOutput
    {
        internal ItemDrop Item;
        internal int BaseMin;
        internal int BaseMax;
        internal float BaseHighChance;
        internal int UpgradeMin;
        internal int UpgradeMax;
        internal float UpgradeHighChance;

        internal int MinAmount
        {
            get { return BaseMin + UpgradeMin; }
        }

        internal int MaxAmount
        {
            get { return BaseMax + UpgradeMax; }
        }

        internal int Roll()
        {
            int amount = BaseMin;

            if (BaseMax > BaseMin &&
                UnityEngine.Random.value < BaseHighChance)
            {
                amount = BaseMax;
            }

            amount += UpgradeMin;

            if (UpgradeMax > UpgradeMin &&
                UnityEngine.Random.value < UpgradeHighChance)
            {
                amount += UpgradeMax - UpgradeMin;
            }

            return amount;
        }
    }

    internal sealed class RecyclePlan
    {
        internal Recipe Recipe;
        internal ItemDrop.ItemData Item;
        internal int ConsumeAmount;
        internal int RequiredLevel;
        internal bool SpecialReverse;
        internal readonly List<RecycleOutput> Outputs =
            new List<RecycleOutput>();
    }

    internal static class GuiAccess
    {
        internal static readonly FieldInfo AvailableRecipes =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_availableRecipes");

        internal static readonly FieldInfo SelectedRecipe =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_selectedRecipe");

        internal static readonly FieldInfo RecipeListBaseSize =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_recipeListBaseSize");

        internal static readonly FieldInfo MinStationBaseColor =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_minStationLevelBasecolor");

        internal static readonly FieldInfo CraftTimer =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_craftTimer");

        internal static readonly FieldInfo MultiCrafting =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_multiCrafting");

        internal static readonly MethodInfo AddRecipeToList =
            AccessTools.Method(
                typeof(InventoryGui),
                "AddRecipeToList",
                new[]
                {
                    typeof(Player),
                    typeof(Recipe),
                    typeof(ItemDrop.ItemData),
                    typeof(bool)
                });

        internal static readonly MethodInfo UpdateCraftingPanel =
            AccessTools.Method(
                typeof(InventoryGui),
                "UpdateCraftingPanel",
                new[] { typeof(bool) });

        private static readonly Type PairType =
            AccessTools.Inner(
                typeof(InventoryGui),
                "RecipeDataPair");

        private static readonly PropertyInfo PairRecipe =
            PairType != null
                ? PairType.GetProperty(
                    "Recipe",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                : null;

        private static readonly PropertyInfo PairItem =
            PairType != null
                ? PairType.GetProperty(
                    "ItemData",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                : null;

        private static readonly PropertyInfo PairElement =
            PairType != null
                ? PairType.GetProperty(
                    "InterfaceElement",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                : null;

        internal static IList GetAvailable(
            InventoryGui gui)
        {
            return AvailableRecipes != null
                ? AvailableRecipes.GetValue(gui) as IList
                : null;
        }

        internal static void GetSelected(
            InventoryGui gui,
            out Recipe recipe,
            out ItemDrop.ItemData item)
        {
            recipe = null;
            item = null;

            if (SelectedRecipe == null ||
                PairRecipe == null ||
                PairItem == null)
                return;

            object pair =
                SelectedRecipe.GetValue(gui);

            if (pair == null)
                return;

            recipe =
                PairRecipe.GetValue(
                    pair,
                    null) as Recipe;

            item =
                PairItem.GetValue(
                    pair,
                    null) as ItemDrop.ItemData;
        }

        internal static GameObject GetPairElement(
            object pair)
        {
            return pair != null &&
                   PairElement != null
                ? PairElement.GetValue(
                    pair,
                    null) as GameObject
                : null;
        }

        internal static void Refresh(
            InventoryGui gui,
            bool focus)
        {
            if (UpdateCraftingPanel != null)
            {
                UpdateCraftingPanel.Invoke(
                    gui,
                    new object[] { focus });
            }
        }
    }

    internal static class RecyclerUi
    {
        private static readonly HashSet<string> GaldrReversePrefabs =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "Bronze",
                "CeramicPlate"
            };

        private static readonly List<RecyclePlan> Plans =
            new List<RecyclePlan>();

        private static readonly List<ItemDrop.ItemData> TempItems =
            new List<ItemDrop.ItemData>();

        private static InventoryGui _gui;
        private static GameObject _tabObject;
        private static Button _tabButton;
        private static ItemDrop.ItemData _armedItem;
        private static Recipe _armedRecipe;

        internal static bool Active { get; private set; }

        internal static void Initialize(
            InventoryGui gui)
        {
            Shutdown();

            _gui = gui;

            if (gui == null ||
                gui.m_tabUpgrade == null)
                return;

            _tabObject =
                UnityEngine.Object.Instantiate(
                    gui.m_tabUpgrade.gameObject,
                    gui.m_tabUpgrade.transform.parent,
                    false);

            _tabObject.name = "WireRecycleTab";

            _tabButton =
                _tabObject.GetComponent<Button>();

            if (_tabButton == null)
            {
                UnityEngine.Object.Destroy(
                    _tabObject);

                _tabObject = null;
                return;
            }

            _tabButton.onClick.RemoveAllListeners();
            _tabButton.onClick.AddListener(
                SelectRecycleTab);

            TMP_Text label =
                _tabObject.GetComponentInChildren<TMP_Text>(
                    true);

            if (label != null)
                label.text = "RECYCLE";

            RectTransform recycleRect =
                _tabObject.transform as RectTransform;

            RectTransform upgradeRect =
                gui.m_tabUpgrade.transform as RectTransform;

            if (recycleRect != null &&
                upgradeRect != null)
            {
                float width =
                    recycleRect.rect.width;

                float maxX =
                    upgradeRect.localPosition.x;

                Transform parent =
                    gui.m_tabUpgrade.transform.parent;

                foreach (Transform child in parent)
                {
                    if (child == _tabObject.transform ||
                        !child.gameObject.activeSelf ||
                        child.GetComponent<Button>() == null)
                        continue;

                    maxX =
                        Mathf.Max(
                            maxX,
                            child.localPosition.x);
                }

                Vector3 p =
                    upgradeRect.localPosition;

                p.x =
                    maxX + width + 10f;

                recycleRect.localPosition = p;
            }

            _tabButton.interactable = true;
            _tabObject.SetActive(false);
        }

        internal static void Shutdown()
        {
            Active = false;
            ClearArmed();
            Plans.Clear();

            if (_tabObject != null)
            {
                UnityEngine.Object.Destroy(
                    _tabObject);
            }

            _tabObject = null;
            _tabButton = null;
            _gui = null;
        }

        internal static void LeaveRecycleTab()
        {
            if (!Active)
                return;

            Active = false;
            ClearArmed();

            if (_tabButton != null)
                _tabButton.interactable = true;
        }

        internal static void SyncTabVisibility(
            InventoryGui gui)
        {
            if (_tabObject == null ||
                gui == null)
                return;

            Player player =
                Player.m_localPlayer;

            CraftingStation station =
                player != null
                    ? player.GetCurrentCraftingStation()
                    : null;

            bool visible =
                station != null &&
                !station.m_upgrader &&
                gui.m_tabUpgrade.gameObject.activeSelf &&
                StationSupportsRecycler(station);

            _tabObject.SetActive(visible);

            if (!visible && Active)
                LeaveRecycleTab();
        }

        private static void SelectRecycleTab()
        {
            if (_gui == null ||
                Player.m_localPlayer == null)
                return;

            CraftingStation station =
                Player.m_localPlayer
                    .GetCurrentCraftingStation();

            if (station == null ||
                station.m_upgrader ||
                !StationSupportsRecycler(station))
                return;

            Active = true;
            ClearArmed();

            _gui.m_tabCraft.interactable = true;
            _gui.m_tabUpgrade.interactable = true;

            if (_tabButton != null)
                _tabButton.interactable = false;

            GuiAccess.Refresh(
                _gui,
                false);
        }

        internal static void RebuildRecipeList(
            InventoryGui gui,
            List<Recipe> vanillaRecipes)
        {
            if (!Active ||
                gui == null ||
                Player.m_localPlayer == null)
                return;

            IList available =
                GuiAccess.GetAvailable(gui);

            if (available == null ||
                GuiAccess.AddRecipeToList == null)
                return;

            for (int i = 0;
                 i < available.Count;
                 i++)
            {
                GameObject element =
                    GuiAccess.GetPairElement(
                        available[i]);

                if (element != null)
                {
                    UnityEngine.Object.Destroy(
                        element);
                }
            }

            available.Clear();
            Plans.Clear();

            Player player =
                Player.m_localPlayer;

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station == null)
                return;

            List<Recipe> source =
                new List<Recipe>();

            if (vanillaRecipes != null)
            {
                for (int i = 0;
                     i < vanillaRecipes.Count;
                     i++)
                {
                    Recipe recipe =
                        vanillaRecipes[i];

                    if (recipe != null &&
                        !source.Contains(recipe))
                    {
                        source.Add(recipe);
                    }
                }
            }

            AddSpecialReverseRecipes(
                player,
                station,
                source);

            Inventory inventory =
                player.GetInventory();

            for (int r = 0;
                 r < source.Count;
                 r++)
            {
                Recipe recipe =
                    source[r];

                bool special =
                    IsSpecialReverse(
                        station,
                        recipe);

                if (!RecipeCanAppear(
                        recipe,
                        station,
                        special))
                    continue;

                TempItems.Clear();

                inventory.GetAllItems(
                    recipe.m_item.m_itemData
                        .m_shared.m_name,
                    TempItems);

                for (int i = 0;
                     i < TempItems.Count;
                     i++)
                {
                    ItemDrop.ItemData item =
                        TempItems[i];

                    int consumeAmount =
                        GetConsumeAmount(
                            item,
                            recipe);

                    if (consumeAmount <= 0)
                        continue;

                    RecyclePlan plan =
                        new RecyclePlan
                        {
                            Recipe = recipe,
                            Item = item,
                            ConsumeAmount =
                                consumeAmount,
                            RequiredLevel =
                                special
                                    ? 1
                                    : Mathf.Max(
                                        1,
                                        recipe.GetRequiredStationLevel(
                                            1)),
                            SpecialReverse =
                                special
                        };

                    BuildOutputs(
                        gui,
                        plan);

                    if (plan.Outputs.Count == 0)
                        continue;

                    bool canRecycle =
                        IsStationUsableForPlan(
                            player,
                            station,
                            plan);

                    GuiAccess.AddRecipeToList.Invoke(
                        gui,
                        new object[]
                        {
                            player,
                            recipe,
                            item,
                            canRecycle
                        });

                    object pair =
                        available.Count > 0
                            ? available[
                                available.Count - 1]
                            : null;

                    GameObject element =
                        GuiAccess.GetPairElement(pair);

                    if (element != null)
                    {
                        PolishListElement(
                            element,
                            plan);
                    }

                    Plans.Add(plan);
                }
            }

            float baseSize = 0f;

            if (GuiAccess.RecipeListBaseSize != null)
            {
                object value =
                    GuiAccess.RecipeListBaseSize
                        .GetValue(gui);

                if (value is float)
                    baseSize = (float)value;
            }

            float height =
                Mathf.Max(
                    baseSize,
                    available.Count *
                    gui.m_recipeListSpace);

            gui.m_recipeListRoot
                .SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    height);
        }

        private static void PolishListElement(
            GameObject element,
            RecyclePlan plan)
        {
            if (element == null ||
                plan == null ||
                plan.Item == null)
                return;

            Transform iconTransform =
                element.transform.Find("icon");

            if (iconTransform != null)
            {
                Image icon =
                    iconTransform.GetComponent<Image>();

                if (icon != null)
                    icon.sprite =
                        plan.Item.GetIcon();
            }

            Transform nameTransform =
                element.transform.Find("name");

            if (nameTransform != null)
            {
                TMP_Text name =
                    nameTransform.GetComponent<TMP_Text>();

                if (name != null)
                {
                    string text =
                        Localization.instance.Localize(
                            plan.Item.m_shared.m_name);

                    if (plan.Item.m_quality > 1)
                    {
                        text +=
                            " Q" +
                            plan.Item.m_quality;
                    }

                    if (plan.ConsumeAmount > 1)
                    {
                        text +=
                            " x" +
                            plan.ConsumeAmount;
                    }

                    name.text = text;
                }
            }

            Transform qualityTransform =
                element.transform.Find(
                    "QualityLevel");

            if (qualityTransform != null)
            {
                qualityTransform.gameObject
                    .SetActive(
                        plan.Item.m_quality > 1);
            }
        }

        internal static void OverrideRecipeDetails(
            InventoryGui gui,
            Player player)
        {
            if (!Active ||
                gui == null ||
                player == null)
                return;

            Recipe recipe;
            ItemDrop.ItemData item;

            GuiAccess.GetSelected(
                gui,
                out recipe,
                out item);

            RecyclePlan plan =
                FindPlan(
                    recipe,
                    item);

            if (plan == null)
            {
                ClearArmed();
                return;
            }

            if (_armedItem != null &&
                (_armedItem != item ||
                 _armedRecipe != recipe))
            {
                ClearArmed();
            }

            gui.m_recipeIcon.enabled = true;
            gui.m_recipeName.enabled = true;
            gui.m_recipeDecription.enabled = true;
            gui.m_qualityPanel.gameObject
                .SetActive(false);
            gui.m_variantButton.gameObject
                .SetActive(false);

            gui.m_recipeIcon.sprite =
                item.GetIcon();

            string itemName =
                Localization.instance.Localize(
                    item.m_shared.m_name);

            gui.m_recipeName.text =
                item.m_quality > 1
                    ? itemName +
                      " Q" +
                      item.m_quality
                    : itemName;

            gui.m_recipeDecription.text =
                BuildRecycleSummary(
                    gui,
                    plan);

            gui.m_itemCraftType.gameObject
                .SetActive(true);

            gui.m_itemCraftType.text =
                "Recycle " +
                plan.ConsumeAmount +
                (plan.ConsumeAmount == 1
                    ? " item"
                    : " items");

            ShowStationLevel(
                gui,
                player,
                plan);

            ShowOutputs(
                gui,
                plan);

            bool valid =
                IsPlanStillValid(
                    player,
                    plan) &&
                IsStationUsableForPlan(
                    player,
                    player.GetCurrentCraftingStation(),
                    plan);

            gui.m_craftButton.interactable =
                valid;

            TMP_Text buttonText =
                gui.m_craftButton
                    .GetComponentInChildren<TMP_Text>(
                        true);

            if (buttonText != null)
            {
                buttonText.text =
                    _armedItem == item &&
                    _armedRecipe == recipe
                        ? "Confirm Recycle"
                        : "Recycle";
            }

            UITooltip tooltip =
                gui.m_craftButton
                    .GetComponent<UITooltip>();

            if (tooltip != null)
            {
                tooltip.m_text =
                    valid
                        ? ""
                        : "Recycling requirements are not met.";
            }

            if (GuiAccess.CraftTimer != null)
            {
                float timer =
                    (float)GuiAccess.CraftTimer
                        .GetValue(gui);

                if (timer >= 0f &&
                    gui.m_craftProgressPanel != null)
                {
                    TMP_Text progress =
                        gui.m_craftProgressPanel
                            .GetComponentInChildren<TMP_Text>(
                                true);

                    if (progress != null)
                        progress.text =
                            "Recycling...";
                }
            }
        }

        internal static bool HandleCraftPressed(
            InventoryGui gui)
        {
            if (!Active)
                return true;

            Recipe recipe;
            ItemDrop.ItemData item;

            GuiAccess.GetSelected(
                gui,
                out recipe,
                out item);

            RecyclePlan plan =
                FindPlan(
                    recipe,
                    item);

            if (plan == null ||
                !IsPlanStillValid(
                    Player.m_localPlayer,
                    plan))
                return false;

            if (_armedItem != item ||
                _armedRecipe != recipe)
            {
                _armedItem = item;
                _armedRecipe = recipe;

                TMP_Text buttonText =
                    gui.m_craftButton
                        .GetComponentInChildren<TMP_Text>(
                            true);

                if (buttonText != null)
                    buttonText.text =
                        "Confirm Recycle";

                return false;
            }

            ClearArmed();
            return true;
        }

        internal static void HandleCraftCancelled()
        {
            if (Active)
                ClearArmed();
        }

        internal static bool CompleteRecycle(
            InventoryGui gui,
            Player player)
        {
            if (!Active ||
                gui == null ||
                player == null)
                return false;

            Recipe recipe;
            ItemDrop.ItemData item;

            GuiAccess.GetSelected(
                gui,
                out recipe,
                out item);

            RecyclePlan plan =
                FindPlan(
                    recipe,
                    item);

            if (plan == null ||
                !IsPlanStillValid(
                    player,
                    plan) ||
                !IsStationUsableForPlan(
                    player,
                    player.GetCurrentCraftingStation(),
                    plan))
                return false;

            Inventory inventory =
                player.GetInventory();

            if (plan.Item.m_equipped)
            {
                player.UnequipItem(
                    plan.Item);
            }

            bool cheated =
                plan.Item.m_cheated;

            inventory.RemoveItem(
                plan.Item,
                plan.ConsumeAmount);

            long playerId =
                player.GetPlayerID();

            string playerName =
                player.GetPlayerName();

            for (int i = 0;
                 i < plan.Outputs.Count;
                 i++)
            {
                RecycleOutput output =
                    plan.Outputs[i];

                int amount =
                    output.Roll();

                if (amount <= 0 ||
                    output.Item == null)
                    continue;

                inventory.AddItem(
                    output.Item.gameObject.name,
                    amount,
                    output.Item.m_itemData.m_quality,
                    output.Item.m_itemData.m_variant,
                    playerId,
                    playerName,
                    cheated);
            }

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station != null)
            {
                station.m_craftItemDoneEffects
                    .Create(
                        player.transform.position,
                        Quaternion.identity);
            }
            else
            {
                gui.m_craftItemDoneEffects
                    .Create(
                        player.transform.position,
                        Quaternion.identity);
            }

            Skills.SkillType recycledSkill =
                plan.Recipe != null &&
                plan.Recipe.m_craftingStation != null
                    ? plan.Recipe.m_craftingStation
                        .m_craftingSkill
                    : Skills.SkillType.None;

            CraftXpDebt.Add(
                player,
                recycledSkill,
                GetBatches(plan));

            ClearArmed();

            GuiAccess.Refresh(
                gui,
                false);

            return true;
        }

        internal static bool ForceFiveCraftDuration(
            InventoryGui gui,
            out bool previous)
        {
            previous = false;

            if (!Active ||
                GuiAccess.MultiCrafting == null)
                return false;

            previous =
                (bool)GuiAccess.MultiCrafting
                    .GetValue(gui);

            GuiAccess.MultiCrafting.SetValue(
                gui,
                true);

            return true;
        }

        internal static void RestoreMultiCrafting(
            InventoryGui gui,
            bool changed,
            bool previous)
        {
            if (changed &&
                GuiAccess.MultiCrafting != null)
            {
                GuiAccess.MultiCrafting.SetValue(
                    gui,
                    previous);
            }
        }

        private static void ClearArmed()
        {
            _armedItem = null;
            _armedRecipe = null;
        }

        private static RecyclePlan FindPlan(
            Recipe recipe,
            ItemDrop.ItemData item)
        {
            if (recipe == null ||
                item == null)
                return null;

            for (int i = 0;
                 i < Plans.Count;
                 i++)
            {
                RecyclePlan plan =
                    Plans[i];

                if (plan.Recipe == recipe &&
                    plan.Item == item)
                    return plan;
            }

            return null;
        }

        private static bool IsPlanStillValid(
            Player player,
            RecyclePlan plan)
        {
            return player != null &&
                   plan != null &&
                   plan.Item != null &&
                   player.GetInventory()
                       .ContainsItem(plan.Item) &&
                   plan.Item.m_stack >=
                       plan.ConsumeAmount;
        }
        private static bool StationSupportsRecycler(
            CraftingStation station)
        {
            if (station == null ||
                station.m_upgrader ||
                ObjectDB.instance == null)
                return false;

            if (IsFoodStation(station))
                return false;

            if (IsGaldrTable(station))
                return true;

            List<Recipe> recipes =
                ObjectDB.instance.m_recipes;

            for (int i = 0;
                 i < recipes.Count;
                 i++)
            {
                Recipe recipe =
                    recipes[i];

                if (!BasicRecipeAllowed(recipe) ||
                    recipe.m_craftingStation == null)
                    continue;

                if (string.Equals(
                        recipe.m_craftingStation.m_name,
                        station.m_name,
                        StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool RecipeCanAppear(
            Recipe recipe,
            CraftingStation station,
            bool special)
        {
            if (!BasicRecipeAllowed(recipe))
                return false;

            if (special)
                return true;

            return recipe.m_craftingStation != null &&
                   station != null &&
                   string.Equals(
                       recipe.m_craftingStation.m_name,
                       station.m_name,
                       StringComparison.Ordinal);
        }

        private static bool BasicRecipeAllowed(
            Recipe recipe)
        {
            if (recipe == null ||
                !recipe.m_enabled ||
                recipe.m_item == null ||
                recipe.m_item.m_itemData == null ||
                recipe.m_noCraftOnlyUpgrade ||
                recipe.m_requireOnlyOneIngredient)
                return false;

            ItemDrop.ItemData.SharedData shared =
                recipe.m_item.m_itemData.m_shared;

            return shared != null &&
                   !shared.m_questItem &&
                   shared.m_itemType !=
                       ItemDrop.ItemData.ItemType.Consumable;
        }

        private static bool IsFoodStation(
            CraftingStation station)
        {
            if (station == null)
                return false;

            string name =
                station.m_name;

            return string.Equals(
                       name,
                       "$piece_cauldron",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       name,
                       "$piece_preptable",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       name,
                       "$piece_meadcauldron",
                       StringComparison.Ordinal);
        }

        private static bool IsGaldrTable(
            CraftingStation station)
        {
            return station != null &&
                   string.Equals(
                       station.m_name,
                       "$piece_magetable",
                       StringComparison.Ordinal);
        }

        private static bool IsSpecialReverse(
            CraftingStation station,
            Recipe recipe)
        {
            return IsGaldrTable(station) &&
                   recipe != null &&
                   recipe.m_item != null &&
                   GaldrReversePrefabs.Contains(
                       recipe.m_item.gameObject.name);
        }

        private static void AddSpecialReverseRecipes(
            Player player,
            CraftingStation station,
            List<Recipe> source)
        {
            if (!IsGaldrTable(station) ||
                ObjectDB.instance == null)
                return;

            List<Recipe> all =
                ObjectDB.instance.m_recipes;

            for (int i = 0;
                 i < all.Count;
                 i++)
            {
                Recipe recipe =
                    all[i];

                if (recipe == null ||
                    recipe.m_item == null ||
                    recipe.m_item.m_itemData == null ||
                    !GaldrReversePrefabs.Contains(
                        recipe.m_item.gameObject.name))
                    continue;

                string key =
                    recipe.m_item.m_itemData
                        .m_shared.m_name;

                if (!player.IsRecipeKnown(key) ||
                    !AllMaterialsKnown(
                        player,
                        recipe))
                    continue;

                if (!source.Contains(recipe))
                    source.Add(recipe);
            }
        }

        private static bool AllMaterialsKnown(
            Player player,
            Recipe recipe)
        {
            Piece.Requirement[] resources =
                recipe.m_resources;

            for (int i = 0;
                 i < resources.Length;
                 i++)
            {
                Piece.Requirement req =
                    resources[i];

                if (req == null ||
                    req.m_resItem == null)
                    continue;

                if (!player.IsMaterialKnown(
                        req.m_resItem.m_itemData
                            .m_shared.m_name))
                    return false;
            }

            return true;
        }

        private static bool IsStationUsableForPlan(
            Player player,
            CraftingStation station,
            RecyclePlan plan)
        {
            if (player == null ||
                station == null ||
                plan == null ||
                !station.CheckUsable(
                    player,
                    false))
                return false;

            if (plan.SpecialReverse)
            {
                return IsGaldrTable(station) &&
                       station.GetLevel() >=
                           plan.RequiredLevel;
            }

            return plan.Recipe != null &&
                   plan.Recipe.m_craftingStation != null &&
                   string.Equals(
                       plan.Recipe.m_craftingStation.m_name,
                       station.m_name,
                       StringComparison.Ordinal) &&
                   station.GetLevel() >=
                       plan.RequiredLevel;
        }

        private static int GetConsumeAmount(
            ItemDrop.ItemData item,
            Recipe recipe)
        {
            if (item == null ||
                recipe == null ||
                recipe.m_amount <= 0)
                return 0;

            if (recipe.m_amount == 1)
                return item.m_stack;

            if (item.m_stack <
                recipe.m_amount)
                return 0;

            int cap =
                Mathf.CeilToInt(
                    recipe.m_amount *
                    1.5f);

            return Mathf.Min(
                item.m_stack,
                cap);
        }

        private static int GetBatches(
            RecyclePlan plan)
        {
            if (plan == null ||
                plan.Recipe == null ||
                plan.Recipe.m_amount <= 0)
                return 0;

            return Mathf.Max(
                1,
                plan.ConsumeAmount /
                plan.Recipe.m_amount);
        }

        private static float GetRecoveryRate(
            ItemDrop.ItemData item)
        {
            int quality =
                GetNonPotentialQuality(item);

            if (quality <= 1)
                return 0.80f;

            if (quality == 2)
                return 0.85f;

            if (quality == 3)
                return 0.90f;

            return 1f;
        }

        private static int GetNonPotentialQuality(
            ItemDrop.ItemData item)
        {
            return Mathf.Clamp(
                item.m_quality,
                1,
                Mathf.Max(
                    1,
                    item.m_shared.m_maxQuality));
        }

        private static float GetCraftBonus(
            InventoryGui gui)
        {
            Player player =
                Player.m_localPlayer;

            if (player == null ||
                gui == null)
                return 0f;

            return Mathf.Clamp01(
                player.GetSkillFactor(
                    Skills.SkillType.Crafting) *
                gui.m_craftBonusChance);
        }

        private static void BuildOutputs(
            InventoryGui gui,
            RecyclePlan plan)
        {
            int batches =
                GetBatches(plan);

            float recovery =
                GetRecoveryRate(
                    plan.Item);

            float craftBonus =
                GetCraftBonus(gui);

            int nonPotentialQuality =
                GetNonPotentialQuality(
                    plan.Item);

            Piece.Requirement[] resources =
                plan.Recipe.m_resources;

            for (int i = 0;
                 i < resources.Length;
                 i++)
            {
                Piece.Requirement req =
                    resources[i];

                if (req == null ||
                    req.m_resItem == null ||
                    req.m_upgraderResource)
                    continue;

                int baseAmount =
                    req.GetAmount(1) *
                    batches;

                if (baseAmount <= 0)
                    continue;

                RecycleOutput output =
                    new RecycleOutput
                    {
                        Item =
                            req.m_resItem
                    };

                if (req.m_resItem.m_itemData
                        .IsEquipable())
                {
                    output.BaseMin =
                        baseAmount;

                    output.BaseMax =
                        baseAmount;
                }
                else
                {
                    ConfigureBaseRecovery(
                        output,
                        baseAmount *
                        recovery,
                        craftBonus);
                }

                int upgradeSpent = 0;

                for (int q = 2;
                     q <= nonPotentialQuality;
                     q++)
                {
                    upgradeSpent +=
                        req.GetAmount(q) *
                        batches;
                }

                ConfigureUpgradeRecovery(
                    output,
                    upgradeSpent *
                    craftBonus);

                MergeOutput(
                    plan.Outputs,
                    output);
            }
        }

        private static void ConfigureBaseRecovery(
            RecycleOutput output,
            float exact,
            float craftBonus)
        {
            if (exact <= 0f)
                return;

            int floor =
                Mathf.FloorToInt(exact);

            int ceil =
                Mathf.CeilToInt(exact);

            if (floor == 0 &&
                ceil == 1)
            {
                output.BaseMin = 1;
                output.BaseMax = 1;
                return;
            }

            output.BaseMin = floor;
            output.BaseMax = ceil;

            if (ceil > floor)
            {
                float fraction =
                    exact - floor;

                output.BaseHighChance =
                    Mathf.Clamp01(
                        fraction +
                        (1f - fraction) *
                        craftBonus);
            }
        }

        private static void ConfigureUpgradeRecovery(
            RecycleOutput output,
            float exact)
        {
            if (exact <= 0f)
                return;

            int floor =
                Mathf.FloorToInt(exact);

            int ceil =
                Mathf.CeilToInt(exact);

            output.UpgradeMin = floor;
            output.UpgradeMax = ceil;

            if (ceil > floor)
            {
                output.UpgradeHighChance =
                    Mathf.Clamp01(
                        exact - floor);
            }
        }

        private static void MergeOutput(
            List<RecycleOutput> outputs,
            RecycleOutput incoming)
        {
            if (incoming == null ||
                incoming.Item == null)
                return;

            string name =
                incoming.Item.m_itemData
                    .m_shared.m_name;

            for (int i = 0;
                 i < outputs.Count;
                 i++)
            {
                RecycleOutput current =
                    outputs[i];

                if (current.Item.m_itemData
                        .m_shared.m_name != name)
                    continue;

                current.BaseMin +=
                    incoming.BaseMin;

                current.BaseMax +=
                    incoming.BaseMax;

                current.BaseHighChance =
                    Mathf.Max(
                        current.BaseHighChance,
                        incoming.BaseHighChance);

                current.UpgradeMin +=
                    incoming.UpgradeMin;

                current.UpgradeMax +=
                    incoming.UpgradeMax;

                current.UpgradeHighChance =
                    Mathf.Max(
                        current.UpgradeHighChance,
                        incoming.UpgradeHighChance);

                return;
            }

            outputs.Add(incoming);
        }
        private static void ShowStationLevel(
            InventoryGui gui,
            Player player,
            RecyclePlan plan)
        {
            bool show =
                plan != null &&
                plan.RequiredLevel > 0;

            gui.m_minStationLevelIcon
                .gameObject.SetActive(show);

            if (!show)
                return;

            gui.m_minStationLevelText
                .gameObject.SetActive(true);

            gui.m_minStationLevelText.text =
                plan.RequiredLevel.ToString();

            Color baseColor =
                Color.white;

            if (GuiAccess.MinStationBaseColor != null)
            {
                object value =
                    GuiAccess.MinStationBaseColor
                        .GetValue(gui);

                if (value is Color)
                    baseColor = (Color)value;
            }

            CraftingStation station =
                player.GetCurrentCraftingStation();

            bool enough =
                IsStationUsableForPlan(
                    player,
                    station,
                    plan);

            gui.m_minStationLevelText.color =
                enough
                    ? baseColor
                    : (Mathf.Sin(
                           Time.time * 10f) >
                       0f
                        ? Color.red
                        : baseColor);
        }

        private static void ShowOutputs(
            InventoryGui gui,
            RecyclePlan plan)
        {
            int slotCount =
                gui.m_recipeRequirementList.Length;

            int visible =
                Mathf.Min(
                    slotCount,
                    plan.Outputs.Count);

            for (int i = 0;
                 i < visible;
                 i++)
            {
                RecycleOutput output =
                    plan.Outputs[i];

                Transform root =
                    gui.m_recipeRequirementList[i]
                        .transform;

                Image icon =
                    root.Find("res_icon")
                        .GetComponent<Image>();

                TMP_Text name =
                    root.Find("res_name")
                        .GetComponent<TMP_Text>();

                TMP_Text amount =
                    root.Find("res_amount")
                        .GetComponent<TMP_Text>();

                icon.gameObject.SetActive(true);
                name.gameObject.SetActive(true);
                amount.gameObject.SetActive(true);

                icon.sprite =
                    output.Item.m_itemData
                        .GetIcon();

                icon.color = Color.white;

                string localized =
                    Localization.instance.Localize(
                        output.Item.m_itemData
                            .m_shared.m_name);

                name.text = localized;

                int min =
                    output.MinAmount;

                int max =
                    output.MaxAmount;

                amount.text =
                    min == max
                        ? min.ToString()
                        : min + "-" + max;

                amount.color =
                    Color.white;

                UITooltip tooltip =
                    root.GetComponent<UITooltip>();

                if (tooltip != null)
                {
                    tooltip.m_text =
                        localized +
                        "\n" +
                        FormatOutputOdds(output);
                }
            }

            for (int i = visible;
                 i < slotCount;
                 i++)
            {
                InventoryGui.HideRequirement(
                    gui.m_recipeRequirementList[i]
                        .transform);
            }
        }

        private static string BuildRecycleSummary(
            InventoryGui gui,
            RecyclePlan plan)
        {
            StringBuilder text =
                new StringBuilder();

            int naturalMax =
                Mathf.Max(
                    1,
                    plan.Item.m_shared.m_maxQuality);

            int countedQuality =
                GetNonPotentialQuality(
                    plan.Item);

            int potential =
                Mathf.Max(
                    0,
                    plan.Item.m_quality -
                    naturalMax);

            float recovery =
                GetRecoveryRate(
                    plan.Item);

            float craftBonus =
                GetCraftBonus(gui);

            text.Append(
                "<color=orange><b>RECYCLING</b></color>");

            text.Append(
                "\nAmount: <color=yellow>" +
                plan.ConsumeAmount +
                "</color>");

            if (plan.Recipe.m_amount > 1)
            {
                text.Append(
                    "\nRecipe batch: " +
                    plan.Recipe.m_amount +
                    "  |  Max per recycle: " +
                    Mathf.CeilToInt(
                        plan.Recipe.m_amount *
                        1.5f));
            }

            text.Append(
                "\nBase recovery: <color=yellow>" +
                Mathf.RoundToInt(
                    recovery * 100f) +
                "%</color>");

            text.Append(
                "\nCrafting rounding bonus: <color=yellow>" +
                (craftBonus * 100f)
                    .ToString("0.#") +
                "%</color>");

            if (countedQuality > 1)
            {
                text.Append(
                    "\nVanilla quality counted: Q" +
                    countedQuality);
            }

            if (potential > 0)
            {
                text.Append(
                    "\nPotential +" +
                    potential +
                    ": <color=#AAAAAA>no extra recycle value</color>");
            }

            text.Append(
                "\n\n<color=orange><b>POSSIBLE RETURNS</b></color>");

            for (int i = 0;
                 i < plan.Outputs.Count;
                 i++)
            {
                RecycleOutput output =
                    plan.Outputs[i];

                string material =
                    Localization.instance.Localize(
                        output.Item.m_itemData
                            .m_shared.m_name);

                text.Append(
                    "\n- " +
                    material +
                    ": " +
                    FormatOutputOdds(output));
            }

            return text.ToString();
        }

        private static string FormatOutputOdds(
            RecycleOutput output)
        {
            SortedDictionary<int, float> odds =
                new SortedDictionary<int, float>();

            AddCombinedOdds(
                odds,
                output.BaseMin,
                output.BaseMax,
                output.BaseHighChance,
                output.UpgradeMin,
                output.UpgradeMax,
                output.UpgradeHighChance);

            if (odds.Count == 1)
            {
                foreach (KeyValuePair<int, float> pair
                         in odds)
                {
                    return pair.Key.ToString();
                }
            }

            StringBuilder text =
                new StringBuilder();

            bool first = true;

            foreach (KeyValuePair<int, float> pair
                     in odds)
            {
                if (pair.Value < 0.0005f)
                    continue;

                if (!first)
                    text.Append(" | ");

                first = false;

                text.Append(
                    pair.Key +
                    " (" +
                    (pair.Value * 100f)
                        .ToString("0.#") +
                    "%)");
            }

            return text.ToString();
        }

        private static void AddCombinedOdds(
            SortedDictionary<int, float> odds,
            int baseMin,
            int baseMax,
            float baseHighChance,
            int upgradeMin,
            int upgradeMax,
            float upgradeHighChance)
        {
            float baseLow =
                baseMax > baseMin
                    ? 1f - baseHighChance
                    : 1f;

            float baseHigh =
                baseMax > baseMin
                    ? baseHighChance
                    : 0f;

            float upgradeLow =
                upgradeMax > upgradeMin
                    ? 1f - upgradeHighChance
                    : 1f;

            float upgradeHigh =
                upgradeMax > upgradeMin
                    ? upgradeHighChance
                    : 0f;

            AddOdds(
                odds,
                baseMin + upgradeMin,
                baseLow * upgradeLow);

            if (upgradeMax > upgradeMin)
            {
                AddOdds(
                    odds,
                    baseMin + upgradeMax,
                    baseLow * upgradeHigh);
            }

            if (baseMax > baseMin)
            {
                AddOdds(
                    odds,
                    baseMax + upgradeMin,
                    baseHigh * upgradeLow);

                if (upgradeMax > upgradeMin)
                {
                    AddOdds(
                        odds,
                        baseMax + upgradeMax,
                        baseHigh * upgradeHigh);
                }
            }
        }

        private static void AddOdds(
            SortedDictionary<int, float> odds,
            int amount,
            float chance)
        {
            if (chance <= 0f)
                return;

            float current;
            odds.TryGetValue(
                amount,
                out current);

            odds[amount] =
                current + chance;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryGuiAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            InventoryGui __instance)
        {
            RecyclerUi.Initialize(
                __instance);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnDestroy")]
    internal static class InventoryGuiDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            RecyclerUi.Shutdown();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class InventoryGuiHidePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            RecyclerUi.LeaveRecycleTab();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
    internal static class InventoryGuiCraftingPanelPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            InventoryGui __instance)
        {
            if (RecyclerUi.Active &&
                (!__instance.m_tabCraft.interactable ||
                 !__instance.m_tabUpgrade.interactable))
            {
                RecyclerUi.LeaveRecycleTab();
            }

            RecyclerUi.SyncTabVisibility(
                __instance);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGuiRecipeListPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            InventoryGui __instance,
            List<Recipe> recipes)
        {
            RecyclerUi.RebuildRecipeList(
                __instance,
                recipes);
        }
    }

    // Crafting-XP debt from recycling. Stored in the character's own
    // vanilla custom data (saved with the character), so dying or
    // relogging no longer clears it.
    internal static class CraftXpDebt
    {
        private const string SaveKey = "Wire.Recycler.CraftXpDebt";

        private static Player _player;

        private static readonly Dictionary<
            Skills.SkillType,
            int> Debt =
                new Dictionary<Skills.SkillType, int>();

        internal static void Reset()
        {
            _player = null;
            Debt.Clear();
        }

        private static void EnsurePlayer(
            Player player)
        {
            if (_player == player)
                return;

            _player = player;
            Debt.Clear();
            Load(player);
        }

        private static void Load(Player player)
        {
            string raw;
            if (player == null || player.m_customData == null ||
                !player.m_customData.TryGetValue(SaveKey, out raw) ||
                string.IsNullOrEmpty(raw))
                return;
            foreach (string part in raw.Split(';'))
            {
                string[] kv = part.Split(':');
                int skill, amount;
                if (kv.Length == 2 &&
                    int.TryParse(kv[0], out skill) &&
                    int.TryParse(kv[1], out amount) &&
                    amount > 0)
                    Debt[(Skills.SkillType)skill] = amount;
            }
        }

        private static void Save()
        {
            if (_player == null || _player.m_customData == null)
                return;
            if (Debt.Count == 0)
            {
                _player.m_customData.Remove(SaveKey);
                return;
            }
            var parts = new List<string>(Debt.Count);
            foreach (var kv in Debt)
                parts.Add(((int)kv.Key).ToString() + ":" + kv.Value.ToString());
            _player.m_customData[SaveKey] = string.Join(";", parts.ToArray());
        }

        internal static void Add(
            Player player,
            Skills.SkillType skill,
            int amount)
        {
            if (player == null ||
                skill == Skills.SkillType.None ||
                amount <= 0)
                return;

            EnsurePlayer(player);

            int current;
            Debt.TryGetValue(
                skill,
                out current);

            long total =
                (long)current + amount;

            Debt[skill] =
                total > int.MaxValue
                    ? int.MaxValue
                    : (int)total;
            Save();
        }

        internal static bool Apply(
            Player player,
            Skills.SkillType skill,
            ref float value)
        {
            if (player == null ||
                skill == Skills.SkillType.None ||
                value <= 0f)
                return false;

            EnsurePlayer(player);

            int current;
            if (!Debt.TryGetValue(
                    skill,
                    out current) ||
                current <= 0)
                return false;

            int craftUnits =
                Mathf.Max(
                    0,
                    Mathf.FloorToInt(
                        value + 0.0001f));

            if (craftUnits <= 0)
                return false;

            int blocked =
                Mathf.Min(
                    current,
                    craftUnits);

            current -= blocked;
            value -= blocked;

            if (current > 0)
                Debt[skill] = current;
            else
                Debt.Remove(skill);
            Save();

            return value <= 0f;
        }
    }

    internal struct UpdateRecipeState
    {
        internal bool Changed;
        internal bool Previous;
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class InventoryGuiUpdateRecipePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            InventoryGui __instance,
            out UpdateRecipeState __state)
        {
            bool previous;

            __state = new UpdateRecipeState
            {
                Changed =
                    RecyclerUi.ForceFiveCraftDuration(
                        __instance,
                        out previous),
                Previous = previous
            };
        }

        [HarmonyPostfix]
        private static void Postfix(
            InventoryGui __instance,
            Player player,
            UpdateRecipeState __state)
        {
            RecyclerUi.RestoreMultiCrafting(
                __instance,
                __state.Changed,
                __state.Previous);

            RecyclerUi.OverrideRecipeDetails(
                __instance,
                player);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            InventoryGui __instance,
            Exception __exception,
            UpdateRecipeState __state)
        {
            if (__exception != null)
            {
                RecyclerUi.RestoreMultiCrafting(
                    __instance,
                    __state.Changed,
                    __state.Previous);
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    internal static class InventoryGuiCraftPressedPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            InventoryGui __instance)
        {
            return RecyclerUi.HandleCraftPressed(
                __instance);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnCraftCancelPressed")]
    internal static class InventoryGuiCraftCancelPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            RecyclerUi.HandleCraftCancelled();
        }
    }

    [HarmonyPatch(
        typeof(InventoryGui),
        "DoCrafting",
        new[] { typeof(Player) })]
    internal static class InventoryGuiDoCraftingPatch
    {
        internal static bool NormalCraftActive
        {
            get;
            private set;
        }

        [HarmonyPrefix]
        private static bool Prefix(
            InventoryGui __instance,
            Player player)
        {
            if (!RecyclerUi.Active)
            {
                NormalCraftActive = true;
                return true;
            }

            NormalCraftActive = false;

            RecyclerUi.CompleteRecycle(
                __instance,
                player);

            return false;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            NormalCraftActive = false;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception)
        {
            NormalCraftActive = false;
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(Player),
        "RaiseSkill",
        new[]
        {
            typeof(Skills.SkillType),
            typeof(float)
        })]
    internal static class PlayerRaiseCraftSkillPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Player __instance,
            Skills.SkillType skill,
            ref float value)
        {
            if (!InventoryGuiDoCraftingPatch
                    .NormalCraftActive)
                return true;

            return !CraftXpDebt.Apply(
                __instance,
                skill,
                ref value);
        }
    }
}
