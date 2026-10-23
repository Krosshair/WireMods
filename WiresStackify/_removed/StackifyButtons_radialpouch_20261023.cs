using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresStackify
{
    // One button on the inventory, between armor and weight, and one on an
    // open chest, above its weight: the game's own armor and weight blocks
    // with the game's pouch icon (the radial menu's All items) and an S.
    // Left click quick stacks, right click restocks.
    internal static class StackifyButtons
    {
        private const string Name = "WireStackify";
        private const string Tooltip = "Left click: quick stack\nRight click: restock";

        // The pouch sits a little darker than the radial menu draws it, as
        // bright as the weight icon next to it.
        private static readonly Color IconTint = new Color(0.71f, 0.71f, 0.71f, 1f);
        private static readonly System.Collections.Generic.List<Image> Icons = new System.Collections.Generic.List<Image>();
        private static GameObject _clickSfx;

        internal sealed class ClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public bool Chest;

            public void OnPointerClick(PointerEventData e)
            {
                Player p = Player.m_localPlayer;
                if (p == null) return;
                bool left = e.button == PointerEventData.InputButton.Left;
                if (!left && e.button != PointerEventData.InputButton.Right) return;
                // The game's button click.
                if (_clickSfx != null) UnityEngine.Object.Instantiate(_clickSfx);
                if (left) QuickStack.Run(p, Chest);
                else Restock.Run(p, Chest);
            }
        }

        // The radial menu's data loads separately: set the icon the first
        // time the inventory opens after it's there.
        [HarmonyPatch(typeof(InventoryGui), "Show")]
        private static class ShowPatch
        {
            private static void Postfix() { SetIcons(); }
        }

        private static void SetIcons()
        {
            if (Icons.Count == 0) return;
            Sprite pouch = null;
            try
            {
                if (Valheim.UI.RadialData.SO != null && Valheim.UI.RadialData.SO.ItemGroupMappings != null)
                {
                    pouch = Valheim.UI.RadialData.SO.ItemGroupMappings.GetMapping("allitems").Sprite;
                }
            }
            catch (Exception) { }
            if (pouch == null) return;
            foreach (Image img in Icons)
                if (img != null)
                {
                    img.sprite = pouch;
                    img.color = IconTint;
                }
            Icons.Clear();
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static class AwakePatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                try { Build(__instance); }
                catch (Exception e) { StackifyPlugin.Log.LogWarning("Couldn't add the Stackify buttons: " + e); }
            }
        }

        private static void Build(InventoryGui gui)
        {
            if (gui.m_takeAllButton != null)
            {
                ButtonSfx sfx = gui.m_takeAllButton.GetComponent<ButtonSfx>();
                if (sfx != null) _clickSfx = sfx.m_sfxPrefab;
            }
            RectTransform armor = gui.m_armor != null ? gui.m_armor.transform.parent as RectTransform : null;
            RectTransform weight = gui.m_weight != null ? gui.m_weight.transform.parent as RectTransform : null;
            RectTransform chestWeight = gui.m_containerWeight != null ? gui.m_containerWeight.transform.parent as RectTransform : null;
            if (armor == null || weight == null) return;

            // Inventory: halfway between armor and weight.
            RectTransform inv = Clone(gui, armor, gui.m_armor, false);
            inv.localPosition = (armor.localPosition + weight.localPosition) * 0.5f;

            // Chest: the same distance above the chest's weight as the
            // inventory button sits above yours.
            if (chestWeight != null)
            {
                RectTransform chest = Clone(gui, chestWeight, gui.m_containerWeight, true);
                chest.localPosition = chestWeight.localPosition + (inv.localPosition - weight.localPosition);
            }
            SetIcons();
        }

        private static RectTransform Clone(InventoryGui gui, RectTransform block, TMP_Text valueText, bool chest)
        {
            GameObject go = UnityEngine.Object.Instantiate(block.gameObject, block.parent, false);
            go.name = Name + (chest ? "Chest" : "Inventory");
            RectTransform r = (RectTransform)go.transform;
            // Right after the block it copies, so it sits behind the panel
            // the same way the armor and weight blocks do.
            r.SetSiblingIndex(block.GetSiblingIndex() + 1);

            // The number becomes an S, the icon becomes the chest.
            string textPath = RelativePath(valueText.transform, block);
            foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (RelativePath(t.transform, r) == textPath) t.text = "S";
                t.raycastTarget = false;
            }
            foreach (Image img in go.GetComponentsInChildren<Image>(true))
            {
                if (img.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    img.sprite = null;
                    img.color = new Color(1f, 1f, 1f, 0f); // hidden until the pouch is found
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    Icons.Add(img);
                }
                else img.raycastTarget = true; // the background takes the clicks
            }

            // Anything else the game put on its armor and weight blocks
            // (their tooltips) now describes the button.
            foreach (UITooltip tip in go.GetComponentsInChildren<UITooltip>(true))
            {
                tip.m_topic = "";
                tip.m_text = Tooltip;
            }
            if (go.GetComponentInChildren<UITooltip>(true) == null)
            {
                UITooltip tip = go.AddComponent<UITooltip>();
                tip.m_text = Tooltip;
                // Borrow the game's tooltip window from any other tooltip.
                UITooltip any = null;
                foreach (UITooltip t in gui.GetComponentsInChildren<UITooltip>(true))
                    if (t.m_tooltipPrefab != null && t.gameObject != go) { any = t; break; }
                if (any != null) tip.m_tooltipPrefab = any.m_tooltipPrefab;
            }
            // Make sure there's something to click even if the block has no
            // background picture.
            if (go.GetComponent<Graphic>() == null)
            {
                Image hit = go.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;
            }
            go.AddComponent<ClickHandler>().Chest = chest;
            return r;
        }

        private static string RelativePath(Transform t, Transform root)
        {
            string path = "";
            while (t != null && t != root)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }
    }
}
