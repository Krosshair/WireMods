using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // The hotbar keeps its slots in a private list of a private type.
    internal static class HotbarAccess
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(HotkeyBar), "m_elements");
        private static FieldInfo _queued, _equiped;

        // Looked up when an equip starts, never per frame.
        internal static HotkeyBar[] Bars() => Object.FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None);

        internal static IList Elements(HotkeyBar bar) => bar != null ? ElementsField?.GetValue(bar) as IList : null;

        internal static GameObject Queued(object element) => Get(ref _queued, element, "m_queued");
        internal static GameObject Equiped(object element) => Get(ref _equiped, element, "m_equiped");

        private static GameObject Get(ref FieldInfo field, object element, string name)
        {
            if (element == null)
                return null;
            if (field == null)
                field = AccessTools.Field(element.GetType(), name);
            return field?.GetValue(element) as GameObject;
        }
    }

    // Fills an item's queued background from the bottom up while it's being
    // equipped, and drains it while it's being unequipped. Only runs while the
    // player actually has an equip or unequip in progress.
    internal static class EquipFillSystem
    {
        private sealed class Original
        {
            internal Image.Type Type;
            internal Image.FillMethod Method;
            internal int Origin;
            internal float Amount;
        }

        private static readonly FieldInfo GridElementsField =
            AccessTools.Field(typeof(InventoryGrid), "m_elements");

        private static readonly Dictionary<Image, Original> Filled = new Dictionary<Image, Original>();
        // Hidden equipped backgrounds; true for hotbar ones.
        private static readonly Dictionary<Image, bool> HiddenEquip = new Dictionary<Image, bool>();
        private static readonly HashSet<Image> Touched = new HashSet<Image>();
        private static readonly List<Image> Release = new List<Image>();
        private static HotkeyBar[] _bars = new HotkeyBar[0];
        private static bool _active;

        internal static bool Busy => _active;

        private static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.EquipProgressFill.Value;

        internal static void Begin()
        {
            if (!On)
                return;

            _bars = HotbarAccess.Bars();
            _active = true;
            SmoothUIDriver.Wake();
        }

        internal static void Tick()
        {
            if (!_active)
                return;

            Player player = Player.m_localPlayer;
            if (!On || player == null)
            {
                RestoreAll();
                return;
            }

            player.GetActionProgress(out _, out float progress, out Player.MinorActionData data);
            if (data == null || data.m_type == Player.MinorActionData.ActionType.Reload)
            {
                RestoreAll();
                return;
            }

            Inventory inventory = player.GetInventory();
            Touched.Clear();

            InventoryGui gui = InventoryGui.instance;
            if (gui != null && gui.m_playerGrid != null && gui.m_playerGrid.gameObject.activeInHierarchy
                && GridElementsField?.GetValue(gui.m_playerGrid) is List<InventoryElement> elements)
            {
                foreach (InventoryElement element in elements)
                {
                    if (element == null || element.m_queued == null || !element.m_queued.enabled)
                        continue;

                    ItemDrop.ItemData item = inventory.GetItemAt(element.Position.x, element.Position.y);
                    Apply(element.m_queued, element.m_equiped, false, item, data, progress);
                }
            }

            foreach (HotkeyBar bar in _bars)
            {
                if (bar == null || !bar.isActiveAndEnabled)
                    continue;

                IList list = HotbarAccess.Elements(bar);
                if (list == null)
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    GameObject queued = HotbarAccess.Queued(list[i]);
                    if (queued == null || !queued.activeInHierarchy)
                        continue;

                    GameObject equiped = HotbarAccess.Equiped(list[i]);
                    Apply(queued.GetComponent<Image>(),
                        equiped != null ? equiped.GetComponent<Image>() : null,
                        true, inventory.GetItemAt(i, 0), data, progress);
                }
            }

            // Anything no longer queued goes back to how the game had it.
            Release.Clear();
            foreach (Image image in Filled.Keys)
                if (!Touched.Contains(image))
                    Release.Add(image);
            foreach (Image image in HiddenEquip.Keys)
                if (!Touched.Contains(image))
                    Release.Add(image);
            foreach (Image image in Release)
                RestoreImage(image);
        }

        private static void Apply(Image queued, Image equiped, bool hotbar,
            ItemDrop.ItemData item, Player.MinorActionData data, float progress)
        {
            if (queued == null || item == null)
                return;

            bool unequip = item.m_equipped;
            float fill = data.m_item == item
                ? (data.m_type == Player.MinorActionData.ActionType.Unequip ? 1f - progress : progress)
                : (unequip ? 1f : 0f);

            if (!Filled.ContainsKey(queued))
            {
                Filled.Add(queued, new Original
                {
                    Type = queued.type,
                    Method = queued.fillMethod,
                    Origin = queued.fillOrigin,
                    Amount = queued.fillAmount
                });
            }

            queued.type = Image.Type.Filled;
            queued.fillMethod = Image.FillMethod.Vertical;
            queued.fillOrigin = (int)Image.OriginVertical.Bottom;
            queued.fillAmount = fill;
            Touched.Add(queued);

            // While unequipping, the equipped background would hide the drain.
            if (unequip && equiped != null)
            {
                if (!HiddenEquip.ContainsKey(equiped))
                    HiddenEquip.Add(equiped, hotbar);
                equiped.enabled = false;
                Touched.Add(equiped);
            }
        }

        private static void RestoreImage(Image image)
        {
            if (Filled.TryGetValue(image, out Original original))
            {
                if (image != null)
                {
                    image.type = original.Type;
                    image.fillMethod = original.Method;
                    image.fillOrigin = original.Origin;
                    image.fillAmount = original.Amount;
                }
                Filled.Remove(image);
            }

            // Hotbar backgrounds are shown by toggling their object, so the image
            // itself is always on. The inventory grid resets its own every update.
            if (HiddenEquip.TryGetValue(image, out bool hotbar))
            {
                if (hotbar && image != null)
                    image.enabled = true;
                HiddenEquip.Remove(image);
            }
        }

        internal static void RestoreAll()
        {
            Release.Clear();
            Release.AddRange(Filled.Keys);
            Release.AddRange(HiddenEquip.Keys);
            foreach (Image image in Release)
                RestoreImage(image);
            Release.Clear();
            Touched.Clear();
            _active = false;
        }
    }
}
