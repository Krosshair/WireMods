using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WiresForgeOfGoodtential
{
    // Everything the Forge knows, said plainly, in vanilla UI. All three
    // hovers use the game's own item tooltip, so they look like hovering
    // an item:
    //  - the idol and trophy explain that one choice;
    //  - a "?" button next to Craft, styled like it, has the whole picture.
    // Texts are only rebuilt when something they depend on changes.
    internal static class ForgeInfo
    {
        private const string ButtonName = "Goodtential info button";
        private const float ButtonGap = 8f;

        private static Button _button;
        private static RectTransform _craftRect;
        private static Vector2 _craftOffsetMax;
        private static bool _craftShrunk;

        private static UITooltip _buttonTip;

        // Requirement slots normally use a small tooltip; while at the Forge
        // the idol and trophy borrow the item tooltip, and get theirs back.
        private static readonly System.Collections.Generic.Dictionary<UITooltip, GameObject> OriginalPrefabs =
            new System.Collections.Generic.Dictionary<UITooltip, GameObject>();

        // ---- Cached texts ----

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
        private static string _idolTopic, _idolText, _trophyTopic, _trophyText, _panelTopic, _panelBody;

        // ---- Called by the Forge UI ----

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
                Build(player, recipe, item);
            }

            GameObject itemTooltip = ItemTooltipPrefab(gui);
            SetTooltip(SlotTip(gui, 0), itemTooltip, _idolTopic, _idolText);
            SetTooltip(SlotTip(gui, 2), itemTooltip, _trophyTopic, _trophyText);

            EnsureButton(gui);
            ShowButton(true);
            SetTooltip(_buttonTip, itemTooltip, _panelTopic, _panelBody);
        }

        // Leaving the Forge (another station, or closing the inventory).
        internal static void Leave()
        {
            ShowButton(false);
            _hasKey = false;

            if (OriginalPrefabs.Count == 0)
                return;
            foreach (var pair in OriginalPrefabs)
                if (pair.Key != null)
                    pair.Key.m_tooltipPrefab = pair.Value;
            OriginalPrefabs.Clear();
        }

        // ---- Texts ----

        private static void Build(Player player, Recipe recipe, ItemDrop.ItemData item)
        {
            int idol = ForgeSelection.SelectedIdolTier;
            int required = ForgeSelection.RequiredTier;
            ItemDrop.ItemData trophy = ForgeSelection.SelectedTrophy;
            float prestige = ForgeSelection.ItemReferencePrestige;
            bool hasIdol = ForgeSelection.HasRequiredIdol(player);

            string idolName = hasIdol ? IdolName(idol) : "";
            string safeName = IdolName(required);

            float keepWin = 0f, keepElse = 0f;
            if (hasIdol)
            {
                keepWin = ForgeRules.KeepChance(ForgeRules.KeepAfterSuccess, idol, required, trophy, prestige);
                keepElse = ForgeRules.KeepChance(ForgeRules.KeepAfterSetback, idol, required, trophy, prestige);
            }

            string fit = !hasIdol ? ""
                : idol < required ? "Too weak for this item: lower odds, and it can't take a trophy."
                : idol > required ? "Stronger than needed: better odds, and more likely kept."
                : "Just right for this item.";

            string kept = hasIdol
                ? "Kept " + Pct(keepWin) + " of the time on a success, " + Pct(keepElse) + " otherwise."
                : "";

            // Idol hover.
            _idolTopic = hasIdol ? idolName : "Idol";
            _idolText = !hasIdol
                ? "You don't have an idol of this kind."
                : fit + "\n" + kept + (idol < required ? "\nSafe from: " + safeName + "." : "");

            // Trophy hover.
            string trophyName = trophy != null ? ForgeData.LocalizeItemName(trophy) : "";
            string trophyState = TrophyState(trophy, idol, required, prestige);
            _trophyTopic = trophy != null ? trophyName : "Trophy";
            _trophyText = trophyState;

            // Panel.
            ForgeOdds odds = hasIdol
                ? ForgeRules.GetOdds(ForgeSelection.Potential, required, idol, trophy, prestige)
                : null;

            StringBuilder b = new StringBuilder();
            if (odds != null)
            {
                Line(b, "Success", Pct(odds.Success));
                if (odds.OrdinaryFailure > 0.0001f)
                    Line(b, odds.FailureIsIgnore ? "Nothing happens" : "Loses a level", Pct(odds.OrdinaryFailure));
                if (odds.CompleteLoss > 0.0001f)
                    Line(b, "Back to its normal best", Pct(odds.CompleteLoss));
                if (odds.Destruction > 0.0001f)
                    Line(b, "Destroyed", Pct(odds.Destruction));
                b.Append('\n');

                b.Append("Idol: ").Append(idolName).Append('\n').Append(fit).Append('\n');
                if (idol != required)
                    b.Append("Weakest safe idol: ").Append(safeName).Append('\n');
                b.Append(kept).Append('\n');
                if (IdolLuck.Losses > 0)
                    b.Append("Lost a few lately, so the next one is more likely kept.\n");
                b.Append('\n');

                b.Append("Trophy: ").Append(trophy != null ? trophyName : "none").Append('\n').Append(trophyState).Append('\n');

                string next = NextStrongerIdol(recipe, item);
                if (next.Length > 0)
                    b.Append('\n').Append(next);
            }
            else
            {
                b.Append("You need an idol of this kind to refine it.\n");
                b.Append("Weakest safe idol: ").Append(safeName).Append('.');
            }

            _panelTopic = ForgeData.LocalizeItemName(item);
            _panelBody = b.ToString().TrimEnd('\n');
        }

        private static string TrophyState(ItemDrop.ItemData trophy, int idol, int required, float prestige)
        {
            if (idol < required)
                return "Your idol is too weak to take a trophy.";
            if (trophy == null)
                return "None. Offer one with T to protect the item and keep your idol more often.";

            string state = ForgeData.GetFavorDescription(trophy, prestige);
            float days = TrophyFatigue.RestingDays(trophy);
            if (days > 0.02f)
                state += "\nOffered recently, so it helps less for now. Full strength again in " + Days(days) + ".";
            return state;
        }

        // "From quality 8 it needs a stronger idol." Only while there is one.
        private static string NextStrongerIdol(Recipe recipe, ItemDrop.ItemData item)
        {
            int required = ForgeSelection.RequiredTier;
            if (required >= ForgeData.MaxTier)
                return "";

            int naturalMax = Mathf.Max(1, recipe.m_item.m_itemData.m_shared.m_maxQuality);
            for (int quality = item.m_quality + 2; quality <= item.m_quality + 4; quality++)
            {
                int tier = ForgeData.GetRequiredTier(item, recipe, quality, ForgeSelection.NativeTier, ForgeSelection.Family);
                if (tier > required)
                    return "Going to quality " + quality + " needs " + IdolName(tier) + " or stronger.";
            }
            return "";
        }

        private static void Line(StringBuilder b, string label, string value) =>
            b.Append(label).Append(": ").Append(value).Append('\n');

        private static string Pct(float chance) =>
            "<color=orange>" + Mathf.RoundToInt(Mathf.Clamp01(chance) * 100f) + "%</color>";

        private static string Days(float days)
        {
            if (days < 0.75f)
                return "less than a day";
            float rounded = Mathf.Round(days * 2f) / 2f;
            return "about " + rounded.ToString("0.#") + (rounded <= 1f ? " day" : " days");
        }

        private static string IdolName(int tier)
        {
            ItemDrop drop = ForgeData.GetIdolDrop(ForgeSelection.Family, tier);
            return drop != null ? ForgeData.LocalizeDropName(drop) : "";
        }

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

        // ---- The "?" button ----

        private static void EnsureButton(InventoryGui gui)
        {
            if (_button != null || gui.m_craftButton == null)
                return;

            // A copy of the craft button, so it looks like it. Hovering it
            // shows the info; clicking does nothing.
            Transform panel = gui.m_craftButton.transform.parent;
            _button = Object.Instantiate(gui.m_craftButton, panel);
            _button.name = ButtonName;
            _button.onClick = new Button.ButtonClickedEvent();
            _button.interactable = true;

            foreach (UIGamePad hint in _button.GetComponentsInChildren<UIGamePad>(true))
                hint.gameObject.SetActive(false);

            _buttonTip = _button.GetComponent<UITooltip>();
            if (_buttonTip == null)
                _buttonTip = _button.gameObject.AddComponent<UITooltip>();

            TMP_Text label = _button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = "?";

            _craftRect = gui.m_craftButton.GetComponent<RectTransform>();
            _craftOffsetMax = _craftRect.offsetMax;
            _craftShrunk = false;

            // A square at the right end of the craft button's row.
            float size = _craftRect.rect.height > 1f ? _craftRect.rect.height : 50f;
            RectTransform rect = _button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(_craftOffsetMax.x, (_craftRect.offsetMin.y + _craftOffsetMax.y) * 0.5f);
            rect.localScale = Vector3.one;
        }

        private static void ShowButton(bool show)
        {
            if (_button == null)
                return;

            _button.gameObject.SetActive(show);

            // Make room for it on the craft button's right while at the Forge.
            if (_craftRect != null && show != _craftShrunk)
            {
                float size = ((RectTransform)_button.transform).sizeDelta.x + ButtonGap;
                _craftRect.offsetMax = show ? _craftOffsetMax - new Vector2(size, 0f) : _craftOffsetMax;
                _craftShrunk = show;
            }
        }

    }
}
