using System;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _staggerRecoveryEnabled;
        private ConfigEntry<bool> _transparentBackground;
        // Same see-through black as the player's own HUD bars.
        private static readonly Color TransparentBackground = new Color32(0, 0, 0, 123);

        private void BindEnhancedOptions()
        {
            _transparentBackground = Config.Bind(
                "Bars", "Transparent bar background", true,
                new ConfigDescription(
                    "Make the solid gray background behind health and stagger bars see-through, like the player's own HUD bars.",
                    null, new ConfigurationManagerAttributes { Order = 180 }));
            _staggerRecoveryEnabled = Config.Bind(
                "Stagger Bar", "Recovery overlay", true,
                new ConfigDescription(
                    "Show a yellow layer inside the enemy stagger bar during a stagger. It recedes from right to left over the enemy's actual stagger time (the window in which your hits deal double damage), revealing the orange bar beneath.",
                    null, new ConfigurationManagerAttributes { Order = 175 }));




        }

        private void SetupEnhancedEffects(EnemyUiState state)
        {
            if (state == null || state.Kind != HudKind.Enemy)
                return;

            if (state.StaggerFast != null && state.StaggerFast.m_bar != null)
            {
                RectTransform original = state.StaggerFast.m_bar;
                var originalImage = original.GetComponent<Image>();
                GameObject overlayGo = new GameObject(
                    "WireStaggerRecovery", typeof(RectTransform), typeof(Image));
                var rect = overlayGo.GetComponent<RectTransform>();
                rect.SetParent(original.parent, false);
                rect.anchorMin = original.anchorMin;
                rect.anchorMax = original.anchorMax;
                rect.pivot = original.pivot;
                rect.anchoredPosition = original.anchoredPosition;
                rect.sizeDelta = original.sizeDelta;
                var image = overlayGo.GetComponent<Image>();
                if (originalImage != null)
                {
                    image.sprite = originalImage.sprite;
                    image.material = originalImage.material;
                    image.preserveAspect = originalImage.preserveAspect;
                }
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.fillAmount = 1f;
                image.color = new Color32(255, 243, 75, 255); // vanilla stamina yellow
                image.raycastTarget = false;
                rect.SetAsLastSibling();
                overlayGo.SetActive(false);
                state.RecoveryOverlay = image;
                state.StaggerAnimator = state.Character != null
                    ? CharacterAnimator(state.Character) : null;
            }

        }

        // The overlay covers exactly the window in which vanilla doubles damage:
        // Character.RPC_Damage applies x2 while Character.IsStaggering() is true,
        // and IsStaggering() is "layer 0 current or next state is tagged stagger".
        // When a stagger starts, a hidden copy of the creature's own animator
        // state machine is run forward (same controller, parameters, state and
        // speed) until that check would turn false. That gives the real end time
        // up front, so the bar drains linearly from the start to the very end of
        // the bonus window. Runs once per stagger, nothing is stored.
        private static readonly AccessTools.FieldRef<Character, Animator> CharacterAnimator =
            AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<Character, CharacterAnimEvent> CharacterAnimEvents =
            AccessTools.FieldRefAccess<Character, CharacterAnimEvent>("m_animEvent");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, float> PauseTimer =
            AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_pauseTimer");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, float> PauseSpeed =
            AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_pauseSpeed");
        private static readonly int StaggerTag = ZSyncAnimation.GetHash("stagger");

        // Vanilla hit-stop (CharacterAnimEvent.FreezeFrame, 0.15 s when an attack
        // lands, e.g. on the shield you parry with) sets the animator speed to
        // 0.0001 and restores it afterwards. Returns the speed the animation will
        // actually play at, and how long the freeze still holds it.
        private static float PlaySpeed(Character c, Animator a, out float hold)
        {
            hold = 0f;
            float speed = a.speed;
            if (speed >= 0.01f) return speed;
            CharacterAnimEvent ev = CharacterAnimEvents(c);
            if (ev != null)
            {
                hold = Mathf.Max(0f, PauseTimer(ev));
                speed = PauseSpeed(ev);
            }
            return speed >= 0.01f ? speed : 1f;
        }
        private const float ProbeStep = 1f / 60f;
        private const float ProbeLimit = 8f;
        private static Animator _probe;

        private static Animator Probe()
        {
            if (_probe == null)
            {
                var go = new GameObject("WireStaggerProbe");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _probe = go.AddComponent<Animator>();
                _probe.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _probe.applyRootMotion = false;
                _probe.speed = 0f;
            }
            return _probe;
        }

        private static bool ProbeStaggering(Animator a)
        {
            if (a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).tagHash == StaggerTag)
                return true;
            return a.GetCurrentAnimatorStateInfo(0).tagHash == StaggerTag;
        }

        // Seconds until IsStaggering() turns false, and seconds already spent in
        // the window. -1 when the animator can't be read.
        private static float StaggerTimeLeft(Character c, Animator real, out float elapsed, out float speed)
        {
            elapsed = 0f;
            speed = 1f;
            if (real == null || !real.isActiveAndEnabled || real.runtimeAnimatorController == null)
                return -1f;
            speed = PlaySpeed(c, real, out float hold);
            AnimatorStateInfo cur = real.GetCurrentAnimatorStateInfo(0);
            bool blending = real.IsInTransition(0);
            AnimatorStateInfo s;
            if (blending && real.GetNextAnimatorStateInfo(0).tagHash == StaggerTag)
                s = real.GetNextAnimatorStateInfo(0);
            else if (cur.tagHash == StaggerTag)
            {
                elapsed = cur.normalizedTime * cur.length / speed;
                if (blending)
                {
                    // Already blending out: the window ends when this blend completes.
                    AnimatorTransitionInfo t = real.GetAnimatorTransitionInfo(0);
                    float dur = t.durationUnit == DurationUnit.Fixed ? t.duration : t.duration * cur.length;
                    return hold + Mathf.Max(0f, (1f - t.normalizedTime) * dur / speed);
                }
                s = cur;
            }
            else return -1f;
            if (elapsed <= 0f) elapsed = s.normalizedTime * s.length / speed;

            Animator p = Probe();
            try
            {
                if (p.runtimeAnimatorController != real.runtimeAnimatorController)
                    p.runtimeAnimatorController = real.runtimeAnimatorController;
                foreach (AnimatorControllerParameter par in real.parameters)
                {
                    switch (par.type)
                    {
                        case AnimatorControllerParameterType.Float: p.SetFloat(par.nameHash, real.GetFloat(par.nameHash)); break;
                        case AnimatorControllerParameterType.Int: p.SetInteger(par.nameHash, real.GetInteger(par.nameHash)); break;
                        case AnimatorControllerParameterType.Bool: p.SetBool(par.nameHash, real.GetBool(par.nameHash)); break;
                        default: p.ResetTrigger(par.nameHash); break;
                    }
                }
                p.speed = speed;
                p.Play(s.fullPathHash, 0, s.normalizedTime);
                p.Update(0f);
                if (!ProbeStaggering(p)) return -1f;
                float time = 0f;
                while (time < ProbeLimit)
                {
                    p.Update(ProbeStep);
                    time += ProbeStep;
                    if (!ProbeStaggering(p)) return hold + time - ProbeStep * 0.5f;
                }
                return hold + time;
            }
            catch { return -1f; }
            // Stays active (no rebind next time); speed 0 makes Unity's own
            // per-frame pass on it a no-op.
            finally { p.speed = 0f; }
        }

        // Swaps the vanilla bar background's color only when the setting
        // changes; no per-frame work after that.
        private void UpdateBarBackground(EnemyUiState state)
        {
            int want = _transparentBackground != null && _transparentBackground.Value ? 1 : 0;
            if (state.BkgApplied == want) return;
            if (state.BkgApplied < 0)
            {
                Transform h = state.HealthRect != null ? state.HealthRect.Find("bkg") : null;
                Transform s = state.StaggerRect != null ? state.StaggerRect.Find("bkg") : null;
                state.HealthBkg = h != null ? h.GetComponent<Image>() : null;
                state.StaggerBkg = s != null ? s.GetComponent<Image>() : null;
                if (state.HealthBkg != null) state.HealthBkgOriginal = state.HealthBkg.color;
                if (state.StaggerBkg != null) state.StaggerBkgOriginal = state.StaggerBkg.color;
            }
            if (state.HealthBkg != null)
                state.HealthBkg.color = want == 1 ? TransparentBackground : state.HealthBkgOriginal;
            if (state.StaggerBkg != null)
                state.StaggerBkg.color = want == 1 ? TransparentBackground : state.StaggerBkgOriginal;
            state.BkgApplied = want;
        }

        private void UpdateEnhancedEffects(EnemyUiState state, bool isEnemy)
        {
            if (state != null) UpdateBarBackground(state);
            if (state == null || state.Kind != HudKind.Enemy ||
                state.Character == null) return;

            Image overlay = state.RecoveryOverlay;
            if (overlay == null) return;
            bool show = isEnemy && _staggerBarEnabled.Value &&
                _staggerRecoveryEnabled.Value &&
                state.StaggerRoot != null && state.StaggerRoot.activeSelf;
            bool staggering = show && state.Character.IsStaggering();
            float now = Time.time;
            if (!staggering)
                state.StaggerBegan = -1f;
            else
            {
                if (state.StaggerAnimator == null)
                    state.StaggerAnimator = CharacterAnimator(state.Character);
                // Recalculate at the start, whenever the play speed changes, or
                // if still staggered past the predicted end.
                bool recheck = state.StaggerBegan < 0f;
                if (!recheck && state.StaggerAnimator != null && now - state.StaggerChecked >= 0.1f)
                {
                    float speedNow = PlaySpeed(state.Character, state.StaggerAnimator, out _);
                    recheck = Mathf.Abs(speedNow - state.StaggerSpeed) > 0.02f ||
                        now >= state.StaggerEnd;
                }
                if (recheck) Restagger(state, now);
            }
            overlay.gameObject.SetActive(staggering);
            if (!staggering) return;
            float length = Mathf.Max(0.05f, state.StaggerEnd - state.StaggerBegan);
            float progress = Mathf.Clamp01((now - state.StaggerBegan) / length);
            RectTransform fill = state.StaggerFast.m_bar;
            RectTransform rect = overlay.rectTransform;
            rect.anchoredPosition = fill.anchoredPosition;
            rect.sizeDelta = fill.sizeDelta;
            overlay.fillAmount = 1f - progress;
        }

        private static void Restagger(EnemyUiState state, float now)
        {
            float left = StaggerTimeLeft(state.Character, state.StaggerAnimator, out float elapsed, out float speed);
            if (state.StaggerBegan < 0f) state.StaggerBegan = now - elapsed;
            state.StaggerEnd = now + (left >= 0f ? left : 0.1f);
            state.StaggerSpeed = speed;
            state.StaggerChecked = now;
        }

        private static void DestroyEnhancedEffects(EnemyUiState state)
        {
            if (state == null) return;
            if (state.RecoveryOverlay != null)
                UnityEngine.Object.Destroy(state.RecoveryOverlay.gameObject);
        }
    }
}
