using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    // Hovering the idol or the trophy at the Forge explains that choice, in
    // the game's own item tooltip and laid out like one: a short line on
    // what it means, then "Label: value" lines with values and keywords in
    // orange. Texts are only rebuilt when something
    // they depend on changes.
    internal static class ForgeInfo
    {
        private struct Key
        {
            internal ItemDrop.ItemData Item;
            internal int Quality;
            internal int Idol;
            internal int Required;
            internal ItemDrop.ItemData Trophy;
            internal int Losses;
            internal int TimeBucket;

            internal bool Same(Key o) =>
                ReferenceEquals(Item, o.Item) && Quality == o.Quality && Idol == o.Idol &&
                Required == o.Required && ReferenceEquals(Trophy, o.Trophy) &&
                Losses == o.Losses && TimeBucket == o.TimeBucket;
        }

        private static Key _key;
        private static bool _hasKey;
        private static string _idolTopic, _idolText, _trophyTopic, _trophyText;

        // Requirement slots normally use a small tooltip; at the Forge the
        // idol and trophy borrow the item tooltip, and get theirs back after.
        private static readonly Dictionary<UITooltip, GameObject> OriginalPrefabs =
            new Dictionary<UITooltip, GameObject>();

        internal static void Refresh(InventoryGui gui, Player player, Recipe recipe, ItemDrop.ItemData item)
        {
            Key key = new Key
            {
                Item = item,
                Quality = item.m_quality,
                Idol = ForgeSelection.SelectedIdolTier,
                Required = ForgeSelection.RequiredTier,
                Trophy = ForgeSelection.SelectedTrophy,
                Losses = IdolLuck.Losses,
                // Trophy rest is counted in game time; once a game minute is plenty.
                TimeBucket = ZNet.instance != null ? (int)(ZNet.instance.GetTimeSeconds() / 60.0) : 0
            };

            if (!_hasKey || !key.Same(_key))
            {
                _key = key;
                _hasKey = true;
                BuildIdol(player);
                BuildTrophy();
            }

            GameObject itemTooltip = ItemTooltipPrefab(gui);
            SetTooltip(SlotTip(gui, 0), itemTooltip, _idolTopic, _idolText);
            SetTooltip(SlotTip(gui, 2), itemTooltip, _trophyTopic, _trophyText);
        }

        // Leaving the Forge (another station, or closing the inventory).
        internal static void Leave()
        {
            _hasKey = false;
            if (OriginalPrefabs.Count == 0)
                return;
            foreach (KeyValuePair<UITooltip, GameObject> pair in OriginalPrefabs)
                if (pair.Key != null)
                    pair.Key.m_tooltipPrefab = pair.Value;
            OriginalPrefabs.Clear();
        }

        // ---- Idol ----

        private static void BuildIdol(Player player)
        {
            int idol = ForgeSelection.SelectedIdolTier;
            int required = ForgeSelection.RequiredTier;
            ItemDrop.ItemData trophy = ForgeSelection.SelectedTrophy;
            float prestige = ForgeSelection.ItemReferencePrestige;
            StringBuilder b = new StringBuilder();

            if (!ForgeSelection.HasRequiredIdol(player))
            {
                _idolTopic = "Idol";
                b.Append("You don't have an idol of this kind.\n");
                Line(b, "Current safe tier", Orange(IdolName(required)));
                _idolText = b.ToString().TrimEnd('\n');
                return;
            }

            ItemDrop drop = ForgeData.GetIdolDrop(ForgeSelection.Family, idol);
            _idolTopic = drop != null ? ForgeData.LocalizeDropName(drop) : "Idol";

            if (idol < required)
                b.Append("Too weak for this item: worse odds, and trophy sacrifices are prohibited.\n");
            else if (idol > required)
                b.Append("Stronger than needed: better odds, and more likely kept.\n");
            else
                b.Append("Just right for this item.\n");
            b.Append('\n');

            ForgeOdds odds = ForgeRules.GetOdds(ForgeSelection.Potential, required, idol, trophy, prestige);
            Line(b, "Success", Pct(odds.Success));
            if (odds.OrdinaryFailure > 0.0001f)
                Line(b, odds.FailureIsIgnore ? "Nothing happens" : "Loses a level", Pct(odds.OrdinaryFailure));
            if (odds.CompleteLoss > 0.0001f)
                Line(b, "Back to its normal best", Pct(odds.CompleteLoss));
            if (odds.Destruction > 0.0001f)
                Line(b, "Destroyed", Pct(odds.Destruction));
            b.Append('\n');

            // Luck from losing a few in a row shows next to the chances it's in.
            string luck = IdolLuck.Losses > 0
                ? " " + Orange("+" + Mathf.RoundToInt(IdolLuck.Bonus * 100f) + "%")
                : "";
            Line(b, "Kept on a success", Pct(ForgeRules.KeepChance(ForgeRules.KeepAfterSuccess, idol, required, trophy, prestige)) + luck);
            Line(b, "Kept otherwise", Pct(ForgeRules.KeepChance(ForgeRules.KeepAfterSetback, idol, required, trophy, prestige)) + luck);

            b.Append('\n');
            Line(b, "Current safe tier", Orange(IdolName(required)));

            _idolText = b.ToString().TrimEnd('\n');
        }

        // ---- Trophy ----

        private static void BuildTrophy()
        {
            int idol = ForgeSelection.SelectedIdolTier;
            int required = ForgeSelection.RequiredTier;
            ItemDrop.ItemData trophy = ForgeSelection.SelectedTrophy;
            float prestige = ForgeSelection.ItemReferencePrestige;

            if (idol >= 0 && idol < required)
            {
                _trophyTopic = "Trophy";
                _trophyText = "Your idol is too weak: trophy sacrifices are prohibited.";
                return;
            }

            if (trophy == null)
            {
                _trophyTopic = "Trophy";
                _trophyText = "None offered. Press " + Orange("T") + " to offer one: it protects the item and makes keeping your idol more likely.";
                return;
            }

            _trophyTopic = ForgeData.LocalizeItemName(trophy);
            StringBuilder b = new StringBuilder();
            b.Append(ForgeData.GetFavorDescription(trophy, prestige)).Append("\n\n");

            float multiplier = ForgeData.GetTrophyBadOutcomeMultiplier(trophy, prestige);
            Line(b, "Fewer failures", Pct(1f - multiplier));
            if (ForgeRules.CompleteLossRiskActive(ForgeSelection.Potential))
                Line(b, "Fewer resets", Pct(1f - Mathf.Pow(multiplier, 1.8f)));
            Line(b, "Idol kept", Orange("+" + Mathf.RoundToInt(ForgeRules.TrophyKeepBonus(trophy, prestige) * 100f) + "%"));

            float days = TrophyFatigue.RestingDays(trophy);
            if (days > 0.02f)
                b.Append('\n').Append("Its magic was drawn on recently, so it's weaker for now. Full strength again in ")
                    .Append(Days(days)).Append('.');

            _trophyText = b.ToString().TrimEnd('\n');
        }

        // ---- Formatting, the way the game's own tooltips do it ----

        private static void Line(StringBuilder b, string label, string value) =>
            b.Append(label).Append(": ").Append(value).Append('\n');

        private static string Pct(float chance) =>
            Orange(Mathf.RoundToInt(Mathf.Clamp01(chance) * 100f) + "%");

        private static string Orange(string text) => "<color=orange>" + text + "</color>";

        private static string Days(float days)
        {
            if (days < 0.75f)
                return "less than " + Orange("a") + " day";
            float rounded = Mathf.Round(days * 2f) / 2f;
            return "about " + Orange(rounded.ToString("0.#")) + (rounded <= 1f ? " day" : " days");
        }

        private static string IdolName(int tier)
        {
            ItemDrop drop = ForgeData.GetIdolDrop(ForgeSelection.Family, tier);
            return drop != null ? ForgeData.LocalizeDropName(drop) : "";
        }

        // ---- Tooltips ----

        private static UITooltip SlotTip(InventoryGui gui, int slot)
        {
            if (gui.m_recipeRequirementList == null || gui.m_recipeRequirementList.Length <= slot)
                return null;
            GameObject root = gui.m_recipeRequirementList[slot];
            return root != null ? root.GetComponent<UITooltip>() : null;
        }

        // The tooltip an inventory item shows on hover.
        private static GameObject ItemTooltipPrefab(InventoryGui gui)
        {
            GameObject element = gui.m_playerGrid != null ? gui.m_playerGrid.m_elementPrefab : null;
            UITooltip tip = element != null ? element.GetComponentInChildren<UITooltip>(true) : null;
            return tip != null ? tip.m_tooltipPrefab : null;
        }

        private static void SetTooltip(UITooltip tip, GameObject prefab, string topic, string text)
        {
            if (tip == null)
                return;
            if (prefab != null && tip.m_tooltipPrefab != prefab)
            {
                if (!OriginalPrefabs.ContainsKey(tip))
                    OriginalPrefabs.Add(tip, tip.m_tooltipPrefab);
                tip.m_tooltipPrefab = prefab;
            }
            tip.enabled = true;
            // Set() also updates a tooltip that's already showing.
            tip.Set(topic, text);
        }
    }
}
