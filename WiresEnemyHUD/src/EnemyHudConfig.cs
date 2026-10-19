using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace EnemyHudRework
{
    // All settings in one place, in the order Configuration Manager shows
    // them: most used first, related settings together, debug last.
    // Values saved under the old section and key names carry over once.
    public sealed partial class EnemyHudReworkPlugin
    {
        private const string SecGeneral = "01 - General";
        private const string SecEnemy = "02 - Enemy Bars";
        private const string SecWeak = "03 - Weakness Icons";
        private const string SecStagger = "04 - Stagger Bar";
        private const string SecBoss = "05 - Boss Bar";
        private const string SecPlayer = "06 - Player Nameplates";
        private const string SecLook = "07 - Appearance";
        private const string SecBarColors = "08 - Bar Colors";
        private const string SecTextColors = "09 - Text Colors";
        private const string SecIconColors = "10 - Icon Colors";
        private const string SecSync = "11 - Multiplayer";
        private const string SecDebug = "12 - Debug";
        private const string SecOldColors = "Colors";

        private static readonly string[] HealthTextModes =
            { "Current HP", "Current HP/Max HP", "Percentage", "None" };

        private int _order;
        // Halfway between vanilla and the bottom of the bar's dark frame.
        private const float StarRowHeightDefault = -2f;

        private void BindConfig()
        {
            // 0.6.0 "Features" values carry over into the new keys below.
            string oldAwareness, oldUppercase;
            TakeOrphan("Features", "NameAwarenessColors", out oldAwareness);
            TakeOrphan("Features", "UppercaseEnemyNames", out oldUppercase);

            // ---- 01 General ----
            _order = 1000;
            _preset = Bind(SecGeneral, "Preset", "Author's Choice",
                "Author's Choice applies my preferred settings, Vanilla a vanilla-style starting point. Picking either one overwrites the settings below.",
                new AcceptableValueList<string>("Author's Choice", "Vanilla"), null,
                "General", "Preset");
            Config.SettingChanged += OnPresetSettingChanged;

            // ---- 02 Enemy bars ----
            _order = 1000;
            _enemyHealthDisplayMode = Bind(SecEnemy, "Health display", "Current HP",
                "HP text on enemy bars: current HP, current/max HP, percentage, or None.",
                new AcceptableValueList<string>(HealthTextModes), null,
                "Enemy Health Bar", "Health display");
            _doubleEnemyHealthBar = Bind(SecEnemy, "Double size", true,
                "Doubles the thickness of enemy health bars while keeping their width.",
                null, null, "Enemy Health Bar", "Double size");
            _showOnDamage = Bind(SecEnemy, "Show on damage", true,
                "Reveal an enemy's bar for 3 seconds after you damage it. Each hit restarts the timer.",
                null, null, "Enemy Health Bar", "Show on damage");
            _keepNameplatesOnScreen = Bind(SecEnemy, "Keep bars on screen", true,
                "Keep visible enemy bars within the screen edges. Bars above the screen slide along the top edge.",
                null, null, "Enemy Health Bar", "Keep bars on screen");
            _nameColorMode = Bind(SecEnemy, "Awareness display", NameColorMode.Pastel,
                "How an enemy shows that it noticed you. Pastel: soft name colors. Normal: stronger Valheim name colors. Icons: the vanilla awareness icons. None: nothing.",
                null, null, "Enemy Health Bar", "Awareness display");
            _starRowHeight = Bind(SecEnemy, "Star row height", StarRowHeightDefault,
                "Height of the level stars and weakness icons under the bar. 0 is the vanilla height; lower numbers move them down, higher up. They still drop below the stagger meter when it shows.",
                new AcceptableValueRange<float>(-12f, 12f), null);
            _uppercaseNames = Bind(SecEnemy, "Uppercase names", false,
                "Show ordinary enemy names in uppercase.",
                null, null, "Enemy Health Bar", "Uppercase names");
            _criticalHitFlash = Bind(SecEnemy, "Critical hit flash", true,
                "Briefly outline an enemy bar in white when a backstab or a hit on a staggered enemy lands.",
                null, null, "Enemy Health Bar", "Critical hit flash");

            if (oldAwareness != null)
                _nameColorMode.Value =
                    oldAwareness == "Normal" ? NameColorMode.Normal :
                    oldAwareness == "Pastel" ? NameColorMode.Pastel : NameColorMode.Icons;
            if (oldUppercase != null)
                _uppercaseNames.Value = string.Equals(oldUppercase, "true", StringComparison.OrdinalIgnoreCase);

            // ---- 03 Weakness icons ----
            _order = 1000;
            _weaknessIcons = Bind(SecWeak, "Enemy weaknesses", true,
                "Colored icons for what an enemy is weak to, at the right end of its star row. Read from the creature itself, so modded creatures work too.",
                null, null, "Enemy Health Bar", "Weakness icons");
            _resistanceIcons = Bind(SecWeak, "Enemy resistances", true,
                "Also show what an enemy resists, as faint gray icons after its weaknesses. Immunities are not shown.",
                null, null, "Enemy Health Bar", "Resistance icons");
            _bossWeaknessIcons = Bind(SecWeak, "Boss weaknesses", true,
                "Colored icons for what a boss is weak to, left of its name.",
                null, null);
            _bossResistanceIcons = Bind(SecWeak, "Boss resistances", true,
                "Faint gray icons for what a boss resists, right of its name.",
                null, null);
            _iconScale = Bind(SecWeak, "Icon size", 1f,
                "Size of the damage type icons, relative to the level stars. 1.0 is exactly star size.",
                new AcceptableValueRange<float>(0.5f, 2f), null);
            _iconsPerRow = Bind(SecWeak, "Icons per row", 4,
                "How many icons fit in an enemy's star row before the next row starts below.",
                new AcceptableValueRange<int>(1, 9), null);
            _bossIconHeight = Bind(SecWeak, "Boss icon height", -2f,
                "Height of the boss icons. 0 stands them on the name's baseline; lower moves them down.",
                new AcceptableValueRange<float>(-20f, 20f), null);
            _bossIconSpacing = Bind(SecWeak, "Boss icon spacing", 3f,
                "Space between the boss name and its icons, on both sides. Higher moves weaknesses further left and resistances further right.",
                new AcceptableValueRange<float>(-10f, 40f), null);

            // ---- 04 Stagger bar ----
            _order = 1000;
            Func<bool> staggerOff = () => _staggerBarEnabled != null && !_staggerBarEnabled.Value;
            _staggerBarEnabled = Bind(SecStagger, "Enabled", true,
                "Show a stagger meter beneath enemy health bars. It fills as the enemy takes stagger damage.",
                null, null, "Stagger Bar", "Enabled");
            _hideForNonStaggerable = Bind(SecStagger, "Hide on unstaggerable enemies", true,
                "Hide the stagger meter on enemies that cannot be staggered.",
                null, staggerOff, "Stagger Bar", "HideForNonStaggerableEnemies");
            _staggerRecoveryEnabled = Bind(SecStagger, "Recovery overlay", true,
                "During a stagger, a yellow layer recedes across the meter over the enemy's real stagger time (the window in which your hits deal double damage).",
                null, staggerOff, "Stagger Bar", "Recovery overlay");
            _staggerVerticalOffset = Bind(SecStagger, "Gap below health bar", 8f,
                "Space between the health bar and the stagger meter. Higher moves the meter lower; the star row moves with it.",
                new AcceptableValueRange<float>(0f, 24f), staggerOff, "Stagger Bar", "VerticalOffset");

            // ---- 05 Boss bar ----
            _order = 1000;
            _bossHealthDisplayMode = Bind(SecBoss, "Health display", "Current HP/Max HP",
                "HP text on boss bars: current HP, current/max HP, percentage, or None.",
                new AcceptableValueList<string>(HealthTextModes), null,
                "Boss Health Bar", "Health display");
            _bossDpsMode = Bind(SecBoss, "DPS", "On + DPS",
                "Damage per second under the boss HP text, from all damage the boss takes. On shows the number (230), On + DPS adds the label (230 DPS).",
                new AcceptableValueList<string>("On", "On + DPS", "Off"), null,
                "DPS Meter", "Boss DPS");
            _bossHealthTextPosition = Bind(SecBoss, "Health text position", BossHealthTextPosition.Tracked,
                "Tracked follows the right edge of the boss health fill, never past the bar's left end. Centered keeps the HP and DPS text in the middle of the bar.",
                null, () => _bossHealthDisplayMode != null && _bossHealthDisplayMode.Value == "None" &&
                    _bossDpsMode != null && _bossDpsMode.Value == "Off",
                "Boss Health Bar", "Health counter position");
            _bossNameColorMode = Bind(SecBoss, "Name color", BossNameColorMode.White,
                "Boss name color: Pastel is soft pink, Vanilla is Valheim red, White is white.",
                null, null, "Boss Health Bar", "Boss name color");

            // ---- 06 Player nameplates ----
            _order = 1000;
            _playerHealthDisplayMode = Bind(SecPlayer, "Health display", "Current HP/Max HP",
                "HP text above other players: current HP, current/max HP, percentage, or None.",
                new AcceptableValueList<string>(HealthTextModes), null,
                "Player Health Bar", "Health display");
            _doublePlayerHealthBar = Bind(SecPlayer, "Double size", true,
                "Doubles the thickness of other players' health bars while keeping their width.",
                null, null, "Player Health Bar", "Double size");
            _playerNameplateAimFade = Bind(SecPlayer, "Fade unless aimed at", true,
                "Fade other players' nameplates to 25% until your crosshair is on that player.",
                null, null, "Player Health Bar", "Fade unfocused player nameplates");

            // ---- 07 Appearance ----
            _order = 1000;
            _transparentBackground = Bind(SecLook, "Transparent bar background", true,
                "Make the gray background behind health and stagger bars see-through, like your own HUD bars.",
                null, null, "Bars", "Transparent bar background");
            _hpFontScale = Bind(SecLook, "HP text size", 1.267f,
                "Scale of the HP and percentage text on all bars. 1.0 is the original size.",
                new AcceptableValueRange<float>(.5f, 2.5f), null,
                "Text", "HP and percent font size");
            _dpsFontScale = Bind(SecLook, "Boss DPS text size", 1.267f,
                "Scale of the boss DPS counter. 1.0 is the original size.",
                new AcceptableValueRange<float>(.5f, 2.5f), null);

            // ---- 08-10 Colors ----
            BindColorSections();
            RefreshFlashColors();

            // ---- 11 Multiplayer ----
            _order = 1000;
            Func<bool> syncOff = () => _syncStaggerForMultiplayer != null && !_syncStaggerForMultiplayer.Value;
            _syncStaggerForMultiplayer = Bind(SecSync, "Sync stagger", true,
                "Share stagger meter progress between players through Valheim's network data.",
                null, null, "Multiplayer", "SyncStaggerPercentage", "09 - Multiplayer", "Sync stagger");
            _syncInterval = Bind(SecSync, "Sync interval (seconds)", 0.10f,
                "Least time between stagger updates per enemy. Lower updates more often.",
                null, syncOff, "Multiplayer", "SyncIntervalSeconds", "09 - Multiplayer", "Sync interval (seconds)");
            _syncMinimumDelta = Bind(SecSync, "Sync minimum change", 0.01f,
                "Least change in stagger progress (0 to 1) before another update is sent.",
                null, syncOff, "Multiplayer", "SyncMinimumDelta", "09 - Multiplayer", "Sync minimum change");

            // ---- 12 Debug ----
            _order = 1000;
            _debugBossHealthBar = Bind(SecDebug, "Test boss bar", false,
                "Show a test boss bar using your own character's health and resistances.",
                null, null, "Debug", "(Debug) Player boss health bar", "10 - Debug", "Test boss bar");
            _debugSeeOwnHealthBar = Bind(SecDebug, "See your own nameplate", false,
                "Show your own multiplayer nameplate above your character.",
                null, null, "Debug", "See your own nameplate", "10 - Debug", "See your own nameplate");

            DropRetiredSettings();
            Config.Save();
        }

        // Settings that no longer exist; removed so the file stays clean.
        private void DropRetiredSettings()
        {
            string[] retired =
            {
                "DPS Meter", "Enemy DPS",
                "DPS Meter", "Player DPS",
                "Text", "DPS font size",
                "Stagger Bar", "StarVerticalOffset",
                SecStagger, "Star row drop",
                "Enemy Health Bar", "Weakness display",
                "Enemy Health Bar", "Weakness ring",
                "Enemy Health Bar", "Weakness hit flash",
                "Enemy Health Bar", "Low-health priority threshold (%)",
                "Status Effects", "Enabled",
                "General", "Enabled",
                "Features", "HealthNumberMode",
                "Features", "ShowMaxHealth",
                "Features", "DoubleHealthBarThickness",
                "Features", "HealthDisplay",
                "Stagger Bar", "Color",
                "Stagger Bar", "Show recovery overlay",
                "Boss Health Bar", "Double size"
            };
            string ignored;
            for (int i = 0; i + 1 < retired.Length; i += 2)
                TakeOrphan(retired[i], retired[i + 1], out ignored);
        }

        private ConfigEntry<T> Bind<T>(
            string section, string key, T value, string description,
            AcceptableValueBase range, Func<bool> readOnly,
            params string[] oldLocations)
        {
            ConfigEntry<T> entry = Config.Bind(section, key, value,
                new ConfigDescription(description, range,
                    new ConfigurationManagerAttributes { Order = _order, ReadOnlyProvider = readOnly }));
            _order -= 10;

            // Old section/key pairs, oldest first; the newest saved value wins.
            string old;
            for (int i = 0; oldLocations != null && i + 1 < oldLocations.Length; i += 2)
            {
                if (oldLocations[i] == null || !TakeOrphan(oldLocations[i], oldLocations[i + 1], out old))
                    continue;
                try { entry.SetSerializedValue(old); }
                catch (Exception) { }
            }
            return entry;
        }

        private bool TakeOrphan(string section, string key, out string value)
        {
            value = null;
            var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                ?.GetValue(Config) as Dictionary<ConfigDefinition, string>;
            var definition = new ConfigDefinition(section, key);
            if (orphans == null || !orphans.TryGetValue(definition, out value)) return false;
            orphans.Remove(definition);
            return true;
        }
    }
}
