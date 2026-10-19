using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {

        private const float TerrariaDpsTimeoutSeconds = 3f;
        private const float TerrariaDpsMinimumWindowSeconds = 1f;
        private const float TerrariaDpsNormalizeAfterSeconds = 3f;

        private ConfigEntry<string> _preset;
        private ConfigEntry<string> _enemyDpsMode;
        private ConfigEntry<string> _bossDpsMode;
        private ConfigEntry<string> _playerDpsMode;
        private bool _applyingPreset;

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
        }

        private readonly Dictionary<EnemyUiState, DpsUiState> _dpsStates =
            new Dictionary<EnemyUiState, DpsUiState>();

        private void BindPresetConfig()
        {
            _preset = Config.Bind(
                "General",
                "Preset",
                "Author's Choice",
                new ConfigDescription(
                    "Select Author's Choice to apply my preferred Enemy HUD settings, or Vanilla to restore a vanilla-style HUD starting point. Selecting either preset applies its values to the options below.",
                    new AcceptableValueList<string>("Author's Choice", "Vanilla"),
                    new ConfigurationManagerAttributes
                    {
                        Order = 1000,
                        Category = " General"
                    }));

            Config.SettingChanged += OnDpsPresetSettingChanged;
        }

        private void BindDpsConfig()
        {
            AcceptableValueList<string> modes =
                new AcceptableValueList<string>(
                    new[] { "On", "On + DPS", "Off" });

            _enemyDpsMode = Config.Bind<string>(
                "DPS Meter",
                "Enemy DPS",
                "Off",
                new ConfigDescription(
                    "Choose DPS display: On shows the number (23); On + DPS adds its label (23 DPS); Off hides the counter.",
                    modes,
                    new ConfigurationManagerAttributes { Order = 300 }));

            _bossDpsMode = Config.Bind<string>(
                "DPS Meter",
                "Boss DPS",
                "On + DPS",
                new ConfigDescription(
                    "Choose boss DPS display: On shows the number (230); On + DPS adds its label (230 DPS); Off hides the counter.",
                    modes,
                    new ConfigurationManagerAttributes { Order = 200 }));

            _playerDpsMode = Config.Bind<string>(
                "DPS Meter",
                "Player DPS",
                "Off",
                new ConfigDescription(
                    "Choose DPS display: On shows the number (23); On + DPS adds its label (23 DPS); Off hides the counter.",
                    modes,
                    new ConfigurationManagerAttributes { Order = 100 }));
        }

        private void OnDpsPresetSettingChanged(
            object sender,
            SettingChangedEventArgs args)
        {
            if (_applyingPreset ||
                _preset == null ||
                args == null ||
                !ReferenceEquals(args.ChangedSetting, _preset))
                return;

            ApplyPreset(_preset.Value);
        }

        private void ApplyPreset(string preset)
        {
            _applyingPreset = true;

            try
            {
                ClearColorOverrides();
                switch (preset)
                {
                    case "Vanilla":
                        ApplyVanillaVisualValues();
                        _staggerBarEnabled.Value = false;
                        SetAllDpsModes("Off");
                        ApplyVisibilityPreset(false);
                        if (_playerNameplateAimFade != null)
                            _playerNameplateAimFade.Value = false;
                        _debugSeeOwnHealthBar.Value = false;
                        _debugBossHealthBar.Value = false;
                        break;

                    default:
                        _nameColorMode.Value = NameColorMode.Pastel;
                        _uppercaseNames.Value = false;
                        _enemyHealthDisplayMode.Value = "Current HP";
                        _doubleEnemyHealthBar.Value = true;
                        _bossHealthDisplayMode.Value = "Current HP/Max HP";
                        _bossHealthTextPosition.Value = BossHealthTextPosition.Tracked;
                        _bossNameColorMode.Value = BossNameColorMode.White;
                        _playerHealthDisplayMode.Value = "Current HP/Max HP";
                        _doublePlayerHealthBar.Value = true;
                        _staggerBarEnabled.Value = true;
                        if (_transparentBackground != null)
                            _transparentBackground.Value = true;
                        _staggerVerticalOffset.Value = 8f;
                        _starVerticalOffset.Value = 8f;
                        _hideForNonStaggerable.Value = true;
                        if (_enemyDpsMode != null)
                            _enemyDpsMode.Value = "Off";
                        if (_bossDpsMode != null)
                            _bossDpsMode.Value = "On + DPS";
                        if (_playerDpsMode != null)
                            _playerDpsMode.Value = "Off";
                        ApplyVisibilityPreset(true);
                        _hpFontScale.Value = 1.267f;
                        _dpsFontScale.Value = 1f;
                        if (_playerNameplateAimFade != null)
                            _playerNameplateAimFade.Value = true;
                        _debugSeeOwnHealthBar.Value = false;
                        _debugBossHealthBar.Value = false;
                        break;
                }
            }
            finally
            {
                _applyingPreset = false;
            }

            Config.Save();
        }

        private void ApplyVanillaVisualValues()
        {
            _nameColorMode.Value = NameColorMode.Icons;
            _uppercaseNames.Value = false;
            _enemyHealthDisplayMode.Value = "None";
            _doubleEnemyHealthBar.Value = false;
            _bossHealthDisplayMode.Value = "None";
            _bossHealthTextPosition.Value = BossHealthTextPosition.Centered;
            _bossNameColorMode.Value = BossNameColorMode.Vanilla;
            _playerHealthDisplayMode.Value = "None";
            _doublePlayerHealthBar.Value = false;
            if (_transparentBackground != null)
                _transparentBackground.Value = false;
            _staggerVerticalOffset.Value = 8f;
            _starVerticalOffset.Value = 8f;
            _hideForNonStaggerable.Value = true;
        }

        private void SetAllDpsModes(string mode)
        {
            if (_enemyDpsMode != null)
                _enemyDpsMode.Value = mode;
            if (_bossDpsMode != null)
                _bossDpsMode.Value = mode;
            if (_playerDpsMode != null)
                _playerDpsMode.Value = mode;
        }
        private void OnDpsPresetDestroy()
        {
            Config.SettingChanged -= OnDpsPresetSettingChanged;

            List<EnemyUiState> states =
                new List<EnemyUiState>(_dpsStates.Keys);

            for (int i = 0; i < states.Count; i++)
                DestroyDpsUi(states[i]);

            _dpsStates.Clear();
        }

        private string GetDpsMode(EnemyUiState state)
        {
            if (state == null)
                return "Off";

            switch (state.Kind)
            {
                case HudKind.Boss:
                    return _bossDpsMode != null
                        ? _bossDpsMode.Value
                        : "Off";

                case HudKind.Player:
                    return _playerDpsMode != null
                        ? _playerDpsMode.Value
                        : "Off";

                default:
                    return _enemyDpsMode != null
                        ? _enemyDpsMode.Value
                        : "Off";
            }
        }

        private static bool IsDpsEnabled(string mode)
        {
            return !string.IsNullOrEmpty(mode) &&
                   !string.Equals(
                       mode,
                       "Off",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShowDpsSuffix(string mode)
        {
            return string.Equals(
                mode,
                "On + DPS",
                StringComparison.Ordinal);
        }

        private void SetupDpsUi(EnemyUiState state)
        {
            if (state == null ||
                state.Character == null ||
                state.Gui == null)
                return;

            DestroyDpsUi(state);

            TMP_Text template = state.NameText;

            EnemyHud enemyHud = EnemyHud.instance;
            if (enemyHud != null &&
                enemyHud.m_baseHud != null)
            {
                Transform enemyName =
                    enemyHud.m_baseHud.transform.Find("Name");

                TMP_Text enemyTemplate =
                    enemyName != null
                        ? enemyName.GetComponent<TMP_Text>()
                        : null;

                if (enemyTemplate != null)
                    template = enemyTemplate;
            }

            if (template == null ||
                template.transform.parent == null)
                return;

            GameObject obj =
                Instantiate(
                    template.gameObject,
                    state.NameText != null &&
                    state.NameText.transform.parent != null
                        ? state.NameText.transform.parent
                        : template.transform.parent,
                    false);

            obj.name = "DpsValue";

            TMP_Text text =
                obj.GetComponent<TMP_Text>();

            RectTransform rect =
                obj.transform as RectTransform;

            if (text == null ||
                rect == null)
            {
                Destroy(obj);
                return;
            }

            text.text = "";
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Right;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            DpsUiState dps =
                new DpsUiState
                {
                    TextObject = obj,
                    Text = text,
                    Rect = rect,
                    BaseFontSize = text.fontSize,
                    HasLastHealth = true,
                    LastHealth = state.Character.GetHealth()
                };

            dps.DamageHandler =
                delegate(float damage, Character attacker)
                {
                    OnCharacterDamagedForDps(
                        state,
                        dps,
                        damage);
                };

            state.Character.m_onDamaged +=
                dps.DamageHandler;

            obj.SetActive(false);
            _dpsStates[state] = dps;
        }

        private void DestroyDpsUi(EnemyUiState state)
        {
            if (state == null)
                return;

            DpsUiState dps;
            if (!_dpsStates.TryGetValue(
                    state,
                    out dps))
                return;

            if (state.Character != null &&
                dps != null &&
                dps.DamageHandler != null)
            {
                state.Character.m_onDamaged -=
                    dps.DamageHandler;
            }

            if (dps != null &&
                dps.TextObject != null)
            {
                Destroy(dps.TextObject);
            }

            _dpsStates.Remove(state);
        }

        private void OnCharacterDamagedForDps(
            EnemyUiState state,
            DpsUiState dps,
            float damage)
        {
            if (state == null ||
                dps == null ||
                state.Character == null)
                return;

            // Character.m_onDamaged is invoked after final mitigation and
            // after health was changed, so this is actual damage taken.
            if (dps.EnabledLastFrame &&
                damage > 0.01f)
            {
                int amount =
                    Mathf.Max(
                        1,
                        Mathf.RoundToInt(damage));

                AddTerrariaDpsDamage(
                    dps,
                    amount,
                    Time.realtimeSinceStartup);
            }

            dps.LastHealth =
                state.Character.GetHealth();

            dps.HasLastHealth = true;
        }

        private void ResetDpsTracker(
            EnemyUiState state,
            DpsUiState dps)
        {
            if (dps == null)
                return;

            dps.Started = false;
            dps.StartTime = 0f;
            dps.EndTime = 0f;
            dps.LastHitTime = 0f;
            dps.Damage = 0;

            if (state != null &&
                state.Character != null)
            {
                dps.LastHealth =
                    state.Character.GetHealth();
                dps.HasLastHealth = true;
            }
            else
            {
                dps.HasLastHealth = false;
            }
        }

        private static void AddTerrariaDpsDamage(
            DpsUiState dps,
            int damage,
            float now)
        {
            if (dps == null ||
                damage <= 0)
                return;

            // Mirrors Terraria.Player.addDPS:
            // a hit starts a measurement; subsequent hits extend it.
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

        private static void CheckTerrariaDpsTime(
            DpsUiState dps,
            float now)
        {
            if (dps == null)
                return;

            // Terraria ends the current measurement after 3 real-time
            // seconds without another hit.
            if (dps.Started &&
                now - dps.LastHitTime >=
                TerrariaDpsTimeoutSeconds)
            {
                dps.Started = false;
            }
        }

        private static int GetTerrariaDps(
            DpsUiState dps,
            float now)
        {
            if (dps == null ||
                !dps.Started)
                return 0;

            // Mirrors Terraria.Player.getDPS. Before one second, damage
            // is treated as if it occurred over one second. Once the
            // current sample grows to 3 seconds, Terraria compresses the
            // accumulated sample back to its average and starts a new
            // effective one-second window from that value.
            float span =
                dps.EndTime -
                dps.StartTime;

            if (span >=
                TerrariaDpsNormalizeAfterSeconds)
            {
                dps.StartTime =
                    now -
                    TerrariaDpsMinimumWindowSeconds;

                dps.Damage =
                    span > 0f
                        ? (int)((float)dps.Damage / span)
                        : dps.Damage;

                span =
                    dps.EndTime -
                    dps.StartTime;
            }

            if (span <
                TerrariaDpsMinimumWindowSeconds)
            {
                span =
                    TerrariaDpsMinimumWindowSeconds;
            }

            return (int)(
                (float)dps.Damage /
                span);
        }
        private void UpdateDpsUi(
            EnemyUiState state,
            float dt)
        {
            if (state == null ||
                state.Character == null)
                return;

            DpsUiState dps;
            if (!_dpsStates.TryGetValue(
                    state,
                    out dps) ||
                dps == null)
            {
                SetupDpsUi(state);

                if (!_dpsStates.TryGetValue(
                        state,
                        out dps) ||
                    dps == null)
                    return;
            }

            string mode =
                GetDpsMode(state);

            bool enabled =
                IsDpsEnabled(mode);

            if (enabled !=
                dps.EnabledLastFrame)
            {
                dps.EnabledLastFrame =
                    enabled;

                ResetDpsTracker(
                    state,
                    dps);
            }

            if (!enabled)
            {
                if (dps.TextObject != null)
                    dps.TextObject.SetActive(false);
                return;
            }

            SampleDpsHealthFallback(
                state,
                dps);

            float now =
                Time.realtimeSinceStartup;

            CheckTerrariaDpsTime(
                dps,
                now);

            int value =
                GetTerrariaDps(
                    dps,
                    now);

            if (dps.Text == null ||
                dps.Rect == null ||
                dps.TextObject == null)
                return;

            dps.Text.text =
                value.ToString() +
                (ShowDpsSuffix(mode)
                    ? " DPS"
                    : "");

            dps.Text.color = Color.white;
            dps.TextObject.SetActive(true);

            if (state.Kind == HudKind.Boss)
            {
                LayoutBossDps(
                    state,
                    dps);
            }
            else
            {
                LayoutNameplateDps(
                    state,
                    dps);
            }
        }

        private static void SampleDpsHealthFallback(
            EnemyUiState state,
            DpsUiState dps)
        {
            if (state == null ||
                state.Character == null ||
                dps == null)
                return;

            float current =
                state.Character.GetHealth();

            if (!dps.HasLastHealth)
            {
                dps.LastHealth = current;
                dps.HasLastHealth = true;
                return;
            }

            float loss =
                dps.LastHealth -
                current;

            // This catches network-replicated health loss when the local
            // client did not execute Character.ApplyDamage and therefore
            // did not receive m_onDamaged. Healing is intentionally ignored.
            if (loss > 0.01f)
            {
                int amount =
                    Mathf.Max(
                        1,
                        Mathf.RoundToInt(loss));

                AddTerrariaDpsDamage(
                    dps,
                    amount,
                    Time.realtimeSinceStartup);
            }

            dps.LastHealth = current;
        }

        private void LayoutNameplateDps(
            EnemyUiState state,
            DpsUiState dps)
        {
            if (state == null ||
                dps == null ||
                dps.Rect == null ||
                dps.Text == null ||
                state.HealthRect == null)
                return;

            dps.Rect.localScale =
                Vector3.one;

            dps.Text.fontSize =
                dps.BaseFontSize *
                0.738f * _dpsFontScale.Value;
            ReportFontIfAdjusted(dps.Text, true);

            dps.Text.alignment =
                TextAlignmentOptions.Right;

            float textWidth =
                Mathf.Max(
                    44f,
                    dps.Text.preferredWidth + 6f);

            float textHeight =
                Mathf.Max(
                    dps.Text.preferredHeight,
                    dps.Text.fontSize * 1.2f);

            dps.Rect.sizeDelta =
                new Vector2(
                    textWidth,
                    textHeight);

            dps.Rect.anchorMin =
                state.HealthRect.anchorMin;
            dps.Rect.anchorMax =
                state.HealthRect.anchorMax;

            float healthScaleX =
                Mathf.Abs(
                    state.HealthRect.localScale.x);

            float rightX =
                state.HealthRect.anchoredPosition.x +
                state.HealthRect.rect.xMax *
                healthScaleX;

            RectTransform lowestBar =
                state.HealthRect;

            bool staggerVisible =
                state.StaggerRoot != null &&
                state.StaggerRoot.activeSelf &&
                state.StaggerRect != null;

            if (staggerVisible)
                lowestBar = state.StaggerRect;

            float barScaleY =
                Mathf.Abs(
                    lowestBar.localScale.y);

            float bottomY =
                lowestBar.anchoredPosition.y +
                lowestBar.rect.yMin *
                barScaleY;

            // Always put DPS below the lowest visible bar. This keeps it
            // out of both enemy and player health bars while preserving
            // the existing right-edge padding.
            float rowY =
                bottomY -
                textHeight * 0.55f -
                2f;

            dps.Rect.pivot =
                new Vector2(1f, 0.5f);

            dps.Rect.anchoredPosition =
                new Vector2(
                    rightX,
                    rowY);
        }

        private void LayoutBossDps(
            EnemyUiState state,
            DpsUiState dps)
        {
            if (state == null ||
                dps == null ||
                dps.Rect == null ||
                dps.Text == null ||
                state.HealthRect == null)
                return;

            dps.Rect.localScale =
                Vector3.one;

            dps.Text.fontSize =
                dps.BaseFontSize *
                0.738f * _dpsFontScale.Value;
            ReportFontIfAdjusted(dps.Text, true);

            float textWidth =
                Mathf.Max(
                    80f,
                    dps.Text.preferredWidth + 12f);

            float textHeight =
                Mathf.Max(
                    dps.Text.preferredHeight,
                    dps.Text.fontSize * 1.2f);

            dps.Rect.sizeDelta =
                new Vector2(
                    textWidth,
                    textHeight);

            Rect barRect =
                state.HealthRect.rect;

            bool tracked =
                _bossHealthTextPosition.Value ==
                BossHealthTextPosition.Tracked;

            float hp =
                Mathf.Max(
                    0f,
                    state.Character.GetHealth());

            float max =
                Mathf.Max(
                    1f,
                    state.Character.GetMaxHealth());

            float healthPercentage =
                Mathf.Clamp01(
                    hp / max);

            float localX =
                barRect.center.x;

            if (tracked)
            {
                if (state.HealthFastFill != null)
                {
                    Rect fillRect =
                        state.HealthFastFill.rect;

                    Vector3 fillRightWorld =
                        state.HealthFastFill.TransformPoint(
                            new Vector3(
                                fillRect.xMax,
                                fillRect.center.y,
                                0f));

                    localX =
                        state.HealthRect.InverseTransformPoint(
                            fillRightWorld).x;
                }
                else
                {
                    localX =
                        Mathf.Lerp(
                            barRect.xMin,
                            barRect.xMax,
                            healthPercentage);
                }
            }

            bool healthTextVisible =
                _bossHealthDisplayMode != null &&
                _bossHealthDisplayMode.Value != "None";

            float hpTextHeight =
                textHeight;

            if (healthTextVisible &&
                state.HealthValueText != null)
            {
                hpTextHeight =
                    Mathf.Max(
                        state.HealthValueText.preferredHeight,
                        state.HealthValueText.fontSize * 1.2f);
            }

            float normalHealthY =
                barRect.yMin -
                hpTextHeight * 0.55f -
                2f;

            float localY =
                healthTextVisible
                    ? normalHealthY -
                      (hpTextHeight + textHeight) * 0.5f
                    : barRect.yMin -
                      textHeight * 0.55f -
                      2f;

            dps.Rect.pivot =
                tracked
                    ? new Vector2(1f, 0.5f)
                    : new Vector2(0.5f, 0.5f);

            dps.Text.alignment =
                tracked
                    ? TextAlignmentOptions.Right
                    : TextAlignmentOptions.Center;

            Vector3 worldPoint =
                state.HealthRect.TransformPoint(
                    new Vector3(
                        localX,
                        localY,
                        0f));

            dps.Rect.position =
                worldPoint;
        }
    }
}
