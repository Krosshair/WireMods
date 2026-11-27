using HarmonyLib;
using UnityEngine;

namespace WiresShakeItUp
{
    internal static class ShakeEngine
    {
        private static readonly AccessTools.FieldRef<GameCamera, float>
            ShakeIntensity =
                AccessTools.FieldRefAccess<GameCamera, float>(
                    "m_shakeIntensity");

        private static readonly AccessTools.FieldRef<GameCamera, bool>
            CameraShakeEnabled =
                AccessTools.FieldRefAccess<GameCamera, bool>(
                    "m_cameraShakeEnabled");

        private static float _phase;
        private static float _kickRemaining;
        private static float _kickDuration;
        private static float _kickPitch;
        private static float _kickYaw;
        private static float _kickRoll;

        private static float _lastAcceptedCall = -999f;
        private static float _lastImpactTime = -999f;
        private static Vector3 _lastSource;
        private static bool _hasLastSource;

        internal static float CurrentIntensity(GameCamera camera)
        {
            return camera == null ? 0f : ShakeIntensity(camera);
        }
        internal static bool IsCameraShakeEnabled(GameCamera camera)
        {
            return camera != null && CameraShakeEnabled(camera);
        }

        internal static float ShapeRumble(
            GameCamera camera,
            float vanillaAmplitude)
        {
            if (!Enabled(camera))
                return vanillaAmplitude;

            float intensity = Mathf.Max(
                0f,
                CurrentIntensity(camera));

            if (intensity <= 0f)
                return 0f;

            float amplitude;

            if (intensity <= 1f)
            {
                // Keep Valheim's good small-impact curve.
                amplitude =
                    intensity * intensity * intensity;
            }
            else
            {
                // Above 1, extra vanilla strength mostly becomes duration
                // instead of exploding cubically into camera rotation.
                float softening =
                    ModConfig.LongShakeSoftening.Value;

                amplitude =
                    1f +
                    (intensity - 1f) *
                    (1f - softening);

                amplitude = Mathf.Min(
                    amplitude,
                    ModConfig.MaxRumbleDegrees.Value);
            }

            return amplitude *
                   ModConfig.RumbleStrength.Value *
                   ModConfig.OverallStrength.Value;
        }
        internal static void ObserveShake(
            GameCamera camera,
            Vector3 point,
            float range,
            float strength,
            float previousIntensity)
        {
            if (!Enabled(camera) || range <= 0f)
                return;

            float distance =
                Vector3.Distance(
                    point,
                    camera.transform.position);

            if (distance > range)
                return;

            distance = Mathf.Max(1f, distance);

            float effective =
                strength *
                (1f - distance / range);

            if (effective <
                ModConfig.ImpactThreshold.Value)
                return;

            // Vanilla only accepts an incoming shake when it is at least
            // as strong as the currently remaining one.
            if (effective + 0.0001f < previousIntensity)
                return;

            float now = Time.unscaledTime;
            bool freshAfterGap =
                now - _lastAcceptedCall > 0.16f;

            bool sourceChanged =
                !_hasLastSource ||
                (point - _lastSource).sqrMagnitude > 4f;

            bool clearlyStronger =
                effective >
                previousIntensity +
                Mathf.Max(
                    0.08f,
                    previousIntensity * 0.12f);

            bool impactReady =
                now - _lastImpactTime > 0.075f;

            if (impactReady &&
                (freshAfterGap ||
                 sourceChanged ||
                 clearlyStronger))
            {
                StartImpact(
                    camera,
                    point,
                    effective,
                    now);
            }

            _lastAcceptedCall = now;
            _lastSource = point;
            _hasLastSource = true;
        }
        private static void StartImpact(
            GameCamera camera,
            Vector3 point,
            float effective,
            float now)
        {
            float magnitude =
                Mathf.Clamp(
                    0.15f +
                    0.55f *
                    Mathf.Sqrt(Mathf.Max(0f, effective)),
                    0f,
                    1.2f);

            magnitude *=
                ModConfig.ImpactStrength.Value *
                ModConfig.OverallStrength.Value;

            _kickDuration =
                ModConfig.ImpactDuration.Value;
            _kickRemaining = _kickDuration;

            Vector3 toSource =
                point - camera.transform.position;

            Vector3 local = Vector3.zero;
            if (toSource.sqrMagnitude > 0.0001f)
            {
                local =
                    camera.transform.InverseTransformDirection(
                        toSource.normalized);
            }

            float hash =
                Mathf.Sin(
                    point.x * 12.9898f +
                    point.z * 78.233f +
                    now * 3.173f);

            float side =
                hash >= 0f ? 1f : -1f;

            if (ModConfig.DirectionalImpact.Value)
            {
                _kickYaw =
                    magnitude *
                    (-local.x * 0.55f +
                     side * 0.12f);

                _kickPitch =
                    -magnitude *
                    (0.68f -
                     local.y * 0.16f);
            }
            else
            {
                _kickYaw =
                    side * magnitude * 0.32f;
                _kickPitch =
                    -magnitude * 0.68f;
            }
            _kickRoll =
                side *
                magnitude *
                0.18f *
                ModConfig.RollStrength.Value;

            _lastImpactTime = now;
        }

