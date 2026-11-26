using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

namespace WiresShakeItUp
{
    // Replaces Valheim's autofocus step. Same single raycast, same limits,
    // but the focus glides in log space with real time instead of jumping
    // 20% per frame. Blur creeps in slowly and clears quickly, so it never
    // hides something you just turned to look at.
    [HarmonyPatch(typeof(CameraEffects), "UpdateDOF")]
    internal static class DepthOfFieldPatch
    {
        private static readonly AccessTools.FieldRef<CameraEffects, DepthOfField> DofRef =
            AccessTools.FieldRefAccess<CameraEffects, DepthOfField>("m_dof");

        private static float _lastTime = -1f;

        [HarmonyPrefix]
        private static bool Prefix(CameraEffects __instance)
        {
            if (!ModConfig.DofEnabled.Value)
            {
                _lastTime = -1f;
                return true;
            }

            DepthOfField dof = DofRef(__instance);
            if (dof == null || !dof.enabled || !__instance.m_dofAutoFocus)
            {
                _lastTime = -1f;
                return false;
            }

            Transform t = __instance.transform;
            float target = __instance.m_dofMaxDistance;
            if (Physics.Raycast(t.position, t.forward, out RaycastHit hit,
                    __instance.m_dofMaxDistance, __instance.m_dofRayMask))
                target = hit.distance;

            // Steering a ship already pushes focus out to the ship minimum.
            // Building gets the same, so the base behind the piece stays readable.
            float min = __instance.m_dofMinDistance;
            Player player = Player.m_localPlayer;
            if (player == null || player.GetControlledShip() != null ||
                (ModConfig.DofClearWhileBuilding.Value && player.InPlaceMode()))
                min = Mathf.Max(min, __instance.m_dofMinDistanceShip);
            target = Mathf.Max(target, min, 0.1f);

            float now = Time.unscaledTime;
            float current = Mathf.Max(dof.focalLength, 0.1f);

            // After a load or a long pause, start in focus instead of easing.
            if (_lastTime < 0f || now - _lastTime > 0.5f)
            {
                _lastTime = now;
                dof.focalLength = target;
                return false;
            }

            float dt = now - _lastTime;
            _lastTime = now;

            // Closer target means the background is about to blur: go slow.
            float settle = target < current
                ? ModConfig.DofBlurInTime.Value
                : ModConfig.DofClearTime.Value;
            float k = 1f - Mathf.Exp(-3f * dt / Mathf.Max(settle, 0.01f));

            dof.focalLength = Mathf.Exp(Mathf.Lerp(
                Mathf.Log(current), Mathf.Log(target), k));
            return false;
        }
    }
}
