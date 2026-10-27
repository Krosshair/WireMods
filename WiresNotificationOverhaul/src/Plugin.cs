using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class WiresNotificationOverhaulPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "wire.valheim.notificationoverhaul";
        public const string PluginName = "Wire's Notification Overhaul";
        public const string PluginVersion = "1.1.9";

        internal enum Toggle
        {
            Off = 0,
            On = 1
        }

        // ConfigurationManager discovers Order through reflection; this does
        // not create a hard dependency on any configuration UI mod.
        internal sealed class ConfigurationManagerAttributes
        {
            public int Order { get; set; }
        }

        // Presets only write alignment and coordinates when selected.
        // Both settings remain independently editable afterwards.
        internal enum PositionPreset
        {
            Default = 0,
            NearCrosshair = 1,
            BottomRight = 2
        }

        internal enum TextAlignment
        {
            Left = 0,
            Right = 1
        }

        internal static ConfigEntry<PositionPreset> Placement;
        internal static ConfigEntry<TextAlignment> NotificationAlignment;
        internal static ConfigEntry<float> IconSize;

        internal static ConfigEntry<Toggle> CompactMode;

        internal static ConfigEntry<Toggle> GroupUnlockNotifications;
        internal static ConfigEntry<float> NotificationDuration;
        internal static ConfigEntry<int> MaxVisibleNotifications;
        internal static ConfigEntry<Toggle> PickupNotificationsOnly;
        internal static ConfigEntry<Vector2> NotificationsPosition;
        internal static ConfigEntry<float> NotificationsFontSize;
        internal static ConfigEntry<Color> NotificationTextColor;
        internal static ConfigEntry<Toggle> MoveCenterNotifications;
        internal static ConfigEntry<Toggle> PreserveCenterMessageColor;
        internal static ConfigEntry<Toggle> XpGainNotifications;
        internal static ConfigEntry<Toggle> XpProgressNotifications;
        internal static ConfigEntry<Toggle> InWorldXpPopups;
        internal static ConfigEntry<Toggle> HideMovementXpPopups;
        internal static ConfigEntry<Toggle> HideRunJumpXp;
        internal static ConfigEntry<Toggle> XpProgressPercent;

        private Harmony _harmony;
        private static bool _pendingPreviousPresetMigration;

        private void Awake()
        {
            // Existing 1.0.63 crosshair coordinates were derived from the
            // crosshair's Y, not the stagger bar's Y, and omitted the
            // right-hand icon's width. Reapply either alternate preset
            // once on upgrade, when HUD geometry is available. Afterwards
            // the coordinates remain freely editable.
            bool previousPresetSemantics = false;
            try
            {
                if (File.Exists(Config.ConfigFilePath))
                {
                    using (var reader = new StreamReader(Config.ConfigFilePath))
                    {
                        string header = reader.ReadLine();
                        int idx = header == null ? -1 : header.LastIndexOf(" v", StringComparison.Ordinal);
                        if (idx >= 0 &&
                            System.Version.TryParse(header.Substring(idx + 2), out System.Version priorVersion))
                            previousPresetSemantics = priorVersion < new System.Version(1, 0, 64);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("Unable to inspect earlier position settings: " + e.Message);
            }

            try { PositionSettingsMigration.Migrate(Config); }
            catch (Exception e) { Logger.LogWarning("Position config migration failed; continuing with defaults: " + e.Message); }
            BindConfig();
            _pendingPreviousPresetMigration =
                previousPresetSemantics && Placement.Value != PositionPreset.Default;

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }
        private void BindConfig()
        {
            Config.SaveOnConfigSet = false;

            // Compact Mode is a personal UI preference, not a
            // multiplayer setting locked by the server.
            CompactMode = Config.Bind(
                "2- General",
                "Compact Mode",
                Toggle.Off,
                "Show short, icon-first notification text. Yellow center-screen messages are compacted only when moved into the left stack.");


            GroupUnlockNotifications = Config.Bind(
                "3- Notification Sources",
                "Grouped Unlock Notifications",
                Toggle.On,
                "Replace vanilla unlock popups and generic new-unlocks count with one stacked message per actual crafting station/category, cycling the icons of the exact unlocks every 0.5 seconds. Unlocks and compendium history remain intact.");



            Placement = Config.Bind(
                "3- Position",
                "Position Preset",
                PositionPreset.Default,
                new ConfigDescription(
                    "Apply a preset to Notification Text Alignment and Notification Panel Position. You can freely edit both afterward. Default = top left; NearCrosshair = to the left of the crosshair; BottomRight = screen corner.",
                    null, new ConfigurationManagerAttributes { Order = 300 }));

            NotificationAlignment = Config.Bind(
                "3- Position",
                "Notification Text Alignment",
                TextAlignment.Left,
                new ConfigDescription(
                    "Left puts icons before left-aligned text. Right puts icons after right-aligned text. Presets set this automatically, but manual changes are respected.",
                    null, new ConfigurationManagerAttributes { Order = 200 }));

            IconSize = Config.Bind(
                "2- General",
                "Icon Size",
                28f,
                new ConfigDescription(
                    "Size of notification icons in UI units, in all placements.",
                    new AcceptableValueRange<float>(12f, 72f)));

            NotificationDuration = BindLocal(
                "2- General",
                "Notification Duration",
                4f,
                "How long the notification stays visible.",
                new AcceptableValueRange<float>(0.1f, 10f));

            MaxVisibleNotifications = BindLocal(
                "2- General",
                "Max Visible Notifications",
                5,
                "How many notifications are visible at the same time.",
                new AcceptableValueRange<int>(1, 10));

            PickupNotificationsOnly = BindLocal(
                "2- General",
                "Pickup Notifications Only",
                Toggle.Off,
                "Only pickup notifications are added to the custom notification panel.");

            NotificationsPosition = Config.Bind(
                "3- Position",
                "Notification Panel Position",
                new Vector2(40f, -160f),
                new ConfigDescription(
                    "Editable hard panel coordinates. The selected preset fills in this value once; editing it later moves the notifications without being overridden.",
                    null, new ConfigurationManagerAttributes { Order = 100 }));

            NotificationsFontSize = BindLocal(
                "2- General",
                "Text Font Size",
                18f,
                "Notification panel text font size.");

            NotificationTextColor = BindLocal(
                "2- General",
                "Text Color",
                Color.white,
                "Notification text color.");

            MoveCenterNotifications = BindLocal(
                "3- Notification Sources",
                "Move Center Notifications To Stack",
                Toggle.On,
                "Move Valheim's large yellow center-screen notifications into the left notification stack instead.");

            PreserveCenterMessageColor = BindLocal(
                "3- Notification Sources",
                "Preserve Center Message Color",
                Toggle.On,
                "Keep the original yellow center-message color when those messages are moved into the left stack.");

            const string Xp = "4- XP Notifications";
            XpGainNotifications = BindOrdered(Xp, "XP Gain Notifications", Toggle.Off,
                "Show skill XP gains in the notification stack, not only level-ups.", 400);
            HideRunJumpXp = BindOrdered(Xp, "No Notifications For Running And Jumping", Toggle.On,
                "With XP Gain Notifications, leave out Run and Jump XP, which come in about once a second while you move. Their level-ups still show.", 350);
            XpProgressNotifications = BindOrdered(Xp, "Show Current / Required XP", Toggle.Off,
                "With XP Gain Notifications, show current XP toward the next level out of the XP required for that level.", 300);
            XpProgressPercent = BindOrdered(Xp, "Show XP Progress As Percent", Toggle.On,
                "With XP Gain Notifications, show how far the skill is toward its next level in percent, with one decimal (100.5/200xp shows as 50.3%). Takes the place of the gained XP and of Show Current / Required XP.", 250);
            InWorldXpPopups = BindOrdered(Xp, "In-World XP Popups", Toggle.Off,
                "Show each XP gain as a popup on your character, in the same style as damage numbers. Works with or without XP Gain Notifications. Only you see them.", 200);
            HideMovementXpPopups = BindOrdered(Xp, "No In-World Popups For Movement Skills", Toggle.On,
                "Skip in-world popups for Run, Jump, Swim, Ride and Sneak, which gain XP about once a second while moving. The notification stack is unaffected.", 100);

            // Moved from Notification Sources in 1.1.7: keep the old values.
            string old;
            if (TakeOrphan("3- Notification Sources", "XP Gain Notifications", out old))
                XpGainNotifications.Value = ParseToggle(old, XpGainNotifications.Value);
            if (TakeOrphan("3- Notification Sources", "Show Current / Required XP", out old))
                XpProgressNotifications.Value = ParseToggle(old, XpProgressNotifications.Value);

            PickupNotificationsOnly.SettingChanged += OnPositionSettingChanged;
            NotificationsPosition.SettingChanged += OnPositionSettingChanged;
            Placement.SettingChanged += OnPresetSettingChanged;
            NotificationAlignment.SettingChanged += OnIconSettingChanged;
            IconSize.SettingChanged += OnIconSettingChanged;

            // Rebuild a notification only after its content or these
            // appearance preferences actually change.
            CompactMode.SettingChanged += OnIconSettingChanged;
            NotificationsFontSize.SettingChanged += OnIconSettingChanged;
            NotificationTextColor.SettingChanged += OnIconSettingChanged;
            PreserveCenterMessageColor.SettingChanged += OnIconSettingChanged;
            XpProgressNotifications.SettingChanged += OnIconSettingChanged;
            XpProgressPercent.SettingChanged += OnIconSettingChanged;

            Config.SaveOnConfigSet = true;
            Config.Save();
        }

        private static void OnPresetSettingChanged(object sender, EventArgs e)
        {
            // A genuine preset: write the other two settings, then leave
            // them alone. No per-frame config overrides.
            _pendingPreviousPresetMigration = false;
            PositionPreset preset = Placement.Value;
            NotificationAlignment.Value =
                preset == PositionPreset.Default ? TextAlignment.Left : TextAlignment.Right;
            NotificationsPosition.Value = NotificationSystem.GetPresetCoordinates(preset);

            NotificationSystem.UpdateEntryLayout();
            NotificationSystem.UpdatePanelPosition();
        }

        internal static void CompleteLegacyPresetMigrationWhenReady()
        {
            if (!_pendingPreviousPresetMigration)
                return;

            Hud hud = Hud.instance;
            if (hud == null || hud.m_crosshair == null ||
                hud.m_healthBarRoot == null || MessageHud.instance == null)
                return;

            // This is the only automatic application, required for
            // upgrading old presets whose saved coordinates were ignored.
            OnPresetSettingChanged(null, EventArgs.Empty);
        }

        private static void OnPositionSettingChanged(object sender, EventArgs e)
        {
            NotificationSystem.UpdatePanelPosition();
        }
        private static void OnIconSettingChanged(object sender, EventArgs e)
        {
            NotificationSystem.UpdateEntryLayout();
        }

        private ConfigEntry<Toggle> BindOrdered(string section, string key, Toggle value, string description, int order)
        {
            return Config.Bind(section, key, value, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { Order = order }));
        }

        private bool TakeOrphan(string section, string key, out string value)
        {
            value = null;
            var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                ?.GetValue(Config) as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
            var definition = new ConfigDefinition(section, key);
            if (orphans == null || !orphans.TryGetValue(definition, out value)) return false;
            orphans.Remove(definition);
            return true;
        }

        private static Toggle ParseToggle(string raw, Toggle fallback)
        {
            Toggle parsed;
            return Enum.TryParse(raw == null ? "" : raw.Trim(), true, out parsed) ? parsed : fallback;
        }

        // Personal look-and-feel settings: never synced or locked by a server.
        private ConfigEntry<T> BindLocal<T>(
            string section,
            string key,
            T defaultValue,
            string description,
            AcceptableValueBase acceptableValues = null)
        {
            return Config.Bind(section, key, defaultValue,
                new ConfigDescription(description, acceptableValues));
        }

        private void OnDestroy()
        {
            NotificationSystem.Shutdown();
            _harmony?.UnpatchSelf();

            if (Config != null)
                Config.Save();
        }
    }
}
