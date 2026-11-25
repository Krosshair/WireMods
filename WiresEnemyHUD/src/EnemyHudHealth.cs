using TMPro;
using UnityEngine;

namespace EnemyHudRework
{
    // HP numbers and the health bar's size.
    public sealed partial class EnemyHudReworkPlugin
    {
        private void CreateHealthValue(EnemyUiState state)
        {
            if (state == null || state.NameText == null || state.NameText.transform.parent == null)
                return;

            // Use the normal enemy HUD text object as the canonical HP-text
            // template for enemy, player and boss HUDs. This keeps the exact
            // same font sizing/material/scale behavior everywhere.
            TMP_Text template = state.NameText;
            EnemyHud enemyHud = EnemyHud.instance;
            if (enemyHud != null && enemyHud.m_baseHud != null)
            {
                Transform enemyName = enemyHud.m_baseHud.transform.Find("Name");
                TMP_Text enemyTemplate = enemyName != null ? enemyName.GetComponent<TMP_Text>() : null;

                if (enemyTemplate != null)
                    template = enemyTemplate;
            }

            GameObject obj = Instantiate(template.gameObject, state.NameText.transform.parent, false);

            obj.name = "HealthValue";
            state.HealthValueObject = obj;
            state.HealthValueText = obj.GetComponent<TMP_Text>();
            state.HealthValueRect = obj.transform as RectTransform;

            if (state.HealthValueText != null)
            {
                state.HealthValueText.text = "";
                state.HealthValueText.color = Color.white;
                state.HealthValueText.alignment = TextAlignmentOptions.Center;
                // The Name prefab uses TMP automatic font sizing.
                // Unless we disable it, TMP silently shrinks text
                // back to its original size after our font setting.
                state.HealthValueText.enableAutoSizing = false;
                state.HealthValueText.textWrappingMode = TextWrappingModes.NoWrap;
                state.HealthValueText.overflowMode = TextOverflowModes.Overflow;
                state.HealthValueText.raycastTarget = false;
                state.HealthValueBaseFontSize = state.HealthValueText.fontSize;
            }

            obj.SetActive(false);
        }

        private string GetHealthDisplayMode(EnemyUiState state)
        {
            if (state == null)
                return "Current HP/Max HP";

            switch (state.Kind)
            {
                case HudKind.Boss:
                    return _bossHealthDisplayMode.Value;
                case HudKind.Player:
                    return _playerHealthDisplayMode.Value;
                default:
                    return _enemyHealthDisplayMode.Value;
            }
        }

        private bool IsDoubleHealthBar(EnemyUiState state)
        {
            if (state == null)
                return false;

            switch (state.Kind)
            {
                case HudKind.Boss:
                    return false;
                case HudKind.Player:
                    return _doublePlayerHealthBar.Value;
                default:
                    return _doubleEnemyHealthBar.Value;
            }
        }