        internal static void AfterCameraShake(
            GameCamera camera,
            float dt)
        {
            if (!Enabled(camera))
                return;

            if (!IsCameraShakeEnabled(camera))
            {
                _kickRemaining = 0f;
                return;
            }

            float intensity =
                Mathf.Max(
                    0f,
                    CurrentIntensity(camera));

            float rumble =
                ShapeRumble(camera, 0f) *
                camera.m_shakeMovement;

            float longness =
                Mathf.InverseLerp(
                    1f,
                    3f,
                    intensity);

            float speed =
                Mathf.Lerp(
                    1.08f,
                    0.72f,
                    longness);

            _phase +=
                dt *
                camera.m_shakeFreq *
                speed;

            float secondary =
                rumble *
                ModConfig.SecondaryMotion.Value;

            float pitch =
                Mathf.Sin(_phase * 1.73f + 0.4f) *
                secondary * 0.42f;

            float yaw =
                Mathf.Cos(_phase * 1.37f + 1.1f) *
                secondary * 0.35f;

            float roll =
                Mathf.Sin(_phase * 0.91f + 2.2f) *
                secondary * 0.08f *
                ModConfig.RollStrength.Value;
            if (_kickRemaining > 0f &&
                _kickDuration > 0f)
            {
                float t =
                    Mathf.Clamp01(
                        _kickRemaining /
                        _kickDuration);

                float envelope =
                    t * t * (3f - 2f * t);

                pitch += _kickPitch * envelope;
                yaw += _kickYaw * envelope;
                roll += _kickRoll * envelope;

                _kickRemaining =
                    Mathf.Max(
                        0f,
                        _kickRemaining - dt);
            }

            if (Mathf.Abs(pitch) > 0.0001f ||
                Mathf.Abs(yaw) > 0.0001f ||
                Mathf.Abs(roll) > 0.0001f)
            {
                camera.transform.rotation =
                    camera.transform.rotation *
                    Quaternion.Euler(
                        pitch,
                        yaw,
                        roll);
            }

            float positionAmount =
                ModConfig.PositionShake.Value *
                ModConfig.OverallStrength.Value *
                Mathf.Clamp01(rumble);

            if (positionAmount > 0.00001f)
            {
                Vector3 offset =
                    camera.transform.right *
                    (Mathf.Sin(_phase * 0.83f + 0.2f) *
                     positionAmount) +
                    camera.transform.up *
                    (Mathf.Cos(_phase * 1.11f + 1.7f) *
                     positionAmount * 0.55f);

                camera.transform.position += offset;
            }
        }
        internal static bool Enabled(GameCamera camera)
        {
            return camera != null &&
                   ModConfig.ShakeEnabled != null &&
                   ModConfig.ShakeEnabled.Value;
        }

        internal static void ResetTransient()
        {
            _phase = 0f;
            _kickRemaining = 0f;
            _kickDuration = 0f;
            _kickPitch = 0f;
            _kickYaw = 0f;
            _kickRoll = 0f;
            _lastAcceptedCall = -999f;
            _lastImpactTime = -999f;
            _lastSource = Vector3.zero;
            _hasLastSource = false;
        }

        internal static void Reset()
        {
            ResetTransient();
        }
    }
}
