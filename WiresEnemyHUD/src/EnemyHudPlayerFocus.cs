using System;
using BepInEx.Configuration;
using UnityEngine;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _playerNameplateAimFade;

        private static Player GetCrosshairPlayer(Player localPlayer)
        {
            if (localPlayer == null || GameCamera.instance == null)
                return null;

            Camera camera = GameCamera.instance.GetComponent<Camera>();
            if (camera == null)
                return null;

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // Cast from the actual screen center, rather than using distance
            // between player HUDs and the camera. Ignore our own collider
            // in third-person view and stop on the first other obstruction.
            RaycastHit[] hits = Physics.RaycastAll(
                ray, 2000f, ~0, QueryTriggerInteraction.Ignore);

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; ++i)
            {
                Collider collider = hits[i].collider;
                if (collider == null)
                    continue;

                Player player = collider.GetComponentInParent<Player>();
                if (player == localPlayer)
                    continue;

                // Other objects block a direct crosshair hit. Never use
                // proximity to override this rule.
                return player;
            }

            return null;
        }

        private void UpdatePlayerNameplateOpacity(
            EnemyUiState state, Player localPlayer,
            Player directlyTargeted, float dt)
        {
            if (state == null || state.Kind != HudKind.Player ||
                state.Gui == null)
                return;

            // Own debug nameplate is never dimmed.
            bool isSelf = state.Character == localPlayer;
            bool enabled = !isSelf &&
                _playerNameplateAimFade != null &&
                _playerNameplateAimFade.Value;

            if (!enabled && state.PlayerOpacityGroup == null)
                return;

            if (state.PlayerOpacityGroup == null)
            {
                CanvasGroup existing = state.Gui.GetComponent<CanvasGroup>();
                state.PlayerOpacityGroup = existing != null
                    ? existing
                    : state.Gui.AddComponent<CanvasGroup>();
                state.PlayerFadeGroupAdded = existing == null;
                state.PlayerOriginalAlpha = existing != null
                    ? existing.alpha : 1f;
            }

            CanvasGroup group = state.PlayerOpacityGroup;
            if (group == null)
                return;

            if (!enabled)
            {
                group.alpha = state.PlayerOriginalAlpha;
                return;
            }

            float target = state.Character == directlyTargeted
                ? state.PlayerOriginalAlpha
                : state.PlayerOriginalAlpha * 0.1f;

            // Instant: 10% until aimed at, full the moment you are.
            if (group.alpha != target) group.alpha = target;
        }

        private static void RestorePlayerNameplateOpacity(EnemyUiState state)
        {
            if (state == null || state.PlayerOpacityGroup == null)
                return;

            state.PlayerOpacityGroup.alpha = state.PlayerOriginalAlpha;
            if (state.PlayerFadeGroupAdded)
                UnityEngine.Object.Destroy(state.PlayerOpacityGroup);
            state.PlayerOpacityGroup = null;
            state.PlayerFadeGroupAdded = false;
        }
    }
}
