using System;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    // The stagger bar and its flash.
    public sealed partial class EnemyHudReworkPlugin
    {
        private void CreateStaggerBar(EnemyUiState state)
        {
            if (state == null || state.Gui == null || state.HealthRect == null)
                return;

            GameObject staggerRoot = Instantiate(state.HealthRect.gameObject, state.HealthRect.parent, false);

            staggerRoot.name = "Stagger";
            state.StaggerRoot = staggerRoot;
            state.StaggerRect = staggerRoot.transform as RectTransform;

            if (state.StaggerRect != null)
                state.StaggerRect.localScale = state.HealthOriginalScale;

            Transform fastTransform = staggerRoot.transform.Find("health_fast");

            Transform slowTransform = staggerRoot.transform.Find("health_slow");

            Transform friendlyTransform = staggerRoot.transform.Find("health_fast_friendly");

            if (friendlyTransform != null)
                friendlyTransform.gameObject.SetActive(false);

            if (fastTransform != null)
            {
                state.StaggerFast = fastTransform.GetComponent<GuiBar>();

                if (state.StaggerFast != null)
                {
                    state.StaggerFast.SetColor(new Color32(0xE7, 0x5D, 0x16, 255));

                    CopyPlayerStaggerBarBehavior(state.StaggerFast);

                    state.StaggerFast.SetValue(0f);
                }
            }

            if (slowTransform != null)
            {
                state.StaggerBackground = slowTransform.GetComponent<GuiBar>();

                if (state.StaggerBackground != null)
                {
                    state.StaggerBackground.gameObject.SetActive(false);
                }
            }

            foreach (Graphic graphic in staggerRoot.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                if (state.StaggerFast != null && graphic.transform.IsChildOf(state.StaggerFast.transform))
                    continue;

                if (state.StaggerBackground != null &&
                    graphic.transform.IsChildOf(state.StaggerBackground.transform))
                    continue;

                if (!(graphic is Image))
                    graphic.gameObject.SetActive(false);
            }

            staggerRoot.SetActive(false);
        }

        private static void CopyPlayerStaggerBarBehavior(GuiBar target)
        {
            if (target == null)
                return;

            Hud hud = Hud.instance;
            GuiBar playerBar = hud != null ? hud.m_staggerProgress : null;

            if (playerBar == null)
            {
                target.m_smoothDrain = false;
                target.m_smoothFill = false;
                target.m_changeDelay = 0f;
                return;
            }

            target.m_smoothDrain = playerBar.m_smoothDrain;

            target.m_smoothFill = playerBar.m_smoothFill;

            target.m_smoothSpeed = playerBar.m_smoothSpeed;

            target.m_changeDelay = playerBar.m_changeDelay;
        }

        private void ApplyStaggerPosition(EnemyUiState state)
        {
            if (state == null || state.StaggerRect == null)
                return;

            Vector2 pos =
                state.HealthOriginalPosition +
                Vector2.down * (_staggerVerticalOffset.Value + GetExtraHealthHeight(state));
            if (state.StaggerRect.anchoredPosition != pos)
                state.StaggerRect.anchoredPosition = pos;
        }

        private void UpdateStaggerBar(EnemyUiState state, bool isEnemy, float dt)
        {
            if (state == null || state.Character == null)
                return;

            bool eligible = state.Kind == HudKind.Player || (state.Kind == HudKind.Enemy && isEnemy);

            if (!_staggerBarEnabled.Value || state.StaggerRoot == null || !eligible)
            {
                SetStaggerLayoutActive(state, false);
                return;
            }

            bool staggerable = state.Character.m_staggerDamageFactor > 0f;

            // An enemy that can't be staggered never gets a stagger bar.
            if (!staggerable)
            {
                SetStaggerLayoutActive(state, false);
                return;
            }

            float staggerPercentage = Mathf.Clamp01(GetDisplayStaggerPercentage(state.Character));

            // Exact Hud.UpdateStagger visibility rule:
            // >0 resets timer, at 0 it remains visible until timer reaches 1 second.
            if (staggerPercentage > 0f)
                state.HideTimer = 0f;
            else
                state.HideTimer += dt;

            bool isStaggering = state.Character.IsStaggering();
            bool recovering = state.Kind == HudKind.Enemy && _staggerRecoveryEnabled != null &&
                _staggerRecoveryEnabled.Value && isStaggering;

            bool visible =
                staggerPercentage > 0f || state.HideTimer < PlayerLikeHideDelay || recovering ||
                state.PredictShowStagger;

            SetStaggerLayoutActive(state, visible);

            if (state.StaggerFast != null)
                state.StaggerFast.SetValue(recovering ? 1f : staggerPercentage);

            // The game keeps the meter full during a stagger and lets it
            // drain; any hit then tops it back up to full (bows always do).
            // That's no new stagger: a fill only counts while not staggering.
            bool reachedFull =
                staggerPercentage >= 0.999f && state.LastStaggerPercentage < 0.999f &&
                !isStaggering && !state.WasStaggering;

            bool beganStagger = isStaggering && !state.WasStaggering;

            // Not when that hit killed it: the bar is gone with it. A flash
            // already running stops if it dies.
            bool dead = state.Character.GetHealth() <= 0f;
            if ((reachedFull || beganStagger) && !dead)
                StartFlash(state);
            else if (dead && state.FlashUntil > Time.time)
                state.FlashUntil = Time.time;

            UpdateFlash(state);

            state.LastStaggerPercentage = staggerPercentage;

            state.WasStaggering = isStaggering;
        }

        private void SetStaggerLayoutActive(EnemyUiState state, bool active)
        {
            if (state == null)
                return;

            if (state.StaggerRoot != null && state.StaggerRoot.activeSelf != active)
                state.StaggerRoot.SetActive(active);

            // The chosen star row height, below the double-height health
            // bar's extra height, and below the stagger bar while it shows
            // (moving with its gap setting).
            float y =
                _starRowHeight.Value - GetExtraHealthHeight(state) -
                (active ? Mathf.Abs(_staggerVerticalOffset.Value) : 0f);

            // The stars only move when that height changes.
            if (state.LastStarY == y)
                return;
            state.LastStarY = y;
            if (state.Weakness != null)
                state.Weakness.Dirty = true;

            Vector2 down = new Vector2(0f, y);
            if (state.Level2 != null) state.Level2.anchoredPosition = state.Level2Original + down;
            if (state.Level3 != null) state.Level3.anchoredPosition = state.Level3Original + down;
            if (state.IconRow != null) state.IconRow.anchoredPosition = state.IconRowOriginal + down;
        }

        private void StartFlash(EnemyUiState state)
        {
            if (state == null)
                return;

            float duration = GetPlayerStaggerFlashDuration();
            state.FlashStarted = Time.time;
            state.FlashUntil = Time.time + duration;
        }

        private static void UpdateFlash(EnemyUiState state)
        {
            if (state == null)
                return;

            float duration = state.FlashUntil - state.FlashStarted;
            if (duration <= 0f || Time.time >= state.FlashUntil)
            {
                SetFlashAlpha(state, 0f);
                return;
            }

            float t = Mathf.Clamp01((Time.time - state.FlashStarted) / duration);

            SetFlashAlpha(state, VanillaFlash(t));
        }

        // The game's own flash animation (stagger_flash): full white on the
        // very first frame, then eases back down to the frame's own color
        // (2t^3 - 3t^2 + 1). It hits, it doesn't fade in.
        internal static float VanillaFlash(float t)
        {
            if (t < 0f || t >= 1f) return 0f;
            return (2f * t - 3f) * t * t + 1f;
        }

        private static void SetFlashAlpha(EnemyUiState state, float alpha)
        {
            if (state == null)
                return;

            // Drawn like the game's own flash: the bar's dark frame itself
            // lights up (see ApplyFrameFlash).
            state.StaggerFlashAlpha = Mathf.Clamp01(alpha);
        }

        // Length of the game's stagger flash animation, read once from your
        // HUD (asking the animator for its clips makes a new list each time).
        private static float _flashDuration = -1f;

        private static float GetPlayerStaggerFlashDuration()
        {
            if (_flashDuration > 0f) return _flashDuration;
            float found = ReadPlayerStaggerFlashDuration();
            if (Hud.instance != null) _flashDuration = found; // known once the HUD exists
            return found;
        }

        private static float ReadPlayerStaggerFlashDuration()
        {
            Hud hud = Hud.instance;
            Animator animator = hud != null ? hud.m_staggerAnimator : null;

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;

                if (clips != null)
                {
                    foreach (AnimationClip clip in clips)
                    {
                        if (clip != null &&
                            clip.name.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            clip.length > 0.01f)
                        {
                            return clip.length;
                        }
                    }
                }
            }

            return 0.30f;
        }
    }
}
