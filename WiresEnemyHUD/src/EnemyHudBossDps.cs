using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace EnemyHudRework
{
    // Boss DPS counter under the boss bar's HP text. Measured like
    // Terraria's DPS meter: hits extend the sample, 3 s without a hit ends it.
    public sealed partial class EnemyHudReworkPlugin
    {
        private const float DpsTimeoutSeconds = 3f;
        private const float DpsMinimumWindowSeconds = 1f;
        private const float DpsNormalizeAfterSeconds = 3f;

        private ConfigEntry<string> _bossDpsMode;

        private sealed class DpsUiState
        {
            public GameObject TextObject;
            public TMP_Text Text;
            public RectTransform Rect;
            public float BaseFontSize;
            public bool EnabledLastFrame;
            public bool HasLastHealth;
            public float LastHealth;
            public Action<float, Character> DamageHandler;
            public bool Started;
            public float StartTime;
            public float EndTime;
            public float LastHitTime;
            public int Damage;
            public int ShownValue = -1;
            public bool ShownSuffix;
        }

        private readonly Dictionary<EnemyUiState, DpsUiState> _dpsStates =
            new Dictionary<EnemyUiState, DpsUiState>();

        private bool BossDpsOn { get { return _bossDpsMode.Value != "Off"; } }

        private void SetupBossDps(EnemyUiState state)
        {
            if (state == null || state.Kind != HudKind.Boss ||
                state.Character == null || state.Gui == null)
                return;

            DestroyBossDps(state);

            // The ordinary enemy name font, like the HP text.
            TMP_Text template = state.NameText;
            EnemyHud enemyHud = EnemyHud.instance;
            if (enemyHud != null && enemyHud.m_baseHud != null)
            {
                Transform enemyName = enemyHud.m_baseHud.transform.Find("Name");
                TMP_Text enemyTemplate = enemyName != null ? enemyName.GetComponent<TMP_Text>() : null;
                if (enemyTemplate != null) template = enemyTemplate;
            }
            if (template == null || template.transform.parent == null)
                return;

            Transform parent = state.NameText != null && state.NameText.transform.parent != null
                ? state.NameText.transform.parent : template.transform.parent;
            GameObject obj = Instantiate(template.gameObject, parent, false);
            obj.name = "DpsValue";
            TMP_Text text = obj.GetComponent<TMP_Text>();
            RectTransform rect = obj.transform as RectTransform;
            if (text == null || rect == null)
            {
                Destroy(obj);
                return;
            }

            text.text = "";
            text.alignment = TextAlignmentOptions.Right;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            var dps = new DpsUiState
            {
                TextObject = obj,
                Text = text,
                Rect = rect,
                BaseFontSize = text.fontSize,
                HasLastHealth = true,
                LastHealth = state.Character.GetHealth()
            };
            // m_onDamaged runs after mitigation: this is damage actually taken.
            dps.DamageHandler = (damage, attacker) =>
            {
                if (dps.EnabledLastFrame && damage > 0.01f)
                    AddDpsDamage(dps, Mathf.Max(1, Mathf.RoundToInt(damage)), Time.realtimeSinceStartup);
                if (state.Character != null) dps.LastHealth = state.Character.GetHealth();
                dps.HasLastHealth = true;
            };
            state.Character.m_onDamaged += dps.DamageHandler;

            obj.SetActive(false);
            _dpsStates[state] = dps;
        }

        private void DestroyBossDps(EnemyUiState state)
        {
            DpsUiState dps;
            if (state == null || !_dpsStates.TryGetValue(state, out dps))
                return;
            if (state.Character != null && dps.DamageHandler != null)
                state.Character.m_onDamaged -= dps.DamageHandler;
            if (dps.TextObject != null)
                Destroy(dps.TextObject);
            _dpsStates.Remove(state);
        }

        private void DestroyAllBossDps()
        {
            foreach (EnemyUiState state in new List<EnemyUiState>(_dpsStates.Keys))
                DestroyBossDps(state);
        }

        private void UpdateBossDps(EnemyUiState state)
        {
            DpsUiState dps;
            if (state == null || state.Kind != HudKind.Boss || state.Character == null ||
                !_dpsStates.TryGetValue(state, out dps) || dps.TextObject == null)
                return;

            bool enabled = BossDpsOn;
            if (enabled != dps.EnabledLastFrame)
            {
                dps.EnabledLastFrame = enabled;
                dps.Started = false;
                dps.Damage = 0;
                dps.LastHealth = state.Character.GetHealth();
                dps.HasLastHealth = true;
            }
            if (!enabled)
            {
                if (dps.TextObject.activeSelf) dps.TextObject.SetActive(false);
                return;
            }

            // Health lost without a local damage event (another player's
            // hits arrive as network health changes). Healing is ignored.
            float current = state.Character.GetHealth();
            if (dps.HasLastHealth && dps.LastHealth - current > 0.01f)
                AddDpsDamage(dps, Mathf.Max(1, Mathf.RoundToInt(dps.LastHealth - current)), Time.realtimeSinceStartup);
            dps.LastHealth = current;
            dps.HasLastHealth = true;

            float now = Time.realtimeSinceStartup;
            if (dps.Started && now - dps.LastHitTime >= DpsTimeoutSeconds)
                dps.Started = false;
            int value = CurrentDps(dps, now);
            bool suffix = _bossDpsMode.Value == "On + DPS";
            if (value != dps.ShownValue || suffix != dps.ShownSuffix)
            {
                dps.Text.text = suffix ? value + " DPS" : value.ToString();
                dps.ShownValue = value;
                dps.ShownSuffix = suffix;
            }
            Color color = _cDpsText.Or(Color.white);
            if (dps.Text.color != color) dps.Text.color = color;
            if (!dps.TextObject.activeSelf) dps.TextObject.SetActive(true);

            LayoutBossDps(state, dps);
        }

        private static void AddDpsDamage(DpsUiState dps, int damage, float now)
        {
            if (damage <= 0) return;
            if (dps.Started)
            {
                dps.LastHitTime = now;
                dps.Damage += damage;
                dps.EndTime = now;
            }
            else
            {
                dps.Started = true;
                dps.StartTime = now;
                dps.EndTime = now;
                dps.LastHitTime = now;
                dps.Damage = damage;
            }
        }

        // Under one second, damage counts as dealt over one second. Past
        // three, the sample folds into its average and continues from there.
        private static int CurrentDps(DpsUiState dps, float now)
        {
            if (!dps.Started) return 0;
            float span = dps.EndTime - dps.StartTime;
            if (span >= DpsNormalizeAfterSeconds)
            {
                dps.StartTime = now - DpsMinimumWindowSeconds;
                dps.Damage = span > 0f ? (int)(dps.Damage / span) : dps.Damage;
                span = dps.EndTime - dps.StartTime;
            }
            if (span < DpsMinimumWindowSeconds) span = DpsMinimumWindowSeconds;
            return (int)(dps.Damage / span);
        }

        private void LayoutBossDps(EnemyUiState state, DpsUiState dps)
        {
            if (state.HealthRect == null) return;

            dps.Rect.localScale = Vector3.one;
            dps.Text.fontSize = dps.BaseFontSize * 0.738f;

            float textWidth = Mathf.Max(80f, dps.Text.preferredWidth + 12f);
            float textHeight = Mathf.Max(dps.Text.preferredHeight, dps.Text.fontSize * 1.2f);
            dps.Rect.sizeDelta = new Vector2(textWidth, textHeight);

            Rect barRect = state.HealthRect.rect;
            bool tracked = _bossHealthTextPosition.Value == BossHealthTextPosition.Tracked;

            float localX = barRect.center.x;
            if (tracked)
            {
                if (state.HealthFastFill != null)
                {
                    Rect fillRect = state.HealthFastFill.rect;
                    Vector3 fillRight = state.HealthFastFill.TransformPoint(
                        new Vector3(fillRect.xMax, fillRect.center.y, 0f));
                    localX = state.HealthRect.InverseTransformPoint(fillRight).x;
                }
                else
                {
                    float hp = Mathf.Max(0f, state.Character.GetHealth());
                    float max = Mathf.Max(1f, state.Character.GetMaxHealth());
                    localX = Mathf.Lerp(barRect.xMin, barRect.xMax, Mathf.Clamp01(hp / max));
                }
                // Never let the number run past the bar's left end.
                localX = Mathf.Max(localX, barRect.xMin + TextWidthIn(dps.Text, dps.Rect, state.HealthRect));
            }

            bool hpShown = _bossHealthDisplayMode.Value != "None" && state.HealthValueText != null;
            float hpHeight = hpShown
                ? Mathf.Max(state.HealthValueText.preferredHeight, state.HealthValueText.fontSize * 1.2f)
                : textHeight;
            float localY = hpShown
                ? barRect.yMin - hpHeight * 0.55f - 2f - (hpHeight + textHeight) * 0.5f
                : barRect.yMin - textHeight * 0.55f - 2f;

            dps.Rect.pivot = tracked ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);
            dps.Text.alignment = tracked ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
            dps.Rect.position = state.HealthRect.TransformPoint(new Vector3(localX, localY, 0f));
        }
    }
}
