using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WiresNativeCursorSupport
{
    internal static class HoverCursorTracker
    {
        private const float MapDragThresholdPixels = 6f;

        private static readonly MethodInfo
            ClosestPinToCursorMethod =
                AccessTools.DeclaredMethod(
                    typeof(Minimap),
                    "GetClosestPinToCursor");

        private static EventSystem _eventSystem;
        private static PointerEventData _pointer;

        private static readonly List<RaycastResult>
            Results =
                new List<RaycastResult>(32);

        private static int _resolvedFrame = -1;
        private static NativeCursorKind _resolvedKind =
            NativeCursorKind.Arrow;

        private static bool _mapPointerDown;
        private static bool _mapDragging;
        private static Vector2 _mapPressPosition;

        internal static NativeCursorKind DesiredKind
        {
            get
            {
                if (_resolvedFrame == Time.frameCount)
                    return _resolvedKind;

                _resolvedFrame = Time.frameCount;
                _resolvedKind = ResolveCurrentKind();
                return _resolvedKind;
            }
        }

        private static NativeCursorKind ResolveCurrentKind()
        {
            bool overLargeMap =
                IsLargeMapSurface();

            if (UpdateMapDragState(overLargeMap))
                return NativeCursorKind.Move;

            EventSystem eventSystem =
                EventSystem.current;

            if (eventSystem == null)
                return NativeCursorKind.Arrow;

            if (_pointer == null ||
                _eventSystem != eventSystem)
            {
                _eventSystem = eventSystem;
                _pointer =
                    new PointerEventData(eventSystem);
            }

            _pointer.Reset();
            _pointer.position =
                Input.mousePosition;

            Results.Clear();

            eventSystem.RaycastAll(
                _pointer,
                Results);

            if (Results.Count == 0)
                return NativeCursorKind.Arrow;

            GameObject hit =
                Results[0].gameObject;

            if (hit == null ||
                !hit.activeInHierarchy)
                return NativeCursorKind.Arrow;

            TMP_InputField tmp =
                hit.GetComponentInParent<TMP_InputField>();

            if (tmp != null &&
                tmp.IsActive() &&
                tmp.IsInteractable())
            {
                return NativeCursorKind.Text;
            }

            InputField legacy =
                hit.GetComponentInParent<InputField>();

            if (legacy != null &&
                legacy.IsActive() &&
                legacy.IsInteractable())
            {
                return NativeCursorKind.Text;
            }

            // An occupied inventory cell represents an item that can
            // actually be picked up. Empty cells and ordinary clickable
            // desktop-style UI remain Arrow.
            InventoryElement element =
                hit.GetComponentInParent<InventoryElement>();

            if (element != null)
            {
                InventoryGrid grid =
                    element.GetComponentInParent<InventoryGrid>();

                Inventory inventory =
                    grid != null
                        ? grid.GetInventory()
                        : null;

                if (inventory != null &&
                    inventory.GetItemAt(
                        element.Position.x,
                        element.Position.y) != null)
                {
                    return NativeCursorKind.Hand;
                }

                return NativeCursorKind.Arrow;
            }

            // Existing map pins are actual objects with left-click,
            // double-click and right-click behavior, so Hand is useful
            // here without turning the whole map into a link-like area.
            if (overLargeMap &&
                IsMapPinUnderPointer())
            {
                return NativeCursorKind.Hand;
            }

            // Native desktop semantics: ordinary buttons, tabs, toggles,
            // sliders, scrollbars, recipe entries and panels keep Arrow.
            return NativeCursorKind.Arrow;
        }

        private static bool UpdateMapDragState(
            bool pointerInsideLargeMap)
        {
            bool leftHeld =
                Input.GetMouseButton(0);

            if (!leftHeld)
            {
                _mapPointerDown = false;
                _mapDragging = false;
                return false;
            }

            if (!_mapPointerDown)
            {
                // Only a press that began on the large map can become a
                // map drag. Moving onto the map while already holding the
                // mouse button must not change the cursor.
                if (!Input.GetMouseButtonDown(0) ||
                    !pointerInsideLargeMap)
                {
                    return false;
                }

                _mapPointerDown = true;
                _mapDragging = false;
                _mapPressPosition =
                    Input.mousePosition;

                return false;
            }

            if (!_mapDragging)
            {
                Vector2 delta =
                    (Vector2)Input.mousePosition -
                    _mapPressPosition;

                if (delta.sqrMagnitude >=
                    MapDragThresholdPixels *
                    MapDragThresholdPixels)
                {
                    _mapDragging = true;
                }
            }

            // Once a real drag has started, keep the move cursor even if
            // the pointer leaves the map rectangle until the button is
            // released, matching normal captured-drag behavior.
            return _mapDragging;
        }

        private static bool IsLargeMapSurface()
        {
            Minimap map =
                Minimap.instance;

            if (map == null ||
                map.m_largeRoot == null ||
                !map.m_largeRoot.activeInHierarchy ||
                map.m_mapImageLarge == null)
                return false;

            RectTransform rect =
                map.m_mapImageLarge.rectTransform;

            return
                RectTransformUtility.RectangleContainsScreenPoint(
                    rect,
                    Input.mousePosition);
        }

        private static bool IsMapPinUnderPointer()
        {
            Minimap map =
                Minimap.instance;

            if (map == null ||
                ClosestPinToCursorMethod == null)
                return false;

            try
            {
                return
                    ClosestPinToCursorMethod.Invoke(
                        map,
                        null) != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void PruneDestroyed()
        {
        }

        internal static void Clear()
        {
            Results.Clear();
            _pointer = null;
            _eventSystem = null;
            _resolvedFrame = -1;
            _resolvedKind = NativeCursorKind.Arrow;
            _mapPointerDown = false;
            _mapDragging = false;
            _mapPressPosition = Vector2.zero;
        }
    }
}
