using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WiresStackify
{
    // Holding a stack, right click outside the inventory: one of it onto the ground. The game's own
    // "click outside to drop" is a button behind the inventory (InventoryGui.m_dropButton) that drops
    // the whole held stack on a left click; this listens on that same button for the right one.
    internal sealed class DropOutside : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) MouseControls.DropOneHeld();
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static class Add
        {
            private static void Postfix(InventoryGui __instance)
            {
                GameObject button = __instance.m_dropButton != null ? __instance.m_dropButton.gameObject : null;
                if (button != null && button.GetComponent<DropOutside>() == null) button.AddComponent<DropOutside>();
            }
        }
    }
}
