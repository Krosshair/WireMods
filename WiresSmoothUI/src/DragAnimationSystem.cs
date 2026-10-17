using HarmonyLib;
using UnityEngine;

namespace WiresSmoothUI
{
    internal static class DragAnimationSystem
    {
        private static readonly AccessTools.FieldRef<InventoryGui, GameObject> DragGo =
            AccessTools.FieldRefAccess<InventoryGui, GameObject>("m_dragGo");

        private static GameObject _current;
        private static Vector3 _smoothPosition;
        private static Vector3 _baseScale = Vector3.one;
        private static Quaternion _baseRotation = Quaternion.identity;

        private static Vector2 _physicsOffset;
        private static Vector2 _physicsVelocity;
        private static float _rotation;
        private static float _rotationVelocity;
        private static float _spawnTimer;
        private static bool _applied;

        internal static bool IsDragging => _current != null;

        internal static void NotifySetup(InventoryGui gui)
        {
            if (gui == null)
                return;

            GameObject drag;
            try
            {
                drag = DragGo(gui);
            }
            catch
            {
                return;
            }

            if (drag == null)
            {
                Restore();
                _current = null;
                return;
            }

            if (_current == drag)
                return;

            Restore();
            _current = drag;
            _smoothPosition = ZInput.instance != null && ZInput.IsMouseActive()
                ? (Vector3)ZInput.pointerPosition
                : drag.transform.position;
            if (ZInput.instance != null && ZInput.IsMouseActive())
                drag.transform.position = _smoothPosition;

            _baseScale = drag.transform.localScale;
            _baseRotation = drag.transform.localRotation;
            _physicsOffset = Vector2.zero;
            _physicsVelocity = Vector2.zero;
            _rotation = 0f;
            _rotationVelocity = 0f;
            _spawnTimer = 0f;
        }

        internal static void Tick()
        {
            GameObject drag = _current;
            if (drag == null)
                return;

            if (_applied)
            {
                Vector3 expectedScale = _baseScale * CurrentScale();
                Quaternion expectedRotation =
                    _baseRotation * Quaternion.Euler(0f, 0f, _rotation);

                if ((drag.transform.localScale - expectedScale).sqrMagnitude > 0.0009f)
                    _baseScale = drag.transform.localScale;

                if (Quaternion.Angle(drag.transform.localRotation, expectedRotation) > 0.05f)
                    _baseRotation = drag.transform.localRotation;
            }

            if (!WiresSmoothUIPlugin.ItemAnimations.Value)
            {
                Restore();
                return;
            }

            float dt = WiresSmoothUIPlugin.Dt;
            _spawnTimer += dt;

            if (ZInput.IsMouseActive())
            {
                Vector3 target = PointerMotion.Position;
                float follow = Mathf.Max(1f, WiresSmoothUIPlugin.DragFollow.Value);
                float followFactor = 1f - Mathf.Exp(-follow * dt);
                _smoothPosition = Vector3.Lerp(_smoothPosition, target, followFactor);

                Vector2 delta = Vector2.ClampMagnitude(PointerMotion.Delta, 24f);
                Vector2 offsetTarget = WiresSmoothUIPlugin.DragPhysics.Value
                    ? -delta * 0.17f
                    : Vector2.zero;
                offsetTarget = Vector2.ClampMagnitude(offsetTarget, 4.25f);

                float rotationTarget = WiresSmoothUIPlugin.DragPhysics.Value
                    ? -Mathf.Clamp(delta.x / 18f, -1f, 1f)
                        * Mathf.Max(0f, WiresSmoothUIPlugin.DragRotation.Value)
                    : 0f;

                float spring = Mathf.Clamp(follow * 0.6f, 7f, 22f);
                _physicsOffset.x = Spring(
                    _physicsOffset.x,
                    ref _physicsVelocity.x,
                    offsetTarget.x,
                    spring,
                    0.68f,
                    dt);
                _physicsOffset.y = Spring(
                    _physicsOffset.y,
                    ref _physicsVelocity.y,
                    offsetTarget.y,
                    spring,
                    0.68f,
                    dt);
                _rotation = Spring(
                    _rotation,
                    ref _rotationVelocity,
                    rotationTarget,
                    spring * 0.8f,
                    0.62f,
                    dt);

                drag.transform.position = _smoothPosition
                    + new Vector3(_physicsOffset.x, _physicsOffset.y, 0f);
            }
            else
            {
                _smoothPosition = drag.transform.position;
                _physicsOffset = Vector2.zero;
                _physicsVelocity = Vector2.zero;
                _rotation = 0f;
                _rotationVelocity = 0f;
            }

            drag.transform.localScale = _baseScale * CurrentScale();
            drag.transform.localRotation =
                _baseRotation * Quaternion.Euler(0f, 0f, _rotation);
            _applied = true;
        }

        private static float Spring(
            float current,
            ref float velocity,
            float target,
            float frequency,
            float damping,
            float dt)
        {
            float omega = Mathf.Max(1f, frequency);
            float acceleration =
                (target - current) * omega * omega
                - 2f * damping * omega * velocity;
            velocity += acceleration * dt;
            return current + velocity * dt;
        }

        private static float CurrentScale()
        {
            float duration = Mathf.Max(0.01f, WiresSmoothUIPlugin.ItemAppearDuration.Value);
            if (_spawnTimer >= duration)
                return 1f;

            float t = Mathf.Clamp01(_spawnTimer / duration);
            float basePop = WiresSmoothUIPlugin.PopScale(t, 0.88f);
            float lift = Mathf.Sin(t * Mathf.PI) * 0.025f;
            return basePop + lift;
        }

        internal static void Restore()
        {
            if (_current != null && _applied)
            {
                _current.transform.localScale = _baseScale;
                _current.transform.localRotation = _baseRotation;
            }

            _applied = false;
            _physicsOffset = Vector2.zero;
            _physicsVelocity = Vector2.zero;
            _rotation = 0f;
            _rotationVelocity = 0f;
        }
    }
}

namespace WiresSmoothUI
{
    [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
    internal static class SmoothUIDragSetupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            DragAnimationSystem.NotifySetup(__instance);
        }
    }
}
