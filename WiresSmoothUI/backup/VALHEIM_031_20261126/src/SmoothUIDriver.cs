using UnityEngine;

namespace WiresSmoothUI
{
    // Only runs while something is animating. Each system wakes it when an
    // animation starts, and it switches itself off the frame everything settles.
    internal sealed class SmoothUIDriver : MonoBehaviour
    {
        private static SmoothUIDriver _instance;
        private bool _wasEnabled = true;

        internal static void Wake()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("WiresSmoothUIDriver");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<SmoothUIDriver>();
            }

            if (!_instance.enabled)
            {
                PointerMotion.Reset();
                _instance.enabled = true;
            }
        }

        private void LateUpdate()
        {
            PointerMotion.Tick();

            if (!WiresSmoothUIPlugin.Enabled.Value)
            {
                if (_wasEnabled)
                    VisualSystems.RestoreEverything();

                _wasEnabled = false;
                enabled = false;
                return;
            }

            _wasEnabled = true;

            if (DragAnimationSystem.IsDragging)
                DragAnimationSystem.Tick();

            SlotAnimationSystem.Tick();
            ItemAnimationSystem.Tick();
            BuildMenuAnimationSystem.Tick();
            ButtonAnimationSystem.Tick();
            TooltipAnimationSystem.Tick();
            SmoothScrollSystem.Tick();
            EquipFillSystem.Tick();
            UiMotion.Tick();

            bool busy = DragAnimationSystem.IsDragging
                || SlotAnimationSystem.Busy
                || ItemAnimationSystem.Busy
                || BuildMenuAnimationSystem.Busy
                || ButtonAnimationSystem.Busy
                || TooltipAnimationSystem.Busy
                || SmoothScrollSystem.Busy
                || EquipFillSystem.Busy
                || UiMotion.Busy;

            if (!busy)
                enabled = false;
        }

        private void OnDestroy()
        {
            VisualSystems.RestoreEverything();
        }
    }

    internal static class PointerMotion
    {
        private static Vector2 _last;
        private static bool _initialized;

        internal static Vector2 Position { get; private set; }
        internal static Vector2 Delta { get; private set; }

        internal static void Reset()
        {
            _initialized = false;
            Delta = Vector2.zero;
        }

        internal static void Tick()
        {
            if (ZInput.instance == null)
            {
                Delta = Vector2.zero;
                return;
            }

            Position = ZInput.pointerPosition;

            if (!_initialized)
            {
                _last = Position;
                _initialized = true;
                Delta = Vector2.zero;
                return;
            }

            Delta = Vector2.ClampMagnitude(Position - _last, 40f);
            _last = Position;
        }
    }

    internal static class VisualSystems
    {
        internal static void RestoreEverything()
        {
            SlotAnimationSystem.RestoreAll();
            ItemAnimationSystem.RestoreAll();
            DragAnimationSystem.Restore();
            BuildMenuAnimationSystem.RestoreAll();
            ButtonAnimationSystem.RestoreAll();
            TooltipAnimationSystem.Restore();
            EquipFillSystem.RestoreAll();
            UiMotion.RestoreAll();
        }
    }
}

