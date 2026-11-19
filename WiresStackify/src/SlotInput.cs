using UnityEngine;
using UnityEngine.EventSystems;

namespace WiresStackify
{
    // On every inventory slot: Unity's UI tells it when the pointer comes on or leaves and when the
    // wheel turns over it, so nothing has to ask every frame.
    internal sealed class SlotInput : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
    {
        private InventoryElement _element;

        private void Awake() => _element = GetComponent<InventoryElement>();

        public void OnPointerEnter(PointerEventData e)
        {
            MouseControls.Entered(_element);
            KeyWatch.Over(_element);
        }

        public void OnPointerExit(PointerEventData e) => KeyWatch.Left(_element);

        private void OnDisable() => KeyWatch.Left(_element);

        public void OnScroll(PointerEventData e)
        {
            if (MouseControls.Wheel(_element, e.scrollDelta.y)) return;
            // nothing to move: the wheel scrolls whatever is around the slot, as without the mod
            if (transform.parent != null) ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, e, ExecuteEvents.scrollHandler);
        }
    }

    // The hotbar numbers over a slot. The game reads no keys while the inventory is open, so this
    // looks at them itself, only while the pointer is over a slot; otherwise it's switched off.
    internal sealed class KeyWatch : MonoBehaviour
    {
        private static KeyWatch s_watch;
        private InventoryElement _element;

        internal static void Over(InventoryElement element)
        {
            if (element == null) return;
            if (s_watch == null)
            {
                var go = new GameObject("Wire's Stackify keys") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                s_watch = go.AddComponent<KeyWatch>();
            }
            s_watch._element = element;
            s_watch.enabled = true;
        }

        internal static void Left(InventoryElement element)
        {
            if (s_watch == null || s_watch._element != element) return;
            s_watch._element = null;
            s_watch.enabled = false;
        }

        private void Update()
        {
            for (int k = 0; k < 8; k++)
                if (ZInput.GetKeyDown(KeyCode.Alpha1 + k))
                {
                    MouseControls.Number(_element, k);
                    return;
                }
        }
    }
}