        private void UpdateHealthValue(EnemyUiState state)
        {
            if (state == null || state.HealthValueObject == null || state.HealthValueText == null ||
                state.HealthValueRect == null)
                return;

            if (!state.IsDebug && state.Character == null)
                return;

            string mode = GetHealthDisplayMode(state);

            // Player HP text is only shown in the doubled player health bar.
            // With vanilla-height player bars, leave the nameplate completely
            // untouched regardless of the selected HP display mode.
            bool playerCounterDisabled =
                (state.Kind == HudKind.Player && !_doublePlayerHealthBar.Value) ||
                (state.Kind == HudKind.Enemy && _dynamicHp.Value && !state.IsDebug &&
                 !LookedAtRecently(state.Character)) ||
                (state.Kind == HudKind.Player && _playerDynamicHp.Value && !state.IsDebug &&
                 state.Character != Player.m_localPlayer && !AimedAtRecently(state));

            float hpNow = state.Character != null ? Mathf.Max(0f, state.Character.GetHealth()) : 0f;
            float maxNow = state.Character != null ? Mathf.Max(1f, state.Character.GetMaxHealth()) : 1f;
            int keyA, keyB;
            if (mode == "None" || playerCounterDisabled) { keyA = -1; keyB = -1; }
            else if (mode == "Percentage") { keyA = Mathf.Clamp(Mathf.RoundToInt(hpNow / maxNow * 100f), 0, 100); keyB = -2; }
            else { keyA = Mathf.CeilToInt(hpNow); keyB = mode == "Current HP/Max HP" ? Mathf.CeilToInt(maxNow) : -3; }
            string nameNow = state.NameText != null ? state.NameText.text : null;
            if (state.HpDrawn && keyA == state.HpA && keyB == state.HpB &&
                state.HpLayout == _layoutVersion && state.HpLift == state.BubbleLift &&
                ReferenceEquals(nameNow, state.HpName))
                return;
            state.HpDrawn = true;
            state.HpA = keyA; state.HpB = keyB;
            state.HpLayout = _layoutVersion; state.HpLift = state.BubbleLift; state.HpName = nameNow;

            if (mode == "None" || playerCounterDisabled)
            {
                state.HealthValueObject.SetActive(false);
                if (state.NameRect != null)
                    state.NameRect.anchoredPosition = NameBase(state);
                return;
            }

            state.HealthValueText.text =
                mode == "Percentage" ? keyA + "%" :
                mode == "Current HP/Max HP" ? keyA + "/" + keyB : keyA.ToString();

            state.HealthValueObject.SetActive(true);
            state.HealthValueText.color = Color.white;

            if (state.Kind == HudKind.Boss && state.HealthRect != null)
            {
                UpdateBossHealthValueLayout(state, Mathf.Clamp01(hpNow / maxNow));
                return;
            }

            if (IsDoubleHealthBar(state) && state.HealthRect != null)
            {
                if (state.NameRect != null)
                    state.NameRect.anchoredPosition = NameBase(state);

                state.HealthValueRect.anchorMin = state.HealthRect.anchorMin;
                state.HealthValueRect.anchorMax = state.HealthRect.anchorMax;
                state.HealthValueRect.pivot = state.HealthRect.pivot;
                state.HealthValueRect.anchoredPosition = state.HealthRect.anchoredPosition;
                state.HealthValueRect.sizeDelta = state.HealthRect.sizeDelta;
                state.HealthValueRect.localScale = Vector3.one;
                state.HealthValueText.fontSize = state.HealthValueBaseFontSize * 0.648f * _hpFontScale.Value;
                state.HealthValueText.alignment = TextAlignmentOptions.Center;
            }
            else if (state.NameRect != null)
            {
                state.HealthValueRect.anchorMin = state.NameRect.anchorMin;
                state.HealthValueRect.anchorMax = state.NameRect.anchorMax;
                state.HealthValueRect.pivot = state.NameRect.pivot;
                state.HealthValueRect.sizeDelta = state.NameRect.sizeDelta;
                state.HealthValueRect.localScale = Vector3.one;
                state.HealthValueText.fontSize = state.HealthValueBaseFontSize * 0.738f * _hpFontScale.Value;
                state.HealthValueText.alignment = TextAlignmentOptions.Center;

                float gap = 5f;
                float nameWidth = state.NameText != null ? state.NameText.preferredWidth : 0f;
                float valueWidth = state.HealthValueText.preferredWidth;

                state.NameRect.anchoredPosition =
                    NameBase(state) + Vector2.left * ((valueWidth + gap) * 0.5f);

                state.HealthValueRect.anchoredPosition =
                    NameBase(state) + Vector2.right * ((nameWidth + gap) * 0.5f);
            }
        }

        private void UpdateBossHealthValueLayout(EnemyUiState state, float healthPercentage)
        {
            if (state == null || state.HealthRect == null || state.HealthValueRect == null ||
                state.HealthValueText == null)
                return;

            if (state.NameRect != null)
                state.NameRect.anchoredPosition = NameBase(state);

            state.HealthValueRect.localScale = Vector3.one;
            state.HealthValueText.fontSize = state.HealthValueBaseFontSize * 0.738f * _hpFontScale.Value;

            float textWidth = Mathf.Max(80f, state.HealthValueText.preferredWidth + 12f);
            float textHeight = Mathf.Max(
                state.HealthValueText.preferredHeight,
                state.HealthValueText.fontSize * 1.2f);

            state.HealthValueRect.sizeDelta = new Vector2(textWidth, textHeight);

            Rect barRect = state.HealthRect.rect;
            bool tracked = _bossHealthTextPosition.Value == BossHealthTextPosition.Tracked;

            float localX = barRect.center.x;
            if (tracked)
            {
                if (state.HealthFastFill != null)
                {
                    Rect fillRect = state.HealthFastFill.rect;
                    Vector3 fillRightWorld =
                        state.HealthFastFill.TransformPoint(
                            new Vector3(fillRect.xMax, fillRect.center.y, 0f));

                    localX = state.HealthRect.InverseTransformPoint(fillRightWorld).x;
                }
                else
                {
                    localX = Mathf.Lerp(barRect.xMin, barRect.xMax, Mathf.Clamp01(healthPercentage));
                }

                // Never let the number run past the bar's left end.
                localX = Mathf.Max(localX, barRect.xMin + TextWidthIn(
                    state.HealthValueText, state.HealthValueRect, state.HealthRect));
            }

            float localY = barRect.yMin - textHeight * 0.55f - 2f;

            state.HealthValueRect.pivot = tracked ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);

