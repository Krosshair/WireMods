using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Crafting panel.
    //  - The recipe rows you can see slide in when the panel comes up and when
    //    you switch Craft/Upgrade, and the tab's label pops. Only rows move;
    //    tabs and the station header stay put.
    //  - Picking a recipe pops its row icon and the required station level,
    //    fades the selection marker in and slides the details in from the right.
    //  - Finishing a craft pops the requirement icons.
    // The craft button itself belongs to the button animation.
    internal static class CraftingMotion
    {
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly FieldInfo RecipesField = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");

        private static readonly Vector3 DetailFrom = new Vector3(14f, 0f, 0f);
        private const float DetailStep = 0.025f;
        private const float DetailDuration = 0.2f;

        private static readonly Vector3 RowFrom = new Vector3(-16f, 0f, 0f);
        private const float RowStep = 0.025f;
        private const float RowDuration = 0.22f;
        private const int MaxRows = 14;

        internal static bool ListPending;

        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.CraftingAnimations.Value;

        internal static object Selected(InventoryGui gui) => SelectedField?.GetValue(gui);

        // The recipe list entries are a private struct; read them through
        // cached properties instead of building a reflection wrapper each time.
        private static readonly System.Type PairType = AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair");
        private static readonly PropertyInfo RecipeProp = AccessTools.Property(PairType, "Recipe");
        private static readonly PropertyInfo ItemProp = AccessTools.Property(PairType, "ItemData");
        private static readonly PropertyInfo RowProp = AccessTools.Property(PairType, "InterfaceElement");

        private static GameObject RowOf(object pair) =>
            pair == null ? null : RowProp?.GetValue(pair) as GameObject;

        internal static bool SameRecipe(object a, object b)
        {
            if (a == null || b == null)
                return a == b;
            return RecipeProp?.GetValue(a) == RecipeProp?.GetValue(b)
                && ItemProp?.GetValue(a) == ItemProp?.GetValue(b);
        }

        // ---- List ----

        internal static void PlayList(InventoryGui gui)
        {
            if (!On || gui == null || gui.m_recipeListRoot == null || !gui.m_recipeListRoot.gameObject.activeInHierarchy
                || !(RecipesField?.GetValue(gui) is IList list))
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(gui.m_recipeListRoot);
            List<Component> rows = new List<Component>(list.Count);
            foreach (object pair in list)
            {
                GameObject row = RowOf(pair);
                if (row != null)
                    rows.Add(row.transform);
            }

            // Only the rows you can see; the rest are scrolled away anyway.
            ScrollRect scroll = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>();
            RectTransform viewport = scroll == null ? null
                : scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;

            int index = 0;
            foreach (Component row in rows)
            {
                if (index >= MaxRows)
                    break;
                if (!row.gameObject.activeInHierarchy || (viewport != null && !Overlaps(row.transform as RectTransform, viewport)))
                    continue;
                UiMotion.Enter(row.transform, index * RowStep, RowDuration, RowFrom, 1f, true);
                index++;
            }
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static bool Overlaps(RectTransform row, RectTransform viewport)
        {
            if (row == null)
                return false;
            row.GetWorldCorners(Corners);
            float minY = Corners[0].y, maxY = Corners[1].y;
            viewport.GetWorldCorners(Corners);
            return maxY > Corners[0].y && minY < Corners[1].y;
        }

        internal static void ExtraTabPressed(Button button)
        {
            InventoryGui gui = InventoryGui.instance;
            if (!On || gui == null || button == null || gui.m_tabCraft == null
                || button == gui.m_tabCraft || button == gui.m_tabUpgrade
                || button.transform.parent != gui.m_tabCraft.transform.parent)
                return;

            TabPressed(button);
            PlayList(gui);
        }

        // The label of the Craft or Upgrade tab you switch to pops.
        internal static void TabPressed(Button tab)
        {
            if (!On || tab == null)
                return;
            TMPro.TMP_Text label = tab.GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null)
                UiMotion.Pop(label.transform, 0.85f, 1.12f, 0.22f);
        }

        // ---- Picking a recipe ----

        // The game fills the details in on its next update, so wait a frame.
        internal static void RecipeChanged(InventoryGui gui)
        {
            if (On)
                UiMotion.NextFrame(() => PlayRecipe(gui));
        }

        private static void PlayRecipe(InventoryGui gui)
        {
            object pair = gui != null ? Selected(gui) : null;
            if (pair == null)
                return;

            GameObject row = RowOf(pair);
            if (row != null)
            {
                Transform icon = row.transform.Find("icon");
                if (icon != null)
                    UiMotion.Pop(icon, 1f, 1.14f, 0.2f);

                Transform marker = row.transform.Find("selected");
                if (marker != null)
                    UiMotion.Enter(marker, 0f, DetailDuration, Vector3.zero, 1f, true);
            }

            int i = 0;
            Detail(gui.m_recipeIcon, ref i);
            Detail(gui.m_recipeName, ref i);
            Detail(gui.m_itemCraftType, ref i);
            Detail(gui.m_upgradeItemIcon, ref i);
            Detail(gui.m_upgradeItemName, ref i);
            Detail(gui.m_recipeDecription, ref i);
            Detail(gui.m_qualityPanel, ref i);
            Detail(gui.m_variantButton, ref i);

            // The required station level sits in its own box at the start of
            // the requirements row: it slides in first, then the materials. The
            // star pops only when the level differs from the last recipe's.
            Image star = gui.m_minStationLevelIcon;
            if (star != null)
            {
                Transform box = star.transform.parent != null && star.transform.parent.name == "level"
                    ? star.transform.parent
                    : star.transform;
                Detail(box, ref i);

                string level = star.gameObject.activeInHierarchy && gui.m_minStationLevelText != null
                    ? gui.m_minStationLevelText.text
                    : "";
                if (level != _lastLevel && level.Length > 0)
                    UiMotion.Pop(star.transform, 0.6f, 1.25f, 0.28f);
                _lastLevel = level;
            }

            foreach (GameObject req in gui.m_recipeRequirementList)
                Detail(req, ref i);
        }

        private static string _lastLevel = "";

        private static void Detail(Object part, ref int index)
        {
            Transform t = part is GameObject go ? go.transform : (part as Component)?.transform;
            if (t == null || !t.gameObject.activeInHierarchy)
                return;
            UiMotion.Enter(t, Mathf.Min(0.25f, index * DetailStep), DetailDuration, DetailFrom, 1f, true);
            index++;
        }

        // ---- Crafting ----

        internal static void Crafted(InventoryGui gui)
        {
            if (!On || gui == null)
                return;

            foreach (GameObject req in gui.m_recipeRequirementList)
            {
                if (req == null || !req.activeInHierarchy)
                    continue;
                Transform icon = req.transform.Find("res_icon");
                if (icon != null)
                    UiMotion.Pop(icon, 0.85f, 1.12f, 0.24f);
            }
        }
    }

    // Clicking a row and gamepad stepping both end up here.
    [HarmonyPatch(typeof(InventoryGui), "SetRecipe")]
    internal static class SmoothUICraftSelectPatch
    {
        [HarmonyPrefix]
        private static void Prefix(InventoryGui __instance, out object __state)
        {
            __state = CraftingMotion.Selected(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryGui __instance, object __state)
        {
            if (!CraftingMotion.SameRecipe(__state, CraftingMotion.Selected(__instance)))
                CraftingMotion.RecipeChanged(__instance);
        }
    }

    // Opening: the list may be rebuilt this frame, so slide the rows in once
    // it has settled.
    [HarmonyPatch(typeof(InventoryGui), "Show")]
    internal static class SmoothUICraftListShowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            if (CraftingMotion.On)
                UiMotion.NextFrame(() => CraftingMotion.PlayList(__instance));
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnTabCraftPressed")]
    internal static class SmoothUICraftListCraftTabPatch
    {
        [HarmonyPrefix]
        private static void Prefix() => CraftingMotion.ListPending = true;

        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance) => CraftingMotion.TabPressed(__instance.m_tabCraft);
    }

    [HarmonyPatch(typeof(InventoryGui), "OnTabUpgradePressed")]
    internal static class SmoothUICraftListUpgradeTabPatch
    {
        [HarmonyPrefix]
        private static void Prefix() => CraftingMotion.ListPending = true;

        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance) => CraftingMotion.TabPressed(__instance.m_tabUpgrade);
    }

    // Runs after other mods (My Little UI sorts and filters here), so rows
    // slide in where they end up.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class SmoothUICraftListRebuildPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryGui __instance)
        {
            if (!CraftingMotion.ListPending)
                return;
            CraftingMotion.ListPending = false;
            CraftingMotion.PlayList(__instance);
        }
    }

    // A craft only counts once it actually took the materials. The finalizer
    // clears the flag even if crafting throws, so building afterwards can't
    // trigger it.
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class SmoothUICraftPatch
    {
        internal static bool InCraft;
        internal static bool Consumed;

        [HarmonyPrefix]
        private static void Prefix()
        {
            InCraft = true;
            Consumed = false;
        }

        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            if (Consumed)
                CraftingMotion.Crafted(__instance);
        }

        [HarmonyFinalizer]
        private static void Finalizer()
        {
            InCraft = false;
            Consumed = false;
        }
    }

    [HarmonyPatch(typeof(Player), "ConsumeResources")]
    internal static class SmoothUICraftConsumePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (SmoothUICraftPatch.InCraft)
                SmoothUICraftPatch.Consumed = true;
        }
    }
}

namespace WiresSmoothUI
{
    // Tabs other mods add next to Craft and Upgrade (Recycler's) rebuild the
    // list from their own click handler, which runs inside this call.
    [HarmonyPatch(typeof(Button), nameof(Button.OnPointerClick))]
    internal static class SmoothUICraftExtraTabClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Button __instance) => CraftingMotion.ExtraTabPressed(__instance);
    }

    [HarmonyPatch(typeof(Button), nameof(Button.OnSubmit))]
    internal static class SmoothUICraftExtraTabSubmitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Button __instance) => CraftingMotion.ExtraTabPressed(__instance);
    }
}
