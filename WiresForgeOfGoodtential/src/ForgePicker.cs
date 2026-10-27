using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresForgeOfGoodtential
{
    // "Swap" next to the idol or trophy opens a copy of the shield style
    // popup listing what you can choose, as icons; hover one to see what it
    // does, click it to pick it. For trophies, clicking the one already
    // offered takes it back off.
    internal static class ForgePicker
    {
        private const int MinColumns = 5;
        private const int MaxColumns = 9;
        private const int BaseRows = 3;

        private static VariantDialog _dialog;
        private static RectTransform _frame;
        private static Vector2 _frameBase;
        private static readonly List<GameObject> Made = new List<GameObject>();

        private struct Option
        {
            internal Sprite Icon;
            internal string Topic;
            internal string Text;
            internal int Tier;
            internal ItemDrop.ItemData Trophy;
        }

        internal static void Open(InventoryGui gui, int kind)
        {
            Player player = Player.m_localPlayer;
            if (gui == null || player == null || !Ensure(gui))
                return;

            List<Option> options = kind == 0 ? Idols(player) : Trophies(player);
            if (options.Count == 0)
                return;

            Fill(gui, options, kind);
            _dialog.gameObject.SetActive(true);
        }

        internal static void Close()
        {
            if (_dialog != null && _dialog.gameObject.activeSelf)
                _dialog.gameObject.SetActive(false);
        }

        // ---- Options ----

        private static List<Option> Idols(Player player)
        {
            List<Option> list = new List<Option>();
            foreach (int tier in ForgeSelection.GetOwnedValidIdolTiers(player))
            {
                ItemDrop drop = ForgeData.GetIdolDrop(ForgeSelection.Family, tier);
                if (drop == null)
                    continue;

                ForgeOdds odds = ForgeRules.GetOdds(
                    ForgeSelection.Potential, ForgeSelection.RequiredTier, tier,
                    tier >= ForgeSelection.RequiredTier ? ForgeSelection.SelectedTrophy : null,
                    ForgeSelection.ItemReferencePrestige);

                string fit = tier < ForgeSelection.RequiredTier ? "Too weak for this item."
                    : tier > ForgeSelection.RequiredTier ? "Stronger than needed."
                    : "Just right for this item.";

                list.Add(new Option
                {
                    Icon = drop.m_itemData.GetIcon(),
                    Topic = ForgeData.LocalizeDropName(drop),
                    Text = fit + "\n\nSuccess: <color=orange>" + Mathf.RoundToInt(odds.Success * 100f) + "%</color>" +
                        (tier == ForgeSelection.SelectedIdolTier ? "\n\nSelected." : ""),
                    Tier = tier
                });
            }
            return list;
        }

        private static List<Option> Trophies(Player player)
        {
            List<Option> list = new List<Option>();
            foreach (ItemDrop.ItemData trophy in ForgeSelection.GetUniqueTrophies(player))
            {
                bool selected = ForgeSelection.SelectedTrophy != null &&
                    ForgeSelection.SelectedTrophy.m_shared.m_name == trophy.m_shared.m_name;

                list.Add(new Option
                {
                    Icon = trophy.GetIcon(),
                    Topic = ForgeData.LocalizeItemName(trophy),
                    Text = ForgeData.GetFavorDescription(trophy, ForgeSelection.ItemReferencePrestige) +
                        (selected ? "\n\nOffered. Click again to take it back." : ""),
                    Trophy = trophy
                });
            }
            return list;
        }

        // ---- Choosing ----

        private static void Choose(InventoryGui gui, int kind, Option option)
        {
            if (ForgeUiPatch.IsRefining(gui))
                return;

            if (kind == 0)
            {
                ForgeSelection.SelectedIdolTier = option.Tier;
                // Under-tier idols can't take a trophy.
                if (option.Tier < ForgeSelection.RequiredTier)
                    ForgeSelection.SelectedTrophy = null;
            }
            else
            {
                bool same = ForgeSelection.SelectedTrophy != null &&
                    ForgeSelection.SelectedTrophy.m_shared.m_name == option.Trophy.m_shared.m_name;
                ForgeSelection.SelectedTrophy = same ? null : option.Trophy;
            }

            if (gui.m_moveItemEffects != null)
                gui.m_moveItemEffects.Create(gui.transform.position, Quaternion.identity);

            Close();
            ForgeUiPatch.RefreshAfterSelection(gui);
        }

        // ---- The popup ----

        private static bool Ensure(InventoryGui gui)
        {
            if (_dialog != null)
                return true;
            if (gui.m_variantDialog == null)
                return false;

            // A copy of the shield style popup: same frame, icon slots and
            // closing. It isn't hooked up to the game, only to us.
            GameObject source = gui.m_variantDialog.gameObject;
            bool wasActive = source.activeSelf;
            source.SetActive(false);
            _dialog = Object.Instantiate(gui.m_variantDialog, source.transform.parent);
            source.SetActive(wasActive);

            _dialog.name = "Goodtential picker";
            _dialog.m_selected = null;

            _frame = _dialog.transform.Find("VariantFrame") as RectTransform;
            if (_frame != null)
                _frameBase = _frame.sizeDelta;
            return true;
        }

        private static void Fill(InventoryGui gui, List<Option> options, int kind)
        {
            foreach (GameObject go in Made)
                if (go != null)
                    Object.Destroy(go);
            Made.Clear();

            int count = options.Count;
            int columns = Mathf.Clamp(Mathf.Max(MinColumns, Mathf.CeilToInt(count / (float)BaseRows)), MinColumns, MaxColumns);
            int rows = Mathf.CeilToInt(count / (float)columns);
            float spacing = _dialog.m_spacing;

            // Grow the frame for more columns or rows than the style popup holds.
            if (_frame != null)
                _frame.sizeDelta = _frameBase + new Vector2(
                    Mathf.Max(0, columns - MinColumns) * spacing,
                    Mathf.Max(0, rows - BaseRows) * spacing);

            GameObject itemTooltip = ItemTooltipPrefab(gui);

            for (int i = 0; i < count; i++)
            {
                Option option = options[i];
                GameObject element = Object.Instantiate(_dialog.m_elementPrefab, _dialog.m_listRoot);
                element.SetActive(true);
                ((RectTransform)element.transform).anchoredPosition =
                    new Vector2(i % columns * spacing, -(i / columns) * spacing);

                Transform buttonTransform = element.transform.Find("Button");
                Button button = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
                if (button == null)
                {
                    Object.Destroy(element);
                    continue;
                }

                button.GetComponent<Image>().sprite = option.Icon;
                button.onClick.AddListener(() => Choose(gui, kind, option));

                // A tooltip needs a prefab to show; without one, none at all.
                UITooltip tip = button.GetComponent<UITooltip>();
                if (tip == null && itemTooltip != null)
                    tip = button.gameObject.AddComponent<UITooltip>();
                if (tip != null)
                {
                    if (itemTooltip != null)
                        tip.m_tooltipPrefab = itemTooltip;
                    tip.m_topic = option.Topic;
                    tip.m_text = option.Text;
                }

                Made.Add(element);
            }
        }

        private static GameObject ItemTooltipPrefab(InventoryGui gui)
        {
            GameObject element = gui.m_playerGrid != null ? gui.m_playerGrid.m_elementPrefab : null;
            UITooltip tip = element != null ? element.GetComponentInChildren<UITooltip>(true) : null;
            return tip != null ? tip.m_tooltipPrefab : null;
        }
    }

    // "Swap" in every language Valheim ships, taken from the game's own
    // translations of "Switch item" where it has one.
    [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
    internal static class ForgeSwapWordPatch
    {
        internal const string Key = "goodtential_swap";

        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>
        {
            { "English", "Swap" }, { "Swedish", "Byt" }, { "French", "Changer" }, { "Italian", "Scambia" },
            { "German", "Wechseln" }, { "Spanish", "Cambiar" }, { "Russian", "Сменить" }, { "Romanian", "Schimbă" },
            { "Bulgarian", "Смени" }, { "Macedonian", "Смени" }, { "Finnish", "Vaihda" }, { "Danish", "Skift" },
            { "Norwegian", "Bytt" }, { "Icelandic", "Skipta" }, { "Turkish", "Değiştir" }, { "Lithuanian", "Keisti" },
            { "Czech", "Přepnout" }, { "Hungarian", "Csere" }, { "Slovak", "Prepnúť" }, { "Polish", "Zmień" },
            { "Dutch", "Wissel" }, { "Portuguese_European", "Trocar" }, { "Portuguese_Brazilian", "Trocar" },
            { "Chinese", "切换" }, { "Chinese_Trad", "切換" }, { "Japanese", "切替" }, { "Korean", "변경" },
            { "Thai", "สลับ" }, { "Croatian", "Promijeni" }, { "Georgian", "შეცვლა" }, { "Greek", "Αλλαγή" },
            { "Ukrainian", "Змінити" }
        };

        private static readonly System.Reflection.MethodInfo AddWord =
            AccessTools.Method(typeof(Localization), "AddWord");

        [HarmonyPostfix]
        private static void Postfix(Localization __instance, string language)
        {
            if (AddWord == null)
                return;
            if (!Words.TryGetValue(language ?? "", out string word))
                word = Words["English"];
            AddWord.Invoke(__instance, new object[] { Key, word });
            _text = null;
        }

        private static string _language;
        private static string _text;

        // Looked up once per language. If the language was set up before
        // this mod loaded, the word is added now.
        internal static string Text()
        {
            Localization loc = Localization.instance;
            if (loc == null)
                return Words["English"];

            string language = loc.GetSelectedLanguage();
            if (_text != null && language == _language)
                return _text;

            string text = loc.Localize("$" + Key);
            if (text.Contains(Key))
            {
                Postfix(loc, language);
                text = loc.Localize("$" + Key);
            }

            _language = language;
            _text = text.Contains(Key) ? Words["English"] : text;
            return _text;
        }
    }
}
