using BepInEx.Configuration;

namespace EnemyHudRework
{
    public sealed partial class EnemyHudReworkPlugin
    {
        private ConfigEntry<string> _preset;
        private bool _applyingPreset;

        private void OnPresetSettingChanged(
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
                ResetIconLook();
                switch (preset)
                {
                    case "Vanilla":
                        ApplyVanillaVisualValues();
                        _staggerBarEnabled.Value = false;
                        ApplyVisibilityPreset(false);
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
                        _bossDpsMode.Value = "On + DPS";
                        _bossNameColorMode.Value = BossNameColorMode.White;
                        _playerHealthDisplayMode.Value = "Current HP/Max HP";
                        _doublePlayerHealthBar.Value = true;
                        _staggerBarEnabled.Value = true;
                        _transparentBackground.Value = true;
                        _staggerVerticalOffset.Value = 8f;
                        _starRowHeight.Value = StarRowHeightDefault;
                        _hideForNonStaggerable.Value = true;
                        ApplyVisibilityPreset(true);
                        _hpFontScale.Value = 1.267f;
                        _dpsFontScale.Value = 1.267f;
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
            _bossDpsMode.Value = "Off";
            _bossNameColorMode.Value = BossNameColorMode.Vanilla;
            _playerHealthDisplayMode.Value = "None";
            _doublePlayerHealthBar.Value = false;
            _transparentBackground.Value = false;
            _staggerVerticalOffset.Value = 8f;
            _starRowHeight.Value = 0f;
            _hideForNonStaggerable.Value = true;
        }

        private void OnPresetDestroy()
        {
            Config.SettingChanged -= OnPresetSettingChanged;
        }
    }
}
