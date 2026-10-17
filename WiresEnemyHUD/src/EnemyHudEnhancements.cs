using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<bool> _staggerRecoveryEnabled;

        private void BindEnhancedOptions()
        {
            _staggerRecoveryEnabled = Config.Bind(
                "Stagger Bar", "Recovery overlay", true,
                new ConfigDescription(
                    "Show a second layer inside the enemy stagger bar during a stagger. It recedes from right to left in time with the stagger animation, revealing the orange bar beneath.",
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
                image.color = new Color32(236, 198, 137, 245);
                image.raycastTarget = false;
                rect.SetAsLastSibling();
                overlayGo.SetActive(false);
                state.RecoveryOverlay = image;
                state.StaggerAnimator = state.Character != null
                    ? state.Character.GetComponentInChildren<Animator>() : null;
            }

            if (state.HasLevel2 && state.Level2 != null)
            {
                GameObject labelGo = new GameObject(
                    "WireExtraStars", typeof(RectTransform), typeof(TextMeshProUGUI));
                RectTransform rect = labelGo.GetComponent<RectTransform>();
                rect.SetParent(state.Level2.parent, false);
                rect.anchorMin = state.Level2.anchorMin;
                rect.anchorMax = state.Level2.anchorMax;
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(36f, 22f);
                TMP_Text label = labelGo.GetComponent<TMP_Text>();
                if (state.NameText != null)
                {
                    label.font = state.NameText.font;
                    label.fontSharedMaterial = state.NameText.fontSharedMaterial;
                    label.fontSize = state.NameText.fontSize;
                }
                label.enableAutoSizing = false;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.color = new Color32(242, 202, 95, 255);
                label.raycastTarget = false;
                state.StarCountLabel = label;
                labelGo.SetActive(false);
            }
        }

        private static float ActualStaggerProgress(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
                return -1f;
            const int layer = 0;
            int tag = Animator.StringToHash("stagger");
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
            if (current.tagHash == tag)
                return Mathf.Clamp01(current.normalizedTime);

            if (animator.IsInTransition(layer))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(layer);
                if (next.tagHash == tag)
                    return Mathf.Clamp01(next.normalizedTime);
            }
            return -1f;
        }

        private void UpdateEnhancedEffects(EnemyUiState state, bool isEnemy)
        {
            if (state == null || state.Kind != HudKind.Enemy ||
                state.Character == null) return;

            UpdateCompactStarCount(state);

            Image overlay = state.RecoveryOverlay;
            if (overlay == null) return;
            bool show = isEnemy && _staggerBarEnabled.Value &&
                _staggerRecoveryEnabled.Value &&
                state.StaggerRoot != null && state.StaggerRoot.activeSelf &&
                state.Character.IsStaggering();
            float progress = show ? ActualStaggerProgress(state.StaggerAnimator) : -1f;
            show = show && progress >= 0f && progress < 1f;
            overlay.gameObject.SetActive(show);
            if (show)
            {
                RectTransform fill = state.StaggerFast.m_bar;
                RectTransform rect = overlay.rectTransform;
                rect.anchoredPosition = fill.anchoredPosition;
                rect.sizeDelta = fill.sizeDelta;
                overlay.fillAmount = 1f - progress;
            }
        }

        private static void UpdateCompactStarCount(EnemyUiState state)
        {
            if (state.StarCountLabel == null || state.Level2 == null)
                return;
            // Vanilla level 1 = 0 stars, 2 = 1, 3 = 2.
            int stars = Mathf.Max(0, state.Character.GetLevel() - 1);
            bool compact = stars >= 3;
            state.StarCountLabel.gameObject.SetActive(compact);
            if (!compact) return;

            state.Level2.gameObject.SetActive(true);
            if (state.Level3 != null)
                state.Level3.gameObject.SetActive(false);
            state.StarCountLabel.text = stars.ToString();

            RectTransform label = state.StarCountLabel.rectTransform;
            label.anchoredPosition = state.Level2.anchoredPosition +
                new Vector2(Mathf.Max(9f, state.Level2.rect.width * 0.5f + 3f), 0f);
        }

        private static void DestroyEnhancedEffects(EnemyUiState state)
        {
            if (state == null) return;
            if (state.StarCountLabel != null)
                UnityEngine.Object.Destroy(state.StarCountLabel.gameObject);
            if (state.RecoveryOverlay != null)
                UnityEngine.Object.Destroy(state.RecoveryOverlay.gameObject);
        }
    }
}
