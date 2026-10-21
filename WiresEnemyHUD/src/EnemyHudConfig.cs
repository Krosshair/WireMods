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
        private const string SecEnemy = "02 - Nameplates";
        private const string SecHud = "03 - Boss Bar";
        private const string SecModded = "04 - Custom Bars";
        private const string SecVisibility = "05 - Visibility";
        private const string SecWeak = "06 - Damage Icons";
        private const string SecPredict = "07 - Hit Predictions";
        private const string SecIconColors = "08 - Icon Shapes and Colors";
        private const string SecTuning = "09 - Fine Tuning";
        private const string SecSync = "10 - Multiplayer";
        private const string SecDebug = "11 - Debug";
        private const string SecPlayer = SecEnemy, SecBoss = SecHud, SecStagger = SecModded, SecFocus = SecVisibility;

        // Earlier layouts, for carrying saved values over: O = 0.8.0 to 0.8.3,
        // L and K = the 0.8.4 test builds.
        private const string O1 = "01 - General", O2 = "02 - Enemy Bars", O3 = "03 - Weakness Icons", O4 = "04 - Stagger Bar",
            O5 = "05 - Boss Bar", O6 = "06 - Player Nameplates", O7 = "07 - Appearance", O8 = "08 - Damage Icons",
            O9 = "09 - Multiplayer", O10 = "10 - Debug", O11 = "11 - Hit Predictions", O12 = "12 - Bubble Bar";
        private const string L3 = "03 - Visibility", L4 = "04 - Focus", L5 = "05 - Damage Icons", L6 = "06 - Icon Shapes and Colors",
            L7 = "07 - Stagger Bar", L8 = "08 - Boss Bar", L9 = "09 - Player Nameplates", L10 = "10 - Hit Predictions", L11 = "11 - Fine Tuning";
        private const string K2 = "02 - Creature Bars", K3 = "03 - Modded Bars", K4 = "04 - Visibility", K5 = "05 - Focus",
            K7 = "07 - Boss Bar", K8 = "08 - Player Nameplates", K9 = "09 - Hit Predictions", K10 = "10 - Icon Shapes and Colors",
            K11 = "11 - Fine Tuning", K12 = "12 - Multiplayer", K13 = "13 - Debug";
        private const string M2 = "02 - In-World Nameplates", M3 = "03 - HUD", M5 = "05 - Visibility";

        private static readonly string[] HealthTextModes =
            { "Current HP", "Current HP/Max HP", "Percentage", "None" };

        private int _order;
        // Halfway between vanilla and the bottom of the bar's dark frame.
        private const float StarRowHeightDefault = -2f;

        // Saved values that moved to another section or name: moved before
        // anything binds, so they're picked up as if they'd always been there.
        private void MoveKey(string fromSection, string fromKey, string toSection, string toKey)
        {
            var orphans = Orphans();
            if (orphans == null) return;
            var from = new ConfigDefinition(fromSection, fromKey);
            string value;
            if (!orphans.TryGetValue(from, out value)) return;
            orphans.Remove(from);
            // A value already saved under the new place is newer: it stays.
            var to = new ConfigDefinition(toSection, toKey);
            if (!orphans.ContainsKey(to)) orphans[to] = value;
        }

        // new section, new key, then (old section, old key) pairs, oldest
        // first. The newest saved value wins.
        private static readonly string[][] Moves =
        {
            new[] { SecEnemy, "Transparent background", O7, "Transparent bar background", O2, "Transparent bar background", K2, "Transparent background", M2, "Transparent background" },
            new[] { SecEnemy, "Creature HP numbers", O2, "Health display", K2, "HP numbers", M2, "Creature HP numbers" },
            new[] { SecEnemy, "Double size creature bars", O2, "Double size", K2, "Double size", M2, "Double size creature bars" },
            new[] { SecEnemy, "Uppercase creature names", O2, "Uppercase names", K2, "Uppercase names", M2, "Uppercase names" },
            new[] { SecEnemy, "Star count past two stars", O2, "Star count past two stars", K2, "Star count past two stars", M2, "Star count past two stars" },
            new[] { SecEnemy, "Critical hit flash", O2, "Critical hit flash", K2, "Critical hit flash", M2, "Critical hit flash" },
            new[] { SecEnemy, "Status effects", O2, "Status effects", K2, "Status effects", M2, "Status effects" },
            new[] { SecEnemy, "Awareness display", O2, "Awareness display", K2, "Awareness display", M2, "Awareness display" },
            new[] { SecEnemy, "Player HP numbers", O6, "Health display", L9, "Health display", K8, "HP numbers", M2, "Player HP numbers" },
            new[] { SecEnemy, "Double size player bars", O6, "Double size", L9, "Double size", K8, "Double size", M2, "Double size player bars" },
            new[] { SecHud, "HP numbers", O5, "Health display", L8, "Health display", K7, "HP numbers", M3, "Boss HP numbers" },
            new[] { SecHud, "HP numbers position", O5, "Health text position", L8, "Health text position", K7, "HP numbers position", M3, "Boss HP numbers position" },
            new[] { SecHud, "DPS", O5, "DPS", L8, "DPS", K7, "DPS", M3, "Boss DPS" },
            new[] { SecHud, "Name color", O5, "Name color", L8, "Name color", K7, "Name color", M3, "Boss name color" },
            new[] { SecModded, "Stagger bar", O4, "Enabled", L7, "Enabled", K3, "Stagger bar" },
            new[] { SecModded, "Stagger recovery overlay", O4, "Recovery overlay", L7, "Recovery overlay", K3, "Stagger recovery overlay" },
            new[] { SecModded, "Bubble bar on nameplates", O12, "Bubble bar on nameplates", K3, "Bubble bar on nameplates" },
            new[] { SecModded, "Bubble bar on your HUD", O12, "Bubble bar on your HUD", K3, "Bubble bar on your HUD" },
            new[] { SecVisibility, "Reveal on hit or parry", L3, "Reveal on hit or parry", K4, "Reveal on hit or parry" },
            new[] { SecVisibility, "Keep nameplates on screen", O2, "Keep bars on screen", L3, "Keep bars on screen", K4, "Keep bars on screen", M5, "Keep bars on screen" },
            new[] { SecVisibility, "Creature HP numbers only on hover", O2, "Dynamic HP numbers", L4, "Dynamic HP numbers", K5, "Creature HP numbers only on focus" },
            new[] { SecVisibility, "Player HP numbers only on hover", O6, "Dynamic HP numbers", L4, "Player HP numbers", K5, "Player HP numbers only on focus" },
            new[] { SecVisibility, "Damage icons only on hover", O3, "Dynamic mode", L4, "Dynamic icons", K5, "Damage icons only on focus" },
            new[] { SecVisibility, "Fade creature nameplates unless hovered", O2, "Fade unless looked at", L4, "Fade unless looked at", K5, "Fade creatures out of focus", M5, "Fade creatures unless hovered" },
            new[] { SecVisibility, "Fade player nameplates unless hovered", O6, "Fade unless aimed at", L4, "Fade players unless aimed at", K5, "Fade players out of focus", M5, "Fade players unless hovered" },
            new[] { SecWeak, "Nameplate icons", L5, "Enemy icons", SecWeak, "Creature icons" },
            new[] { SecWeak, "Boss icons", L5, "Boss icons" },
            new[] { SecWeak, "Immunities", L5, "Immunities" },
            new[] { SecWeak, "Limited mode", O3, "Limited mode", L5, "Limited mode" },
            new[] { SecWeak, "Hit response", L5, "Hit response" },
            new[] { SecWeak, "Icon style", O8, "Icon style", L5, "Icon style" },
            new[] { SecTuning, "Text size", O7, "HP text size", K11, "Text size" },
            new[] { SecTuning, "Star row height", O2, "Star row height", K11, "Star row height" },
            new[] { SecTuning, "Stagger bar gap", O4, "Gap below health bar", K11, "Stagger bar gap" },
            new[] { SecTuning, "Icon size", O3, "Icon size", K11, "Icon size" },
            new[] { SecTuning, "Icons per row", O3, "Icons per row", K11, "Icons per row" },
            new[] { SecTuning, "Boss icon height", O3, "Boss icon height", K11, "Boss icon height" },
            new[] { SecTuning, "Boss icon spacing", O3, "Boss icon spacing", K11, "Boss icon spacing" },
            new[] { SecTuning, "Hover delay", O3, "Dynamic mode delay", O6, "Delay", O2, "Delay", L11, "Look-away delay", K11, "Focus delay" },
            new[] { SecTuning, "Limited mode delay", O3, "Limited mode delay", K11, "Limited mode delay" },
            new[] { SecSync, "Sync info", O9, "Sync stagger", K12, "Sync info" },
        };

        private void MigrateLayout()
        {
            // 0.8.0: the bar and text color sections are gone.
            DropSection("08 - Bar Colors");
            DropSection("09 - Text Colors");
            DropSection("Colors");
            DropSection("08 - Colors");
            DropSection("10 - Icon Colors");
            DropSection("08 - Icon Colors");
            MoveSection("13 - Hit Predictions", SecPredict);
            MoveSection("14 - Bubble Bar", O12);

            // Newest source first; older ones only fill in what's missing.
            foreach (string[] m in Moves)
                for (int i = m.Length - 2; i >= 2; i -= 2)
                    MoveKey(m[i], m[i + 1], m[0], m[1]);
            // Whole sections that only moved.
            MoveSection(K9, SecPredict);
            MoveSection(L10, SecPredict);
            MoveSection(O11, SecPredict);
            MoveSection(K10, SecIconColors);
            MoveSection(L6, SecIconColors);
            MoveSection(O8, SecIconColors);
            MoveSection(K13, SecDebug);
            MoveSection(O10, SecDebug);
        }

        private void BindConfig()
        {
            MigrateLayout();

            // 0.6.0 "Features" values carry over into the new keys below.
            string oldAwareness, oldUppercase;
            TakeOrphan("Features", "NameAwarenessColors", out oldAwareness);
            TakeOrphan("Features", "UppercaseEnemyNames", out oldUppercase);

            // Within a section, each group starts with a dropdown where it
            // can: Configuration Manager draws a divider above dropdowns.

            // ---- 01 General ----
            _order = 1000;
            _preset = Bind(SecGeneral, "Preset", "Author's Choice",
                "Author's Choice applies my preferred settings, Vanilla a vanilla-style starting point. Picking either one overwrites the settings below.",
                new AcceptableValueList<string>("Author's Choice", "Vanilla"), null,
                "General", "Preset");
            Config.SettingChanged += OnPresetSettingChanged;
            Config.SettingChanged += (s, e) => { MarkAllWeaknessDirty(); InvalidatePredictions(); _layoutVersion++; };

            // ---- 02 Nameplates ----
            // All bars, then creatures (look, then extras), then players.
            _order = 1000;
            _transparentBackground = Bind(SecEnemy, "Transparent background", true,
                "Make the gray background behind every bar the mod touches (health, stagger, bubble) see-through, like your own HUD bars.",
                null, null, "Bars", "Transparent bar background");
            _enemyHealthDisplayMode = Bind(SecEnemy, "Creature HP numbers", "Current HP",
                "HP numbers on creature bars: current HP, current/max HP, percentage, or None.",
                new AcceptableValueList<string>(HealthTextModes), null,
                "Enemy Health Bar", "Health display");
            _doubleEnemyHealthBar = Bind(SecEnemy, "Double size creature bars", true,
                "Doubles the thickness of creature health bars while keeping their width.",
                null, null, "Enemy Health Bar", "Double size");
            _uppercaseNames = Bind(SecEnemy, "Uppercase creature names", false,
                "Show creature names in uppercase.",
                null, null, "Enemy Health Bar", "Uppercase names");
            _starCount = Bind(SecEnemy, "Star count past two stars", true,
                "Creatures with more than two stars (from level mods) show one star and the count next to it, instead of the game's nothing. Also keeps other mods from switching the stars around. Off is the game's own stars.",
                null, null);
            _starCount.SettingChanged += (s, e) =>
            {
                foreach (EnemyUiState st in _states.Values)
                    if (st != null && st.Character != null) ApplyLevelStars(st, st.Character);
            };
            _criticalHitFlash = Bind(SecEnemy, "Critical hit flash", true,
                "A creature bar's frame flashes white, like the game's own flashes, when a backstab or a hit on a staggered creature lands. Never on the killing blow.",
                null, null, "Enemy Health Bar", "Critical hit flash");
            _statusEffects = Bind(SecEnemy, "Status effects", true,
                "Icons left of a creature's or player's health bar while they're burning, frozen, shocked, poisoned or hit by spirit: the same icons as those damage types. With Icon style on Game icons, the game's own status icons instead. Other players need Enemy HUD for theirs to show.",
                null, null);
            _nameColorMode = Bind(SecEnemy, "Awareness display", NameColorMode.Pastel,
                "How a creature shows that it noticed you. Pastel: soft name colors. Normal: stronger Valheim name colors. Icons: the vanilla awareness icons. None: nothing.",
                null, null, "Enemy Health Bar", "Awareness display");
            _playerHealthDisplayMode = Bind(SecEnemy, "Player HP numbers", "Current HP/Max HP",
                "HP numbers on other players' bars: current HP, current/max HP, percentage, or None.",
                new AcceptableValueList<string>(HealthTextModes), null,
                "Player Health Bar", "Health display");
            _doublePlayerHealthBar = Bind(SecEnemy, "Double size player bars", true,
                "Doubles the thickness of other players' health bars while keeping their width.",
                null, null, "Player Health Bar", "Double size");

            if (oldAwareness != null)
                _nameColorMode.Value =
                    oldAwareness == "Normal" ? NameColorMode.Normal :
                    oldAwareness == "Pastel" ? NameColorMode.Pastel : NameColorMode.Icons;
            if (oldUppercase != null)
                _uppercaseNames.Value = string.Equals(oldUppercase, "true", StringComparison.OrdinalIgnoreCase);

            // ---- 03 Boss bar ----
            _order = 1000;
            _bossHealthDisplayMode = Bind(SecHud, "HP numbers", "Current HP/Max HP",
                "HP numbers on boss bars: current HP, current/max HP, percentage, or None.",
                new AcceptableValueList<string>(HealthTextModes), null,
                "Boss Health Bar", "Health display");
            _bossHealthTextPosition = Bind(SecHud, "HP numbers position", BossHealthTextPosition.Tracked,
                "Tracked follows the right edge of the boss health fill, never past the bar's left end. Centered keeps the HP numbers and DPS in the middle of the bar.",
                null, () => _bossHealthDisplayMode != null && _bossHealthDisplayMode.Value == "None" &&
                    _bossDpsMode != null && _bossDpsMode.Value == "Off",
                "Boss Health Bar", "Health counter position");
            _bossDpsMode = Bind(SecHud, "DPS", "On + DPS",
                "Damage per second under the boss HP numbers, from all damage the boss takes. On shows the number (230), On + DPS adds the label (230 DPS).",
                new AcceptableValueList<string>("On", "On + DPS", "Off"), null,
                "DPS Meter", "Boss DPS");
            _bossNameColorMode = Bind(SecHud, "Name color", BossNameColorMode.White,
                "Boss name color: Pastel is soft pink, Vanilla is Valheim red, White is white.",
                null, null, "Boss Health Bar", "Boss name color");

            // ---- 04 Custom bars: stagger, then bubble ----
            _order = 1000;
            Func<bool> staggerOff = () => _staggerBarEnabled != null && !_staggerBarEnabled.Value;
            _staggerBarEnabled = Bind(SecModded, "Stagger bar", true,
                "A stagger meter under creature and player health bars, styled after your own. It fills as they take stagger damage and flashes when they stagger.",
                null, null, "Stagger Bar", "Enabled");
            _staggerRecoveryEnabled = Bind(SecModded, "Stagger recovery overlay", true,
                "During a stagger, a yellow layer recedes across the stagger bar over the creature's real stagger time (the window in which your hits deal double damage).",
                null, staggerOff, "Stagger Bar", "Recovery overlay");
            BindBubbleConfig(SecModded);

            // ---- 05 Visibility: when bars show, then hover ----
            _order = 1000;
            // 0.8.4: "Show on damage" and "Show on parry" became one setting.
            string oldDamage, oldParry, oldDamage0;
            bool hadDamage = TakeOrphan(O2, "Show on damage", out oldDamage);
            bool hadParry = TakeOrphan(O2, "Show on parry", out oldParry);
            bool hadDamage0 = TakeOrphan("Enemy Health Bar", "Show on damage", out oldDamage0);
            _showOnDamage = Bind(SecVisibility, "Reveal on hit or parry", true,
                "Show a creature's bar for 3 seconds after you hit it or parry its attack; each one restarts the timer. While revealed it also counts as hovered for the hover settings below.",
                null, null);
            if (hadDamage || hadParry || hadDamage0)
                _showOnDamage.Value = IsTrue(hadDamage ? oldDamage : oldDamage0) || IsTrue(oldParry);
            _showOnDamage.SettingChanged += (s, e) => OnShowOnDamageChanged();
            _showOnParry = _showOnDamage;
            _keepNameplatesOnScreen = Bind(SecVisibility, "Keep nameplates on screen", true,
                "Keep visible creature nameplates within the screen edges. Ones above the screen slide along the top edge.",
                null, null, "Enemy Health Bar", "Keep bars on screen");
            // Hover: the creature you're looking at (or just hit or
            // parried), the player your crosshair is on. HP numbers, then
            // icons, then fading.
            _dynamicHp = Bind(SecVisibility, "Creature HP numbers only on hover", false,
                "HP numbers only show on the creature you're looking at, or just hit or parried. Bosses always show theirs.",
                null, null);
            _playerDynamicHp = Bind(SecVisibility, "Player HP numbers only on hover", false,
                "HP numbers on other players' bars only show on the player your crosshair is on.",
                null, null);
            _dynamicMode = Bind(SecVisibility, "Damage icons only on hover", true,
                "Damage type icons only show on the creature you're looking at, or just hit or parried. Bosses always show theirs. Works together with Limited mode.",
                null, null);
            _enemyAimFade = Bind(SecVisibility, "Fade creature nameplates unless hovered", false,
                "Dim creature nameplates to 10% unless you're looking at that creature, or just hit or parried it. Bosses always show fully.",
                null, null);
            _playerNameplateAimFade = Bind(SecVisibility, "Fade player nameplates unless hovered", true,
                "Dim other players' nameplates to 10% unless your crosshair is on them.",
                null, null, "Player Health Bar", "Fade unfocused player nameplates");

            // ---- 06 Damage icons: what shows, then how ----
            _order = 1000;
            // 0.8.4: four on/off settings became two choices.
            string ow, orr, obw, obr, ow0, or0;
            bool hw = TakeOrphan(O3, "Enemy weaknesses", out ow), hr = TakeOrphan(O3, "Enemy resistances", out orr);
            bool hbw = TakeOrphan(O3, "Boss weaknesses", out obw), hbr = TakeOrphan(O3, "Boss resistances", out obr);
            bool hw0 = TakeOrphan("Enemy Health Bar", "Weakness icons", out ow0), hr0 = TakeOrphan("Enemy Health Bar", "Resistance icons", out or0);
            // On/off behaviors first, then each choice under its own divider.
            _limitedMode = Bind(SecWeak, "Limited mode", true,
                "Only show icons for the damage types you can deal right now: your weapon in hand, plus its ammo.",
                null, null);
            _limitedMode.SettingChanged += (s, e) => OnLimitedModeChanged();
            _hitResponse = Bind(SecWeak, "Hit response", true,
                "When a creature is hit by a damage type it's weak to, that icon flashes white and swells for a moment; types it resists or is immune to go darker and shrink instead.",
                null, null);
            _enemyIconShow = Bind(SecWeak, "Nameplate icons", IconShow.Both,
                "Damage type icons at the right end of a nameplate's star row: weaknesses in color, resistances in faint gray. Read from the creature itself, so modded creatures work too.",
                null, null);
            _playerIcons = Bind(SecWeak, "Resistances for player nameplates", true,
                "Shows other players' damage type icons too, with the same settings as other nameplates: what their armor and status effects make them weak or resistant to. Limited mode doesn't apply to them, as they're there to help in multiplayer.",
                null, null);
            _bossIconShow = Bind(SecWeak, "Boss icons", IconShow.Both,
                "Damage type icons on boss bars: weaknesses left of the name, resistances right of it.",
                null, null);
            if (hw || hr || hw0 || hr0)
                _enemyIconShow.Value = ToIconShow(hw || hw0 ? IsTrue(hw ? ow : ow0) : true, hr || hr0 ? IsTrue(hr ? orr : or0) : true);
            if (hbw || hbr)
                _bossIconShow.Value = ToIconShow(hbw ? IsTrue(obw) : true, hbr ? IsTrue(obr) : true);
            _immunityShow = Bind(SecWeak, "Immunities", ImmunityShow.Notable,
                "Damage types a creature is immune to, as darker icons after its resistances. Notable only leaves out spirit, chop and pickaxe, which most creatures are immune to; All shows every immunity. With All, Limited mode is recommended to keep it uncluttered.",
                null, null);
            _iconStyle = Bind(SecWeak, "Icon style", IconStyle.Shapes,
                "The look of every icon the mod draws: weaknesses, resistances, immunities and status effects. Shapes are made to be told apart at a glance at this size, with the shapes and colors set in Icon Shapes and Colors. Game icons uses the game's own skill and status effect icons instead (axe for chop, clubs for blunt, the poison effect for poison, ectoplasm for spirit...). NOT recommended: they are detailed pictures, hard to tell apart this small.",
                null, null);
            _iconStyle.SettingChanged += (s, e) => RefreshIconLook();

            // ---- 07 Hit predictions ----
            BindHitPredictionConfig(SecPredict);

            // ---- 08 Icon shapes and colors ----
            BindColorSections();

            // ---- 09 Fine tuning ----
            _order = 1000;
            // Sizes, then positions, then timing.
            _hpFontScale = Bind(SecTuning, "Text size", 1.267f,
                "Scale of the mod's numbers: HP numbers on all bars and the boss DPS. 1.0 is the original size.",
                new AcceptableValueRange<float>(.5f, 2.5f), null,
                "Text", "HP and percent font size");
            _dpsFontScale = _hpFontScale;
            _iconScale = Bind(SecTuning, "Icon size", 1f,
                "Size of the damage type icons, relative to the level stars. 1.0 is exactly star size.",
                new AcceptableValueRange<float>(0.5f, 2f), null);
            _iconsPerRow = Bind(SecTuning, "Icons per row", 4,
                "How many icons fit in a creature's star row before the next row starts below.",
                new AcceptableValueRange<int>(1, 9), null);
            _starRowHeight = Bind(SecTuning, "Star row height", StarRowHeightDefault,
                "Height of the level stars and damage type icons under the bar. 0 is the vanilla height; lower numbers move them down, higher up. They still drop below the stagger bar when it shows.",
                new AcceptableValueRange<float>(-12f, 12f), null);
            _staggerVerticalOffset = Bind(SecTuning, "Stagger bar gap", 8f,
                "Space between the health bar and the stagger bar. Higher moves the stagger bar lower; the star row moves with it.",
                new AcceptableValueRange<float>(0f, 24f), null, "Stagger Bar", "VerticalOffset");
            _bossIconHeight = Bind(SecTuning, "Boss icon height", -2f,
                "Height of the boss icons. 0 stands them on the name's baseline; lower moves them down.",
                new AcceptableValueRange<float>(-20f, 20f), null);
            _bossIconSpacing = Bind(SecTuning, "Boss icon spacing", 3f,
                "Space between the boss name and its icons, on both sides. Higher moves weaknesses further left and resistances further right.",
                new AcceptableValueRange<float>(-10f, 40f), null);
            _lookAwayDelay = Bind(SecTuning, "Hover delay", 0f,
                "For the hover settings in Visibility: how many seconds a creature or player still counts as hovered after you look away (or after a hit or parry reveal ends). 0 is right away.",
                new AcceptableValueRange<float>(0f, 3f), null);
            _dynamicDelay = _dynamicHpDelay = _playerAimDelay = _lookAwayDelay;
            _limitedDelay = Bind(SecTuning, "Limited mode delay", 0f,
                "How many seconds icons for a weapon you switched away from stay shown in Limited mode. 0 hides them right away.",
                new AcceptableValueRange<float>(0f, 3f), null);

            // ---- 10 Multiplayer ----
            _order = 1000;
            _syncStaggerForMultiplayer = Bind(SecSync, "Sync info", true,
                "Share what only your game knows with other players who have Enemy HUD: stagger, bubbles, status effects, critical hits and hit responses of creatures your game runs. Off keeps it all to yourself (and others won't see those for your creatures).",
                null, null, "Multiplayer", "SyncStaggerPercentage", "11 - Multiplayer", "Sync stagger");

            // ---- 11 Debug ----
            _order = 1000;
            _debugBossHealthBar = Bind(SecDebug, "Test boss bar", false,
                "Show a test boss bar using your own character's health and resistances.",
                null, null, "Debug", "(Debug) Player boss health bar", "12 - Debug", "Test boss bar");
            _debugSeeOwnHealthBar = Bind(SecDebug, "See your own nameplate", false,
                "Show your own multiplayer nameplate above your character.",
                null, null, "Debug", "See your own nameplate", "12 - Debug", "See your own nameplate");
            // Only shown with "Advanced settings" ticked in the config manager.
            MarkAdvanced(_debugBossHealthBar);
            MarkAdvanced(_debugSeeOwnHealthBar);

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
                O4, "Star row drop",
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
                "Boss Health Bar", "Double size",
                O9, "Sync interval (seconds)",
                O9, "Sync minimum change",
                O7, "Health segments",
                O7, "Boss DPS text size",
                O4, "Hide on unstaggerable enemies",
                O3, "Damage icons in tooltips",
                "Stagger Bar", "Hide on unstaggerable enemies"
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

        private Dictionary<ConfigDefinition, string> Orphans()
        {
            return AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                ?.GetValue(Config) as Dictionary<ConfigDefinition, string>;
        }

        private void DropSection(string section)
        {
            var orphans = Orphans();
            if (orphans == null) return;
            foreach (ConfigDefinition d in new List<ConfigDefinition>(orphans.Keys))
                if (d.Section == section) orphans.Remove(d);
        }

        // Saved values of a renamed section, moved to its new name before
        // anything binds there (the newer name loses to the moved values).
        private void MoveSection(string from, string to)
        {
            var orphans = Orphans();
            if (orphans == null) return;
            foreach (ConfigDefinition d in new List<ConfigDefinition>(orphans.Keys))
            {
                if (d.Section != from) continue;
                var target = new ConfigDefinition(to, d.Key);
                if (!orphans.ContainsKey(target)) orphans[target] = orphans[d]; // newer stays
                orphans.Remove(d);
            }
        }

        private static void MarkAdvanced(ConfigEntryBase entry)
        {
            foreach (object tag in entry.Description.Tags)
                if (tag is ConfigurationManagerAttributes a) a.IsAdvanced = true;
        }

        private static bool IsTrue(string v)
        {
            return string.Equals(v != null ? v.Trim() : null, "true", StringComparison.OrdinalIgnoreCase);
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
