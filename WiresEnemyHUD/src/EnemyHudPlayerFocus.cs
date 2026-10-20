using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _playerNameplateAimFade;

        private static readonly RaycastHit[] CrosshairHits = new RaycastHit[64];
        private static GameCamera _crosshairCameraOwner;
        private static Camera _crosshairCamera;

        private static Player GetCrosshairPlayer(Player localPlayer)
        {
            if (localPlayer == null || GameCamera.instance == null)
                return null;

            if (_crosshairCameraOwner != GameCamera.instance)
            {
                _crosshairCameraOwner = GameCamera.instance;
                _crosshairCamera = GameCamera.instance.GetComponent<Camera>();
            }
            Camera camera = _crosshairCamera;
            if (camera == null)
                return null;

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // Cast from the actual screen center, rather than using distance
            // between player HUDs and the camera. Ignore our own collider
            // in third-person view and stop on the first other obstruction.
            // Into a reused buffer, nearest first by picking, no sorting or
            // new arrays each frame.
            int count = Physics.RaycastNonAlloc(
                ray, CrosshairHits, 2000f, ~0, QueryTriggerInteraction.Ignore);

            for (int n = 0; n < count; ++n)
            {
                int best = -1;
                for (int i = 0; i < count; ++i)
                    if (CrosshairHits[i].collider != null &&
                        (best < 0 || CrosshairHits[i].distance < CrosshairHits[best].distance))
                        best = i;
                if (best < 0) break;
                Collider collider = CrosshairHits[best].collider;
                CrosshairHits[best] = default(RaycastHit);

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
            if (state == null || state.Kind == HudKind.Boss ||
                state.Gui == null)
                return;

            // Players: your crosshair on them. Creatures: the same "looked
            // at" check as their Dynamic HP numbers. Each with its delay.
            // Own debug nameplate is never dimmed.
            bool player = state.Kind == HudKind.Player;
            bool isSelf = state.Character == localPlayer;
            bool enabled = !isSelf && !state.IsDebug &&
                (player ? _playerNameplateAimFade.Value : _enemyAimFade.Value);

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
                // Turned off: undo it once (alpha, trails, our CanvasGroup);
                // after that this returns at the top.
                RestorePlayerNameplateOpacity(state);
                return;
            }

            bool focused = player ? AimedAtRecently(state)
                : LookedAtRecently(state.Character, _dynamicHpDelay.Value);
            float target = focused
                ? state.PlayerOriginalAlpha
                : state.PlayerOriginalAlpha * 0.1f;

            // Instant: 10% until aimed at, full the moment you are.
            if (group.alpha != target) group.alpha = target;
            SetTrailsHidden(state, !focused);
        }

        // A bar's fill is drawn over its damage trail, which runs the full
        // width underneath. See-through, the trail would show through the
        // fill (yellow under red turns orange), so while a nameplate is
        // faded each trail is clipped to start where its fill ends: the
        // trail still shows, nothing overlaps. The clip only moves when the
        // fill's length changes.
        private static void SetTrailsHidden(EnemyUiState state, bool clipped)
        {
            if (state.Trails == null)
            {
                if (!clipped) return;
                var list = new List<TrailClip>();
                foreach (GuiBar bar in state.Gui.GetComponentsInChildren<GuiBar>(true))
                {
                    if (bar.name.IndexOf("slow", StringComparison.OrdinalIgnoreCase) < 0 || bar.transform.parent == null)
                        continue;
                    bool bubble = state.Bubble != null && state.Bubble.Root != null &&
                        bar.transform.IsChildOf(state.Bubble.Root.transform);
                    bool health = state.HealthRect != null && bar.transform.parent == state.HealthRect;
                    if (!bubble && !health) continue;
                    RectMask2D mask = bar.GetComponent<RectMask2D>();
                    if (mask == null) mask = bar.gameObject.AddComponent<RectMask2D>();
                    mask.enabled = false;
                    list.Add(new TrailClip { Mask = mask, Bubble = bubble });
                }
                state.Trails = list.ToArray();
            }
            if (state.TrailsHidden != clipped)
            {
                state.TrailsHidden = clipped;
                foreach (TrailClip t in state.Trails)
                {
                    if (t.Mask == null) continue;
                    t.Mask.enabled = clipped;
                    t.Shown = -1f;
                }
            }
            if (!clipped) return;
            // The fill's share, from the values the bar shows: two numbers
            // compared; the clip only moves when one of them changed.
            float hp = state.Character != null ? state.Character.GetHealth() : 0f;
            float max = state.Character != null ? state.Character.GetMaxHealth() : 1f;
            float healthShare = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            float bubbleShare = state.BubbleMax > 0f
                ? Mathf.Clamp01((state.BubbleMax - state.BubbleDamage) / state.BubbleMax) : 0f;
            foreach (TrailClip t in state.Trails)
            {
                if (t.Mask == null) continue;
                float share = t.Bubble ? bubbleShare : healthShare;
                if (share == t.Shown && t.Layout == _layoutVersionStatic) continue;
                t.Shown = share;
                t.Layout = _layoutVersionStatic;
                t.Mask.padding = new Vector4(t.Mask.rectTransform.rect.width * share, 0f, 0f, 0f);
            }
        }

        private static void RestorePlayerNameplateOpacity(EnemyUiState state)
        {
            if (state == null || state.PlayerOpacityGroup == null)
                return;

            state.PlayerOpacityGroup.alpha = state.PlayerOriginalAlpha;
            SetTrailsHidden(state, false);
            if (state.PlayerFadeGroupAdded)
                UnityEngine.Object.Destroy(state.PlayerOpacityGroup);
            state.PlayerOpacityGroup = null;
            state.PlayerFadeGroupAdded = false;
        }
    }
}
