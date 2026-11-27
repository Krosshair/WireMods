using HarmonyLib;
using UnityEngine;

namespace WiresShakeItUp
{
    // Camera that reacts to how you move: FOV for speed, falling and
    // exhaustion, a dip when you land, and a camera that trails and leads
    // your movement a little. Rotation is never touched, so aiming stays 1:1.
    internal static class DynamicCamera
    {
        // Valheim: fall damage starts at a 4 m drop and is full at 20 m, landing
        // effects start at 0.8 m (Character.UpdateGroundContact). Under its gravity
        // of 20 m/s2 (DynamicsManager) that is a free-fall speed of sqrt(2 * 20 * h).
        private const float DamageSpeedStart = 12.65f; // 4 m
        private const float DamageSpeedFull = 28.28f;  // 20 m
        private const float LandSpeedStart = 5.66f;    // 0.8 m

        // My picks, scaled by Strength.
        private const float SprintFov = 5f;   // degrees at run speed, up to twice that faster (ships)
        private const float FallFov = 8f;     // degrees at full-damage fall speed
        private const float TiredFov = 3f;    // degrees narrower when exhausted or overweight
        private const float TiredBelow = 0.25f; // stamina share where tiredness starts
        private const float FovTime = 0.35f;  // seconds for FOV changes to settle
        private const float DipMax = 0.25f;   // metres the camera drops landing at full-damage speed
        private const float DipOmega = 12f;   // spring speed: back in about 0.4 s
        private const float FollowLag = 0.15f; // seconds the camera trails behind
        private const float FollowLead = 0.06f; // seconds of movement it leads by
        private const float FollowMax = 0.5f; // metres, at most

        private static readonly AccessTools.FieldRef<GameCamera, Camera> CameraRef =
            AccessTools.FieldRefAccess<GameCamera, Camera>("m_camera");
        private static readonly AccessTools.FieldRef<GameCamera, bool> FreeFlyRef =
            AccessTools.FieldRefAccess<GameCamera, bool>("m_freeFly");

        private static float _fov;
        private static float _dip;
        private static float _dipVel;
        private static float _lastFall;
        private static bool _wasGrounded = true;
        private static Vector3 _smoothVel;

        internal static void Apply(GameCamera gc)
        {
            if (FreeFlyRef(gc)) return;
            Camera camera = CameraRef(gc);
            if (camera == null) return;

            float dt = Time.deltaTime;
            float strength = ModConfig.DynStrength.Value;
            Player p = Player.m_localPlayer;
            bool active = ModConfig.DynEnabled.Value && p != null && !p.IsDead() &&
                          !(p.IsAttached() && p.GetAttachCameraPoint() != null);

            float target = 0f;
            Vector3 vel = Vector3.zero;
            if (active)
            {
                Ship ship = p.GetControlledShip();
                Vector3 full = p.GetVelocity();
                Vector3 v = new Vector3(full.x, 0f, full.z);
                float speed = ship != null ? Mathf.Abs(ship.GetSpeed()) : v.magnitude;
                if (ModConfig.DynSpeedFov.Value && p.m_runSpeed > p.m_speed)
                    target += Mathf.Clamp((speed - p.m_speed) / (p.m_runSpeed - p.m_speed), 0f, 2f) * SprintFov;

                bool grounded = p.IsOnGround();
                // Downward speed, so a feather cape's slow fall stays calm.
                float fall = grounded ? 0f : Mathf.Max(0f, -full.y);
                if (ModConfig.DynFallFov.Value)
                    target += Mathf.Clamp01((fall - DamageSpeedStart) / (DamageSpeedFull - DamageSpeedStart)) * FallFov;

                if (ModConfig.DynTiredFov.Value)
                {
                    float tired = Mathf.Clamp01((TiredBelow - p.GetStaminaPercentage()) / TiredBelow);
                    if (p.IsEncumbered()) tired = 1f;
                    target -= tired * TiredFov;
                }

                // Landing: the fall speed is read the frame before touchdown.
                if (grounded && !_wasGrounded && ModConfig.DynLanding.Value && _lastFall > LandSpeedStart)
                {
                    float amount = Mathf.Clamp01((_lastFall - LandSpeedStart) / (DamageSpeedFull - LandSpeedStart));
                    // A critically damped spring started at speed v peaks at v / (omega * e).
                    _dipVel -= amount * DipMax * strength * DipOmega * 2.7182818f;
                }
                _lastFall = fall;
                _wasGrounded = grounded;

                if (ModConfig.DynFollow.Value && ship == null && !p.IsAttached())
                    vel = full;
            }

            _fov = Mathf.Lerp(_fov, target * strength, 1f - Mathf.Exp(-3f * dt / FovTime));
            float fov = gc.m_fov + _fov + (WideLens.On ? ModConfig.WideLensFov.Value : 0f);
            camera.fieldOfView = fov;
            if (gc.m_skyCamera != null) gc.m_skyCamera.fieldOfView = fov;

            // Critically damped spring back to rest.
            float acc = -DipOmega * DipOmega * _dip - 2f * DipOmega * _dipVel;
            _dipVel += acc * dt;
            _dip += _dipVel * dt;

            // Follow: the camera position lags like a first-order follower, plus a lead.
            _smoothVel = Vector3.Lerp(_smoothVel, vel, 1f - Mathf.Exp(-dt / FollowLag));
            Vector3 follow = (_smoothVel * FollowLead + (_smoothVel - vel) * FollowLag) * strength;
            follow = Vector3.ClampMagnitude(follow, FollowMax);

            Vector3 offset = follow + Vector3.up * _dip;
            float length = offset.magnitude;
            if (length < 0.001f) return;
            // Don't push the camera into walls Valheim just kept it out of.
            Vector3 pos = gc.transform.position;
            Vector3 dir = offset / length;
            if (Physics.SphereCast(pos, gc.m_nearClipPlaneMax * 0.5f, dir, out RaycastHit hit, length, gc.m_blockCameraMask))
                length = Mathf.Max(0f, hit.distance - 0.05f);
            gc.transform.position = pos + dir * length;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class DynamicCameraPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameCamera __instance)
        {
            DynamicCamera.Apply(__instance);
        }
    }
}
