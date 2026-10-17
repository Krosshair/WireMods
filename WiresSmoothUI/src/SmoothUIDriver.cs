using UnityEngine;

namespace WiresSmoothUI
{
    internal sealed class SmoothUIDriver : MonoBehaviour
    {        private bool _wasEnabled;

        private void LateUpdate()
        {
            PointerMotion.Tick();

            if (!WiresSmoothUIPlugin.Enabled.Value)
            {
                if (_wasEnabled)
                    VisualSystems.RestoreEverything();

                _wasEnabled = false;
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
            MicroMotionSystem.Tick();
            SmoothScrollSystem.Tick();
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
            MicroMotionSystem.RestoreAll();
        }
    }
}

