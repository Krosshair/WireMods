using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WiresForgeOfGoodtential
{
    // Vanilla's Forge upgrade list requires the native safe-tier idol.
    // Goodtential accepts any owned idol of the correct family, including
    // lower-tier idols used as risky offerings.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class ForgeRecipeListPatch
    {
        private static readonly FieldInfo EntriesField =
            AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");

        private static PropertyInfo RecipeProperty;
        private static PropertyInfo ItemProperty;
        private static PropertyInfo ElementProperty;

        private sealed class Entry
        {
            internal object Data;
            internal GameObject Element;
            internal bool Usable;
            internal float Success;
            internal float Severity;
            internal int OriginalIndex;
        }

        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            Player player = Player.m_localPlayer;
            CraftingStation station =
                player != null
                    ? player.GetCurrentCraftingStation()
                    : null;

            if (__instance == null ||
                station == null || !station.m_upgrader ||
                EntriesField == null)
                return;

            IList entries =
                EntriesField.GetValue(__instance) as IList;

            if (entries == null || entries.Count == 0)
                return;

            Type pairType = entries[0].GetType();

            if (RecipeProperty == null)
            {
                const BindingFlags flags = BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Instance;
                RecipeProperty = pairType.GetProperty("Recipe", flags);
                ItemProperty = pairType.GetProperty("ItemData", flags);
                ElementProperty =
                    pairType.GetProperty("InterfaceElement", flags);
            }

            if (RecipeProperty == null ||
                ItemProperty == null ||
                ElementProperty == null)
                return;

            List<Entry> sorted = new List<Entry>(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                object pair = entries[i];
                Recipe recipe =
                    RecipeProperty.GetValue(pair, null) as Recipe;
                ItemDrop.ItemData item =
                    ItemProperty.GetValue(pair, null) as ItemDrop.ItemData;
                GameObject element =
                    ElementProperty.GetValue(pair, null) as GameObject;

                Entry e = new Entry();
                e.Data = pair;
                e.Element = element;
                e.OriginalIndex = i;

                int nativeTier;
                IdolFamily family;
                Piece.Requirement unused;

                if (recipe != null && item != null &&
                    element != null &&
                    ForgeData.TryGetNativeIdol(
                        recipe, out nativeTier,
                        out family, out unused))
                {
                    int highest =
                        ForgeSelection.FindHighestOwnedIdol(
                            player, family, nativeTier);

                    e.Usable = highest >= 0;

                    if (e.Usable)
                    {
                        int required =
                            ForgeData.GetRequiredTier(
                                item, recipe, item.m_quality + 1,
                                nativeTier, family);

                        float continuous =
                            ForgeData.GetContinuousTier(
                                item, recipe, item.m_quality,
                                nativeTier, family);

                        float reference =
                            ForgeData.GetItemReferencePrestige(
                                continuous);

                        int normal =
                            Mathf.Max(1,
                                recipe.m_item.m_itemData
                                    .m_shared.m_maxQuality);

                        ForgeOdds odds = ForgeRules.GetOdds(
                            Mathf.Max(0, item.m_quality - normal),
                            required, highest, null, reference);

                        e.Success = odds.Success;
                        e.Severity =
                            (odds.FailureIsIgnore
                                ? 0f
                                : odds.OrdinaryFailure) +
                            odds.CompleteLoss * 2f +
                            odds.Destruction * 10f;
                    }
                }

                // Vanilla greys out an item unless the safe-tier idol
                // is available. Undo that only when an actual usable
                // idol (including a weaker one) is in the inventory.
                if (element != null)
                {
                    Transform icon = element.transform.Find("icon");
                    Transform name = element.transform.Find("name");

                    if (icon != null)
                    {
                        Image img = icon.GetComponent<Image>();
                        if (img != null)
                            img.color = e.Usable ? Color.white :
                                new Color(1f, 0f, 1f, 0f);
                    }

                    if (name != null)
                    {
                        TMP_Text txt = name.GetComponent<TMP_Text>();
                        if (txt != null)
                            txt.color = e.Usable
                                ? Color.white
                                : new Color(.66f, .66f, .66f, 1f);
                    }
                }

                sorted.Add(e);
            }

            sorted.Sort(delegate(Entry x, Entry y)
            {
                if (x.Usable != y.Usable)
                    return x.Usable ? -1 : 1;

                if (x.Usable && y.Usable)
                {
                    int success = y.Success.CompareTo(x.Success);
                    if (success != 0)
                        return success;

                    int risk = x.Severity.CompareTo(y.Severity);
                    if (risk != 0)
                        return risk;
                }

                return x.OriginalIndex.CompareTo(y.OriginalIndex);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                entries[i] = sorted[i].Data;
                GameObject element = sorted[i].Element;

                if (element == null)
                    continue;

                RectTransform rect =
                    element.transform as RectTransform;
                if (rect != null)
                    rect.anchoredPosition =
                        new Vector2(0f,
                            -i * __instance.m_recipeListSpace);
            }
        }
    }
}
