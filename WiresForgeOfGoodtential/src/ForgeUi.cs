using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresForgeOfGoodtential
{
    internal sealed class GoodtentialCycleButton :
        MonoBehaviour
    {
        internal int Kind;

        internal void Cycle()
        {
            InventoryGui gui =
                GetComponentInParent<InventoryGui>();

            Player player = Player.m_localPlayer;

            if (gui == null ||
                player == null ||
                ForgeUiPatch.IsRefining(gui))
            {
                return;
            }

            int oldIdol =
                ForgeSelection.SelectedIdolTier;

            ItemDrop.ItemData oldTrophy =
                ForgeSelection.SelectedTrophy;

            if (Kind == 0)
            {
                ForgeSelection.CycleIdol(
                    player,
                    1);
            }
            else
            {
                ForgeSelection.CycleTrophy(
                    player,
                    1);
            }

            bool changed =
                oldIdol !=
                    ForgeSelection.SelectedIdolTier ||
                oldTrophy !=
                    ForgeSelection.SelectedTrophy;

            if (changed &&
                gui.m_moveItemEffects != null)
            {
                gui.m_moveItemEffects.Create(
                    gui.transform.position,
                    Quaternion.identity);
            }

            ForgeUiPatch.RefreshAfterSelection(gui);
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

            DisableItemTooltip(
                gui.m_recipeRequirementList[0]);

            DisableItemTooltip(
                gui.m_recipeRequirementList[2]);

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

            GameObject visual;

            if (existing == null)
            {
                UIGamePad sourceHint =
                    gui.m_takeAllButton
                        .GetComponent<UIGamePad>();

                bool sourceHintEnabled =
                    sourceHint != null &&
                    sourceHint.enabled;

                if (sourceHint != null)
                    sourceHint.enabled = false;

                Button clonedButton =
                    UnityEngine.Object.Instantiate(
                        gui.m_takeAllButton,
                        root.transform);

                if (sourceHint != null)
                    sourceHint.enabled =
                        sourceHintEnabled;

                visual =
                    clonedButton.gameObject;

                visual.name = visualName;

                clonedButton.onClick =
                    new Button.ButtonClickedEvent();

                UIGamePad clonedHint =
                    visual.GetComponent<UIGamePad>();

                if (clonedHint != null)
                    clonedHint.enabled = false;

                RectTransform rect =
                    visual.GetComponent<RectTransform>();

                if (rect != null)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot =
                        new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.offsetMin =
                        new Vector2(3f, 3f);
                    rect.offsetMax =
                        new Vector2(-3f, -3f);
                    rect.localScale = Vector3.one;
                }

                UITooltip[] tips =
                    visual.GetComponentsInChildren<
                        UITooltip>(true);

                for (int i = 0;
                     i < tips.Length;
                     i++)
                {
                    tips[i].m_topic = "";
                    tips[i].m_text = "";
                    tips[i].enabled = false;
                }
            }
            else
            {
                visual = existing.gameObject;
            }

            visual.SetActive(true);

            Button button =
                visual.GetComponent<Button>();

            if (button == null)
                return;

            TextMeshProUGUI label =
                visual.GetComponentInChildren<
                    TextMeshProUGUI>(true);

            if (label != null)
            {
                label.text =
                    kind == 0
                        ? "CI"
                        : "CT";

                label.alignment =
                    TextAlignmentOptions.Center;
            }

            GoodtentialCycleButton cycle =
                visual.GetComponent<
                    GoodtentialCycleButton>();

            if (cycle == null)
            {
                cycle =
                    visual.AddComponent<
                        GoodtentialCycleButton>();

                button.onClick =
                    new Button.ButtonClickedEvent();

                button.onClick.AddListener(
                    cycle.Cycle);
            }

            cycle.Kind = kind;

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

                    canCycle =
                        idolTiers.Count > 1 ||
                        (idolTiers.Count > 0 &&
                         !idolTiers.Contains(
                             ForgeSelection
                                 .SelectedIdolTier));
                }
                else
                {
                    canCycle =
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

            int owned = 0;

            if (selected != null &&
                selected.m_itemData != null &&
                selected.m_itemData.m_shared != null)
            {
                owned =
                    player.GetInventory().CountItems(
                        selected.m_itemData
                            .m_shared.m_name);
            }

            string materialName =
                ForgeData.TierNames[
                    ForgeSelection.SelectedIdolTier];

            string idolType =
                ForgeSelection.Family ==
                    IdolFamily.Battle
                    ? "Battle"
                    : "Protection";

            SetRequirement(
                root,
                selected != null
                    ? selected.m_itemData.GetIcon()
                    : null,
                materialName,
                idolType,
                owned > 0,
                true);
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

            UITooltip tip =
                root.GetComponent<UITooltip>();

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
                nameText.color = Color.white;
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

            if (tip != null)
            {
                tip.m_topic = "";
                tip.m_text = "";
                tip.enabled = false;
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
            {
                oddsText +=
                    "   " +
                    FormatChance(
                        odds.Destruction) +
                    " destruction";
            }

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
                    "Requires " +
                    minimumName +
                    " or a stronger idol.";
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
        }
    }
}
