using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace WiresShakeItUp
{
    internal static class ZoomEngine
    {
        internal struct Capture
        {
            internal bool Active;
            internal float Target;
        }

        private sealed class State
        {
            internal bool Initialized;
            internal float Visual;
        }

        private static readonly AccessTools.FieldRef<GameCamera, float>
            Distance =
                AccessTools.FieldRefAccess<GameCamera, float>("m_distance");

        private static ConditionalWeakTable<GameCamera, State> States =
            new ConditionalWeakTable<GameCamera, State>();

        internal static Capture Begin(GameCamera camera, float dt)
        {
            Capture capture = new Capture();

            if (camera == null ||
                ModConfig.SmoothZoomEnabled == null ||
                !ModConfig.SmoothZoomEnabled.Value ||
                Player.m_localPlayer == null ||
                Player.m_localPlayer.InIntro())
            {
                return capture;
            }

            float target = Distance(camera);
            State state = States.GetOrCreateValue(camera);

            if (!state.Initialized)
            {
                state.Initialized = true;
                state.Visual = target;
            }

            state.Visual =
                EaseToward(
                    state.Visual,
                    target,
                    dt,
                    ModConfig.ZoomResponse.Value);

            capture.Active = true;
            capture.Target = target;

            // Only the visible distance is eased. The real target is restored
            // immediately afterwards so wheel/controller input remains fully responsive.
            Distance(camera) = state.Visual;

            return capture;
        }

        internal static void End(GameCamera camera, Capture capture)
        {
            if (capture.Active && camera != null)
                Distance(camera) = capture.Target;
        }

        internal static float EaseToward(
            float current,
            float target,
            float dt,
            float response)
        {
            float linear =
                Mathf.Clamp01(
                    Mathf.Max(0f, dt) *
                    Mathf.Max(0.01f, response));

            float eased =
                Mathf.Sin(
                    linear *
                    Mathf.PI *
                    0.5f);

            float next =
                Mathf.Lerp(
                    current,
                    target,
                    eased);

            if (Mathf.Abs(next - target) < 0.002f)
                next = target;

            return next;
        }

        internal static void Reset()
        {
            States =
                new ConditionalWeakTable<GameCamera, State>();
        }
    }

    internal static class CollisionAutoZoomEngine
    {
        internal struct Capture
        {
            internal Vector3 OffsetEye;
            internal Vector3 RequestedEnd;
        }

        private sealed class State
        {
            internal bool Active;
            internal float VisualDistance;
            internal int LastFrame = -1000;
        }

        private const float CollisionEpsilon = 0.015f;

        private static ConditionalWeakTable<GameCamera, State> States =
            new ConditionalWeakTable<GameCamera, State>();

        internal static Capture Begin(
            Vector3 offsetEye,
            Vector3 requestedEnd)
        {
            return new Capture
            {
                OffsetEye = offsetEye,
                RequestedEnd = requestedEnd
            };
        }

        internal static void End(
            GameCamera camera,
            ref Vector3 end,
            Capture capture)
        {
            if (camera == null ||
                ModConfig.CollisionAutoZoomEnabled == null ||
                !ModConfig.CollisionAutoZoomEnabled.Value)
                return;

            Vector3 requestedRay =
                capture.RequestedEnd -
                capture.OffsetEye;

            float requestedDistance =
                requestedRay.magnitude;

            if (requestedDistance <= 0.001f)
                return;

            float vanillaDistance =
                Mathf.Clamp(
                    Vector3.Distance(
                        capture.OffsetEye,
                        end),
                    0f,
                    requestedDistance);

            bool blocked =
                vanillaDistance <
                requestedDistance -
                CollisionEpsilon;

            State state =
                States.GetOrCreateValue(camera);

            if (Time.frameCount -
                state.LastFrame > 2)
            {
                state.Active = false;
                state.VisualDistance =
                    vanillaDistance;
            }

            state.LastFrame =
                Time.frameCount;

            if (!state.Active)
            {
                if (!blocked)
                    return;

                state.Active = true;
                state.VisualDistance =
                    vanillaDistance;

                // Collision closing in stays immediate and entirely vanilla-safe.
                return;
            }

            if (vanillaDistance <
                state.VisualDistance)
            {
                state.VisualDistance =
                    vanillaDistance;
            }
            else
            {
                // As space opens again, smoothly ease the collision-forced
                // camera distance back toward vanilla's available distance.
                state.VisualDistance =
                    ZoomEngine.EaseToward(
                        state.VisualDistance,
                        vanillaDistance,
                        Time.unscaledDeltaTime,
                        ModConfig.CollisionAutoZoomResponse.Value);
            }

            if (!blocked &&
                Mathf.Abs(
                    state.VisualDistance -
                    requestedDistance) <
                0.002f)
            {
                state.VisualDistance =
                    requestedDistance;
                state.Active = false;
                return;
            }

            end =
                capture.OffsetEye +
                requestedRay /
                requestedDistance *
                Mathf.Min(
                    state.VisualDistance,
                    vanillaDistance);
        }

        internal static void Reset()
        {
            States =
                new ConditionalWeakTable<GameCamera, State>();
        }
    }

    [HarmonyPatch]
    internal static class CameraPositionPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(
                typeof(GameCamera),
                "GetCameraPosition",
                new[]
                {
                    typeof(float),
                    typeof(Vector3).MakeByRefType(),
                    typeof(Quaternion).MakeByRefType()
                });
        }

        [HarmonyPrefix]
        private static void Prefix(
            GameCamera __instance,
            float dt,
            out ZoomEngine.Capture __state)
        {
            __state =
                ZoomEngine.Begin(
                    __instance,
                    dt);
        }

        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            ZoomEngine.Capture __state)
        {
            ZoomEngine.End(
                __instance,
                __state);
        }
    }

    [HarmonyPatch(typeof(GameCamera), "CollideRay2")]
    internal static class CollisionAutoZoomPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Vector3 offsetedEyePos,
            Vector3 end,
            out CollisionAutoZoomEngine.Capture __state)
        {
            __state =
                CollisionAutoZoomEngine.Begin(
                    offsetedEyePos,
                    end);
        }

        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            ref Vector3 end,
            CollisionAutoZoomEngine.Capture __state)
        {
            CollisionAutoZoomEngine.End(
                __instance,
                ref end,
                __state);
        }
    }
}