            state.HealthValueText.alignment =
                tracked ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;

            Vector3 worldPoint = state.HealthRect.TransformPoint(new Vector3(localX, localY, 0f));

            state.HealthValueRect.position = worldPoint;
        }

        // A text's visible width, in another rect's local units.
        private static float TextWidthIn(TMP_Text text, RectTransform textRect, RectTransform space)
        {
            return text.preferredWidth * Mathf.Abs(textRect.lossyScale.x) /
                Mathf.Max(0.0001f, Mathf.Abs(space.lossyScale.x));
        }

        private static void CaptureHealthFill(
            Transform healthRoot,
            string childName,
            out RectTransform fill,
            out float originalHeight)
        {
            fill = null;
            originalHeight = 0f;
            if (healthRoot == null)
                return;

            Transform child = healthRoot.Find(childName);
            GuiBar bar = child != null ? child.GetComponent<GuiBar>() : null;
            if (bar == null || bar.m_bar == null)
                return;

            fill = bar.m_bar;
            originalHeight = fill.rect.height;
        }

        private float GetHealthFillBaseHeight(EnemyUiState state)
        {
            if (state == null)
                return 0f;
            if (state.HealthFastOriginalHeight > 0f)
                return state.HealthFastOriginalHeight;
            if (state.HealthSlowOriginalHeight > 0f)
                return state.HealthSlowOriginalHeight;
            if (state.HealthFriendlyOriginalHeight > 0f)
                return state.HealthFriendlyOriginalHeight;
            return Mathf.Max(0f, state.HealthOriginalHeight * 0.5f);
        }

        private static void SetFillHeight(RectTransform fill, float originalHeight, float multiplier)
        {
            if (fill == null || originalHeight <= 0f)
                return;
            fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalHeight * multiplier);
        }

        private void ApplyHealthBarScale(EnemyUiState state)
        {
            if (state == null || state.HealthRect == null)
                return;

            float multiplier = IsDoubleHealthBar(state) ? HealthHeightMultiplier : 1f;
            float extraLocalHeight = IsDoubleHealthBar(state)
                ? GetHealthFillBaseHeight(state) * (HealthHeightMultiplier - 1f)
                : 0f;
            if (state.ScaleLayout == _layoutVersion && state.HealthRect.sizeDelta.y == state.ScaleHeight)
                return;

            state.HealthRect.localScale = state.HealthOriginalScale;
            state.HealthRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                state.HealthOriginalHeight + extraLocalHeight);

            SetFillHeight(state.HealthFastFill, state.HealthFastOriginalHeight, multiplier);
            SetFillHeight(state.HealthSlowFill, state.HealthSlowOriginalHeight, multiplier);
            SetFillHeight(state.HealthFriendlyFill, state.HealthFriendlyOriginalHeight, multiplier);

            float extraHeight = GetExtraHealthHeight(state);
            state.HealthRect.anchoredPosition =
                state.HealthOriginalPosition + Vector2.down * (extraHeight * 0.5f);
            state.ScaleLayout = _layoutVersion;
            state.ScaleHeight = state.HealthRect.sizeDelta.y;
        }

        private float GetExtraHealthHeight(EnemyUiState state)
        {
            if (state == null || state.HealthRect == null || !IsDoubleHealthBar(state))
                return 0f;

            float extraLocalHeight = GetHealthFillBaseHeight(state) * (HealthHeightMultiplier - 1f);
            return Mathf.Abs(extraLocalHeight * state.HealthOriginalScale.y);
        }
    }
}
