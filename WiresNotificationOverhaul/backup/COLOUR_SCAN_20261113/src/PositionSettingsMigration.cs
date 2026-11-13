using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;

namespace WiresNotificationOverhaul
{
    internal static class PositionSettingsMigration
    {
        // Migrate only the retired local position settings. Retain all other
        // user config entries, including notification sources and XP settings.
        internal static void Migrate(ConfigFile config)
        {
            if (config == null || string.IsNullOrEmpty(config.ConfigFilePath) ||
                !File.Exists(config.ConfigFilePath))
                return;

            string[] oldLines = File.ReadAllLines(config.ConfigFilePath);
            var cleaned = new List<string>(oldLines.Length + 12);
            var legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var modern = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string section = "";
            bool removed = false;

            foreach (string line in oldLines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                    trimmed.EndsWith("]", StringComparison.Ordinal))
                    section = trimmed.Substring(1, trimmed.Length - 2);

                int equal = line.IndexOf('=');
                if (equal > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    string key = line.Substring(0, equal).Trim();
                    string value = line.Substring(equal + 1).Trim();

                    if (section == "3- Position")
                        modern.Add(key);

                    if (section == "2- General" &&
                        (key == "Notification Placement" ||
                         key == "Near Crosshair Offset" ||
                         key == "Bottom Right Offset" ||
                         key == "Icons On Right" ||
                         key == "Notifications Panel Position"))
                    {
                        legacy[key] = value;
                        removed = true;
                        continue;
                    }
                }

                cleaned.Add(line);
            }

            if (!removed)
                return;

            // Reuse old placement and panel coordinates when moving to the
            // new position category. Right-hand icons migrate to Right text
            // alignment unless a new alignment value already exists.
            string preset = legacy.TryGetValue("Notification Placement", out string oldPreset)
                ? oldPreset : "Default";
            if (preset == "TopLeft")
                preset = "Default";
            if (preset != "Default" && preset != "NearCrosshair" &&
                preset != "BottomRight")
                preset = "Default";

            string alignment = legacy.TryGetValue("Icons On Right", out string icons) &&
                               icons.Equals("On", StringComparison.OrdinalIgnoreCase)
                ? "Right" : "Left";

            string manualPosition = legacy.TryGetValue("Notifications Panel Position",
                out string oldPosition) ? oldPosition : "{\"x\":40.0,\"y\":-160.0}";

            // Append the section if needed, or append any missing values to
            // an existing section. All keys are present exactly once.
            bool hasNewSection = false;
            foreach (string line in cleaned)
                if (line.Trim() == "[3- Position]")
                    hasNewSection = true;

            var additions = new List<string>(3);
            if (!modern.Contains("Position Preset"))
                additions.Add("Position Preset = " + preset);
            if (!modern.Contains("Notification Text Alignment"))
                additions.Add("Notification Text Alignment = " + alignment);
            if (!modern.Contains("Notification Panel Position"))
                additions.Add("Notification Panel Position = " + manualPosition);

            if (!hasNewSection)
            {
                cleaned.Add("[3- Position]");
                cleaned.AddRange(additions);
            }
            else
            {
                int idx = cleaned.FindIndex(x => x.Trim() == "[3- Position]");
                cleaned.InsertRange(idx + 1, additions);
            }

            // Preserve an independent backup of the pre-migration config.
            string backup = config.ConfigFilePath + ".position-migration-1.0.61.bak";
            if (!File.Exists(backup))
                File.Copy(config.ConfigFilePath, backup);

            File.WriteAllLines(config.ConfigFilePath, cleaned.ToArray());
            config.Reload();
        }
    }
}
