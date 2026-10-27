using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresForgeOfGoodtential
{
    // The "Swap" button next to the idol (Kind 0) or trophy (Kind 1):
    // opens the picker for it.
    internal sealed class GoodtentialCycleButton :
        MonoBehaviour
    {
        internal int Kind;
        internal Image Icon;
        internal bool FollowsInteractable;
        internal Color TintColor = Color.white;
        internal Color DisabledColor = Color.grey;
        internal bool TooltipAdded;

        internal void Cycle()
        {
            InventoryGui gui =
                GetComponentInParent<InventoryGui>();

            if (gui == null ||
                Player.m_localPlayer == null ||
                ForgeUiPatch.IsRefining(gui))
            {
                return;
            }

            ForgePicker.Open(gui, Kind);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class ForgeUiPatch
    {
        // The selected recipe lives in a private struct field. Read through
        // small compiled getters made once, not reflection every frame
        // (which also boxed the struct each call).
        private static readonly FieldInfo SelectedRecipeField =
            AccessTools.Field(
                typeof(InventoryGui),
                "m_selectedRecipe");

        private static readonly Func<InventoryGui, Recipe> GetSelectedRecipe =
            StructGetter<Recipe>("Recipe");

        private static readonly Func<InventoryGui, ItemDrop.ItemData> GetSelectedItem =
            StructGetter<ItemDrop.ItemData>("ItemData");

        private static readonly AccessTools.FieldRef<InventoryGui, float> CraftTimer =
            AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");

        // gui.m_selectedRecipe.<property>, as IL.
        private static Func<InventoryGui, T> StructGetter<T>(string property)
        {
            try
            {
                if (SelectedRecipeField == null)
                    return null;
                MethodInfo getter = SelectedRecipeField.FieldType
                    .GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetGetMethod(true);
                if (getter == null)
                    return null;
                System.Reflection.Emit.DynamicMethod dm = new System.Reflection.Emit.DynamicMethod(
                    "WireForge_Get" + property, typeof(T), new[] { typeof(InventoryGui) }, typeof(InventoryGui), true);
                System.Reflection.Emit.ILGenerator il = dm.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldflda, SelectedRecipeField);
                il.Emit(System.Reflection.Emit.OpCodes.Call, getter);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (Func<InventoryGui, T>)dm.CreateDelegate(typeof(Func<InventoryGui, T>));
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool TryGetSelected(
            InventoryGui gui,
            out Recipe recipe,
            out ItemDrop.ItemData item)
        {
            recipe = null;
            item = null;

            if (gui == null ||
                GetSelectedRecipe == null ||
                GetSelectedItem == null)
            {
                return false;
            }

            recipe = GetSelectedRecipe(gui);
            item = GetSelectedItem(gui);

            return recipe != null &&
                item != null;
        }

        internal static bool IsRefining(
            InventoryGui gui)
        {
            return gui != null &&
                CraftTimer(gui) >= 0f;
        }

        [HarmonyPrefix]
        private static void Prefix(
            InventoryGui __instance)
        {
            Player player = Player.m_localPlayer;

            if (__instance == null ||
                player == null)
            {
                return;
            }

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station == null ||
                !station.m_upgrader)
            {
                RestoreRequirementSlots(__instance);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            InventoryGui __instance)
        {
            RefreshAfterSelection(__instance);
        }

        internal static void RefreshAfterSelection(
            InventoryGui gui)
        {
            Player player = Player.m_localPlayer;

            if (gui == null ||
                player == null)
            {
                return;
            }

            CraftingStation station =
                player.GetCurrentCraftingStation();

            if (station == null ||
                !station.m_upgrader)
            {
                RestoreRequirementSlots(gui);
                ForgeSelection.ResetCurrent();
                ForgeInfo.Leave();
                ForgePicker.Close();
                return;
            }

            Recipe recipe;
            ItemDrop.ItemData item;

            if (!TryGetSelected(
                gui,
                out recipe,
                out item) ||
                !ForgeSelection.Refresh(
                    recipe,
                    item,
                    player))
            {
                return;
            }

            gui.m_craftingStationName.text =
                "Forge of Goodtential";

            EnsureRequirementSlots(gui);
            RenderIdolSlot(gui, player);
            RenderTrophySlot(gui, player);
            RenderRisk(gui, recipe, item);
            UpdateCraftButton(
                gui,
                player,
                station,
                recipe,
                item);
            ForgeInfo.Refresh(gui, player, recipe, item);
        }

        private static void RestoreRequirementSlots(
            InventoryGui gui)
        {
            if (gui == null ||
                gui.m_recipeRequirementList == null)
            {
                return;
            }

            for (int i = 0;
                 i < gui.m_recipeRequirementList.Length;
                 i++)
            {
                GameObject root =
                    gui.m_recipeRequirementList[i];

                if (root == null)
                    continue;

                Transform custom =
                    root.transform.Find(
                        "Goodtential cycle button");

                if (custom != null)
                {
                    custom.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(
                        custom.gameObject);
                }

                UITooltip tip =
                    root.GetComponent<UITooltip>();

                if (tip != null)
                    tip.enabled = true;
            }
        }

        private static void EnsureRequirementSlots(
            InventoryGui gui)
        {
            if (gui.m_recipeRequirementList == null ||
                gui.m_recipeRequirementList.Length == 0)
            {
                return;
            }

            if (gui.m_recipeRequirementList.Length < 4)
            {
                GameObject original =
                    gui.m_recipeRequirementList[0];

                RectTransform originalRect =
                    original.GetComponent<RectTransform>();

                System.Collections.Generic.List<GameObject> slots =
                    new System.Collections.Generic.List<GameObject>(
                        gui.m_recipeRequirementList);

                while (slots.Count < 4)
                {
                    int index = slots.Count;

                    GameObject clone =
                        UnityEngine.Object.Instantiate(
                            original,
                            original.transform.parent);

                    clone.name =
                        "Goodtential requirement " +
                        index;

                    RectTransform cloneRect =
                        clone.GetComponent<RectTransform>();

                    if (originalRect != null &&
                        cloneRect != null)
                    {
                        cloneRect.anchoredPosition =
                            originalRect.anchoredPosition +
                            new Vector2(
                                (originalRect.rect.width + 8f) *
                                    index,
                                0f);
                    }

                    slots.Add(clone);
                }

                gui.m_recipeRequirementList =
                    slots.ToArray();
            }

            // The idol and trophy slots get their own hover text
            // (ForgeInfo); the cycle buttons stay silent.
            ConfigureCycleButton(
                gui,
                gui.m_recipeRequirementList[1],
                0);

            ConfigureCycleButton(
                gui,
                gui.m_recipeRequirementList[3],
                1);
        }

        private static void DisableItemTooltip(
            GameObject root)
        {
            if (root == null)
                return;

            UITooltip target =
                root.GetComponent<UITooltip>();

            if (target == null)
                return;

            target.m_topic = "";
            target.m_text = "";
            target.enabled = false;
        }

        // The slot right of the idol (and of the trophy) holds a wooden swap
        // button with an icon on it in the style of the round icon buttons
        // above the inventory: the Trophies icon for the trophy, the Forge of
        // Potential's map icon (restyled to match) for the idol. Clicking
        // opens the picker.
        private const float TrophySwapIconSize = 38f;
        private const float IdolSwapIconSize = 40f;

        private static void ConfigureCycleButton(
            InventoryGui gui,
            GameObject root,
            int kind)
        {
            if (gui == null ||
                root == null ||
                gui.m_takeAllButton == null)
            {
                return;
            }

            root.SetActive(true);

            string[] requirementChildren =
            {
                "res_icon",
                "res_name",
                "res_amount"
            };

            for (int i = 0;
                 i < requirementChildren.Length;
                 i++)
            {
                Transform child =
                    root.transform.Find(
                        requirementChildren[i]);

                if (child != null)
                    child.gameObject.SetActive(false);
            }

            DisableItemTooltip(root);

            const string visualName =
                "Goodtential cycle button";

            Transform existing =
                root.transform.Find(visualName);

            Button button;

            if (existing == null)
            {
                // The wooden take-all button, without its gamepad hint.
                UIGamePad sourceHint =
                    gui.m_takeAllButton.GetComponent<UIGamePad>();
                bool sourceHintEnabled =
                    sourceHint != null && sourceHint.enabled;
                if (sourceHint != null)
                    sourceHint.enabled = false;

                button =
                    UnityEngine.Object.Instantiate(
                        gui.m_takeAllButton,
                        root.transform);

                if (sourceHint != null)
                    sourceHint.enabled = sourceHintEnabled;

                button.name = visualName;
                button.onClick =
                    new Button.ButtonClickedEvent();

                UIGamePad hint = button.GetComponent<UIGamePad>();
                if (hint != null)
                    hint.enabled = false;

                RectTransform rect =
                    (RectTransform)button.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.offsetMin = new Vector2(3f, 3f);
                rect.offsetMax = new Vector2(-3f, -3f);
                rect.localScale = Vector3.one;

                // No text on it, just the icon.
                foreach (TMPro.TMP_Text label in
                         button.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    label.gameObject.SetActive(false);

                Sprite sprite = kind == 0 ? ForgeMapIcon() : TrophiesIcon(gui);
                if (sprite != null)
                {
                    GameObject iconObject = new GameObject(
                        "icon", typeof(RectTransform), typeof(Image));
                    iconObject.transform.SetParent(button.transform, false);
                    RectTransform iconRect = (RectTransform)iconObject.transform;
                    iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                    iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                    iconRect.pivot = new Vector2(0.5f, 0.5f);
                    iconRect.anchoredPosition = Vector2.zero;
                    float size = kind == 0 ? IdolSwapIconSize : TrophySwapIconSize;
                    iconRect.sizeDelta = new Vector2(size, size);
                    Image image = iconObject.GetComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }

                GoodtentialCycleButton cycle =
                    button.gameObject.AddComponent<
                        GoodtentialCycleButton>();
                Transform iconTransform = button.transform.Find("icon");
                cycle.Icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
                TMPro.TMP_Text letter = button.GetComponentInChildren<TMPro.TMP_Text>(true);

                // The letter was what the button tinted orange, and grey while
                // disabled. The icon takes its colour and its place as the
                // button's tint target, so it behaves the same.
                if (cycle.Icon != null && letter != null)
                {
                    cycle.Icon.color = letter.color;
                    if (button.targetGraphic == letter)
                        button.targetGraphic = cycle.Icon;
                    else
                    {
                        cycle.TintColor = letter.color;
                        ButtonTextColor textColor = button.GetComponent<ButtonTextColor>();
                        cycle.DisabledColor = textColor != null ? textColor.m_disabledColor : Color.grey;
                        cycle.FollowsInteractable = true;
                    }
                }
                button.onClick.AddListener(
                    cycle.Cycle);
            }
            else
            {
                button = existing.GetComponent<Button>();
                if (button == null)
                    return;
            }

            button.gameObject.SetActive(true);

            GoodtentialCycleButton swap =
                button.GetComponent<GoodtentialCycleButton>();
            if (swap != null)
                swap.Kind = kind;

            // The same small tooltip the round buttons use: just the action.
            UITooltip tip = button.GetComponent<UITooltip>();
            if (tip == null && swap != null && !swap.TooltipAdded)
            {
                swap.TooltipAdded = true;
                Transform round = gui.transform.Find("root/Info/Trophies");
                UITooltip roundTip = round != null ? round.GetComponent<UITooltip>() : null;
                if (roundTip != null)
                {
                    tip = button.gameObject.AddComponent<UITooltip>();
                    tip.m_tooltipPrefab = roundTip.m_tooltipPrefab;
                }
            }
            if (tip != null)
            {
                tip.enabled = true;
                tip.m_topic = "";
                tip.m_text = ForgeSwapWordPatch.Text();
            }

            Player player = Player.m_localPlayer;

            bool canCycle = false;

            if (player != null)
            {
                if (kind == 0)
                {
                    System.Collections.Generic.List<int>
                        idolTiers =
                            ForgeSelection
                                .GetOwnedValidIdolTiers(
                                    player);

                    canCycle = idolTiers.Count > 1 &&
                        ForgeSelection.SelectedIdolTier >= 0;
                }
                else
                {
                    canCycle =
                        ForgeSelection.HasRequiredIdol(player) &&
                        ForgeSelection.SelectedIdolTier >=
                            ForgeSelection.RequiredTier &&
                        ForgeSelection
                            .GetUniqueTrophies(player)
                            .Count > 0;
                }
            }

            button.interactable =
                !IsRefining(gui) &&
                canCycle;

            // When the button doesn't tint the icon itself, grey it here.
            if (swap != null && swap.Icon != null && swap.FollowsInteractable)
                swap.Icon.color = button.interactable ? swap.TintColor : swap.DisabledColor;
        }

        // The icon on the round Trophies button above the inventory.
        private static Sprite TrophiesIcon(InventoryGui gui)
        {
            Transform t = gui != null ? gui.transform.Find("root/Info/Trophies/Image") : null;
            Image image = t != null ? t.GetComponent<Image>() : null;
            return image != null ? image.sprite : null;
        }

        private static Sprite _forgeIcon;

        // The Forge of Potential's map icon, restyled like the round
        // buttons' icons.
        private static Sprite ForgeMapIcon()
        {
            if (_forgeIcon != null || Minimap.instance == null)
                return _forgeIcon;

            foreach (Minimap.LocationSpriteData data in Minimap.instance.m_locationIcons)
            {
                if (data.m_name == "AncientUpgradeStation")
                {
                    _forgeIcon = ForgeIconStyle.Restyle(data.m_icon);
                    break;
                }
            }
            return _forgeIcon;
        }

        private static void RenderIdolSlot(
            InventoryGui gui,
            Player player)
        {
            if (gui.m_recipeRequirementList == null ||
                gui.m_recipeRequirementList.Length < 1)
            {
                return;
            }

            GameObject root =
                gui.m_recipeRequirementList[0];

            ItemDrop selected =
                ForgeData.GetIdolDrop(
                    ForgeSelection.Family,
                    ForgeSelection.SelectedIdolTier);

            bool hasIdol =
                ForgeSelection.HasRequiredIdol(player);

            string materialName =
                hasIdol
                    ? ForgeData.TierNames[
                        ForgeSelection.SelectedIdolTier]
                    : ForgeUiLabels.GetIdol();

            string idolType =
                !hasIdol
                    ? ""
                    : ForgeSelection.Family ==
                        IdolFamily.Battle
                        ? "Battle"
                        : "Protection";

            SetRequirement(
                root,
                hasIdol && selected != null
                    ? selected.m_itemData.GetIcon()
                    : null,
                materialName,
                idolType,
                true,
                hasIdol);
        }

        private static void RenderTrophySlot(
            InventoryGui gui,
            Player player)
        {
            if (gui.m_recipeRequirementList == null ||
                gui.m_recipeRequirementList.Length < 3)
            {
                return;
            }

            GameObject root =
                gui.m_recipeRequirementList[2];

            ItemDrop.ItemData trophy =
                ForgeSelection.SelectedTrophy;

            if (trophy == null)
            {
                SetRequirement(
                    root,
                    null,
                    "Trophy",
                    "",
                    true,
                    false);

                return;
            }

            int owned =
                player.GetInventory().CountItems(
                    trophy.m_shared.m_name);

            string trophyName =
                ForgeData.LocalizeItemName(trophy);

            const string trophySuffix =
                " Trophy";

            if (trophyName.EndsWith(
                trophySuffix,
                StringComparison.CurrentCultureIgnoreCase))
            {
                trophyName =
                    trophyName.Substring(
                        0,
                        trophyName.Length -
                        trophySuffix.Length);
            }

            SetRequirement(
                root,
                trophy.GetIcon(),
                trophyName,
                "",
                owned > 0,
                true);
        }

        private static void SetRequirement(
            GameObject root,
            Sprite sprite,
            string name,
            string amount,
            bool available,
            bool showIcon)
        {
            if (root == null)
                return;

            Transform iconTransform =
                root.transform.Find("res_icon");

            Transform nameTransform =
                root.transform.Find("res_name");

            Transform amountTransform =
                root.transform.Find("res_amount");

            Image icon =
                iconTransform != null
                    ? iconTransform.GetComponent<Image>()
                    : null;

            TMP_Text nameText =
                nameTransform != null
                    ? nameTransform.GetComponent<TMP_Text>()
                    : null;

            TMP_Text amountText =
                amountTransform != null
                    ? amountTransform.GetComponent<TMP_Text>()
                    : null;

            if (icon != null)
            {
                icon.gameObject.SetActive(showIcon);

                if (sprite != null)
                    icon.sprite = sprite;

                icon.color = Color.white;
            }

            if (nameText != null)
            {
                nameText.gameObject.SetActive(true);
                nameText.text = name;
                nameText.color = available
                    ? Color.white
                    : Color.red;
            }

            if (amountText != null)
            {
                bool showAmount =
                    !string.IsNullOrEmpty(amount);

                amountText.gameObject.SetActive(
                    showAmount);

                if (showAmount)
                {
                    amountText.text = amount;
                    amountText.enableAutoSizing = true;
                    amountText.fontSizeMin = 8f;
                    amountText.color =
                        available
                            ? Color.white
                            : Color.red;
                }
            }

        }

        private static void RenderRisk(
            InventoryGui gui,
            Recipe recipe,
            ItemDrop.ItemData item)
        {
            int naturalMax =
                Mathf.Max(
                    1,
                    recipe.m_item.m_itemData
                        .m_shared.m_maxQuality);

            int potential =
                Mathf.Max(
                    0,
                    item.m_quality - naturalMax);

            if (!ForgeSelection.HasRequiredIdol(
                Player.m_localPlayer))
            {
                gui.m_itemCraftType.gameObject.SetActive(true);
                gui.m_itemCraftType.text =
                    ForgeUiLabels.GetNoIdol();
                return;
            }

            ForgeOdds odds =
                ForgeRules.GetOdds(
                    potential,
                    ForgeSelection.RequiredTier,
                    ForgeSelection.SelectedIdolTier,
                    ForgeSelection.SelectedTrophy,
                    ForgeSelection.ItemReferencePrestige);

            gui.m_itemCraftType
                .gameObject.SetActive(true);

            float ignoreChance =
                odds.FailureIsIgnore
                    ? odds.OrdinaryFailure
                    : 0f;

            float failChance =
                odds.FailureIsIgnore
                    ? 0f
                    : odds.OrdinaryFailure;

            string oddsText =
                FormatChance(
                    odds.Success) +
                " success";

            if (ignoreChance > 0.0001f)
            {
                oddsText +=
                    "   " +
                    FormatChance(ignoreChance) +
                    " ignore";
            }

            if (failChance > 0.0001f)
            {
                oddsText +=
                    "   " +
                    FormatChance(failChance) +
                    " fail";
            }

            if (odds.CompleteLoss > 0.0001f)
            {
                oddsText +=
                    "   " +
                    FormatChance(
                        odds.CompleteLoss) +
                    " reset";
            }

            if (odds.Destruction > 0.0001f)
                oddsText += "   " +
                    FormatChance(odds.Destruction) +
                    " destruction";

            gui.m_itemCraftType.text =
                oddsText;
        }

        private static string FormatChance(float chance)
        {
            float percent = Mathf.Round(Mathf.Clamp01(chance) * 1000f) / 10f;
            float rounded = Mathf.Round(percent);

            if (Mathf.Approximately(percent, rounded))
                return Mathf.RoundToInt(percent).ToString() + "%";

            return percent.ToString("0.0") + "%";
        }

        private static void UpdateCraftButton(
            InventoryGui gui,
            Player player,
            CraftingStation station,
            Recipe recipe,
            ItemDrop.ItemData item)
        {
            bool noCost =
                player.NoCostCheat() ||
                (ZoneSystem.instance != null &&
                 ZoneSystem.instance.GetGlobalKey(
                     GlobalKeys.NoCraftCost));

            bool idolOk =
                ForgeSelection.HasRequiredIdol(player);

            bool trophyOk =
                ForgeSelection.HasSelectedTrophy(player);

            // At an upgrader station Valheim intentionally ignores the
            // item's normal crafting ingredients and consumes only upgrader
            // resources. Goodtential must preserve that behavior: the chosen
            // idol and optional trophy are the complete attempt cost.
            bool stationUsable =
                station.CheckUsable(
                    player,
                    false);

            bool itemStillPresent =
                player.GetInventory().ContainsItem(item);

            bool requirementsOk =
                idolOk &&
                trophyOk;

            gui.m_craftButton.interactable =
                itemStillPresent &&
                stationUsable &&
                idolOk &&
                (noCost || requirementsOk);

            UITooltip tip =
                gui.m_craftButton
                    .GetComponent<UITooltip>();

            if (tip == null)
                return;

            if (!noCost && !idolOk)
            {
                ItemDrop minimum =
                    ForgeData.GetIdolDrop(
                        ForgeSelection.Family,
                        ForgeSelection.RequiredTier);

                string minimumName =
                    minimum != null
                        ? ForgeData.LocalizeDropName(minimum)
                        : ForgeData.TierNames[
                            ForgeSelection.RequiredTier] +
                          " Idol";

                tip.m_text =
                    "No idol of this type is available.";
            }
            else if (!noCost && !trophyOk)
            {
                tip.m_text =
                    "The selected trophy is no longer in your inventory.";
            }
            else
            {
                tip.m_text = "";
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnCraftCancelPressed")]
    internal static class ForgeCraftCancelPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            ForgeSelection.CancelPending();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class ForgeInventoryHidePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            ForgeSelection.ResetAll();
            ForgeInfo.Leave();
            ForgePicker.Close();
        }
    }
}
