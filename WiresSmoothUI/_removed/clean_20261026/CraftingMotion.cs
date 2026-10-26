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
    //    you switch Craft/Upgrade. Only rows move; tabs and the station header
    //    stay put.
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

        internal static bool ListPending;

        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.CraftingAnimations.Value;

        internal static object Selected(InventoryGui gui) => SelectedField?.GetValue(gui);

        private static GameObject RowOf(object pair) =>
            pair == null ? null : Traverse.Create(pair).Property("InterfaceElement").GetValue() as GameObject;

        internal static bool SameRecipe(object a, object b)
        {
            if (a == null || b == null)
                return a == b;
            Traverse ta = Traverse.Create(a), tb = Traverse.Create(b);
            return ta.Property("Recipe").GetValue() == tb.Property("Recipe").GetValue()
                && ta.Property("ItemData").GetValue() == tb.Property("ItemData").GetValue();
        }

        // ---- List ----

        internal static void PlayList(InventoryGui gui)
        {
            if (!On || gui == null || gui.m_recipeListRoot == null || !gui.m_recipeListRoot.gameObject.activeInHierarchy
                || !(RecipesField?.GetValue(gui) is IList list))
                return;

            Canvas.ForceUpdateCanvases();
            List<Component> rows = new List<Component>(list.Count);
            foreach (object pair in list)
            {
                GameObject row = RowOf(pair);
                if (row != null)
                    rows.Add(row.transform);
            }

            ScrollRect scroll = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>();
            PanelMotionSystem.Rows(rows, scroll != null ? PanelMotionSystem.Viewport(scroll) : null);
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
            foreach (GameObject req in gui.m_recipeRequirementList)
                Detail(req, ref i);

            if (gui.m_minStationLevelIcon != null)
                UiMotion.Pop(gui.m_minStationLevelIcon.transform, 0.6f, 1.25f, 0.28f);
        }

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
        private static void Postfix(InventoryGui __instance) => FeedbackPops.Tab(__instance.m_tabCraft);
    }

    [HarmonyPatch(typeof(InventoryGui), "OnTabUpgradePressed")]
    internal static class SmoothUICraftListUpgradeTabPatch
    {
        [HarmonyPrefix]
        private static void Prefix() => CraftingMotion.ListPending = true;

        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance) => FeedbackPops.Tab(__instance.m_tabUpgrade);
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
